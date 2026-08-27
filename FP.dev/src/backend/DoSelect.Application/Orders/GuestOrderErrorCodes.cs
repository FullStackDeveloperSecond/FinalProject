namespace DoSelect.Application.Orders;

/// <summary>
/// 訪客查單（Challenge／驗證／限單 Cookie）專用錯誤碼，對應
/// API錯誤碼目錄.md 的 guest_order_* 條目。命名與註解風格比照
/// <c>DoSelect.Application.Security.AdminAuthErrorCodes</c>。
/// </summary>
public static class GuestOrderErrorCodes
{
    /// <summary>驗證碼錯誤、Challenge 已鎖定或已失效；訊息不得揭露訂單是否存在。</summary>
    public const string VerificationInvalid = "guest_order_verification_invalid";

    /// <summary>限單存取權杖已到期或已被撤銷。</summary>
    public const string AccessExpired = "guest_order_access_expired";

    /// <summary>權杖嘗試存取另一張訂單——回應必須跟「資源不存在」無法區分。</summary>
    public const string ScopeMismatch = "guest_order_scope_mismatch";
}
