using DoSelect.Application.Security;
using DoSelect.Domain.Security;
using DoSelect.Infrastructure.Persistence;

namespace DoSelect.Infrastructure.Security;

/// <summary>
/// <see cref="IAdminSecurityAuditWriter"/> 的臨時實作：只把事件加進目前 DbContext 的變更
/// 追蹤，交易與 SaveChanges 完全交給呼叫端控制（見 AdminAuthController 的交易邊界）。
/// </summary>
public sealed class EfAdminSecurityAuditWriter : IAdminSecurityAuditWriter
{
    private readonly DoSelectDbContext _dbContext;

    public EfAdminSecurityAuditWriter(DoSelectDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public Task WriteAsync(AdminSecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        _dbContext.AdminSecurityAuditEntries.Add(new AdminSecurityAuditEntry(
            auditEvent.EventType,
            auditEvent.AdminUserId,
            auditEvent.IpAddress,
            auditEvent.Detail,
            auditEvent.OccurredAtUtc.UtcDateTime));

        return Task.CompletedTask;
    }
}
