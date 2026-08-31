using DoSelect.Application.Orders;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Orders;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Orders;

/// <summary>
/// <see cref="IOrderInvoiceIssuanceReader"/> 的 Orders 側實作。
/// </summary>
/// <remarks>
/// <para>
/// 這個檔案<b>屬於 Orders</b>。發票模組不直接讀 <c>Orders</c>／<c>OrderItems</c>，
/// 而是透過這個埠取得快照（alex 2026-08-29 Issue #65 A1 裁定）。
/// </para>
/// <para>
/// 只讀不寫，也不開交易：開票的交易由呼叫端（idempotency executor）擁有。
/// </para>
/// </remarks>
public sealed class OrderInvoiceIssuanceReader : IOrderInvoiceIssuanceReader
{
    private readonly DoSelectDbContext _context;

    public OrderInvoiceIssuanceReader(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<InvoiceOrderSnapshot?> FindIssuanceSnapshotAsync(
        Guid orderPublicId,
        CancellationToken cancellationToken = default)
    {
        if (orderPublicId == Guid.Empty)
        {
            return null;
        }

        var order = await _context.Orders.AsNoTracking()
            .Where(candidate => candidate.PublicId == orderPublicId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.OrderStatus,
                candidate.PaymentStatus,
                candidate.PaidAmount,
                candidate.ShippingFee,
                candidate.AssemblyFee,
                candidate.RecipientEmail,
                candidate.InvoiceBuyerType,
                candidate.InvoiceBuyerEmail,
                candidate.InvoiceCarrierType,
                candidate.InvoiceCarrierValueMasked,
                candidate.InvoiceCompanyTaxId,
                candidate.InvoiceCompanyName,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return null;
        }

        var items = await _context.OrderItems.AsNoTracking()
            .Where(item => item.OrderId == order.Id)
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.PublicId,
                item.ProductNameSnapshot,
                item.SkuCodeSnapshot,
                item.Quantity,
                item.FinalUnitPrice,
                item.DiscountAllocation,
                item.LineTotal,
            })
            .ToArrayAsync(cancellationToken);

        var lines = new List<InvoiceOrderLineSource>(items.Length + 2);

        foreach (var item in items)
        {
            lines.Add(new InvoiceOrderLineSource(
                item.Id,
                new InvoiceOrderLine(
                    item.PublicId,
                    InvoiceLineKind.Merchandise,
                    item.ProductNameSnapshot,
                    item.SkuCodeSnapshot,
                    item.Quantity,
                    item.FinalUnitPrice,
                    item.DiscountAllocation,
                    item.LineTotal)));
        }

        // 運費與組裝費是非商品列，沒有對應的 OrderItem，所以 OrderItemId 為 null
        // （SimulatedInvoiceItems.OrderItemId 可為 null，DEC-P299 只要求商品列必須有）。
        // 金額為 0 時不列 —— 開一列 0 元的運費只會讓明細更難讀。
        if (order.ShippingFee > 0m)
        {
            lines.Add(NonMerchandiseLine(InvoiceLineKind.Shipping, order.ShippingFee));
        }

        if (order.AssemblyFee > 0m)
        {
            lines.Add(NonMerchandiseLine(InvoiceLineKind.AssemblyFee, order.AssemblyFee));
        }

        return new InvoiceOrderSnapshot(
            order.Id,
            order.OrderStatus == OrderStatus.Cancelled,
            order.PaymentStatus == PaymentStatus.Paid,
            order.PaidAmount,
            order.InvoiceBuyerType ?? SimulatedInvoiceBuyerType.Individual,
            // 買受人 Email 未填時退回收件人 Email：模擬發票一定要寄得出去，
            // 而結帳時的發票偏好是選填。
            order.InvoiceBuyerEmail ?? order.RecipientEmail,
            order.InvoiceCarrierType,
            order.InvoiceCarrierValueMasked,
            order.InvoiceCompanyTaxId,
            order.InvoiceCompanyName,
            lines);
    }

    /// <remarks>
    /// <c>SkuCodeSnapshot</c> 用 <see cref="InvoiceLineSkuCodes"/> 的保留值 ——
    /// 發票明細不另外持久化種類欄位，就是靠這個值識別非商品列（DEC-P299）。
    /// <c>ProductNameSnapshot</c> 則是給人看的名稱，不放保留值。
    /// </remarks>
    private static InvoiceOrderLineSource NonMerchandiseLine(InvoiceLineKind kind, decimal amount)
    {
        var (name, skuCode) = kind == InvoiceLineKind.Shipping
            ? ("運費", InvoiceLineSkuCodes.Shipping)
            : ("組裝費", InvoiceLineSkuCodes.AssemblyFee);

        return new InvoiceOrderLineSource(
            OrderItemId: null,
            new InvoiceOrderLine(
                OrderItemPublicId: null,
                kind,
                name,
                skuCode,
                Quantity: 1,
                amount,
                DiscountAmount: 0m,
                amount));
    }
}

/// <summary>
/// <see cref="IOrderInvoiceReferenceReader"/> 的 Orders 側實作。
/// </summary>
public sealed class OrderInvoiceReferenceReader : IOrderInvoiceReferenceReader
{
    private readonly DoSelectDbContext _context;

    public OrderInvoiceReferenceReader(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<OrderInvoiceReference?> FindAsync(
        Guid orderPublicId,
        CancellationToken cancellationToken = default)
    {
        if (orderPublicId == Guid.Empty)
        {
            return null;
        }

        return await Project(_context.Orders.AsNoTracking()
                .Where(order => order.PublicId == orderPublicId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <remarks>
    /// 一次 <c>WHERE Id IN (...)</c>，不是逐筆查 —— 後台發票清單每一列都要訂單摘要，
    /// 逐筆會是 N+1。
    /// </remarks>
    public async Task<IReadOnlyDictionary<long, OrderInvoiceReference>> FindManyAsync(
        IReadOnlyCollection<long> orderIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderIds);

        // 空集合不要往下打一個 `IN ()` 的查詢。
        var wanted = orderIds.Where(id => id > 0).Distinct().ToArray();
        if (wanted.Length == 0)
        {
            return new Dictionary<long, OrderInvoiceReference>();
        }

        var rows = await Project(_context.Orders.AsNoTracking()
                .Where(order => wanted.Contains(order.Id)))
            .ToArrayAsync(cancellationToken);

        return rows.ToDictionary(row => row.OrderId);
    }

    private static IQueryable<OrderInvoiceReference> Project(IQueryable<Order> orders) =>
        orders.Select(order => new OrderInvoiceReference(
            order.Id,
            order.PublicId,
            order.OrderNumber,
            order.MemberUserId,
            order.GuestEmailNormalized));
}
