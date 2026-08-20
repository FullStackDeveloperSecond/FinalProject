using DoSelect.Application.Support.Admin;
using DoSelect.Application.Support.Admin.Dtos;
using DoSelect.Domain.Support;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Support.Admin;

/// <summary>
/// Queries SupportTickets directly (not vw_CaseWorkbench) so due/usage/overdue are computed from
/// the caller-supplied <c>nowUtc</c> instant instead of SQL server local time
/// (SYSUTCDATETIME()). The pause-cap arithmetic mirrors vw_CaseWorkbench's confirmed
/// EffectiveSla CROSS APPLY so the two read models never disagree on the same ticket.
/// </summary>
public sealed class SupportSlaQueueStore : ISupportSlaQueueStore
{
    private const int MaxPausedSeconds = 259_200;

    private readonly DoSelectDbContext _dbContext;

    public SupportSlaQueueStore(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SupportSlaQueuePage> QueryPageAsync(
        int pageSize,
        SupportSlaCursorPosition? after,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var active = _dbContext.SupportTickets
            .AsNoTracking()
            .Where(t => t.Status != SupportTicketStatus.Resolved
                && t.Status != SupportTicketStatus.Closed
                && t.Status != SupportTicketStatus.Cancelled);

        var withPause = active.Select(t => new
        {
            t.PublicId,
            t.TicketNumber,
            t.Priority,
            t.Status,
            t.CreatedAtUtc,
            t.FirstResponseDueAtUtc,
            t.ResolutionDueAtUtc,
            t.FirstHumanResponseAtUtc,
            t.LastActivityAtUtc,
            t.RowVersion,
            t.AssigneeAdminUserId,
            // WaitingForInternal never pauses the clock; only an active WaitingForCustomer
            // period (started before now) accrues additional paused seconds beyond the
            // already-persisted PausedSeconds total.
            ActiveWaitingSeconds = t.Status == SupportTicketStatus.WaitingForCustomer
                    && t.WaitingForCustomerStartedAtUtc != null
                    && t.WaitingForCustomerStartedAtUtc < nowUtc
                ? EF.Functions.DateDiffSecond(t.WaitingForCustomerStartedAtUtc!.Value, nowUtc)
                : 0,
            t.PausedSeconds,
        });

        var withDue = withPause.Select(x => new
        {
            x.PublicId,
            x.TicketNumber,
            x.Priority,
            x.Status,
            x.FirstResponseDueAtUtc,
            x.ResolutionDueAtUtc,
            x.LastActivityAtUtc,
            x.RowVersion,
            x.AssigneeAdminUserId,
            EffectivePausedSeconds = x.PausedSeconds + x.ActiveWaitingSeconds > MaxPausedSeconds
                ? MaxPausedSeconds
                : x.PausedSeconds + x.ActiveWaitingSeconds,
            x.FirstHumanResponseAtUtc,
            x.CreatedAtUtc,
        });

        var withRatio = withDue.Select(x => new
        {
            x.PublicId,
            x.TicketNumber,
            x.Priority,
            x.Status,
            x.FirstResponseDueAtUtc,
            x.ResolutionDueAtUtc,
            x.LastActivityAtUtc,
            x.RowVersion,
            x.AssigneeAdminUserId,
            // Before first human response the effective due time is the first-response target
            // as-is (no pause credit yet). After first response it is the resolution target
            // pushed out by the capped paused seconds.
            EffectiveDueAtUtc = x.FirstHumanResponseAtUtc == null
                ? x.FirstResponseDueAtUtc
                : x.ResolutionDueAtUtc.AddSeconds(x.EffectivePausedSeconds),
            // Elapsed active time excludes credited customer-wait seconds only for the
            // resolution target; the target duration is symmetric with the same exclusion, so
            // ratio > 1.0 lines up exactly with IsOverdue below for both phases.
            TargetSeconds = x.FirstHumanResponseAtUtc == null
                ? EF.Functions.DateDiffSecond(x.CreatedAtUtc, x.FirstResponseDueAtUtc)
                : EF.Functions.DateDiffSecond(x.CreatedAtUtc, x.ResolutionDueAtUtc),
            ElapsedActiveSeconds = x.FirstHumanResponseAtUtc == null
                ? EF.Functions.DateDiffSecond(x.CreatedAtUtc, nowUtc)
                : EF.Functions.DateDiffSecond(x.CreatedAtUtc, nowUtc) - x.EffectivePausedSeconds,
        });

        var projected = withRatio.Select(x => new
        {
            x.PublicId,
            x.TicketNumber,
            x.Priority,
            x.Status,
            x.FirstResponseDueAtUtc,
            x.ResolutionDueAtUtc,
            x.LastActivityAtUtc,
            x.RowVersion,
            x.AssigneeAdminUserId,
            x.EffectiveDueAtUtc,
            IsOverdue = x.EffectiveDueAtUtc < nowUtc,
            // Guarded defensively even though the SupportTicket constructor already enforces
            // FirstResponseDueAtUtc/ResolutionDueAtUtc strictly after CreatedAtUtc.
            UsageRatio = x.TargetSeconds > 0
                ? (double)(x.ElapsedActiveSeconds < 0 ? 0 : x.ElapsedActiveSeconds) / x.TargetSeconds
                : 0d,
        });

        var withAssignee =
            from t in projected
            join a in _dbContext.AdminProfiles.AsNoTracking().Where(p => p.IsActive)
                on t.AssigneeAdminUserId equals a.UserId into adminJoin
            from admin in adminJoin.DefaultIfEmpty()
            select new
            {
                t.PublicId,
                t.TicketNumber,
                t.Priority,
                t.Status,
                t.FirstResponseDueAtUtc,
                t.ResolutionDueAtUtc,
                t.EffectiveDueAtUtc,
                t.IsOverdue,
                t.UsageRatio,
                t.LastActivityAtUtc,
                t.RowVersion,
                AssigneePublicId = (Guid?)admin.PublicId,
                AssigneeDisplayName = admin.DisplayName,
            };

        var filtered = after is null
            ? withAssignee
            : withAssignee.Where(x =>
                (x.IsOverdue ? 1 : 0) < (after.IsOverdue ? 1 : 0)
                || ((x.IsOverdue ? 1 : 0) == (after.IsOverdue ? 1 : 0)
                    && x.EffectiveDueAtUtc > after.EffectiveDueAtUtc)
                || ((x.IsOverdue ? 1 : 0) == (after.IsOverdue ? 1 : 0)
                    && x.EffectiveDueAtUtc == after.EffectiveDueAtUtc
                    && x.PublicId > after.TicketPublicId));

        var rows = await filtered
            .OrderByDescending(x => x.IsOverdue)
            .ThenBy(x => x.EffectiveDueAtUtc)
            .ThenBy(x => x.PublicId)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize) : rows;

        var items = page.Select(x => new SupportSlaItemDto(
            x.PublicId,
            x.TicketNumber,
            x.Priority,
            x.AssigneePublicId is null
                ? null
                : new AdminAssigneeSummaryDto(x.AssigneePublicId.Value, x.AssigneeDisplayName),
            x.Status,
            x.FirstResponseDueAtUtc,
            x.ResolutionDueAtUtc,
            x.EffectiveDueAtUtc,
            x.UsageRatio,
            x.IsOverdue,
            x.LastActivityAtUtc,
            x.RowVersion)).ToList();

        return new SupportSlaQueuePage(items, hasMore);
    }
}
