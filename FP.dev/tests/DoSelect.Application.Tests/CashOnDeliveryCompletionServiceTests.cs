using DoSelect.Application.Payments;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;

namespace DoSelect.Application.Tests;

public sealed class CashOnDeliveryCompletionServiceTests
{
    [Theory]
    [InlineData(FulfillmentStatus.Delivered)]
    [InlineData(FulfillmentStatus.PickedUp)]
    public void DeliveredOrPickedUp_CreatesACollectionPlan(FulfillmentStatus fulfillmentStatus)
    {
        var result = Decide(Snapshot(fulfillmentStatus: fulfillmentStatus));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [PaymentAttemptStatus.Processing, PaymentAttemptStatus.Paid],
            result.Plan!.AttemptTransitions);
        Assert.Equal(PaymentStatus.Paid, result.Plan.OrderPaymentStatus);
        Assert.Equal(1000m, result.Plan.OrderPaidAmount);
        Assert.True(result.Plan.RequestSimulatedInvoice);
    }

    [Theory]
    [InlineData(FulfillmentStatus.Pending)]
    [InlineData(FulfillmentStatus.Preparing)]
    [InlineData(FulfillmentStatus.Shipped)]
    [InlineData(FulfillmentStatus.InTransit)]
    [InlineData(FulfillmentStatus.PickupReady)]
    [InlineData(FulfillmentStatus.DeliveryFailed)]
    [InlineData(FulfillmentStatus.Returned)]
    public void NonTerminalFulfillment_DoesNotCollect(FulfillmentStatus fulfillmentStatus)
    {
        var result = Decide(Snapshot(fulfillmentStatus: fulfillmentStatus));

        Assert.Equal(PaymentErrorCodes.PaymentStateConflict, result.ErrorCode);
    }

    [Fact]
    public void OnlinePaymentMethod_DoesNotUseTheCashOnDeliveryPath()
    {
        var result = Decide(Snapshot(method: PaymentMethod.CreditCard));

        Assert.Equal(PaymentErrorCodes.PaymentStateConflict, result.ErrorCode);
    }

    [Fact]
    public void CancelledOrder_DoesNotCollect()
    {
        var result = Decide(Snapshot(orderStatus: OrderStatus.Cancelled));

        Assert.Equal(PaymentErrorCodes.PaymentStateConflict, result.ErrorCode);
    }

    [Fact]
    public void AmountMismatch_DoesNotCollect()
    {
        var result = Decide(Snapshot(attemptAmount: 999m));

        Assert.Equal(PaymentErrorCodes.PaymentStateConflict, result.ErrorCode);
    }

    private static CashOnDeliveryCompletionResult Decide(CashOnDeliveryCompletionSnapshot snapshot) =>
        new CashOnDeliveryCompletionService().Decide(snapshot);

    private static CashOnDeliveryCompletionSnapshot Snapshot(
        PaymentMethod method = PaymentMethod.CashOnDelivery,
        PaymentAttemptStatus attemptStatus = PaymentAttemptStatus.AwaitingPayment,
        decimal attemptAmount = 1000m,
        OrderStatus orderStatus = OrderStatus.Confirmed,
        PaymentStatus orderPaymentStatus = PaymentStatus.AwaitingPayment,
        decimal orderPaidAmount = 0m,
        decimal orderGrandTotal = 1000m,
        FulfillmentStatus fulfillmentStatus = FulfillmentStatus.Delivered) =>
        new(
            PaymentAttemptId: 42,
            method,
            attemptStatus,
            attemptAmount,
            OrderId: 7,
            orderStatus,
            orderPaymentStatus,
            orderPaidAmount,
            orderGrandTotal,
            fulfillmentStatus);
}
