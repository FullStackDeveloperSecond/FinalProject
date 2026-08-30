using DoSelect.Application.Common;
using DoSelect.Application.Orders;
using DoSelect.Domain.Invoicing;

namespace DoSelect.Application.Invoicing;

/// <summary>
/// 把 Invoicing 自己的發票資料與 Orders 的訂單投影合併成對外 DTO。
/// </summary>
/// <remarks>
/// <para>
/// 合併發生在<b>這一層</b>，不在任何一邊的 Infrastructure：Invoicing 不讀 Orders
/// （Issue #65 A1），Orders 也不讀 <c>SimulatedInvoices</c>。兩個 Reader 各自只碰自己的表。
/// </para>
/// <para>
/// 清單一律走 <c>FindManyAsync</c> 批次補訂單，不在迴圈裡逐筆查 —— 那是 alex 明列的驗收條件。
/// </para>
/// </remarks>
public sealed class InvoiceQueryService
{
    private readonly IInvoiceQueryReader _invoices;
    private readonly IOrderInvoiceReferenceReader _orders;

    public InvoiceQueryService(IInvoiceQueryReader invoices, IOrderInvoiceReferenceReader orders)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(orders);

        _invoices = invoices;
        _orders = orders;
    }

    /// <summary>
    /// 前台：某一張訂單的發票。
    /// </summary>
    /// <remarks>
    /// 會員的擁有者比對在<b>這裡</b>做，所以不必啟動 HTTP 就測得到；
    /// 訪客的 Scope 由呼叫端先用 <c>GuestOrderAccessScopeAuthorizer</c> 驗過才進來。
    /// </remarks>
    public async Task<SimulatedInvoiceDto?> FindForOrderAsync(
        InvoiceViewer viewer,
        Guid orderPublicId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        var order = await _orders.FindAsync(orderPublicId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        // 會員必須是這張訂單的擁有者。回 null（呼叫端折成 404）而不是 403 ——
        // 區分「不存在」與「不是你的」等於告訴外人這個 id 存在。
        if (viewer is InvoiceViewer.Member member &&
            !string.Equals(order.MemberUserId, member.MemberUserId, StringComparison.Ordinal))
        {
            return null;
        }

        var invoice = await _invoices.FindByOrderAsync(order.OrderId, cancellationToken);
        return invoice is null ? null : ToDto(invoice, order.OrderPublicId);
    }

    /// <summary>後台：一張發票的完整內容。</summary>
    public async Task<AdminInvoiceDto?> FindAsync(
        Guid invoicePublicId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _invoices.FindAsync(invoicePublicId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var orders = await _orders.FindManyAsync([invoice.OrderId], cancellationToken);
        if (!orders.TryGetValue(invoice.OrderId, out var order))
        {
            // 發票的 OrderId 是外鍵，訂單不見代表資料不一致 —— 與其回一張沒有訂單的發票，
            // 不如當成找不到，讓呼叫端回 404 而不是一份殘缺的內容。
            return null;
        }

        return new AdminInvoiceDto(
            ToDto(invoice, order.OrderPublicId),
            order.OrderNumber,
            InvoiceActions.For(invoice.Status));
    }

    /// <summary>後台清單。</summary>
    public async Task<PageResult<AdminInvoiceSummaryDto>> ListAsync(
        AdminInvoiceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = await _invoices.ListAsync(query, cancellationToken);

        // 一次補完整頁的訂單，不在迴圈裡逐筆查。
        var orders = await _orders.FindManyAsync(
            [.. page.Items.Select(row => row.OrderId).Distinct()],
            cancellationToken);

        var items = page.Items
            .Select(row =>
            {
                // 訂單查不到時不編一個假的 PublicId：留空並讓介面看得出來，
                // 比顯示一個不存在的訂單好。
                var order = orders.GetValueOrDefault(row.OrderId);
                return new AdminInvoiceSummaryDto(
                    row.PublicId,
                    row.InvoiceNumber,
                    order?.OrderPublicId ?? Guid.Empty,
                    order?.OrderNumber ?? string.Empty,
                    row.Status,
                    row.NetAmount,
                    row.TaxAmount,
                    row.GrossAmount,
                    row.IssuedAtUtc,
                    row.DemoMarker,
                    row.RowVersion);
            })
            .ToArray();

        return new PageResult<AdminInvoiceSummaryDto>(
            items, page.PageNumber, page.PageSize, page.TotalCount);
    }

    private static SimulatedInvoiceDto ToDto(InvoiceRow row, Guid orderPublicId) =>
        new(
            row.PublicId,
            row.InvoiceNumber,
            orderPublicId,
            row.Status,
            row.BuyerType,
            MaskEmail(row.BuyerEmail),
            row.CarrierType,
            row.CarrierValueMasked,
            MaskTaxId(row.CompanyTaxId),
            row.NetAmount,
            row.TaxAmount,
            row.GrossAmount,
            row.Currency,
            InvoiceCalculator.BusinessTaxRate,
            row.Items,
            row.Allowances,
            row.IssuedAtUtc,
            row.VoidedAtUtc,
            row.DemoMarker,
            row.RowVersion);

    /// <summary>
    /// 買受人 Email 遮蔽。
    /// </summary>
    /// <remarks>
    /// `API Endpoint目錄` 第 74 行：前台只回遮蔽後的買受人資料。遮蔽在<b>這一層</b>做，
    /// 不在 Reader —— Reader 回原值是為了讓寫入端與稽核用得到，
    /// 對外的每一條路徑都必須自己遮。
    /// </remarks>
    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0)
        {
            // 沒有 @ 就不是可辨識的 Email；整段遮掉而不是原樣回傳。
            return new string('*', email.Length);
        }

        var name = email[..at];
        var visible = name.Length <= 1 ? name : name[..1];
        return $"{visible}{new string('*', Math.Max(name.Length - visible.Length, 1))}{email[at..]}";
    }

    /// <remarks>統一編號固定八碼，只露最後三碼。</remarks>
    private static string? MaskTaxId(string? taxId) =>
        string.IsNullOrWhiteSpace(taxId)
            ? null
            : taxId.Length <= 3
                ? new string('*', taxId.Length)
                : new string('*', taxId.Length - 3) + taxId[^3..];
}
