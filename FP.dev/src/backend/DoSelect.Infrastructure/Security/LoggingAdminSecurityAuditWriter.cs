using DoSelect.Application.Security;
using Microsoft.Extensions.Logging;

namespace DoSelect.Infrastructure.Security;

/// <summary>
/// <see cref="IAdminSecurityAuditWriter"/> 的臨時實作：只寫結構化 Log，不落地任何資料表
/// （alex review 第二輪 P1#1——SH-11／DES-24 中央 AuditLog 落地前不得建立臨時 Schema）。
/// </summary>
public sealed class LoggingAdminSecurityAuditWriter : IAdminSecurityAuditWriter
{
    private readonly ILogger<LoggingAdminSecurityAuditWriter> _logger;

    public LoggingAdminSecurityAuditWriter(ILogger<LoggingAdminSecurityAuditWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public void Write(AdminSecurityAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        _logger.LogWarning(
            "AdminSecurityAudit {EventType} AdminUserId={AdminUserId} IpAddress={IpAddress} " +
            "Detail={Detail} OccurredAtUtc={OccurredAtUtc}",
            auditEvent.EventType,
            auditEvent.AdminUserId,
            auditEvent.IpAddress,
            auditEvent.Detail,
            auditEvent.OccurredAtUtc);
    }
}
