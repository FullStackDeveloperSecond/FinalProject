using DoSelect.Application.Common;
using DoSelect.Application.Orders;
using DoSelect.Application.Refunds;
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
    private readonly IRefundInvoiceReferenceReader _refunds;

    public InvoiceQueryService(
        IInvoiceQueryReader invoices,
        IOrderInvoiceReferenceReader orders,
        IRefundInvoiceReferenceReader refunds)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(refunds);

        _invoices = invoices;
        _orders = orders;
        _refunds = refunds;
    }

    /// <summary>
    /// 前台：某一張訂單的發票。
    /// </summary>
    /// <remarks>
    /// 會員的擁有者比對在<b>這裡</b>做，所以不必啟動 HTTP 就測得到；
    /// 訪客的 Scope 由呼叫端先用 <c>GuestOrderAccessScopeAuthorizer</c> 驗過才進來。
    /// </remarks>
    public async Task<InvoiceForOrderResult> FindForOrderAsync(
        InvoiceViewer viewer,
        Guid orderPublicId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        var order = await _orders.FindAsync(orderPublicId, cancellationToken);
        if (order is null)
        {
            return new InvoiceForOrderResult.NotFound();
        }

        // 會員必須是這張訂單的擁有者。結果只讓呼叫端決定是否繼續檢查同一瀏覽器
        // 的 Guest cookie；若 Guest 也不能證明權限，對外仍折成 404 而不是 403。
        if (viewer is InvoiceViewer.Member member &&
            !string.Equals(order.MemberUserId, member.MemberUserId, StringComparison.Ordinal))
        {
            return new InvoiceForOrderResult.MemberAccessDenied();
        }

        var invoice = await _invoices.FindByOrderAsync(order.OrderId, cancellationToken);
        return invoice is null
            ? new InvoiceForOrderResult.NotFound()
            : new InvoiceForOrderResult.Found(
                await ToDtoAsync(invoice, order.OrderPublicId, cancellationToken));
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
            throw new InvalidOperationException(
                $"Invoice '{invoice.PublicId}' references missing order '{invoice.OrderId}'.");
        }

        return new AdminInvoiceDto(
            await ToDtoAsync(invoice, order.OrderPublicId, cancellationToken),
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

        var missingOrderIds = page.Items.Select(row => row.OrderId)
            .Distinct()
            .Where(orderId => !orders.ContainsKey(orderId))
            .ToArray();
        if (missingOrderIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"Invoice rows reference missing orders: {string.Join(", ", missingOrderIds)}.");
        }

        var items = page.Items
            .Select(row =>
            {
                var order = orders[row.OrderId];
                return new AdminInvoiceSummaryDto(
                    row.PublicId,
                    row.InvoiceNumber,
                    order.OrderPublicId,
                    order.OrderNumber,
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

    private async Task<SimulatedInvoiceDto> ToDtoAsync(
        InvoiceRow row,
        Guid orderPublicId,
        CancellationToken cancellationToken)
    {
        var orderItemIds = row.Items
            .Where(item => item.OrderItemId.HasValue)
            .Select(item => item.OrderItemId!.Value)
            .Distinct()
            .ToArray();
        var refundIds = row.Allowances.Select(allowance => allowance.RefundId)
            .Distinct()
            .ToArray();

        var orderItemPublicIds = await _orders.FindItemPublicIdsAsync(
            orderItemIds, cancellationToken);
        var refundPublicIds = await _refunds.FindManyAsync(refundIds, cancellationToken);

        var itemsById = row.Items.ToDictionary(item => item.Id);
        var items = row.Items.Select(item =>
        {
            Guid? orderItemPublicId = null;
            if (item.Kind == InvoiceLineKind.Merchandise)
            {
                if (item.OrderItemId is not { } orderItemId ||
                    !orderItemPublicIds.TryGetValue(orderItemId, out var publicId))
                {
                    throw new InvalidOperationException(
                        $"Merchandise invoice item '{item.PublicId}' has no order item projection.");
                }

                orderItemPublicId = publicId;
            }
            else if (item.OrderItemId.HasValue)
            {
                throw new InvalidOperationException(
                    $"Non-merchandise invoice item '{item.PublicId}' references an order item.");
            }

            return new SimulatedInvoiceItemDto(
                item.PublicId,
                orderItemPublicId,
                item.Kind,
                item.ProductName,
                item.SkuCode,
                item.Quantity,
                item.UnitPrice,
                item.DiscountAmount,
                item.NetAmount,
                item.TaxAmount,
                item.GrossAmount);
        }).ToArray();

        var allowances = row.Allowances.Select(allowance =>
        {
            if (!refundPublicIds.TryGetValue(allowance.RefundId, out var refundPublicId))
            {
                throw new InvalidOperationException(
                    $"Invoice allowance '{allowance.PublicId}' references a missing refund.");
            }

            var allowanceItems = allowance.Items.Select(item =>
            {
                if (!itemsById.TryGetValue(item.SimulatedInvoiceItemId, out var invoiceItem))
                {
                    throw new InvalidOperationException(
                        $"Invoice allowance item '{item.PublicId}' references a missing invoice item.");
                }

                return new SimulatedInvoiceAllowanceItemDto(
                    item.PublicId,
                    invoiceItem.PublicId,
                    invoiceItem.Kind,
                    item.Quantity,
                    item.NetAmount,
                    item.TaxAmount,
                    item.GrossAmount);
            }).ToArray();

            return new SimulatedInvoiceAllowanceDto(
                allowance.PublicId,
                allowance.AllowanceNumber,
                row.PublicId,
                refundPublicId,
                allowance.NetAmount,
                allowance.TaxAmount,
                allowance.GrossAmount,
                allowanceItems,
                allowance.IssuedAtUtc,
                InvoiceAllowanceWriteConstants.DemoMarker);
        }).ToArray();

        return new SimulatedInvoiceDto(
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
            items,
            allowances,
            row.IssuedAtUtc,
            row.VoidedAtUtc,
            row.DemoMarker,
            row.RowVersion);
    }

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
