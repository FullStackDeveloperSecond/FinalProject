namespace DoSelect.Domain.Security;

/// <summary>M-01B 需要稽核的安全事件種類，只涵蓋這次範圍，不預先擴充。</summary>
public enum AdminSecurityAuditEventType
{
    ChallengeInvalidatedRateLimit,
    RebindConfirmed,
    RebindFailed,
    RecoveryCodeRedeemed,
    SessionsRevoked,
}

/// <summary>
/// SH-11／DES-24 中央 AuditLog 落地前的最小替代方案，只涵蓋 M-01B（管理員登入／TOTP／
/// Recovery Code／Rebind／Session 撤銷）需要的安全事件。純插入、不可修改，之後可整批
/// 遷移到正式 AuditLog。
/// </summary>
public sealed class AdminSecurityAuditEntry
{
    private AdminSecurityAuditEntry() { }

    public AdminSecurityAuditEntry(
        AdminSecurityAuditEventType eventType,
        string? adminUserId,
        string? ipAddress,
        string? detail,
        DateTime occurredAtUtc)
    {
        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("OccurredAtUtc must be UTC.", nameof(occurredAtUtc));
        }

        EventType = eventType;
        AdminUserId = Optional(adminUserId);
        IpAddress = Optional(ipAddress);
        Detail = Optional(detail);
        OccurredAtUtc = occurredAtUtc;
    }

    public long Id { get; private set; }

    public AdminSecurityAuditEventType EventType { get; private set; }

    public string? AdminUserId { get; private set; }

    public string? IpAddress { get; private set; }

    public string? Detail { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
