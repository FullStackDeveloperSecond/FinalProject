using DoSelect.Application.Idempotency;
using DoSelect.Application.Orders;
using DoSelect.Application.Support.Dtos;
using DoSelect.Domain.Payments;

namespace DoSelect.Application.Payments;

/// <summary>建立或重試付款嘗試的公開 Request；金額與訂單識別由 route／後端決定。</summary>
public sealed record CreatePaymentAttemptRequest(
    PaymentMethod Method,
    [RowVersionRequired] byte[] OrderRowVersion);

/// <summary>由受信任 API 邊界組合，不接受客戶端指定 Actor 或冪等作用域。</summary>
public sealed record CreatePaymentAttemptCommand(
    Guid OrderPublicId,
    PaymentMethod Method,
    byte[] OrderRowVersion,
    string IdempotencyKey,
    OrderActor Actor);

public interface IPaymentAttemptWriter
{
    Task<IdempotencyExecutionResult<PaymentAttemptDto>> CreateAsync(
        CreatePaymentAttemptCommand command,
        CancellationToken cancellationToken = default);
}

public static class PaymentAttemptWriteConstants
{
    public const string Operation = "payment-attempt.create";
}

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
/// 建立付款嘗試的決策結果：拒絕，或通過並帶出建立計畫。
/// </summary>
public sealed class StartPaymentAttemptResult
{
    private StartPaymentAttemptResult(string? errorCode, PaymentAttemptPlan? plan)
    {
        ErrorCode = errorCode;
        Plan = plan;
    }

    public bool IsSuccess => ErrorCode is null;

    public string? ErrorCode { get; }

    /// <summary>拒絕時為 <c>null</c>。</summary>
    public PaymentAttemptPlan? Plan { get; }

    public static StartPaymentAttemptResult Failure(string errorCode) => new(errorCode, null);

    public static StartPaymentAttemptResult Approved(PaymentAttemptPlan plan) => new(null, plan);
}

/// <summary>
/// 決定要不要為一張訂單建立新的付款嘗試。本服務只做決策，不寫資料庫。
/// </summary>
/// <remarks>
/// **冪等不由本服務負責。** 重播與 Payload 衝突判斷屬於呼叫端外層的共用
/// <c>IIdempotencyExecutor</c>，Request Hash 至少涵蓋
/// <c>orderPublicId + method + orderRowVersion</c>。
/// <para>
/// 本服務曾經自行比對既有付款嘗試，但那需要保存「建立當下的原始 Request」，
/// 而 <c>PaymentAttempts</c> 沒有這個欄位；改用目前訂單快照代替則會把
/// 「訂單版本改變後真正的重播被誤判成衝突」與「同 Key 換新版本被誤判成重播」
/// 兩個錯誤放回來。因此改由共用 Executor 負責，
/// <c>PaymentAttempt.IdempotencyKey</c> 的唯一索引只作資料庫最後防線。
/// </para>
/// </remarks>
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

        // 呼叫端持有的版本與訂單目前版本不符，代表它看到的金額或狀態已經過期。
        // 重播與 Payload 衝突由外層的 IIdempotencyExecutor 在此之前判斷完畢，
        // 走到這裡的一定是首次建立。
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
}
