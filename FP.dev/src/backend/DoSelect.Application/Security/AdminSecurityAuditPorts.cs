namespace DoSelect.Application.Security;

/// <summary>M-01B 需要稽核的安全事件種類，只涵蓋這次範圍，不預先擴充。</summary>
public enum AdminSecurityAuditEventType
{
    ChallengeInvalidatedRateLimit,
    RebindConfirmed,
    RebindFailed,
    RecoveryCodeRedeemed,
    EnrollmentConfirmed,
    SessionsRevoked,
}

public sealed record AdminSecurityAuditEvent(
    AdminSecurityAuditEventType EventType,
    string? AdminUserId,
    string? IpAddress,
    string? Detail,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// ⚠ alex review（PR #38 第二輪）：不得為此建立資料表或 Migration——SH-11／DES-24
/// 中央 AuditLog 落地前，安全事件一律只寫結構化 Log，不建立臨時 Schema。這個介面
/// 只是把「寫一筆安全事件」的呼叫點抽象出來，之後中央 AuditLog 完成後可直接替換
/// 底層實作（改成寫真正的 Audit 表），呼叫端不需要跟著改。
/// </summary>
public interface IAdminSecurityAuditWriter
{
    void Write(AdminSecurityAuditEvent auditEvent);
}
