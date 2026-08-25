using System.Data;
using System.Globalization;
using DoSelect.Application.Invoicing;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DoSelect.Infrastructure.Invoicing;

/// <summary>
/// 以 <see cref="DoSelectDbContext"/> 讀出原發票的可折讓餘額，並由成功 Refund 的分攤推導折讓明細。
/// 只讀取本模組擁有的發票、折讓與退款資料表；<c>OrderItemId</c> 僅作為對應鍵，不查 <c>OrderItems</c>。
/// </summary>
public sealed class InvoiceAllowanceReader : IInvoiceAllowanceReader
{
    private const string SequenceLockResource = "doselect:allowance-sequence:";

    private readonly DoSelectDbContext _context;

    public InvoiceAllowanceReader(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    public async Task<InvoiceAllowanceSnapshot?> FindByRefundAsync(
        Guid refundPublicId,
        CancellationToken cancellationToken = default)
    {
        if (refundPublicId == Guid.Empty)
        {
            return null;
        }

        var refund = await _context.Refunds
            .AsNoTracking()
            .Where(candidate => candidate.PublicId == refundPublicId)
            .Select(candidate => new { candidate.Id, candidate.OrderId, candidate.Status })
            .SingleOrDefaultAsync(cancellationToken);

        // 退款不存在才回 null；存在但狀態不對要把狀態帶回去，
        // 讓上層能回報 refund_state_conflict 而不是 resource_not_found。
        if (refund is null)
        {
            return null;
        }

        if (refund.Status != RefundStatus.Succeeded)
        {
            return new InvoiceAllowanceSnapshot(
                refund.Status, refund.Id, null, null, false, [], []);
        }

        var invoice = await _context.SimulatedInvoices
            .AsNoTracking()
            .Where(candidate => candidate.OrderId == refund.OrderId)
            .Select(candidate => new { candidate.Id, candidate.Status })
            .SingleOrDefaultAsync(cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        var items = await _context.SimulatedInvoiceItems
            .AsNoTracking()
            .Where(item => item.SimulatedInvoiceId == invoice.Id)
            .Select(item => new
            {
                item.Id,
                item.PublicId,
                item.OrderItemId,
                item.SkuCodeSnapshot,
                item.Quantity,
                item.GrossAmount,
            })
            .ToArrayAsync(cancellationToken);

        var itemIds = items.Select(item => item.Id).ToArray();
        var allowed = await _context.SimulatedInvoiceAllowanceItems
            .AsNoTracking()
            .Where(allowanceItem => itemIds.Contains(allowanceItem.SimulatedInvoiceItemId))
            .GroupBy(allowanceItem => allowanceItem.SimulatedInvoiceItemId)
            .Select(group => new
            {
                SimulatedInvoiceItemId = group.Key,
                Quantity = group.Sum(allowanceItem => allowanceItem.Quantity),
                GrossAmount = group.Sum(allowanceItem => allowanceItem.GrossAmount),
            })
            .ToArrayAsync(cancellationToken);

        var capacities = items
            .Select(item =>
            {
                var used = allowed.SingleOrDefault(entry => entry.SimulatedInvoiceItemId == item.Id);
                return new InvoiceAllowanceCapacity(
                    item.PublicId,
                    item.Quantity,
                    used?.Quantity ?? 0,
                    item.GrossAmount,
                    used?.GrossAmount ?? 0m);
            })
            .ToArray();

        // 每一列都必須能明確歸類。未知的非商品識別值會在這裡丟例外，
        // 不靜默略過 —— 略過會讓折讓金額短少，卻沒有任何跡象（DEC-P299）。
        var lines = items
            .Select(item => new AllowanceInvoiceLine(
                item.PublicId,
                item.OrderItemId,
                InvoiceLineSkuCodes.ResolveKind(item.OrderItemId, item.SkuCodeSnapshot),
                item.Quantity))
            .ToArray();

        return new InvoiceAllowanceSnapshot(
            refund.Status,
            refund.Id,
            invoice.Id,
            invoice.Status,
            await HasAllowanceAsync(refund.Id, cancellationToken),
            capacities,
            await BuildRefundedLinesAsync(refund.Id, lines, capacities, cancellationToken));
    }

    /// <summary>原發票的一列，已解析出種類。折讓明細要對應回這裡。</summary>
    private sealed record AllowanceInvoiceLine(
        Guid PublicId,
        long? OrderItemId,
        InvoiceLineKind Kind,
        int Quantity);

    /// <summary>
    /// 取得下一個折讓流水號。
    /// </summary>
    /// <remarks>
    /// 流水號只有在取號與寫入落在同一個交易內才有意義，因此本方法要求呼叫端已開啟交易，
    /// 並以交易範圍的應用程式鎖把同月份前綴的取號序列化。最後一道保證是
    /// <c>UX_SimulatedInvoiceAllowances_AllowanceNumber</c> 唯一索引：即使鎖失效，
    /// 重複號碼也會在寫入時被資料庫拒絕，不會產生兩張相同號碼的折讓。
    /// </remarks>
    public async Task<int> NextAllowanceSequenceAsync(
        DateTime issuedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (issuedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The value must use UTC.", nameof(issuedAtUtc));
        }

        var transaction = _context.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "The allowance sequence must be taken inside the transaction that writes the allowance.");

        var prefix = $"{DemoAllowanceNumber.Prefix}-{issuedAtUtc:yyyyMM}-";
        await AcquireSequenceLockAsync(prefix, transaction, cancellationToken);

        // 依已發出的最大號碼推進，而不是既有筆數。折讓一旦被刪除，
        // 以筆數推進會把已用過的號碼再發一次。
        var issuedNumbers = await _context.SimulatedInvoiceAllowances
            .AsNoTracking()
            .Where(allowance => allowance.AllowanceNumber.StartsWith(prefix))
            .Select(allowance => allowance.AllowanceNumber)
            .ToArrayAsync(cancellationToken);

        var highest = 0;
        foreach (var number in issuedNumbers)
        {
            if (int.TryParse(
                    number[prefix.Length..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var value) &&
                value > highest)
            {
                highest = value;
            }
        }

        return highest + 1;
    }

    /// <summary>
    /// 以交易範圍的應用程式鎖序列化同月份的取號。交易提交或回滾時自動釋放。
    /// </summary>
    private async Task AcquireSequenceLockAsync(
        string prefix,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;
            SELECT @result;
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.DbType = DbType.String;
        parameter.Size = 255;
        parameter.Value = SequenceLockResource + prefix;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (Convert.ToInt32(result, CultureInfo.InvariantCulture) < 0)
        {
            throw new InvalidOperationException(
                "The allowance sequence lock could not be acquired.");
        }
    }

    private Task<bool> HasAllowanceAsync(long refundId, CancellationToken cancellationToken) =>
        _context.SimulatedInvoiceAllowances
            .AsNoTracking()
            .AnyAsync(allowance => allowance.RefundId == refundId, cancellationToken);

    /// <summary>
    /// 折讓明細由成功 Refund 的 <see cref="RefundAllocationType.ItemRefund"/> 分攤推導，
    /// 以 <c>OrderItemId</c> 對應到原發票明細。扣回類型與非商品組成不建立折讓明細。
    /// </summary>
    /// <remarks>
    /// 運費與組裝費依 DEC-P299 以 <c>SkuCodeSnapshot</c> 保留值對應到原發票明細，
    /// 數量取自原發票該列本身，不需要退款分攤的數量。
    /// 商品折讓的數量必須取自 <c>RefundAllocations.Quantity</c>（DEC-P286），
    /// 該欄位尚未隨 DES-21 落地，且裁定禁止以金額比例、固定值或
    /// <c>ReturnItems.Quantity</c> 反推，因此有商品分攤時一律拒絕，
    /// 而不是以估算數量建立不可變的財務紀錄。
    /// </remarks>
    private async Task<IReadOnlyList<RefundedInvoiceLine>> BuildRefundedLinesAsync(
        long refundId,
        IReadOnlyList<AllowanceInvoiceLine> invoiceLines,
        IReadOnlyList<InvoiceAllowanceCapacity> capacities,
        CancellationToken cancellationToken)
    {
        var allocations = await _context.RefundAllocations
            .AsNoTracking()
            .Where(allocation => allocation.RefundId == refundId)
            .Select(allocation => new
            {
                allocation.AllocationType,
                allocation.OrderItemId,
                allocation.Amount,
            })
            .ToArrayAsync(cancellationToken);

        var allowable = allocations
            .Where(allocation => InvoiceAllowancePolicy.CreatesAllowanceLine(allocation.AllocationType))
            .ToArray();

        var merchandiseByOrderItemId = invoiceLines
            .Where(line => line.Kind == InvoiceLineKind.Merchandise && line.OrderItemId.HasValue)
            .ToDictionary(line => line.OrderItemId!.Value, line => line);

        // 商品折讓的數量沒有可信來源前不得開立。
        if (allowable.Any(allocation =>
                InvoiceAllowancePolicy.MapsToAnOrderItem(allocation.AllocationType) &&
                allocation.OrderItemId is { } orderItemId &&
                merchandiseByOrderItemId.ContainsKey(orderItemId)))
        {
            throw new InvalidOperationException(
                "A credit note line needs RefundAllocations.Quantity, which has not shipped yet. " +
                "Deriving the quantity from the refunded amount is not permitted by DEC-P286.");
        }

        var refunded = new List<RefundedInvoiceLine>();

        foreach (var (allocationType, kind) in NonMerchandiseKinds)
        {
            var amount = allowable
                .Where(allocation => allocation.AllocationType == allocationType)
                .Sum(allocation => allocation.Amount);

            if (amount <= 0m)
            {
                continue;
            }

            // 原發票必須確實收取過這筆費用，否則沒有可折讓的對象（DEC-P298）。
            var line = invoiceLines.SingleOrDefault(candidate => candidate.Kind == kind)
                ?? throw new InvalidOperationException(
                    $"The refund allocates {allocationType} but the invoice has no {kind} line.");

            var capacity = capacities.Single(candidate =>
                candidate.SimulatedInvoiceItemPublicId == line.PublicId);

            // 退款不得超過原發票該列剩餘可折讓金額。
            if (amount > capacity.RemainingGrossAmount)
            {
                throw new InvalidOperationException(
                    $"The refunded {kind} amount exceeds what the invoice line can still allow.");
            }

            refunded.Add(new RefundedInvoiceLine(line.PublicId, line.Quantity, amount));
        }

        return refunded;
    }

    /// <summary>非商品分攤與原發票明細種類的固定對應（DEC-P298）。</summary>
    private static readonly (RefundAllocationType AllocationType, InvoiceLineKind Kind)[]
        NonMerchandiseKinds =
        [
            (RefundAllocationType.OriginalShipping, InvoiceLineKind.Shipping),
            (RefundAllocationType.AssemblyFee, InvoiceLineKind.AssemblyFee),
        ];
}
