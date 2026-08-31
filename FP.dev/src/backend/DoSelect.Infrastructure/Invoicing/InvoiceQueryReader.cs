using DoSelect.Application.Common;
using DoSelect.Application.Invoicing;
using DoSelect.Domain.Invoicing;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Invoicing;

/// <summary>
/// <see cref="IInvoiceQueryReader"/> 的實作。
/// </summary>
/// <remarks>
/// <para>
/// <b>只讀 Invoicing 自己的四張表</b>（<c>SimulatedInvoices</c>、<c>SimulatedInvoiceItems</c>、
/// <c>SimulatedInvoiceAllowances</c>、<c>SimulatedInvoiceAllowanceItems</c>）。
/// 訂單那一半由 <c>IOrderInvoiceReferenceReader</c> 提供，在 Application 層合併
/// （alex 2026-08-29 Issue #65 A1）。
/// </para>
/// <para>
/// 每個查詢都是<b>固定次數</b>的往返：發票一次、明細一次、折讓一次、折讓明細一次。
/// 不隨資料筆數成長。
/// </para>
/// </remarks>
public sealed class InvoiceQueryReader : IInvoiceQueryReader
{
    private readonly DoSelectDbContext _context;

    public InvoiceQueryReader(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<InvoiceRow?> FindByOrderAsync(
        long orderId, CancellationToken cancellationToken = default)
    {
        if (orderId <= 0)
        {
            return null;
        }

        // 先在實體上篩，再投影 —— 反過來的話條件會落在 InvoiceHeader 的建構式上，
        // EF 沒辦法把它翻回欄位，整個查詢會在執行期炸掉。
        var invoice = await Headers(_context.SimulatedInvoices
                .Where(candidate => candidate.OrderId == orderId))
            .SingleOrDefaultAsync(cancellationToken);

        return invoice is null ? null : await ComposeAsync([invoice], cancellationToken) is [var row] ? row : null;
    }

    public async Task<InvoiceRow?> FindAsync(
        Guid invoicePublicId, CancellationToken cancellationToken = default)
    {
        if (invoicePublicId == Guid.Empty)
        {
            return null;
        }

        var invoice = await Headers(_context.SimulatedInvoices
                .Where(candidate => candidate.PublicId == invoicePublicId))
            .SingleOrDefaultAsync(cancellationToken);

        return invoice is null ? null : await ComposeAsync([invoice], cancellationToken) is [var row] ? row : null;
    }

    public async Task<PageResult<InvoiceRow>> ListAsync(
        AdminInvoiceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var pageNumber = Math.Max(query.PageNumber, 1);

        var filtered = _context.SimulatedInvoices.AsNoTracking().AsQueryable();

        if (query.Statuses is { Count: > 0 } requested)
        {
            // 沒送、或送了空集合，都是「不篩狀態」。
            // 這跟 EfAdminCouponService、ReturnStore、SupportTicketStore 一致 ——
            // 同一個後台的清單端點對 statuses=[] 給出不同答案，比語意上更漂亮的選擇更糟。
            var statuses = requested.ToArray();
            filtered = filtered.Where(invoice => statuses.Contains(invoice.Status));
        }

        if (query.FromUtc is { } from)
        {
            filtered = filtered.Where(invoice => invoice.IssuedAtUtc >= from);
        }

        if (query.ToUtc is { } to)
        {
            filtered = filtered.Where(invoice => invoice.IssuedAtUtc < to);
        }

        var keyword = query.Q?.Trim();
        if (!string.IsNullOrEmpty(keyword))
        {
            // 只比對發票號碼：訂單編號在 Orders，這個 Reader 不碰它。
            filtered = filtered.Where(invoice => invoice.InvoiceNumber.Contains(keyword));
        }

        var totalCount = await filtered.CountAsync(cancellationToken);

        // 頁碼可能大到讓 int 溢位（EfProductSearchService 也是這樣處理同一個問題）。
        var skip = ((long)pageNumber - 1) * pageSize;
        if (skip > int.MaxValue)
        {
            return new PageResult<InvoiceRow>([], pageNumber, pageSize, totalCount);
        }

        // 排序同樣要在投影前。InvoiceNumber 有唯一索引，所以翻頁順序完整且穩定。
        var ordered = filtered.OrderByDescending(invoice => invoice.InvoiceNumber);

        var headers = await Project(ordered)
            .Skip((int)skip)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        var rows = await ComposeAsync(headers, cancellationToken);
        return new PageResult<InvoiceRow>(rows, pageNumber, pageSize, totalCount);
    }

    private static IQueryable<InvoiceHeader> Headers(IQueryable<SimulatedInvoice> invoices) =>
        Project(invoices.AsNoTracking());

    /// <remarks>
    /// 投影之後就不能再加條件或排序了：EF 翻不了對建構式參數的存取。
    /// 呼叫端一律先把 <see cref="IQueryable{T}"/> 篩好、排好再交進來。
    /// </remarks>
    private static IQueryable<InvoiceHeader> Project(IQueryable<SimulatedInvoice> invoices) =>
        invoices.Select(invoice => new InvoiceHeader(
            invoice.Id,
            invoice.OrderId,
            invoice.PublicId,
            invoice.InvoiceNumber,
            invoice.Status,
            invoice.BuyerType,
            invoice.BuyerEmail,
            invoice.CarrierType,
            invoice.CarrierValueMasked,
            invoice.CompanyTaxId,
            invoice.NetAmount,
            invoice.TaxAmount,
            invoice.IssuedAmount,
            invoice.Currency,
            invoice.IssuedAtUtc,
            invoice.VoidedAtUtc,
            invoice.DemoMarker,
            invoice.RowVersion));

    /// <remarks>
    /// 明細、折讓與折讓明細各<b>一次</b>批次查詢，不論頁面上有幾張發票 ——
    /// 逐張查會隨頁面大小形成 N+1。
    /// </remarks>
    private async Task<IReadOnlyList<InvoiceRow>> ComposeAsync(
        IReadOnlyList<InvoiceHeader> headers, CancellationToken cancellationToken)
    {
        if (headers.Count == 0)
        {
            return [];
        }

        var invoiceIds = headers.Select(header => header.Id).ToArray();

        var items = await _context.SimulatedInvoiceItems.AsNoTracking()
            .Where(item => invoiceIds.Contains(item.SimulatedInvoiceId))
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.SimulatedInvoiceId,
                item.Id,
                item.PublicId,
                item.OrderItemId,
                item.ProductNameSnapshot,
                item.SkuCodeSnapshot,
                item.Quantity,
                item.UnitPrice,
                item.DiscountAmount,
                item.NetAmount,
                item.TaxAmount,
                item.GrossAmount,
            })
            .ToArrayAsync(cancellationToken);

        var allowances = await _context.SimulatedInvoiceAllowances.AsNoTracking()
            .Where(allowance => invoiceIds.Contains(allowance.SimulatedInvoiceId))
            .OrderBy(allowance => allowance.Id)
            .Select(allowance => new
            {
                allowance.Id,
                allowance.SimulatedInvoiceId,
                allowance.PublicId,
                allowance.AllowanceNumber,
                allowance.RefundId,
                allowance.NetAmount,
                allowance.TaxAmount,
                allowance.Amount,
                allowance.IssuedAtUtc,
            })
            .ToArrayAsync(cancellationToken);

        var allowanceIds = allowances.Select(allowance => allowance.Id).ToArray();
        var allowanceItems = allowanceIds.Length == 0
            ? []
            : await _context.SimulatedInvoiceAllowanceItems.AsNoTracking()
                .Where(item => allowanceIds.Contains(item.AllowanceId))
                .OrderBy(item => item.Id)
                .Select(item => new
                {
                    item.AllowanceId,
                    item.PublicId,
                    item.SimulatedInvoiceItemId,
                    item.Quantity,
                    item.NetAmount,
                    item.TaxAmount,
                    item.GrossAmount,
                })
                .ToArrayAsync(cancellationToken);

        var itemsByInvoice = items
            .GroupBy(item => item.SimulatedInvoiceId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<InvoiceItemRow>)[.. group.Select(item =>
                    new InvoiceItemRow(
                        item.Id,
                        item.OrderItemId,
                        item.PublicId,
                        InvoiceLineSkuCodes.IsReserved(item.SkuCodeSnapshot)
                            ? item.SkuCodeSnapshot == InvoiceLineSkuCodes.Shipping
                                ? InvoiceLineKind.Shipping
                                : InvoiceLineKind.AssemblyFee
                            : InvoiceLineKind.Merchandise,
                        item.ProductNameSnapshot,
                        item.SkuCodeSnapshot,
                        item.Quantity,
                        item.UnitPrice,
                        item.DiscountAmount,
                        item.NetAmount,
                        item.TaxAmount,
                        item.GrossAmount))]);

        var allowanceItemsByAllowance = allowanceItems
            .GroupBy(item => item.AllowanceId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var allowancesByInvoice = allowances
            .GroupBy(allowance => allowance.SimulatedInvoiceId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<InvoiceAllowanceRow>)[.. group.Select(allowance =>
                    new InvoiceAllowanceRow(
                        allowance.RefundId,
                        allowance.PublicId,
                        allowance.AllowanceNumber,
                        allowance.NetAmount,
                        allowance.TaxAmount,
                        allowance.Amount,
                        [.. (allowanceItemsByAllowance.GetValueOrDefault(allowance.Id) ?? [])
                            .Select(item => new InvoiceAllowanceItemRow(
                                item.PublicId,
                                item.SimulatedInvoiceItemId,
                                item.Quantity,
                                item.NetAmount,
                                item.TaxAmount,
                                item.GrossAmount))],
                        allowance.IssuedAtUtc))]);

        return
        [
            .. headers.Select(header => new InvoiceRow(
                header.OrderId,
                header.PublicId,
                header.InvoiceNumber,
                header.Status,
                header.BuyerType,
                header.BuyerEmail,
                header.CarrierType,
                header.CarrierValueMasked,
                header.CompanyTaxId,
                header.NetAmount,
                header.TaxAmount,
                header.GrossAmount,
                header.Currency,
                header.IssuedAtUtc,
                header.VoidedAtUtc,
                header.DemoMarker,
                header.RowVersion,
                itemsByInvoice.GetValueOrDefault(header.Id) ?? [],
                allowancesByInvoice.GetValueOrDefault(header.Id) ?? [])),
        ];
    }

    /// <summary>投影出來的發票表頭，帶內部 Id 供批次補明細用。</summary>
    private sealed record InvoiceHeader(
        long Id,
        long OrderId,
        Guid PublicId,
        string InvoiceNumber,
        SimulatedInvoiceStatus Status,
        SimulatedInvoiceBuyerType BuyerType,
        string? BuyerEmail,
        string? CarrierType,
        string? CarrierValueMasked,
        string? CompanyTaxId,
        decimal NetAmount,
        decimal TaxAmount,
        decimal GrossAmount,
        string Currency,
        DateTime? IssuedAtUtc,
        DateTime? VoidedAtUtc,
        string DemoMarker,
        byte[] RowVersion);
}
