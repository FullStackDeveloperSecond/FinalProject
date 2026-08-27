using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;

namespace DoSelect.Domain.Tests;

public sealed class PaymentPolicyTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(PaymentMethod.CreditCard, PaymentSettlementKind.Realtime)]
    [InlineData(PaymentMethod.LinePay, PaymentSettlementKind.Realtime)]
    [InlineData(PaymentMethod.ApplePay, PaymentSettlementKind.Realtime)]
    [InlineData(PaymentMethod.GooglePay, PaymentSettlementKind.Realtime)]
    [InlineData(PaymentMethod.ATM, PaymentSettlementKind.Deferred)]
    [InlineData(PaymentMethod.ConvenienceCode, PaymentSettlementKind.Deferred)]
    [InlineData(PaymentMethod.CashOnDelivery, PaymentSettlementKind.CashOnDelivery)]
    public void KindOf_ClassifiesAllSevenSimulatedMethods(
        PaymentMethod method,
        PaymentSettlementKind expected) =>
        Assert.Equal(expected, PaymentMethodPolicy.KindOf(method));

    [Fact]
    public void KindOf_CoversEveryDeclaredPaymentMethod()
    {
        var methods = Enum.GetValues<PaymentMethod>();

        Assert.Equal(7, methods.Length);
        Assert.All(methods, method => PaymentMethodPolicy.KindOf(method));
    }

    [Fact]
    public void RealtimeMethods_ExpireInFifteenMinutes()
    {
        var expiry = PaymentMethodPolicy.ResolveInstructionExpiry(
            PaymentMethod.CreditCard, NowUtc, orderPaymentDueAtUtc: null);

        Assert.Equal(NowUtc.AddMinutes(15), expiry);
    }

    [Theory]
    [InlineData(PaymentMethod.ATM)]
    [InlineData(PaymentMethod.ConvenienceCode)]
    public void DeferredMethods_ExpireInThreeDays(PaymentMethod method)
    {
        var expiry = PaymentMethodPolicy.ResolveInstructionExpiry(
            method, NowUtc, orderPaymentDueAtUtc: null);

        Assert.Equal(NowUtc.AddDays(3), expiry);
    }

    [Fact]
    public void CashOnDelivery_HasNoOnlinePaymentWindow()
    {
        var expiry = PaymentMethodPolicy.ResolveInstructionExpiry(
            PaymentMethod.CashOnDelivery, NowUtc, NowUtc.AddDays(3));

        Assert.Null(expiry);
    }

    [Fact]
    public void InstructionExpiry_NeverOutlivesTheOrderPaymentDeadline()
    {
        var due = NowUtc.AddMinutes(5);

        var expiry = PaymentMethodPolicy.ResolveInstructionExpiry(
            PaymentMethod.ATM, NowUtc, due);

        Assert.Equal(due, expiry);
    }

    [Fact]
    public void InstructionExpiry_RejectsNonUtcInput() =>
        Assert.Throws<ArgumentException>(() => PaymentMethodPolicy.ResolveInstructionExpiry(
            PaymentMethod.CreditCard,
            new DateTime(2026, 8, 19, 2, 0, 0, DateTimeKind.Local),
            null));

    [Theory]
    [InlineData(PaymentAttemptStatus.Paid, true)]
    [InlineData(PaymentAttemptStatus.Failed, true)]
    [InlineData(PaymentAttemptStatus.Expired, true)]
    [InlineData(PaymentAttemptStatus.Cancelled, true)]
    [InlineData(PaymentAttemptStatus.Pending, false)]
    [InlineData(PaymentAttemptStatus.AwaitingPayment, false)]
    [InlineData(PaymentAttemptStatus.Processing, false)]
    public void IsTerminal_MatchesTheDocumentedStateMachine(
        PaymentAttemptStatus status,
        bool expected) =>
        Assert.Equal(expected, PaymentAttemptPolicy.IsTerminal(status));

    [Fact]
    public void FirstAttempt_OnAPayableOrder_IsAllowed() =>
        Assert.Null(PaymentAttemptPolicy.FindStartRejection(
            Context(latestAttemptStatus: null),
            Request(PaymentMethod.CreditCard)));

    [Theory]
    [InlineData(PaymentAttemptStatus.Failed)]
    [InlineData(PaymentAttemptStatus.Cancelled)]
    [InlineData(PaymentAttemptStatus.Expired)]
    public void RetryAfterATerminalAttempt_IsAllowedWithinTheOrderDeadline(
        PaymentAttemptStatus latest) =>
        Assert.Null(PaymentAttemptPolicy.FindStartRejection(
            Context(latestAttemptStatus: latest),
            Request(PaymentMethod.CreditCard)));

    [Theory]
    [InlineData(PaymentAttemptStatus.Pending)]
    [InlineData(PaymentAttemptStatus.AwaitingPayment)]
    [InlineData(PaymentAttemptStatus.Processing)]
    public void RetryWhileAnAttemptIsStillInFlight_Conflicts(PaymentAttemptStatus latest) =>
        Assert.Equal(
            PaymentErrorCodes.PaymentStateConflict,
            PaymentAttemptPolicy.FindStartRejection(
                Context(latestAttemptStatus: latest),
                Request(PaymentMethod.CreditCard)));

    [Fact]
    public void AlreadyPaidOrder_Conflicts() =>
        Assert.Equal(
            PaymentErrorCodes.PaymentStateConflict,
            PaymentAttemptPolicy.FindStartRejection(
                Context(latestAttemptStatus: PaymentAttemptStatus.Paid, isPaid: true),
                Request(PaymentMethod.CreditCard)));

    [Fact]
    public void AttemptAfterTheOrderDeadline_IsRejected() =>
        Assert.Equal(
            PaymentErrorCodes.OrderPaymentDeadlineExpired,
            PaymentAttemptPolicy.FindStartRejection(
                Context(latestAttemptStatus: null, paymentDueAtUtc: NowUtc),
                Request(PaymentMethod.CreditCard)));

    [Fact]
    public void CashOnDelivery_IgnoresTheOnlinePaymentDeadline() =>
        Assert.Null(PaymentAttemptPolicy.FindStartRejection(
            Context(
                latestAttemptStatus: null,
                orderStatus: OrderStatus.Confirmed,
                paymentDueAtUtc: NowUtc.AddDays(-1)),
            Request(PaymentMethod.CashOnDelivery)));

    [Fact]
    public void CashOnDelivery_RequiresAShippingMethodThatSupportsIt() =>
        Assert.Equal(
            PaymentErrorCodes.PaymentMethodNotAllowed,
            PaymentAttemptPolicy.FindCashOnDeliveryRejection(
                Eligibility(shippingMethodAllowsCashOnDelivery: false), 1000m));

    [Fact]
    public void CashOnDelivery_RejectsAssemblyBuilds() =>
        Assert.Equal(
            PaymentErrorCodes.PaymentCodRestrictedItem,
            PaymentAttemptPolicy.FindCashOnDeliveryRejection(
                Eligibility(containsAssemblyBuild: true), 1000m));

    [Fact]
    public void CashOnDelivery_RejectsPrepaymentOnlySkus() =>
        Assert.Equal(
            PaymentErrorCodes.PaymentCodRestrictedItem,
            PaymentAttemptPolicy.FindCashOnDeliveryRejection(
                Eligibility(containsPrepaymentOnlySku: true), 1000m));

    [Fact]
    public void CashOnDelivery_AcceptsExactlyTheAmountCeiling()
    {
        Assert.Null(PaymentAttemptPolicy.FindCashOnDeliveryRejection(Eligibility(), 20000m));
        Assert.Equal(
            PaymentErrorCodes.PaymentCodAmountExceeded,
            PaymentAttemptPolicy.FindCashOnDeliveryRejection(Eligibility(), 20000.01m));
    }

    [Fact]
    public void RestrictedItem_WinsOverTheAmountCeiling() =>
        Assert.Equal(
            PaymentErrorCodes.PaymentCodRestrictedItem,
            PaymentAttemptPolicy.FindCashOnDeliveryRejection(
                Eligibility(containsAssemblyBuild: true), 99999m));

    [Fact]
    public void TheCodCeilingUsesTheSameAmountTheAttemptWouldBeCreatedWith()
    {
        // 上限比對與實際建立金額必須是同一個數字。分成兩個來源時，
        // 資格檢查可能用低金額通過、實際卻建立高金額的付款嘗試。
        var context = Context(
            latestAttemptStatus: null,
            orderStatus: OrderStatus.Confirmed,
            payableAmount: 20000.01m);

        Assert.Equal(
            PaymentErrorCodes.PaymentCodAmountExceeded,
            PaymentAttemptPolicy.FindStartRejection(
                context, Request(PaymentMethod.CashOnDelivery)));
    }

    // 線上付款只在 PendingPayment；COD 只在 Confirmed。兩個維度必須一起判斷。
    [Theory]
    [InlineData(PaymentMethod.CreditCard, OrderStatus.PendingPayment, true)]
    [InlineData(PaymentMethod.CreditCard, OrderStatus.Confirmed, false)]
    [InlineData(PaymentMethod.CreditCard, OrderStatus.Processing, false)]
    [InlineData(PaymentMethod.CreditCard, OrderStatus.Completed, false)]
    [InlineData(PaymentMethod.CreditCard, OrderStatus.Cancelled, false)]
    [InlineData(PaymentMethod.LinePay, OrderStatus.PendingPayment, true)]
    [InlineData(PaymentMethod.LinePay, OrderStatus.Confirmed, false)]
    [InlineData(PaymentMethod.ApplePay, OrderStatus.PendingPayment, true)]
    [InlineData(PaymentMethod.ApplePay, OrderStatus.Confirmed, false)]
    [InlineData(PaymentMethod.GooglePay, OrderStatus.PendingPayment, true)]
    [InlineData(PaymentMethod.GooglePay, OrderStatus.Confirmed, false)]
    [InlineData(PaymentMethod.ATM, OrderStatus.PendingPayment, true)]
    [InlineData(PaymentMethod.ATM, OrderStatus.Confirmed, false)]
    [InlineData(PaymentMethod.ATM, OrderStatus.Processing, false)]
    [InlineData(PaymentMethod.ConvenienceCode, OrderStatus.PendingPayment, true)]
    [InlineData(PaymentMethod.ConvenienceCode, OrderStatus.Confirmed, false)]
    [InlineData(PaymentMethod.CashOnDelivery, OrderStatus.PendingPayment, false)]
    [InlineData(PaymentMethod.CashOnDelivery, OrderStatus.Confirmed, true)]
    [InlineData(PaymentMethod.CashOnDelivery, OrderStatus.Processing, false)]
    [InlineData(PaymentMethod.CashOnDelivery, OrderStatus.Completed, false)]
    [InlineData(PaymentMethod.CashOnDelivery, OrderStatus.Cancelled, false)]
    public void ThePaymentMethodAndOrderStatusMatrixIsExplicit(
        PaymentMethod method,
        OrderStatus orderStatus,
        bool expected) =>
        Assert.Equal(expected, PaymentAttemptPolicy.IsPayable(method, orderStatus));

    [Fact]
    public void EveryMethodAndStatusCombinationHasARuling()
    {
        // 新增付款方式或訂單狀態時必須同時裁定，不能靜默落到預設值。
        foreach (var method in Enum.GetValues<PaymentMethod>())
        {
            foreach (var orderStatus in Enum.GetValues<OrderStatus>())
            {
                PaymentAttemptPolicy.IsPayable(method, orderStatus);
            }
        }
    }

    [Fact]
    public void ACodOrderCannotStartAnOnlinePaymentAttempt()
    {
        // COD 訂單進入 Confirmed 後，不得再建立信用卡或 ATM 等線上付款嘗試。
        var context = Context(latestAttemptStatus: null, orderStatus: OrderStatus.Confirmed);

        Assert.Equal(
            PaymentErrorCodes.PaymentStateConflict,
            PaymentAttemptPolicy.FindStartRejection(context, Request(PaymentMethod.CreditCard)));
        Assert.Equal(
            PaymentErrorCodes.PaymentStateConflict,
            PaymentAttemptPolicy.FindStartRejection(context, Request(PaymentMethod.ATM)));
    }

    [Fact]
    public void APendingPaymentOrderCannotStartACashOnDeliveryAttempt()
    {
        // COD 在建立訂單時就產生付款紀錄並讓訂單進入 Confirmed，
        // 因此沒有「PendingPayment 的訂單再補一筆 COD」這種流程。
        Assert.Equal(
            PaymentErrorCodes.PaymentStateConflict,
            PaymentAttemptPolicy.FindStartRejection(
                Context(latestAttemptStatus: null, orderStatus: OrderStatus.PendingPayment),
                Request(PaymentMethod.CashOnDelivery)));
    }

    [Fact]
    public void ACancelledOrderCannotStartAPaymentAttemptEvenThoughItIsNotPaid()
    {
        // 已取消的訂單 IsPaid 也是 false。只看付款旗標會讓它通過檢查。
        var context = Context(latestAttemptStatus: null, orderStatus: OrderStatus.Cancelled);

        Assert.False(context.IsPaid);
        Assert.Equal(
            PaymentErrorCodes.PaymentStateConflict,
            PaymentAttemptPolicy.FindStartRejection(context, Request(PaymentMethod.CreditCard)));
    }

    [Fact]
    public void ACompletedOrderCannotStartAPaymentAttempt() =>
        Assert.Equal(
            PaymentErrorCodes.PaymentStateConflict,
            PaymentAttemptPolicy.FindStartRejection(
                Context(latestAttemptStatus: null, orderStatus: OrderStatus.Completed),
                Request(PaymentMethod.CreditCard)));

    [Fact]
    public void FindStartRejection_RejectsNonPositiveAmounts() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentAttemptPolicy.FindStartRejection(
            Context(latestAttemptStatus: null, payableAmount: 0m),
            new PaymentAttemptRequest(PaymentMethod.CreditCard, NowUtc)));

    [Fact]
    public void FindStartRejection_RejectsNonUtcRequestTime() =>
        Assert.Throws<ArgumentException>(() => PaymentAttemptPolicy.FindStartRejection(
            Context(latestAttemptStatus: null),
            new PaymentAttemptRequest(
                PaymentMethod.CreditCard,
                new DateTime(2026, 8, 19, 2, 0, 0, DateTimeKind.Local))));

    private static OrderPaymentContext Context(
        PaymentAttemptStatus? latestAttemptStatus,
        bool isPaid = false,
        DateTime? paymentDueAtUtc = null,
        OrderStatus orderStatus = OrderStatus.PendingPayment,
        decimal payableAmount = 1000m) =>
        new(
            orderStatus,
            payableAmount,
            isPaid,
            latestAttemptStatus,
            paymentDueAtUtc ?? NowUtc.AddMinutes(30),
            Eligibility());

    private static PaymentAttemptRequest Request(PaymentMethod method) =>
        new(method, NowUtc);

    private static CashOnDeliveryEligibility Eligibility(
        bool shippingMethodAllowsCashOnDelivery = true,
        bool containsAssemblyBuild = false,
        bool containsPrepaymentOnlySku = false) =>
        new(
            shippingMethodAllowsCashOnDelivery,
            containsAssemblyBuild,
            containsPrepaymentOnlySku);
}
