using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;

namespace DoSelect.Application.Payments;

/// <summary>
/// 物流流程把訂單推進到 Delivered／PickedUp 時，用來決定貨到付款是否可以收款。
/// 本服務只產生計畫；物流 writer 必須在自己的交易內套用計畫並送出模擬發票 Outbox。
/// </summary>
public sealed record CashOnDeliveryCompletionSnapshot(
    long PaymentAttemptId,
    PaymentMethod Method,
    PaymentAttemptStatus AttemptStatus,
    decimal AttemptAmount,
    long OrderId,
    OrderStatus OrderStatus,
    PaymentStatus OrderPaymentStatus,
    decimal OrderPaidAmount,
    decimal OrderGrandTotal,
    FulfillmentStatus FulfillmentStatus);

public sealed record CashOnDeliveryCompletionPlan(
    long PaymentAttemptId,
    long OrderId,
    IReadOnlyList<PaymentAttemptStatus> AttemptTransitions,
    PaymentStatus OrderPaymentStatus,
    decimal OrderPaidAmount,
    bool RequestSimulatedInvoice);

public sealed class CashOnDeliveryCompletionResult
{
    private CashOnDeliveryCompletionResult(
        string? errorCode,
        CashOnDeliveryCompletionPlan? plan)
    {
        ErrorCode = errorCode;
        Plan = plan;
    }

    public bool IsSuccess => ErrorCode is null;
    public string? ErrorCode { get; }
    public CashOnDeliveryCompletionPlan? Plan { get; }

    public static CashOnDeliveryCompletionResult Failure(string errorCode) =>
        new(errorCode, null);

    public static CashOnDeliveryCompletionResult Approved(CashOnDeliveryCompletionPlan plan) =>
        new(null, plan);
}

public sealed class CashOnDeliveryCompletionService
{
    public CashOnDeliveryCompletionResult Decide(CashOnDeliveryCompletionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (PaymentMethodPolicy.KindOf(snapshot.Method) != PaymentSettlementKind.CashOnDelivery ||
            snapshot.FulfillmentStatus is not (FulfillmentStatus.Delivered or FulfillmentStatus.PickedUp) ||
            snapshot.OrderStatus == OrderStatus.Cancelled ||
            snapshot.AttemptStatus is not (PaymentAttemptStatus.AwaitingPayment or PaymentAttemptStatus.Processing) ||
            snapshot.OrderPaymentStatus != PaymentStatus.AwaitingPayment ||
            snapshot.OrderPaidAmount != 0m ||
            snapshot.AttemptAmount != snapshot.OrderGrandTotal)
        {
            return CashOnDeliveryCompletionResult.Failure(
                PaymentErrorCodes.PaymentStateConflict);
        }

        return CashOnDeliveryCompletionResult.Approved(new CashOnDeliveryCompletionPlan(
            snapshot.PaymentAttemptId,
            snapshot.OrderId,
            snapshot.AttemptStatus == PaymentAttemptStatus.Processing
                ? [PaymentAttemptStatus.Paid]
                : [PaymentAttemptStatus.Processing, PaymentAttemptStatus.Paid],
            PaymentStatus.Paid,
            snapshot.AttemptAmount,
            RequestSimulatedInvoice: true));
    }
}
