using DoSelect.Application.Invoicing;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Invoicing;

/// <summary>
/// 以 <see cref="DoSelectDbContext"/> 讀出原發票的可折讓餘額，並由成功 Refund 的分攤推導折讓明細。
/// 只讀取本模組擁有的發票、折讓與退款資料表；<c>OrderItemId</c> 僅作為對應鍵，不查 <c>OrderItems</c>。
/// </summary>
public sealed class InvoiceAllowanceReader : IInvoiceAllowanceReader
{
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

        // 只有成功的退款才建立折讓。
        if (refund is null || refund.Status != RefundStatus.Succeeded)
        {
            return null;
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

        var invoiceItemByOrderItemId = items
            .Where(item => item.OrderItemId.HasValue)
            .ToDictionary(item => item.OrderItemId!.Value, item => item.PublicId);

        return new InvoiceAllowanceSnapshot(
            invoice.Id,
            refund.Id,
            invoice.Status,
            await HasAllowanceAsync(refund.Id, cancellationToken),
            capacities,
            await BuildRefundedLinesAsync(
                refund.Id, capacities, invoiceItemByOrderItemId, cancellationToken));
    }

    public async Task<int> NextAllowanceSequenceAsync(
        DateTime issuedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (issuedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The value must use UTC.", nameof(issuedAtUtc));
        }

        var prefix = $"{DemoAllowanceNumber.Prefix}-{issuedAtUtc:yyyyMM}-";
        var issued = await _context.SimulatedInvoiceAllowances
            .AsNoTracking()
            .CountAsync(
                allowance => allowance.AllowanceNumber.StartsWith(prefix),
                cancellationToken);

        return issued + 1;
    }

    private Task<bool> HasAllowanceAsync(long refundId, CancellationToken cancellationToken) =>
        _context.SimulatedInvoiceAllowances
            .AsNoTracking()
            .AnyAsync(allowance => allowance.RefundId == refundId, cancellationToken);

    /// <summary>
    /// 折讓明細由成功 Refund 的 <see cref="RefundAllocationType.ItemRefund"/> 分攤推導，
    /// 以 <c>OrderItemId</c> 對應到原發票明細。扣回類型與非商品組成不建立折讓明細。
    /// </summary>
    private async Task<IReadOnlyList<RefundedInvoiceLine>> BuildRefundedLinesAsync(
        long refundId,
        IReadOnlyList<InvoiceAllowanceCapacity> capacities,
        IReadOnlyDictionary<long, Guid> invoiceItemByOrderItemId,
        CancellationToken cancellationToken)
    {
        var allocations = await _context.RefundAllocations
            .AsNoTracking()
            .Where(allocation =>
                allocation.RefundId == refundId &&
                allocation.AllocationType == RefundAllocationType.ItemRefund &&
                allocation.OrderItemId != null)
            .Select(allocation => new
            {
                OrderItemId = allocation.OrderItemId!.Value,
                allocation.Amount,
            })
            .ToArrayAsync(cancellationToken);

        return allocations
            .Where(allocation => invoiceItemByOrderItemId.ContainsKey(allocation.OrderItemId))
            .GroupBy(allocation => allocation.OrderItemId)
            .Select(group =>
            {
                var itemPublicId = invoiceItemByOrderItemId[group.Key];
                var amount = group.Sum(allocation => allocation.Amount);
                var capacity = capacities.Single(candidate =>
                    candidate.SimulatedInvoiceItemPublicId == itemPublicId);

                return new RefundedInvoiceLine(itemPublicId, DeriveQuantity(capacity, amount), amount);
            })
            .Where(line => line.Quantity > 0)
            .ToArray();
    }

    /// <summary>
    /// 折讓數量的暫行推導規則。
    /// </summary>
    /// <remarks>
    /// <c>RefundAllocations</c> 目前沒有數量欄位，因此折讓數量以退款金額佔該發票明細含稅小計的
    /// 比例推導，四捨五入後夾在剩餘可折讓數量以內。**金額本身直接取自退款分攤，不受此推導影響。**
    /// 正式來源需要 <c>RefundAllocations.Quantity</c>，或 kafen 提供已核准退貨的數量摘要，待 alex 裁定。
    /// 在那之前，部分金額退款所推導的折讓數量可能與實際退貨件數不同。
    /// </remarks>
    private static int DeriveQuantity(InvoiceAllowanceCapacity capacity, decimal amount)
    {
        if (capacity.GrossAmount <= 0m || capacity.RemainingQuantity <= 0)
        {
            return 0;
        }

        var derived = (int)Math.Round(
            capacity.Quantity * amount / capacity.GrossAmount,
            MidpointRounding.AwayFromZero);

        return Math.Clamp(derived, 1, capacity.RemainingQuantity);
    }
}
