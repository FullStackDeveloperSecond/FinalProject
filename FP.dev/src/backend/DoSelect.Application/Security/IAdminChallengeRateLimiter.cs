namespace DoSelect.Application.Security;

/// <summary>
/// 限制管理員 2FA 挑戰階段端點（TOTP 驗證、Recovery Code 兌換、Enrollment／Rebind confirm）
/// 的嘗試次數。依 IP＋challenge（或 Rebind 場景）＋帳號三者組合限流，超過門檻由呼叫端
/// 讓 challenge 立即失效並寫入稽核（alex review P1#3）。
/// </summary>
public interface IAdminChallengeRateLimiter
{
    /// <summary>
    /// 嘗試消耗一次配額。<paramref name="challengeKey"/> 對挑戰式端點傳入 challengePublicId，
    /// 對 Rebind confirm（無 challenge 概念）傳入固定值 "rebind"。回傳 false 代表已超過門檻。
    /// </summary>
    bool TryAcquire(string ipAddress, string challengeKey, string userId);
}
