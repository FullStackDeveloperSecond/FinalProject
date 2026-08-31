using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;

namespace DoSelect.Application.Payments;

/// <summary>
/// 決定一筆模擬付款可不可以完成，以及要走哪些狀態轉換。本服務<b>只做決策</b>，不寫資料庫。
/// </summary>
/// <remarks>
/// <para>
/// 規則集中在這裡，所以不必啟動 HTTP、也不必連資料庫就測得到。Writer 只負責
/// 在同一交易內重新讀一次快照、套用計畫、寫稽核。
/// </para>
/// <para>
/// <b>冪等不由本服務負責。</b>重播與 Payload 衝突屬於外層共用的
/// <c>IIdempotencyExecutor</c>，走到這裡的一定是首次執行。
/// </para>
/// <para>
/// <b>金額只有一個來源。</b>呼叫端不能指定金額；成功時寫回訂單的實付金額就是
/// 付款嘗試的金額，而付款嘗試的金額必須等於訂單總額 —— 這是 alex 在 Issue #65 C1
/// 要求釘住的 <c>Order.GrandTotal = PaymentAttempt.Amount = Order.PaidAmount</c>。
/// </para>
/// </remarks>
public sealed class CompleteSimulatedPaymentService
{
    /// <summary>
    /// 還可以被模擬完成的付款嘗試狀態。
    /// </summary>
    /// <remarks>
    /// <c>Paid</c>／<c>Failed</c>／<c>Expired</c>／<c>Cancelled</c> 都是終態，
    /// 再送一次不是重播（重播由冪等鍵判斷），而是對已經結束的付款動手。
    /// </remarks>
    private static readonly PaymentAttemptStatus[] CompletableStatuses =
        [PaymentAttemptStatus.AwaitingPayment, PaymentAttemptStatus.Processing];

    public CompleteSimulatedPaymentResult Decide(
        SimulatedPaymentSnapshot snapshot,
        SimulatedPaymentOutcome outcome,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!CompletableStatuses.Contains(snapshot.AttemptStatus))
        {
            return CompleteSimulatedPaymentResult.Failure(
                PaymentErrorCodes.PaymentStateConflict);
        }
        var kind = PaymentMethodPolicy.KindOf(snapshot.Method);
        // 貨到付款不是「使用者模擬付款」：只能由物流流程在 Delivered／PickedUp
        // 時呼叫 CashOnDeliveryCompletionService。這裡一律拒絕，避免提早收款。
        if (kind == PaymentSettlementKind.CashOnDelivery)
        {
            return CompleteSimulatedPaymentResult.Failure(
                PaymentErrorCodes.PaymentStateConflict);
        }

        const OrderStatus expectedOrderStatus = OrderStatus.PendingPayment;
        if (snapshot.OrderStatus != expectedOrderStatus)
        {
            return CompleteSimulatedPaymentResult.Failure(
                PaymentErrorCodes.PaymentStateConflict);
        }

        if (snapshot.OrderPaymentStatus != PaymentStatus.AwaitingPayment ||
            snapshot.OrderPaidAmount != 0m)
        {
            return CompleteSimulatedPaymentResult.Failure(
                PaymentErrorCodes.PaymentStateConflict);
        }

        // 訂單已取消就不該再有任何付款結果寫回去 —— 那會讓一張取消的訂單變成已付款。
        if (snapshot.OrderStatus == OrderStatus.Cancelled)
        {
            return CompleteSimulatedPaymentResult.Failure(
                PaymentErrorCodes.PaymentStateConflict);
        }

        return outcome switch
        {
            SimulatedPaymentOutcome.Succeeded => DecideSucceeded(snapshot, nowUtc),
            SimulatedPaymentOutcome.Failed => DecideFailed(snapshot),
            SimulatedPaymentOutcome.Expired => DecideExpired(snapshot),
            SimulatedPaymentOutcome.Cancelled => DecideCancelled(snapshot),
            _ => CompleteSimulatedPaymentResult.Failure(PaymentErrorCodes.PaymentStateConflict),
        };
    }

    private static CompleteSimulatedPaymentResult DecideSucceeded(
        SimulatedPaymentSnapshot snapshot,
        DateTime nowUtc)
    {
        // 付款指示過期後不能再成功付款；這一條要比訂單期限先判，因為它比較窄。
        if (snapshot.InstructionExpiresAtUtc is { } instructionExpiry &&
            instructionExpiry <= nowUtc)
        {
            return CompleteSimulatedPaymentResult.Failure(
                PaymentErrorCodes.PaymentAttemptExpired);
        }

        if (snapshot.OrderPaymentDueAtUtc is { } orderDue && orderDue <= nowUtc)
        {
            return CompleteSimulatedPaymentResult.Failure(
                PaymentErrorCodes.OrderPaymentDeadlineExpired);
        }

        // 金額鏈的中間一環。付款嘗試的金額與訂單總額對不起來時，寫哪一個都是錯的：
        // 寫嘗試金額會讓 PaidAmount 與 GrandTotal 不符，寫訂單總額則等於無視實際收款。
        // 這是資料不一致，不是使用者做錯事，所以擋下來而不是自己挑一個。
        if (snapshot.AttemptAmount != snapshot.OrderGrandTotal)
        {
            return CompleteSimulatedPaymentResult.Failure(
                PaymentErrorCodes.PaymentStateConflict);
        }

        return CompleteSimulatedPaymentResult.Approved(new SimulatedPaymentPlan(
            snapshot.PaymentAttemptId,
            snapshot.OrderId,
            PathTo(snapshot.AttemptStatus, PaymentAttemptStatus.Paid),
            FailureCode: null,
            PaymentStatus.Paid,
            snapshot.AttemptAmount,
            PaymentMethodPolicy.KindOf(snapshot.Method) == PaymentSettlementKind.CashOnDelivery
                ? null
                : OrderStatus.Confirmed));
    }

    private static CompleteSimulatedPaymentResult DecideFailed(SimulatedPaymentSnapshot snapshot) =>
        CompleteSimulatedPaymentResult.Approved(new SimulatedPaymentPlan(
            snapshot.PaymentAttemptId,
            snapshot.OrderId,
            PathTo(snapshot.AttemptStatus, PaymentAttemptStatus.Failed),
            SimulatedPaymentWriteConstants.SimulatedFailureCode,
            PaymentStatus.Failed,
            // 失敗不改變已收金額。這一筆從來沒有收到過錢，寫 0 是還原而不是扣款。
            OrderPaidAmount: 0m,
            OrderStatusTransition: null));

    private static CompleteSimulatedPaymentResult DecideExpired(SimulatedPaymentSnapshot snapshot)
    {
        // 付款嘗試的狀態機只允許 AwaitingPayment → Expired。已經在 Processing 的
        // 付款正在等結果，只能成功或失敗 —— 讓它過期會在 Writer 裡炸成 500，
        // 所以在這裡就回 409。
        if (snapshot.AttemptStatus != PaymentAttemptStatus.AwaitingPayment)
        {
            return CompleteSimulatedPaymentResult.Failure(
                PaymentErrorCodes.PaymentStateConflict);
        }

        return CompleteSimulatedPaymentResult.Approved(new SimulatedPaymentPlan(
            snapshot.PaymentAttemptId,
            snapshot.OrderId,
            [PaymentAttemptStatus.Expired],
            FailureCode: null,
            PaymentStatus.Expired,
            OrderPaidAmount: 0m,
            OrderStatusTransition: null));
    }

    private static CompleteSimulatedPaymentResult DecideCancelled(SimulatedPaymentSnapshot snapshot)
    {
        if (snapshot.AttemptStatus != PaymentAttemptStatus.AwaitingPayment)
        {
            return CompleteSimulatedPaymentResult.Failure(
                PaymentErrorCodes.PaymentStateConflict);
        }

        return CompleteSimulatedPaymentResult.Approved(new SimulatedPaymentPlan(
            snapshot.PaymentAttemptId,
            snapshot.OrderId,
            [PaymentAttemptStatus.Cancelled],
            FailureCode: null,
            PaymentStatus.Cancelled,
            OrderPaidAmount: 0m,
            OrderStatusTransition: null));
    }

    /// <summary>
    /// 從目前狀態走到目標狀態要經過的每一步。
    /// </summary>
    /// <remarks>
    /// <c>AwaitingPayment</c> 到 <c>Paid</c>／<c>Failed</c> 中間一定要經過
    /// <c>Processing</c>（<see cref="PaymentAttempt"/> 的狀態機不允許跳過）。
    /// 已經在 <c>Processing</c> 的就只差最後一步。
    /// </remarks>
    private static IReadOnlyList<PaymentAttemptStatus> PathTo(
        PaymentAttemptStatus current,
        PaymentAttemptStatus target) =>
        current == PaymentAttemptStatus.Processing
            ? [target]
            : [PaymentAttemptStatus.Processing, target];
}
