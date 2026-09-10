using DoSelect.Application.Orders;

namespace DoSelect.Application.Invoicing;

public sealed record InvoiceIssuanceOrderDto(
    Guid OrderPublicId,
    string OrderNumber,
    bool OrderIsPaid,
    bool OrderIsCancelled,
    byte[] RowVersion,
    bool HasInvoice);

public sealed class InvoiceIssuanceOrderQueryService
{
    private readonly IOrderInvoiceIssuanceReader _orders;
    private readonly IInvoiceExistenceReader _invoices;

    public InvoiceIssuanceOrderQueryService(
        IOrderInvoiceIssuanceReader orders,
        IInvoiceExistenceReader invoices)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(invoices);

        _orders = orders;
        _invoices = invoices;
    }

    public async Task<InvoiceIssuanceOrderDto?> FindAsync(
        Guid orderPublicId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orders.FindAdminSummaryAsync(orderPublicId, cancellationToken);
        return await ToDtoAsync(order, cancellationToken);
    }

    public async Task<InvoiceIssuanceOrderDto?> FindAsync(
        string orderReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderReference);

        var normalized = orderReference.Trim();
        if (normalized.Length == 0 || normalized.Length > 64)
        {
            return null;
        }

        var order = Guid.TryParse(normalized, out var orderPublicId)
            ? await _orders.FindAdminSummaryAsync(orderPublicId, cancellationToken)
            : await _orders.FindAdminSummaryByOrderNumberAsync(normalized, cancellationToken);
        return await ToDtoAsync(order, cancellationToken);
    }

    private async Task<InvoiceIssuanceOrderDto?> ToDtoAsync(
        InvoiceIssuanceOrderSummary? order,
        CancellationToken cancellationToken)
    {
        if (order is null)
        {
            return null;
        }

        return new InvoiceIssuanceOrderDto(
            order.OrderPublicId,
            order.OrderNumber,
            order.OrderIsPaid,
            order.OrderIsCancelled,
            order.RowVersion,
            await _invoices.HasInvoiceAsync(order.OrderId, cancellationToken));
    }
}
