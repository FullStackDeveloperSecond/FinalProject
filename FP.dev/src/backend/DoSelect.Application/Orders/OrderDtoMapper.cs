using DoSelect.Application.Returns;
using DoSelect.Domain.Orders;

namespace DoSelect.Application.Orders;

/// <summary>
/// Keeps the customer-safe order projection identical for ordinary order reads, Checkout creation,
/// and idempotent Checkout replay.
/// </summary>
public static class OrderDtoMapper
{
    private const string CancelAction = "cancel";
    private const string RequestReturnAction = "requestReturn";

    public static OrderDto Map(Order order, IReadOnlyList<OrderItem> items, OrderShipmentDto? shipment = null)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(items);

        var itemDtos = items
            .Select(item => new OrderItemDto(
                item.PublicId,
                item.SkuCodeSnapshot,
                item.ProductNameSnapshot,
                item.SkuNameSnapshot,
                item.Quantity,
                item.FinalUnitPrice,
                item.LineTotal,
                item.ReturnableQuantity,
                item.ReturnedQuantity))
            .ToList();

        var actions = new List<string>();
        if (order.OrderStatus == OrderStatus.PendingPayment)
        {
            actions.Add(CancelAction);
        }

        var hasReturnableQuantity = itemDtos.Any(item => item.ReturnableQuantity > item.ReturnedQuantity);
        var isDelivered = order.FulfillmentStatus is FulfillmentStatus.Delivered or FulfillmentStatus.PickedUp;
        if (isDelivered && hasReturnableQuantity)
        {
            actions.Add(RequestReturnAction);
        }

        var recipient = new OrderRecipientSummaryDto(
            order.RecipientName,
            order.ShippingMethodCode,
            order.StoreName);

        var amounts = new OrderAmountsDto(
            order.MerchandiseSubtotal,
            order.ItemDiscountTotal,
            order.ShippingFee,
            order.AssemblyFee,
            order.GrandTotal,
            order.PaidAmount,
            order.RefundedAmount,
            order.Currency);

        return new OrderDto(
            order.PublicId,
            order.OrderNumber,
            order.OrderStatus,
            order.PaymentStatus,
            order.FulfillmentStatus,
            order.AssemblyStatus,
            order.OrderRefundStatus,
            itemDtos,
            recipient,
            amounts,
            order.PaymentDueAtUtc,
            order.ConfirmedAtUtc,
            order.PaidAtUtc,
            order.ShippedAtUtc,
            order.DeliveredAtUtc,
            order.CompletedAtUtc,
            order.CancelledAtUtc,
            order.DeliveredAtUtc is { } deliveredAtUtc
                ? ReturnEligibilityPolicy.ComputeCoolingOffDeadlineUtc(deliveredAtUtc)
                : null,
            actions,
            order.RowVersion,
            shipment);
    }
}
