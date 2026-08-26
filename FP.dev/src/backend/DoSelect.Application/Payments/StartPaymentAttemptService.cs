using DoSelect.Domain.Payments;

namespace DoSelect.Application.Payments;

/// <summary>
/// 既有付款嘗試的最小識別資料，用於 Idempotency-Key 重播比對。
/// </summary>
/// <remarks>
/// <paramref name="OrderRowVersion"/> 是**建立當下**呼叫端持有的訂單版本，
/// 屬於原始 Request 的一部分。重播比對必須用它，不能用目前的訂單快照。
/// </remarks>
public sealed record ExistingPaymentAttempt(
    Guid PublicId,
    long OrderId,
    PaymentMethod Method,
    decimal Amount,
    byte[] OrderRowVersion,
    PaymentAttemptStatus Status);

/// <summary>
/// 訂單的付款狀態快照。<paramref name="OrderId"/> 為內部識別，不得對外回傳。
/// </summary>
/// <remarks>
/// <paramref name="RowVersion"/> 是讀取當下的訂單版本，用來比對呼叫端持有的
/// <c>orderRowVersion</c>。<see cref="OrderPaymentContext.PayableAmount"/> 是唯一的
/// 可信金額來源；呼叫端不得指定金額。
/// </remarks>
public sealed record OrderPaymentSnapshot(
    long OrderId,
    byte[] RowVersion,
    OrderPaymentContext Context);

/// <summary>
/// 建立付款嘗試所需的讀取埠。實作屬於 Infrastructure，不在此層存取 DbContext。
/// </summary>
public interface IPaymentAttemptReader
{
    /// <summary>
    /// 以 Idempotency-Key 找既有嘗試。`UX_PaymentAttempts_IdempotencyKey` 為全域唯一，
    /// 因此不需要再以訂單縮小範圍。
    /// </summary>
    Task<ExistingPaymentAttempt?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<OrderPaymentSnapshot?> FindOrderPaymentSnapshotAsync(
        Guid orderPublicId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 建立付款嘗試的請求。依正式契約只帶 <paramref name="Method"/> 與
/// <paramref name="OrderRowVersion"/>，**沒有金額欄位** —— 金額由後端訂單決定。
/// </summary>
public sealed record StartPaymentAttemptRequest(
    Guid OrderPublicId,
    PaymentMethod Method,
    byte[] OrderRowVersion,
    string IdempotencyKey);

/// <summary>
/// 通過檢查後要建立的付款嘗試。實際寫入與同交易副作用由 Checkout／付款端點負責。
/// </summary>
/// <remarks>
/// <paramref name="ExpectedOrderRowVersion"/> 是本次決策所依據的訂單版本。
/// 從讀取快照到實際寫入之間，訂單仍可能被取消、改變狀態或金額，而預設隔離等級
/// 不會自動擋住這件事。Writer **必須**在同一交易內以條件式更新或再次比對這個版本，
/// 不得只依賴本服務讀取當下的比對結果。
/// </remarks>
public sealed record PaymentAttemptPlan(
    long OrderId,
    PaymentMethod Method,
    decimal Amount,
    string IdempotencyKey,
    PaymentSettlementKind SettlementKind,
    DateTime? InstructionExpiresAtUtc,
    byte[] ExpectedOrderRowVersion);

/// <summary>
/// 建立付款嘗試的決策結果。三種情形：拒絕、既有嘗試重播、通過並帶出建立計畫。
/// </summary>
public sealed class StartPaymentAttemptResult
{
    private StartPaymentAttemptResult(
        string? errorCode,
        Guid? existingAttemptPublicId,
        PaymentAttemptPlan? plan)
    {
        ErrorCode = errorCode;
        ExistingAttemptPublicId = existingAttemptPublicId;
        Plan = plan;
    }

    public bool IsSuccess => ErrorCode is null;

    public string? ErrorCode { get; }

    /// <summary>同一 Idempotency-Key 搭配相同 Payload 時，回傳既有嘗試而不建立第二筆。</summary>
    public bool IsReplay => IsSuccess && Plan is null;

    public Guid? ExistingAttemptPublicId { get; }

    /// <summary>重播時為 <c>null</c>。</summary>
    public PaymentAttemptPlan? Plan { get; }

    public static StartPaymentAttemptResult Failure(string errorCode) =>
        new(errorCode, null, null);

    public static StartPaymentAttemptResult Replay(Guid existingAttemptPublicId) =>
        new(null, existingAttemptPublicId, null);

    public static StartPaymentAttemptResult Approved(PaymentAttemptPlan plan) =>
        new(null, null, plan);
}

/// <summary>
/// 決定要不要為一張訂單建立新的付款嘗試。本服務只做決策與冪等比對，不寫資料庫，
/// 也不自建冪等表；冪等以 <c>PaymentAttempt.IdempotencyKey</c> 的唯一索引為準。
/// </summary>
public sealed class StartPaymentAttemptService
{
    private readonly IPaymentAttemptReader _attemptReader;
    private readonly TimeProvider _timeProvider;

    public StartPaymentAttemptService(
        IPaymentAttemptReader attemptReader,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(attemptReader);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _attemptReader = attemptReader;
        _timeProvider = timeProvider;
    }

    public async Task<StartPaymentAttemptResult> StartAsync(
        StartPaymentAttemptRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Idempotency-Key 為必填 Header，缺少時由 API 層以驗證錯誤擋下。
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException("The idempotency key is required.", nameof(request));
        }

        var idempotencyKey = request.IdempotencyKey.Trim();
        var snapshot = await _attemptReader.FindOrderPaymentSnapshotAsync(
            request.OrderPublicId,
            cancellationToken);

        if (snapshot is null)
        {
            return StartPaymentAttemptResult.Failure(PaymentErrorCodes.ResourceNotFound);
        }

        // 金額只有一個來源：後端訂單。
        var payableAmount = snapshot.Context.PayableAmount;

        // 冪等重播必須先於 RowVersion 閘門。
        // 第一次建立成功但回應遺失、之後訂單版本又改變時，呼叫端以原 Key 與原 Request
        // 重送應該拿回原本那筆，而不是 concurrency_conflict —— 那次建立已經發生了，
        // 版本閘門只適用於「首次建立」。
        var existing = await _attemptReader.FindByIdempotencyKeyAsync(
            idempotencyKey,
            cancellationToken);

        if (existing is not null)
        {
            return IsSamePayload(existing, snapshot.OrderId, request)
                ? StartPaymentAttemptResult.Replay(existing.PublicId)
                : StartPaymentAttemptResult.Failure(PaymentErrorCodes.IdempotencyPayloadConflict);
        }

        // 首次建立才檢查版本：呼叫端持有的版本與訂單目前版本不符，
        // 代表它看到的金額或狀態已經過期。
        if (!RowVersionMatches(request.OrderRowVersion, snapshot.RowVersion))
        {
            return StartPaymentAttemptResult.Failure(PaymentErrorCodes.ConcurrencyConflict);
        }

        var requestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var rejection = PaymentAttemptPolicy.FindStartRejection(
            snapshot.Context,
            new PaymentAttemptRequest(request.Method, requestedAtUtc));

        if (rejection is not null)
        {
            return StartPaymentAttemptResult.Failure(rejection);
        }

        return StartPaymentAttemptResult.Approved(new PaymentAttemptPlan(
            snapshot.OrderId,
            request.Method,
            payableAmount,
            idempotencyKey,
            PaymentMethodPolicy.KindOf(request.Method),
            PaymentMethodPolicy.ResolveInstructionExpiry(
                request.Method,
                requestedAtUtc,
                snapshot.Context.PaymentDueAtUtc),
            snapshot.RowVersion));
    }

    private static bool RowVersionMatches(byte[]? presented, byte[]? current) =>
        presented is not null &&
        current is not null &&
        presented.AsSpan().SequenceEqual(current);

    /// <summary>
    /// 是否為同一個命令。
    /// </summary>
    /// <remarks>
    /// 比對的是**建立當下保存的原始 Request**，不是目前的訂單快照。
    /// 用目前訂單金額代替原始 payload 會有兩個錯誤方向：訂單金額事後改變會讓真正的
    /// 重播被誤判成衝突；相同 Key 換上新的 <c>orderRowVersion</c> 也會因為目前金額
    /// 相同而被誤判成重播。
    /// </remarks>
    private static bool IsSamePayload(
        ExistingPaymentAttempt existing,
        long orderId,
        StartPaymentAttemptRequest request) =>
        existing.OrderId == orderId &&
        existing.Method == request.Method &&
        RowVersionMatches(request.OrderRowVersion, existing.OrderRowVersion);
}
