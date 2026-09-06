using DoSelect.Application.Auditing;
using DoSelect.Application.Outbox;
using DoSelect.Application.Support;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DoSelect.Infrastructure.Outbox;

public sealed class SupportSlaMaintenanceJob(
    DoSelectDbContext context,
    IOutboxWriter outboxWriter,
    IAuditWriter auditWriter,
    TimeProvider timeProvider,
    ILogger<SupportSlaMaintenanceJob> logger)
{
    public const int BatchSize = 200;
    public static readonly TimeSpan AutoCloseDelay = TimeSpan.FromDays(3);

    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var processed = await StageAutoClosuresAsync(nowUtc, cancellationToken);
        processed += await StageThresholdEventsAsync(
            nowUtc,
            BatchSize - processed,
            cancellationToken);

        if (processed == 0)
        {
            return 0;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return processed;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(exception, "Support SLA maintenance lost a concurrency race; the batch will retry.");
            throw;
        }
    }

    private async Task<int> StageAutoClosuresAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var cutoffUtc = nowUtc - AutoCloseDelay;
        var tickets = await context.SupportTickets
            .Where(ticket =>
                ticket.Status == SupportTicketStatus.Resolved &&
                ticket.ResolvedAtUtc != null &&
                ticket.ResolvedAtUtc <= cutoffUtc)
            .OrderBy(ticket => ticket.ResolvedAtUtc)
            .ThenBy(ticket => ticket.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var ticket in tickets)
        {
            ticket.Transition(SupportTicketStatus.Closed, nowUtc);
            context.SupportStatusHistories.Add(new SupportStatusHistory(
                ticket.Id,
                SupportTicketStatus.Resolved,
                SupportTicketStatus.Closed,
                reasonCode: "sla-auto-close",
                note: null,
                actorUserId: null,
                occurredAtUtc: nowUtc));
            context.SupportSlaEvents.Add(new SupportSlaEvent(
                ticket.Id,
                SupportSlaEventType.Closed,
                SupportSlaTargetType.Resolution,
                ticket.ResolvedAtUtc is { } resolvedAtUtc
                    ? AsUtc(resolvedAtUtc).Add(AutoCloseDelay)
                    : null,
                (int)AutoCloseDelay.TotalSeconds,
                nowUtc,
                metadataJson: null));

            var correlationId = $"sla-close:{ticket.PublicId:N}";
            auditWriter.Add(AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                AuditActor.Create(AuditActorType.System, publicId: null, roles: []),
                AuditActions.SupportTicketChangeStatus,
                AuditResourceTypes.SupportTicket,
                ticket.PublicId,
                AuditResult.Success,
                errorCode: null,
                [AuditFieldChange.Code(
                    "status",
                    nameof(SupportTicketStatus.Resolved),
                    nameof(SupportTicketStatus.Closed))],
                reason: "sla-auto-close",
                correlationId,
                traceId: Guid.CreateVersion7().ToString("N"),
                jobPublicId: null,
                remoteIpAddress: null));
        }

        return tickets.Count;
    }

    private async Task<int> StageThresholdEventsAsync(
        DateTime nowUtc,
        int remaining,
        CancellationToken cancellationToken)
    {
        if (remaining <= 0)
        {
            return 0;
        }

        var activeAdmins = await (
                from user in context.Users.AsNoTracking()
                join profile in context.AdminProfiles.AsNoTracking()
                    on user.Id equals profile.UserId
                where user.AccountType == AccountType.Admin &&
                    user.AccountStatus == AccountStatus.Active &&
                    profile.IsActive
                select new ActiveAdmin(user.Id, user.PublicId))
            .ToListAsync(cancellationToken);
        var adminsById = activeAdmins
            .GroupBy(admin => admin.UserId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var supervisors = await (
                from user in context.Users.AsNoTracking()
                join profile in context.AdminProfiles.AsNoTracking()
                    on user.Id equals profile.UserId
                join userRole in context.UserRoles.AsNoTracking()
                    on user.Id equals userRole.UserId
                join role in context.Roles.AsNoTracking()
                    on userRole.RoleId equals role.Id
                where user.AccountType == AccountType.Admin &&
                    user.AccountStatus == AccountStatus.Active &&
                    profile.IsActive &&
                    role.Name == AuditRoleNames.CustomerServiceSupervisor
                select new ActiveAdmin(user.Id, user.PublicId))
            .Distinct()
            .ToListAsync(cancellationToken);

        var processed = 0;
        DateTime? lastDueAtUtc = null;
        long lastTicketId = 0;
        while (processed < remaining)
        {
            var query = context.SupportTickets.Where(ticket =>
                ticket.Status == SupportTicketStatus.Open ||
                ticket.Status == SupportTicketStatus.Assigned ||
                ticket.Status == SupportTicketStatus.InProgress ||
                ticket.Status == SupportTicketStatus.WaitingForCustomer ||
                ticket.Status == SupportTicketStatus.WaitingForInternal);
            if (lastDueAtUtc is { } cursorDueAtUtc)
            {
                query = query.Where(ticket =>
                    (ticket.FirstHumanResponseAtUtc == null
                        ? ticket.FirstResponseDueAtUtc
                        : ticket.ResolutionDueAtUtc) > cursorDueAtUtc ||
                    ((ticket.FirstHumanResponseAtUtc == null
                        ? ticket.FirstResponseDueAtUtc
                        : ticket.ResolutionDueAtUtc) == cursorDueAtUtc &&
                     ticket.Id > lastTicketId));
            }

            var tickets = await query
                .OrderBy(ticket => ticket.FirstHumanResponseAtUtc == null
                    ? ticket.FirstResponseDueAtUtc
                    : ticket.ResolutionDueAtUtc)
                .ThenBy(ticket => ticket.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (tickets.Count == 0)
            {
                break;
            }

            var lastTicket = tickets[^1];
            lastDueAtUtc = lastTicket.FirstHumanResponseAtUtc is null
                ? lastTicket.FirstResponseDueAtUtc
                : lastTicket.ResolutionDueAtUtc;
            lastTicketId = lastTicket.Id;

            var ticketIds = tickets.Select(ticket => ticket.Id).ToArray();
            var existingEvents = (await context.SupportSlaEvents.AsNoTracking()
                    .Where(item => ticketIds.Contains(item.SupportTicketId) &&
                        (item.EventType == SupportSlaEventType.Warning80 ||
                         item.EventType == SupportSlaEventType.Overdue100))
                    .Select(item => new
                    {
                        item.SupportTicketId,
                        item.EventType,
                        item.TargetType,
                        item.OccurredAtUtc,
                    })
                    .ToListAsync(cancellationToken))
                .GroupBy(item => (item.SupportTicketId, item.EventType, item.TargetType))
                .ToDictionary(
                    group => group.Key,
                    group => group.Max(item => item.OccurredAtUtc));

            var reopenedAt = (await context.SupportStatusHistories.AsNoTracking()
                    .Where(item => ticketIds.Contains(item.SupportTicketId) &&
                        item.ReasonCode == "reopened")
                    .Select(item => new { item.SupportTicketId, item.OccurredAtUtc })
                    .ToListAsync(cancellationToken))
                .GroupBy(item => item.SupportTicketId)
                .ToDictionary(group => group.Key, group => group.Max(item => item.OccurredAtUtc));

            foreach (var ticket in tickets)
            {
                var target = ticket.FirstHumanResponseAtUtc is null
                    ? SupportSlaTargetType.FirstResponse
                    : SupportSlaTargetType.Resolution;
                var cycleStartedAtUtc = target == SupportSlaTargetType.Resolution &&
                    ticket.ReopenCount > 0 &&
                    reopenedAt.TryGetValue(ticket.Id, out var reopenedAtUtc)
                        ? reopenedAtUtc
                        : (DateTime?)null;
                var duration = ResolveTargetDuration(ticket, target, reopenedAt);
                var pauseSeconds = EffectivePauseSeconds(ticket, nowUtc);
                var effectiveDueAtUtc = AsUtc(target == SupportSlaTargetType.FirstResponse
                        ? ticket.FirstResponseDueAtUtc
                        : ticket.ResolutionDueAtUtc)
                    .AddSeconds(pauseSeconds);
                var warningAtUtc = effectiveDueAtUtc - TimeSpan.FromTicks(duration.Ticks / 5);

                var hasOverdue = HasCurrentCycleEvent(
                    existingEvents,
                    ticket.Id,
                    SupportSlaEventType.Overdue100,
                    target,
                    cycleStartedAtUtc);
                if (nowUtc >= effectiveDueAtUtc && !hasOverdue)
                {
                    var recipients = new List<ActiveAdmin>();
                    if (ticket.AssigneeAdminUserId is { } assigneeId &&
                        adminsById.TryGetValue(assigneeId, out var assignee))
                    {
                        recipients.Add(assignee);
                    }

                    recipients.AddRange(supervisors);
                    StageThreshold(
                        ticket,
                        target,
                        SupportSlaEventType.Overdue100,
                        SupportSlaNotificationContract.Overdue100TemplateKey,
                        effectiveDueAtUtc,
                        duration,
                        recipients,
                        nowUtc);
                    processed++;
                }
                else
                {
                    var hasWarning = HasCurrentCycleEvent(
                        existingEvents,
                        ticket.Id,
                        SupportSlaEventType.Warning80,
                        target,
                        cycleStartedAtUtc);
                    if (nowUtc >= warningAtUtc && !hasWarning && !hasOverdue &&
                        ticket.AssigneeAdminUserId is { } warningAssigneeId &&
                        adminsById.TryGetValue(warningAssigneeId, out var warningAssignee))
                    {
                        StageThreshold(
                            ticket,
                            target,
                            SupportSlaEventType.Warning80,
                            SupportSlaNotificationContract.Warning80TemplateKey,
                            effectiveDueAtUtc,
                            duration,
                            [warningAssignee],
                            nowUtc);
                        processed++;
                    }
                }

                if (processed == remaining)
                {
                    break;
                }
            }
        }

        return processed;
    }

    private static bool HasCurrentCycleEvent(
        IReadOnlyDictionary<(long SupportTicketId, SupportSlaEventType EventType,
            SupportSlaTargetType TargetType), DateTime> existingEvents,
        long supportTicketId,
        SupportSlaEventType eventType,
        SupportSlaTargetType targetType,
        DateTime? cycleStartedAtUtc)
    {
        return existingEvents.TryGetValue(
                (supportTicketId, eventType, targetType),
                out var occurredAtUtc) &&
            (cycleStartedAtUtc is null || occurredAtUtc >= cycleStartedAtUtc.Value);
    }

    private void StageThreshold(
        SupportTicket ticket,
        SupportSlaTargetType target,
        SupportSlaEventType eventType,
        string templateKey,
        DateTime dueAtUtc,
        TimeSpan duration,
        IEnumerable<ActiveAdmin> recipients,
        DateTime nowUtc)
    {
        context.SupportSlaEvents.Add(new SupportSlaEvent(
            ticket.Id,
            eventType,
            target,
            dueAtUtc,
            checked((int)duration.TotalSeconds),
            nowUtc,
            metadataJson: null));

        var targetCode = target == SupportSlaTargetType.FirstResponse ? "fr" : "rs";
        var thresholdCode = eventType == SupportSlaEventType.Warning80 ? "w80" : "o100";
        var correlationId = $"sla:{ticket.PublicId:N}:{targetCode}:{thresholdCode}";
        foreach (var recipient in recipients
                     .GroupBy(item => item.PublicId)
                     .Select(group => group.First()))
        {
            outboxWriter.Add(OutboxWriteRequest.Create(
                Guid.CreateVersion7(),
                AuditResourceTypes.SupportTicket,
                ticket.PublicId,
                new EmailNotificationRequestedV1(
                    Guid.CreateVersion7(),
                    templateKey,
                    SupportSlaNotificationContract.RecipientPurpose,
                    SupportSlaNotificationContract.EmailRecipientResourceType,
                    recipient.PublicId,
                    SupportSlaNotificationContract.Locale,
                    SupportSlaNotificationContract.ParameterSetVersion),
                nowUtc,
                nowUtc,
                correlationId));
            outboxWriter.Add(OutboxWriteRequest.Create(
                Guid.CreateVersion7(),
                AuditResourceTypes.SupportTicket,
                ticket.PublicId,
                new InAppNotificationRequestedV1(
                    Guid.CreateVersion7(),
                    recipient.PublicId,
                    templateKey,
                    SupportSlaNotificationContract.InAppResourceType,
                    ticket.PublicId,
                    SupportSlaNotificationContract.Locale,
                    SupportSlaNotificationContract.ParameterSetVersion),
                nowUtc,
                nowUtc,
                correlationId));
        }
    }

    private static TimeSpan ResolveTargetDuration(
        SupportTicket ticket,
        SupportSlaTargetType target,
        IReadOnlyDictionary<long, DateTime> reopenedAt)
    {
        if (target == SupportSlaTargetType.FirstResponse)
        {
            return ticket.FirstResponseDueAtUtc - ticket.CreatedAtUtc;
        }

        if (ticket.ReopenCount > 0 && reopenedAt.TryGetValue(ticket.Id, out var anchorUtc))
        {
            return ticket.ResolutionDueAtUtc - anchorUtc;
        }

        return ticket.ResolutionDueAtUtc - ticket.CreatedAtUtc;
    }

    private static int EffectivePauseSeconds(SupportTicket ticket, DateTime nowUtc)
    {
        if (ticket.Status != SupportTicketStatus.WaitingForCustomer ||
            ticket.WaitingForCustomerStartedAtUtc is null)
        {
            return ticket.PausedSeconds;
        }

        var activePause = Math.Max(
            0L,
            (long)(nowUtc - ticket.WaitingForCustomerStartedAtUtc.Value).TotalSeconds);
        return ticket.PausedSeconds +
            (int)Math.Min(activePause, 259_200 - ticket.PausedSeconds);
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record ActiveAdmin(string UserId, Guid PublicId);
}
