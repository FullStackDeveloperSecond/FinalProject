using DoSelect.Application.Payments;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;

namespace DoSelect.Application.Tests;

/// <summary>
/// 模擬付款完成的決策規則。
/// </summary>
/// <remarks>
/// 規則放在 Application 層，所以這些測試不必連資料庫 —— 但也因此證明不了
/// 交易邊界與並行行為，那些由 <c>SimulatedPaymentWriterSqlServerTests</c> 負責。
/// </remarks>
public sealed class CompleteSimulatedPaymentServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Succeeded_PaysTheOrderWithTheAttemptAmount()
    {
        var result = Decide(SimulatedPaymentOutcome.Succeeded);

        var plan = result.Plan!;
        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Paid, plan.OrderPaymentStatus);

        // 金額鏈：訂單總額 = 付款嘗試金額 = 寫回訂單的實付金額。
        Assert.Equal(1000m, plan.OrderPaidAmount);
    }

    [Fact]
    public void Succeeded_GoesThroughProcessingBecauseTheStateMachineForbidsTheShortcut()
    {
        // PaymentAttempt 不允許 AwaitingPayment 直接跳到 Paid。計畫要自己帶出中繼步驟，
        // 否則 Writer 套用時會丟 InvalidOperationException，變成 500。
        var result = Decide(SimulatedPaymentOutcome.Succeeded);

        Assert.Equal(
            [PaymentAttemptStatus.Processing, PaymentAttemptStatus.Paid],
            result.Plan!.AttemptTransitions);
    }

    [Fact]
    public void Succeeded_FromProcessingOnlyNeedsTheLastStep()
    {
        var result = Decide(
            SimulatedPaymentOutcome.Succeeded,
            Snapshot(attemptStatus: PaymentAttemptStatus.Processing));

        Assert.Equal([PaymentAttemptStatus.Paid], result.Plan!.AttemptTransitions);
    }

    [Fact]
    public void Succeeded_IsRefusedWhenTheAttemptAmountDoesNotMatchTheOrderTotal()
    {
        // 金額對不起來時寫哪一個都是錯的，所以擋下來而不是自己挑一個。
        var result = Decide(
            SimulatedPaymentOutcome.Succeeded,
            Snapshot(attemptAmount: 999m, orderGrandTotal: 1000m));

        Assert.Equal(PaymentErrorCodes.PaymentStateConflict, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void Succeeded_IsRefusedAfterThePaymentInstructionExpired()
    {
        var result = Decide(
            SimulatedPaymentOutcome.Succeeded,
            Snapshot(instructionExpiresAtUtc: NowUtc.AddSeconds(-1)));

        Assert.Equal(PaymentErrorCodes.PaymentAttemptExpired, result.ErrorCode);
    }

    [Fact]
    public void Succeeded_TreatsTheExpiryInstantItselfAsExpired()
    {
        // 邊界要往「已過期」倒。剛好等於期限還放行，等於期限比宣告的多一瞬間。
        var result = Decide(
            SimulatedPaymentOutcome.Succeeded,
            Snapshot(instructionExpiresAtUtc: NowUtc));

        Assert.Equal(PaymentErrorCodes.PaymentAttemptExpired, result.ErrorCode);
    }

    [Fact]
    public void Succeeded_IsAllowedOneTickBeforeTheExpiry()
    {
        // 對照組。少了它，上面那條在「永遠回 expired」的實作下也會過。
        var result = Decide(
            SimulatedPaymentOutcome.Succeeded,
            Snapshot(instructionExpiresAtUtc: NowUtc.AddTicks(1)));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Succeeded_IsRefusedAfterTheOrderPaymentDeadline()
    {
        var result = Decide(
            SimulatedPaymentOutcome.Succeeded,
            Snapshot(
                instructionExpiresAtUtc: NowUtc.AddHours(1),
                orderPaymentDueAtUtc: NowUtc.AddSeconds(-1)));

        Assert.Equal(PaymentErrorCodes.OrderPaymentDeadlineExpired, result.ErrorCode);
    }

    [Fact]
    public void Succeeded_ReportsTheAttemptExpiryWhenBothDeadlinesPassed()
    {
        // 兩個期限都過時回比較窄的那一個：付款指示是這一筆嘗試的問題，
        // 訂單期限則是整張訂單的問題，先講前者才對得上使用者要做的事。
        var result = Decide(
            SimulatedPaymentOutcome.Succeeded,
            Snapshot(
                instructionExpiresAtUtc: NowUtc.AddSeconds(-1),
                orderPaymentDueAtUtc: NowUtc.AddSeconds(-1)));

        Assert.Equal(PaymentErrorCodes.PaymentAttemptExpired, result.ErrorCode);
    }

    [Theory]
    [InlineData(PaymentAttemptStatus.Paid)]
    [InlineData(PaymentAttemptStatus.Failed)]
    [InlineData(PaymentAttemptStatus.Expired)]
    [InlineData(PaymentAttemptStatus.Cancelled)]
    [InlineData(PaymentAttemptStatus.Pending)]
    public void ATerminalOrUnstartedAttemptCannotBeCompleted(PaymentAttemptStatus status)
    {
        // Pending 也不行：付款指示還沒發出去，沒有東西可以模擬完成。
        var result = Decide(
            SimulatedPaymentOutcome.Succeeded,
            Snapshot(attemptStatus: status));

        Assert.Equal(PaymentErrorCodes.PaymentStateConflict, result.ErrorCode);
    }

    [Theory]
    [InlineData(SimulatedPaymentOutcome.Succeeded)]
    [InlineData(SimulatedPaymentOutcome.Failed)]
    [InlineData(SimulatedPaymentOutcome.Expired)]
    public void ACancelledOrderRefusesEveryOutcome(SimulatedPaymentOutcome outcome)
    {
        // 取消的訂單不該再有任何付款結果寫回去。
        var result = Decide(outcome, Snapshot(orderStatus: OrderStatus.Cancelled));

        Assert.Equal(PaymentErrorCodes.PaymentStateConflict, result.ErrorCode);
    }

    [Fact]
    public void Failed_MarksTheOrderFailedWithoutTouchingTheAmount()
    {
        var result = Decide(SimulatedPaymentOutcome.Failed);

        var plan = result.Plan!;
        Assert.Equal(PaymentStatus.Failed, plan.OrderPaymentStatus);
        Assert.Equal(0m, plan.OrderPaidAmount);
        Assert.Equal(
            [PaymentAttemptStatus.Processing, PaymentAttemptStatus.Failed],
            plan.AttemptTransitions);
    }

    [Fact]
    public void Failed_CarriesAFailureCodeBecauseTheStateMachineRequiresOne()
    {
        // PaymentAttempt.Transition 對 Failed 一定要 failureCode，留白會丟例外。
        var result = Decide(SimulatedPaymentOutcome.Failed);

        Assert.False(string.IsNullOrWhiteSpace(result.Plan!.FailureCode));
    }

    [Fact]
    public void Failed_IsStillAllowedAfterTheDeadlinesPassed()
    {
        // 過期的付款嘗試標成失敗是合理的收尾，不該被期限擋住 ——
        // 期限只擋「還能不能付款成功」。
        var result = Decide(
            SimulatedPaymentOutcome.Failed,
            Snapshot(
                instructionExpiresAtUtc: NowUtc.AddSeconds(-1),
                orderPaymentDueAtUtc: NowUtc.AddSeconds(-1)));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Expired_MarksBothTheAttemptAndTheOrderExpired()
    {
        var result = Decide(SimulatedPaymentOutcome.Expired);

        var plan = result.Plan!;
        Assert.Equal([PaymentAttemptStatus.Expired], plan.AttemptTransitions);
        Assert.Equal(PaymentStatus.Expired, plan.OrderPaymentStatus);
        Assert.Equal(0m, plan.OrderPaidAmount);
    }

    [Fact]
    public void Expired_IsRefusedForAnAttemptAlreadyProcessing()
    {
        // 狀態機只允許 AwaitingPayment → Expired。放行會在 Writer 裡炸成 500，
        // 所以在決策層就回 409。
        var result = Decide(
            SimulatedPaymentOutcome.Expired,
            Snapshot(attemptStatus: PaymentAttemptStatus.Processing));

        Assert.Equal(PaymentErrorCodes.PaymentStateConflict, result.ErrorCode);
    }

    [Fact]
    public void EveryApprovedPlanCarriesTheInternalKeysTheWriterNeeds()
    {
        var result = Decide(SimulatedPaymentOutcome.Succeeded);

        Assert.Equal(42L, result.Plan!.PaymentAttemptId);
        Assert.Equal(7L, result.Plan.OrderId);
    }

    private static CompleteSimulatedPaymentResult Decide(
        SimulatedPaymentOutcome outcome,
        SimulatedPaymentSnapshot? snapshot = null) =>
        new CompleteSimulatedPaymentService().Decide(snapshot ?? Snapshot(), outcome, NowUtc);

    private static SimulatedPaymentSnapshot Snapshot(
        PaymentAttemptStatus attemptStatus = PaymentAttemptStatus.AwaitingPayment,
        decimal attemptAmount = 1000m,
        DateTime? instructionExpiresAtUtc = null,
        OrderStatus orderStatus = OrderStatus.Confirmed,
        PaymentStatus orderPaymentStatus = PaymentStatus.AwaitingPayment,
        decimal orderGrandTotal = 1000m,
        DateTime? orderPaymentDueAtUtc = null) =>
        new(
            42L,
            attemptStatus,
            attemptAmount,
            instructionExpiresAtUtc,
            7L,
            orderStatus,
            orderPaymentStatus,
            orderGrandTotal,
            orderPaymentDueAtUtc);
}
