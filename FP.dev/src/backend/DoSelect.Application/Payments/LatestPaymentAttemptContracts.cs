using DoSelect.Domain.Payments;

namespace DoSelect.Application.Payments;

/// <summary>
/// 誰在查這筆付款嘗試。
/// </summary>
/// <remarks>
/// 與 <c>InvoiceViewer</c> 同形。訪客的 Scope 已由 <c>GuestOrderAccessScopeAuthorizer</c>
/// 對「這一張訂單」驗過，所以這裡不帶任何識別 —— 再比一次會變成第二份平行的驗證邏輯。
/// </remarks>
public abstract record PaymentAttemptViewer
{
    private PaymentAttemptViewer()
    {
    }

    public sealed record Member(string MemberUserId) : PaymentAttemptViewer;

    public sealed record Guest : PaymentAttemptViewer;
}

/// <summary>
/// 查詢最新一筆付款嘗試的結果。
/// </summary>
/// <remarks>
/// <see cref="MemberAccessDenied"/> 不是對外的答案，而是要呼叫端<b>接著檢查同一個瀏覽器
/// 的 Guest cookie</b>：同一台裝置可以同時有會員 cookie 與某張訪客訂單的有效 token
/// （alex 2026-09-01 Issue #86 C1）。Guest 也證明不了權限時，對外一樣折成 404。
/// </remarks>
public abstract record LatestPaymentAttemptResult
{
    private LatestPaymentAttemptResult()
    {
    }

    public sealed record Found(PaymentAttemptDto Attempt) : LatestPaymentAttemptResult;

    public sealed record NotFound : LatestPaymentAttemptResult;

    public sealed record MemberAccessDenied : LatestPaymentAttemptResult;
}

/// <summary>
/// 擁有者比對需要的訂單欄位。
/// </summary>
/// <param name="OrderId">
/// 內部識別，只用來把付款嘗試對回這張訂單，不對外輸出。
/// </param>
public sealed record PaymentAttemptOrderReference(long OrderId, string? MemberUserId);

/// <summary>
/// 最新一筆付款嘗試的讀取埠。實作屬於 Infrastructure。
/// </summary>
public interface ILatestPaymentAttemptReader
{
    /// <summary>以對外識別取得訂單的擁有者資訊；訂單不存在時回 <c>null</c>。</summary>
    Task<PaymentAttemptOrderReference?> FindOrderAsync(
        Guid orderPublicId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 這張訂單最新的一筆付款嘗試，<b>包含所有終態</b>；一筆都沒有時回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 終態不會被跳過（alex 2026-09-01 Issue #86 A1）：付款失敗或逾期之後重新整理，
    /// 使用者仍要看得到剛才發生什麼事，而不是回到一張空的建立表單。
    /// </remarks>
    Task<PaymentAttempt?> FindLatestAsync(
        long orderId,
        CancellationToken cancellationToken = default);
}
