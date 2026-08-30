using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Idempotency;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;

namespace DoSelect.Application.Payments;

/// <summary>
/// 模擬付款要模擬出來的結果（`API DTO與Schema契約` 第 115 行）。
/// </summary>
public enum SimulatedPaymentOutcome
{
    Succeeded,
    Failed,
    Expired,
}

/// <summary>
/// 完成一筆模擬付款的請求。
/// </summary>
/// <remarks>
/// <paramref name="SimulationKey"/> 就是這個操作的冪等鍵 —— 長度限制與購物車的
/// <c>IdempotencyKey</c> 一致（8..128）。同一把鍵重播必須拿回同一個結果，
/// 而不是把訂單再付一次款。
/// </remarks>
/// <remarks>
/// 驗證屬性掛在<b>建構式參數</b>上，不是 <c>[property:]</c>。這個專案裝了
/// <c>SystemTextJsonValidationMetadataProvider</c>，它看到 record 主建構式的參數
/// 有 property-target 的驗證中繼資料時會直接丟例外（那些規則永遠不會被套用），
/// 端點因此變成 500 而不是 400。
/// </remarks>
public sealed record CompleteSimulatedPaymentRequest(
    SimulatedPaymentOutcome Outcome,
    [Required, StringLength(128, MinimumLength = 8)] string SimulationKey);

/// <summary>付款指示的顯示資訊（`API DTO與Schema契約` 第 114 行）。</summary>
public sealed record PaymentInstructionDto(
    string Type,
    string? MaskedAccount,
    string? Code,
    DateTime? ExpiresAtUtc);

/// <summary>對外的付款嘗試。內部 <c>PaymentAttemptId</c>／<c>OrderId</c> 都不在裡面。</summary>
public sealed record PaymentAttemptDto(
    Guid PublicId,
    PaymentMethod Method,
    PaymentAttemptStatus Status,
    decimal Amount,
    string Currency,
    PaymentInstructionDto? Instruction,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc,
    byte[] RowVersion);

/// <summary>
/// 決策所需的快照。<paramref name="OrderId"/> 與 <paramref name="PaymentAttemptId"/>
/// 是內部識別，只在同一交易內用來寫既有外鍵，不對外輸出。
/// </summary>
public sealed record SimulatedPaymentSnapshot(
    long PaymentAttemptId,
    PaymentAttemptStatus AttemptStatus,
    decimal AttemptAmount,
    DateTime? InstructionExpiresAtUtc,
    long OrderId,
    OrderStatus OrderStatus,
    PaymentStatus OrderPaymentStatus,
    decimal OrderGrandTotal,
    DateTime? OrderPaymentDueAtUtc);

/// <summary>
/// 通過檢查後要執行的狀態轉換。
/// </summary>
/// <remarks>
/// <paramref name="AttemptTransitions"/> 是<b>依序</b>要走的狀態。付款嘗試的狀態機
/// 不允許從 <c>AwaitingPayment</c> 直接跳到 <c>Paid</c>，中間一定要經過
/// <c>Processing</c>；把這件事放進計畫裡，Writer 就不必自己推導。
/// </remarks>
public sealed record SimulatedPaymentPlan(
    long PaymentAttemptId,
    long OrderId,
    IReadOnlyList<PaymentAttemptStatus> AttemptTransitions,
    string? FailureCode,
    PaymentStatus OrderPaymentStatus,
    decimal OrderPaidAmount);

/// <summary>決策結果：拒絕，或通過並帶出執行計畫。</summary>
public sealed class CompleteSimulatedPaymentResult
{
    private CompleteSimulatedPaymentResult(string? errorCode, SimulatedPaymentPlan? plan)
    {
        ErrorCode = errorCode;
        Plan = plan;
    }

    public bool IsSuccess => ErrorCode is null;

    public string? ErrorCode { get; }

    /// <summary>拒絕時為 <c>null</c>。</summary>
    public SimulatedPaymentPlan? Plan { get; }

    public static CompleteSimulatedPaymentResult Failure(string errorCode) => new(errorCode, null);

    public static CompleteSimulatedPaymentResult Approved(SimulatedPaymentPlan plan) =>
        new(null, plan);
}

/// <summary>
/// 完成一筆模擬付款的命令。冪等與交易由 Writer 的實作負責。
/// </summary>
public sealed record CompleteSimulatedPaymentCommand(
    Guid PaymentAttemptPublicId,
    SimulatedPaymentOutcome Outcome,
    string SimulationKey,
    string MemberUserId,
    string CorrelationId,
    string TraceId,
    System.Net.IPAddress? ClientIpAddress);

/// <summary>
/// 寫入端口。實作屬於 Infrastructure —— 這一層不碰 DbContext。
/// </summary>
public interface ISimulatedPaymentWriter
{
    Task<IdempotencyExecutionResult<PaymentAttemptDto>> CompleteAsync(
        CompleteSimulatedPaymentCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>模擬付款端點用得到的常數。</summary>
public static class SimulatedPaymentWriteConstants
{
    public const string Operation = "simulated-payment.complete";

    /// <summary>
    /// 模擬失敗時寫進 <c>PaymentAttempt.FailureCode</c> 的值。
    /// </summary>
    /// <remarks>
    /// 固定值，因為這是展示用的模擬，沒有真的金流回傳原因；
    /// 留白會讓狀態機拒絕轉換（<c>Failed</c> 必須帶 failureCode）。
    /// </remarks>
    public const string SimulatedFailureCode = "simulated_failure";
}
