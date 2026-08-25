using DoSelect.Domain.Security;

namespace DoSelect.Application.Security;

public sealed record AdminSecurityAuditEvent(
    AdminSecurityAuditEventType EventType,
    string? AdminUserId,
    string? IpAddress,
    string? Detail,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// SH-11／DES-24 中央 AuditLog 落地前的最小替代介面，只涵蓋 M-01B（管理員登入／TOTP／
/// Recovery Code／Rebind／Session 撤銷）需要的安全事件。之後 alex 的正式 AuditLog 完成後，
/// 可整個替換底層實作而不影響呼叫端。
/// </summary>
public interface IAdminSecurityAuditWriter
{
    /// <summary>
    /// 把事件加入目前 DbContext 的變更追蹤，不自行呼叫 SaveChanges——交易邊界由呼叫端
    /// （Controller）控制，讓安全狀態變更與稽核紀錄能在同一交易 commit／rollback，
    /// 稽核寫入失敗時安全狀態變更也必須一併回滾。
    /// </summary>
    Task WriteAsync(AdminSecurityAuditEvent auditEvent, CancellationToken cancellationToken = default);
}
