using DoSelect.Domain.Orders;

namespace DoSelect.Domain.Payments;

/// <summary>
/// 付款相關的錯誤碼，值必須與 API錯誤碼目錄 一致。
/// </summary>
public static class PaymentErrorCodes
{
    public const string PaymentMethodNotAllowed = "payment_method_not_allowed";
    public const string PaymentStateConflict = "payment_state_conflict";
    public const string PaymentAttemptExpired = "payment_attempt_expired";
    public const string PaymentCodAmountExceeded = "payment_cod_amount_exceeded";
    public const string PaymentCodRestrictedItem = "payment_cod_restricted_item";
    public const string PaymentEventDuplicate = "payment_event_duplicate";
    public const string OrderPaymentDeadlineExpired = "order_payment_deadline_expired";
    public const string IdempotencyPayloadConflict = "idempotency_payload_conflict";
    public const string ConcurrencyConflict = "concurrency_conflict";
    public const string ResourceNotFound = "resource_not_found";
}

/// <summary>
/// 七類模擬付款的結算類型。決定保留期限與是否等待線上付款。
/// </summary>
public enum PaymentSettlementKind
{
    /// <summary>即時付款：信用卡、LINE Pay、Apple Pay、Google Pay。</summary>
    Realtime,

    /// <summary>延遲付款：ATM 虛擬帳號、超商繳費代碼。</summary>
    Deferred,

    /// <summary>貨到付款：交付或取貨時完成付款。</summary>
    CashOnDelivery,
}

public static class PaymentMethodPolicy
{
    /// <summary>即時付款的保留期限。</summary>
    public static readonly TimeSpan RealtimeInstructionLifetime = TimeSpan.FromMinutes(15);

    /// <summary>ATM 與超商代碼的保留期限。</summary>
    public static readonly TimeSpan DeferredInstructionLifetime = TimeSpan.FromDays(3);

    /// <summary>貨到付款的最終應付金額上限。</summary>
    public const decimal CashOnDeliveryMaximumAmount = 20000m;

    public static PaymentSettlementKind KindOf(PaymentMethod method) => method switch
    {
        PaymentMethod.CreditCard or PaymentMethod.LinePay or
            PaymentMethod.ApplePay or PaymentMethod.GooglePay => PaymentSettlementKind.Realtime,
        PaymentMethod.ATM or PaymentMethod.ConvenienceCode => PaymentSettlementKind.Deferred,
        PaymentMethod.CashOnDelivery => PaymentSettlementKind.CashOnDelivery,
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };

    /// <summary>
    /// 這次付款嘗試的保留期限。貨到付款不等待線上付款期限，回傳 <c>null</c>。
    /// 保留期限不得晚於訂單原付款期限，因此兩者取較早者：不能給顧客一個比訂單本身還長的付款視窗。
    /// </summary>
    public static DateTime? ResolveInstructionExpiry(
        PaymentMethod method,
        DateTime createdAtUtc,
        DateTime? orderPaymentDueAtUtc)
    {
        RequireUtc(createdAtUtc, nameof(createdAtUtc));
        if (orderPaymentDueAtUtc.HasValue)
        {
            RequireUtc(orderPaymentDueAtUtc.Value, nameof(orderPaymentDueAtUtc));
        }

        var lifetime = KindOf(method) switch
        {
            PaymentSettlementKind.Realtime => RealtimeInstructionLifetime,
            PaymentSettlementKind.Deferred => DeferredInstructionLifetime,
            _ => (TimeSpan?)null,
        };

        if (lifetime is not { } window)
        {
            return null;
        }

        var expiry = createdAtUtc + window;
        return orderPaymentDueAtUtc is { } due && due < expiry ? due : expiry;
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The value must use UTC.", parameterName);
        }
    }
}

/// <summary>
/// 貨到付款資格的輸入。配送能力、組裝與 SKU 預付旗標由配送與型錄模組提供，本層只做判斷。
/// </summary>
/// <remarks>
/// 刻意不帶金額。貨到付款上限與付款嘗試金額必須是同一份後端數字，
/// 分成兩個欄位就可能出現「資格用一個金額、實際建立用另一個」的矛盾。
/// </remarks>
public sealed record CashOnDeliveryEligibility(
    bool ShippingMethodAllowsCashOnDelivery,
    bool ContainsAssemblyBuild,
    bool ContainsPrepaymentOnlySku);

/// <summary>
/// 一張訂單在建立新付款嘗試當下的付款狀態。由呼叫端於同一交易內查得後傳入。
/// </summary>
/// <remarks>
/// <paramref name="PayableAmount"/> 是後端訂單的最終應付金額，也是唯一可信來源。
/// 呼叫端不得指定金額：正式契約要求
/// <c>Order.GrandTotal = PaymentAttempt.Amount = Order.PaidAmount = Invoice.IssuedAmount</c>。
/// </remarks>
public sealed record OrderPaymentContext(
    OrderStatus OrderStatus,
    decimal PayableAmount,
    bool IsPaid,
    PaymentAttemptStatus? LatestAttemptStatus,
    DateTime? PaymentDueAtUtc,
    CashOnDeliveryEligibility CashOnDelivery);

/// <summary>
/// 建立付款嘗試的請求。刻意沒有金額欄位 —— 金額一律取自
/// <see cref="OrderPaymentContext.PayableAmount"/>。
/// </summary>
public sealed record PaymentAttemptRequest(
    PaymentMethod Method,
    DateTime RequestedAtUtc);

public static class PaymentAttemptPolicy
{
    /// <summary>
    /// 終態的付款嘗試不再變動，也不阻擋新的嘗試。
    /// </summary>
    public static bool IsTerminal(PaymentAttemptStatus status) => status is
        PaymentAttemptStatus.Paid or
        PaymentAttemptStatus.Failed or
        PaymentAttemptStatus.Expired or
        PaymentAttemptStatus.Cancelled;

    /// <summary>
    /// 能不能為這張訂單建立新的付款嘗試。可以時回傳 <c>null</c>，否則回傳錯誤碼。
    /// 付款失敗或顧客取消不會取消訂單，只要訂單付款期限未到就能再建立一筆。
    /// </summary>
    public static string? FindStartRejection(
        OrderPaymentContext context,
        PaymentAttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.CashOnDelivery);
        ArgumentNullException.ThrowIfNull(request);

        if (context.PayableAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context));
        }

        if (request.RequestedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The value must use UTC.", nameof(request));
        }

        // 訂單狀態先於付款旗標判斷。已取消的訂單 IsPaid 也是 false，
        // 只看付款旗標會讓它通過檢查並建立新的付款嘗試。
        if (!IsPayable(context.OrderStatus))
        {
            return PaymentErrorCodes.PaymentStateConflict;
        }

        if (context.IsPaid)
        {
            return PaymentErrorCodes.PaymentStateConflict;
        }

        // 一次只允許一筆進行中的嘗試；重試前必須讓前一筆走到終態。
        if (context.LatestAttemptStatus is { } latest && !IsTerminal(latest))
        {
            return PaymentErrorCodes.PaymentStateConflict;
        }

        var isCashOnDelivery = PaymentMethodPolicy.KindOf(request.Method) ==
            PaymentSettlementKind.CashOnDelivery;

        // 貨到付款不等待線上付款期限。
        if (!isCashOnDelivery &&
            context.PaymentDueAtUtc is { } due &&
            request.RequestedAtUtc >= due)
        {
            return PaymentErrorCodes.OrderPaymentDeadlineExpired;
        }

        return isCashOnDelivery
            ? FindCashOnDeliveryRejection(context.CashOnDelivery, context.PayableAmount)
            : null;
    }

    /// <summary>
    /// 哪些訂單狀態還能建立付款嘗試。已取消或已完成的訂單都不行。
    /// </summary>
    public static bool IsPayable(OrderStatus orderStatus) => orderStatus switch
    {
        OrderStatus.PendingPayment => true,

        // 貨到付款的訂單先確認再收款，因此這兩個狀態仍可能建立嘗試。
        OrderStatus.Confirmed => true,
        OrderStatus.Processing => true,

        OrderStatus.Completed => false,
        OrderStatus.Cancelled => false,

        _ => throw new ArgumentOutOfRangeException(nameof(orderStatus)),
    };

    /// <summary>
    /// 貨到付款資格：配送方式必須支援，訂單不得含組裝電腦或任一預付限定 SKU，
    /// 且折扣後含運費的最終應付金額不得超過上限。
    /// </summary>
    public static string? FindCashOnDeliveryRejection(
        CashOnDeliveryEligibility eligibility,
        decimal payableAmount)
    {
        ArgumentNullException.ThrowIfNull(eligibility);

        if (!eligibility.ShippingMethodAllowsCashOnDelivery)
        {
            return PaymentErrorCodes.PaymentMethodNotAllowed;
        }

        if (eligibility.ContainsAssemblyBuild || eligibility.ContainsPrepaymentOnlySku)
        {
            return PaymentErrorCodes.PaymentCodRestrictedItem;
        }

        // 上限比對的金額與實際建立的付款嘗試金額是同一個，不會互相矛盾。
        return payableAmount > PaymentMethodPolicy.CashOnDeliveryMaximumAmount
            ? PaymentErrorCodes.PaymentCodAmountExceeded
            : null;
    }
}
