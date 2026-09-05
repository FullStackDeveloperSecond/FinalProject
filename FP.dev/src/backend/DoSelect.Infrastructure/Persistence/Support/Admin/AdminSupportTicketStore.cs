using DoSelect.Application.Auditing;
using DoSelect.Application.Outbox;
using DoSelect.Application.Support;
using DoSelect.Application.Support.Admin;
using DoSelect.Application.Support.Dtos;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Support;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Support.Admin;

public sealed class AdminSupportTicketStore : IAdminSupportTicketStore
{
    private readonly DoSelectDbContext _dbContext;
    private readonly IAuditWriter _auditWriter;
    private readonly IOutboxWriter _outboxWriter;

    public AdminSupportTicketStore(
        DoSelectDbContext dbContext,
        IAuditWriter auditWriter,
        IOutboxWriter outboxWriter)
    {
        _dbContext = dbContext;
        _auditWriter = auditWriter;
        _outboxWriter = outboxWriter;
    }

    public async Task<SupportTicketClaimResult> ClaimAsync(
        Guid ticketPublicId,
        string adminUserId,
        byte[] expectedRowVersion,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        // The claimant must resolve to an active AdminProfile before the ticket is touched;
        // a missing/inactive profile must not update the ticket or append assignment history.
        var adminProfile = await _dbContext.AdminProfiles
            .AsNoTracking()
            .Where(p => p.UserId == adminUserId)
            .Select(p => new { p.PublicId, p.DisplayName, p.IsActive })
            .SingleOrDefaultAsync(cancellationToken);

        if (adminProfile is null || !adminProfile.IsActive)
        {
            return SupportTicketClaimResult.AdminNotEligible;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // A tracked SaveChangesAsync cannot tell "already claimed by someone else" apart
            // from "some other field went stale" after the fact. This conditional UPDATE
            // encodes the full claimability rule (unassigned + Open + matching RowVersion) in
            // the WHERE clause so its affected-row count is the single source of truth.
            var affected = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE SupportTickets
                 SET AssigneeAdminUserId = {adminUserId},
                     Status = {nameof(SupportTicketStatus.Assigned)},
                     UpdatedAtUtc = {occurredAtUtc},
                     LastActivityAtUtc = {occurredAtUtc}
                 WHERE PublicId = {ticketPublicId}
                   AND AssigneeAdminUserId IS NULL
                   AND Status = {nameof(SupportTicketStatus.Open)}
                   AND RowVersion = {expectedRowVersion}
                 """,
                cancellationToken);

            if (affected == 0)
            {
                var current = await _dbContext.SupportTickets
                    .AsNoTracking()
                    .Where(t => t.PublicId == ticketPublicId)
                    .Select(t => new { t.AssigneeAdminUserId, t.Status })
                    .SingleOrDefaultAsync(cancellationToken);

                await transaction.RollbackAsync(cancellationToken);

                if (current is null)
                {
                    return SupportTicketClaimResult.NotFound;
                }

                // Still unassigned and Open here means the row itself matched the business
                // rule but the caller's RowVersion did not — some other field went stale, not
                // the claimability of the ticket.
                var stillClaimable = current.AssigneeAdminUserId is null
                    && current.Status == SupportTicketStatus.Open;
                return stillClaimable
                    ? SupportTicketClaimResult.ConcurrencyConflict
                    : SupportTicketClaimResult.AssignmentConflict;
            }

            var row = await (
                from t in _dbContext.SupportTickets.AsNoTracking()
                where t.PublicId == ticketPublicId
                select new
                {
                    t.Id,
                    t.PublicId,
                    t.TicketNumber,
                    t.Category,
                    t.Subject,
                    t.Status,
                    t.Priority,
                    t.OrderId,
                    t.CreatedAtUtc,
                    t.LastActivityAtUtc,
                    t.FirstResponseDueAtUtc,
                    t.ResolutionDueAtUtc,
                    t.FirstHumanResponseAtUtc,
                    t.ResolvedAtUtc,
                    t.ClosedAtUtc,
                    t.ReopenCount,
                    t.RowVersion,
                }).SingleAsync(cancellationToken);

            Guid? orderPublicId = row.OrderId is null
                ? null
                : await _dbContext.Orders
                    .AsNoTracking()
                    .Where(o => o.Id == row.OrderId)
                    .Select(o => (Guid?)o.PublicId)
                    .SingleOrDefaultAsync(cancellationToken);

            await _dbContext.SupportAssignmentHistories.AddAsync(
                new SupportAssignmentHistory(
                    row.Id,
                    fromAdminUserId: null,
                    toAdminUserId: adminUserId,
                    AssignmentAction.Claim,
                    reason: null,
                    actorUserId: adminUserId,
                    occurredAtUtc),
                cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var claimed = new ClaimedSupportTicket(
                row.PublicId,
                row.TicketNumber,
                row.Category,
                row.Subject,
                row.Status,
                row.Priority,
                orderPublicId,
                adminProfile.PublicId,
                adminProfile.DisplayName,
                row.CreatedAtUtc,
                row.LastActivityAtUtc,
                row.FirstResponseDueAtUtc,
                row.ResolutionDueAtUtc,
                row.FirstHumanResponseAtUtc,
                row.ResolvedAtUtc,
                row.ClosedAtUtc,
                row.ReopenCount,
                row.RowVersion);
            return SupportTicketClaimResult.Claimed(claimed);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task<SupportTicketAssignResult> AssignAsync(
        SupportTicketAssignCommand command,
        CancellationToken cancellationToken) =>
        AssignOrTransferAsync(command, AssignmentAction.Assign, cancellationToken);

    public Task<SupportTicketAssignResult> TransferAsync(
        SupportTicketAssignCommand command,
        CancellationToken cancellationToken) =>
        AssignOrTransferAsync(command, AssignmentAction.Reassign, cancellationToken);

    /// <summary>
    /// Shared implementation for assign (unassigned + Open -&gt; target) and transfer (assigned
    /// to someone else, non-terminal -&gt; a different target). The only difference between the
    /// two is the WHERE-clause precondition; the affected-row-count-as-source-of-truth pattern,
    /// history/audit staging, and conflict classification are identical.
    /// </summary>
    private async Task<SupportTicketAssignResult> AssignOrTransferAsync(
        SupportTicketAssignCommand command,
        AssignmentAction action,
        CancellationToken cancellationToken)
    {
        var actor = await ResolveActiveAdminAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
        {
            return SupportTicketAssignResult.AdminNotEligible;
        }

        var target = await ResolveEligibleTargetAsync(command.TargetAdminPublicId, cancellationToken);
        if (target is null)
        {
            return SupportTicketAssignResult.TargetNotEligible;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Read the pre-image once, before the conditional UPDATE, to capture who the ticket
            // was assigned to for the history row's FromAdminUserId. This is only ever recorded
            // when the UPDATE below actually affects the row: any interceding write by another
            // transaction would also bump RowVersion, causing the UPDATE's RowVersion match to
            // fail and affected to be 0 — so a successful UPDATE guarantees this pre-image is
            // still accurate for what was just overwritten.
            var before = await _dbContext.SupportTickets.AsNoTracking()
                .Where(t => t.PublicId == command.TicketPublicId)
                .Select(t => new { t.AssigneeAdminUserId, t.Status })
                .SingleOrDefaultAsync(cancellationToken);
            if (before is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return SupportTicketAssignResult.NotFound;
            }

            var affected = action == AssignmentAction.Assign
                ? await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE SupportTickets
                     SET AssigneeAdminUserId = {target.Value.UserId},
                         Status = {nameof(SupportTicketStatus.Assigned)},
                         UpdatedAtUtc = {command.OccurredAtUtc},
                         LastActivityAtUtc = {command.OccurredAtUtc}
                     WHERE PublicId = {command.TicketPublicId}
                       AND AssigneeAdminUserId IS NULL
                       AND Status = {nameof(SupportTicketStatus.Open)}
                       AND RowVersion = {command.ExpectedRowVersion}
                     """,
                    cancellationToken)
                : await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE SupportTickets
                     SET AssigneeAdminUserId = {target.Value.UserId},
                         UpdatedAtUtc = {command.OccurredAtUtc},
                         LastActivityAtUtc = {command.OccurredAtUtc}
                     WHERE PublicId = {command.TicketPublicId}
                       AND AssigneeAdminUserId IS NOT NULL
                       AND AssigneeAdminUserId <> {target.Value.UserId}
                       AND Status NOT IN ({nameof(SupportTicketStatus.Closed)}, {nameof(SupportTicketStatus.Cancelled)})
                       AND RowVersion = {command.ExpectedRowVersion}
                     """,
                    cancellationToken);

            if (affected == 0)
            {
                var current = await _dbContext.SupportTickets.AsNoTracking()
                    .Where(t => t.PublicId == command.TicketPublicId)
                    .Select(t => new { t.AssigneeAdminUserId, t.Status })
                    .SingleOrDefaultAsync(cancellationToken);

                await transaction.RollbackAsync(cancellationToken);

                if (current is null)
                {
                    return SupportTicketAssignResult.NotFound;
                }

                var stillEligible = action == AssignmentAction.Assign
                    ? current.AssigneeAdminUserId is null && current.Status == SupportTicketStatus.Open
                    : current.AssigneeAdminUserId is not null
                        && current.AssigneeAdminUserId != target.Value.UserId
                        && current.Status is not (SupportTicketStatus.Closed or SupportTicketStatus.Cancelled);
                return stillEligible
                    ? SupportTicketAssignResult.ConcurrencyConflict
                    : SupportTicketAssignResult.AssignmentConflict;
            }

            var row = await (
                from t in _dbContext.SupportTickets.AsNoTracking()
                where t.PublicId == command.TicketPublicId
                select new
                {
                    t.Id,
                    t.PublicId,
                    t.TicketNumber,
                    t.Category,
                    t.Subject,
                    t.Status,
                    t.Priority,
                    t.OrderId,
                    t.CreatedAtUtc,
                    t.LastActivityAtUtc,
                    t.FirstResponseDueAtUtc,
                    t.ResolutionDueAtUtc,
                    t.FirstHumanResponseAtUtc,
                    t.ResolvedAtUtc,
                    t.ClosedAtUtc,
                    t.ReopenCount,
                    t.RowVersion,
                }).SingleAsync(cancellationToken);

            Guid? orderPublicId = row.OrderId is null
                ? null
                : await _dbContext.Orders
                    .AsNoTracking()
                    .Where(o => o.Id == row.OrderId)
                    .Select(o => (Guid?)o.PublicId)
                    .SingleOrDefaultAsync(cancellationToken);

            await _dbContext.SupportAssignmentHistories.AddAsync(
                new SupportAssignmentHistory(
                    row.Id,
                    fromAdminUserId: before.AssigneeAdminUserId,
                    toAdminUserId: target.Value.UserId,
                    action,
                    reason: command.Reason,
                    actorUserId: command.ActorUserId,
                    command.OccurredAtUtc),
                cancellationToken);

            _auditWriter.Add(AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                AuditActor.Create(AuditActorType.Admin, actor.Value.PublicId, command.ActorRoles),
                action == AssignmentAction.Assign ? AuditActions.SupportTicketAssign : AuditActions.SupportTicketTransfer,
                AuditResourceTypes.SupportTicket,
                command.TicketPublicId,
                AuditResult.Success,
                errorCode: null,
                action == AssignmentAction.Assign
                    ?
                    [
                        AuditFieldChange.Changed("assignee"),
                        AuditFieldChange.Code("status", nameof(SupportTicketStatus.Open), nameof(SupportTicketStatus.Assigned)),
                    ]
                    : [AuditFieldChange.Changed("assignee")],
                action == AssignmentAction.Assign ? "assign" : "transfer",
                command.CorrelationId,
                command.TraceId,
                jobPublicId: null,
                command.RemoteIpAddress));

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var updated = new ClaimedSupportTicket(
                row.PublicId,
                row.TicketNumber,
                row.Category,
                row.Subject,
                row.Status,
                row.Priority,
                orderPublicId,
                target.Value.PublicId,
                target.Value.DisplayName,
                row.CreatedAtUtc,
                row.LastActivityAtUtc,
                row.FirstResponseDueAtUtc,
                row.ResolutionDueAtUtc,
                row.FirstHumanResponseAtUtc,
                row.ResolvedAtUtc,
                row.ClosedAtUtc,
                row.ReopenCount,
                row.RowVersion);
            return SupportTicketAssignResult.Success(updated);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task<SupportTicketMutationResult> ChangePriorityAsync(
        SupportTicketChangePriorityCommand command,
        CancellationToken cancellationToken) =>
        MutateAsync(
            command,
            ticket =>
            {
                var before = ticket.Priority;
                ticket.ChangePriority(command.Priority, command.OccurredAtUtc);
                return (
                    HistoryStatus: (SupportStatusHistory?)null,
                    SlaEvent: (SupportSlaEvent?)null,
                    AuditAction: AuditActions.SupportTicketChangePriority,
                    AuditReason: "change_priority",
                    AuditChanges: (IReadOnlyCollection<AuditFieldChange>)
                        [AuditFieldChange.Code("priority", before.ToString(), command.Priority.ToString())]);
            },
            cancellationToken);

    public Task<SupportTicketMutationResult> ChangeStatusAsync(
        SupportTicketChangeStatusCommand command,
        CancellationToken cancellationToken) =>
        MutateAsync(
            command,
            ticket =>
            {
                var before = ticket.Status;
                // Cancelled and the Resolved->InProgress reopen edge each have their own
                // dedicated Action (with their own extra business rules — cancel requires no
                // human reply yet, reopen only leaves Resolved) and their own history reasonCode.
                // The generic change-status action deliberately excludes both so a caller cannot
                // sidestep those rules through the general-purpose route.
                if (command.TargetStatus == SupportTicketStatus.Cancelled ||
                    command.TargetStatus == SupportTicketStatus.Assigned ||
                    (before == SupportTicketStatus.Resolved && command.TargetStatus == SupportTicketStatus.InProgress))
                {
                    throw new InvalidOperationException(
                        "Use the dedicated cancel/reopen action for this transition.");
                }

                SupportSlaEvent? slaEvent = null;
                if (before == SupportTicketStatus.WaitingForCustomer &&
                    command.TargetStatus == SupportTicketStatus.InProgress)
                {
                    var resumedSeconds = ticket.ResumeFromCustomerWait(command.OccurredAtUtc);
                    slaEvent = new SupportSlaEvent(
                        ticket.Id,
                        SupportSlaEventType.Resumed,
                        SupportSlaTargetType.Resolution,
                        DateTime.SpecifyKind(
                            ticket.ResolutionDueAtUtc.AddSeconds(ticket.PausedSeconds),
                            DateTimeKind.Utc),
                        resumedSeconds,
                        command.OccurredAtUtc,
                        metadataJson: null);
                }
                else
                {
                    ticket.Transition(command.TargetStatus, command.OccurredAtUtc);
                }
                var history = new SupportStatusHistory(
                    ticket.Id,
                    before,
                    command.TargetStatus,
                    reasonCode: null,
                    note: command.Reason,
                    actorUserId: command.ActorUserId,
                    command.OccurredAtUtc);
                return (
                    HistoryStatus: history,
                    SlaEvent: slaEvent,
                    AuditAction: AuditActions.SupportTicketChangeStatus,
                    AuditReason: "change_status",
                    AuditChanges: (IReadOnlyCollection<AuditFieldChange>)
                        [AuditFieldChange.Code("status", before.ToString(), command.TargetStatus.ToString())]);
            },
            cancellationToken);

    public Task<SupportTicketMutationResult> CancelAsync(
        SupportTicketReasonCommand command,
        CancellationToken cancellationToken) =>
        MutateAsync(
            command,
            ticket =>
            {
                var canCancel = ticket.Status is SupportTicketStatus.Open or SupportTicketStatus.Assigned
                    && ticket.FirstHumanResponseAtUtc is null;
                if (!canCancel)
                {
                    throw new InvalidOperationException("The ticket can only be cancelled while Open or Assigned and before a human has publicly replied.");
                }

                var before = ticket.Status;
                ticket.Transition(SupportTicketStatus.Cancelled, command.OccurredAtUtc);
                var history = new SupportStatusHistory(
                    ticket.Id,
                    before,
                    SupportTicketStatus.Cancelled,
                    reasonCode: null,
                    note: command.Reason,
                    actorUserId: command.ActorUserId,
                    command.OccurredAtUtc);
                return (
                    HistoryStatus: history,
                    SlaEvent: (SupportSlaEvent?)null,
                    AuditAction: AuditActions.SupportTicketCancel,
                    AuditReason: "cancel",
                    AuditChanges: (IReadOnlyCollection<AuditFieldChange>)
                        [AuditFieldChange.Code("status", before.ToString(), nameof(SupportTicketStatus.Cancelled))]);
            },
            cancellationToken);

    public Task<SupportTicketMutationResult> ReopenAsync(
        SupportTicketReasonCommand command,
        CancellationToken cancellationToken) =>
        MutateAsync(
            command,
            ticket =>
            {
                var before = ticket.Status;
                // Domain must not depend on Application, so the Priority -> resolution-target
                // mapping is resolved here (Infrastructure already references Application) and
                // handed to the named Reopen operation, which enforces the 3-day window,
                // ReopenCount, and the recompute atomically — never via the generic Transition.
                var resolutionTarget = SupportSlaPolicy.GetTargets(ticket.Priority).Resolution;
                ticket.Reopen(command.OccurredAtUtc, resolutionTarget);
                var history = new SupportStatusHistory(
                    ticket.Id,
                    before,
                    SupportTicketStatus.InProgress,
                    reasonCode: "reopened",
                    note: command.Reason,
                    actorUserId: command.ActorUserId,
                    command.OccurredAtUtc);
                return (
                    HistoryStatus: history,
                    SlaEvent: (SupportSlaEvent?)null,
                    AuditAction: AuditActions.SupportTicketReopen,
                    AuditReason: "reopen",
                    AuditChanges: (IReadOnlyCollection<AuditFieldChange>)
                        [AuditFieldChange.Code("status", before.ToString(), nameof(SupportTicketStatus.InProgress))]);
            },
            cancellationToken);

    /// <summary>
    /// Shared tracked-entity mutation path for change-priority/change-status/cancel/reopen: load
    /// the ticket within the caller's actor scope, run the domain mutation (which may throw
    /// InvalidOperationException for an illegal state — mapped to StateConflict, leaving the
    /// tracked entity un-saved so nothing commits), stage the optional history row and the audit
    /// entry, then a single SaveChangesAsync with the RowVersion original-value check commits
    /// everything atomically or throws DbUpdateConcurrencyException on a stale RowVersion.
    /// </summary>
    private async Task<SupportTicketMutationResult> MutateAsync(
        SupportTicketActionCommand command,
        Func<SupportTicket, (
            SupportStatusHistory? HistoryStatus,
            SupportSlaEvent? SlaEvent,
            string AuditAction,
            string AuditReason,
            IReadOnlyCollection<AuditFieldChange> AuditChanges)> mutate,
        CancellationToken cancellationToken)
    {
        var actor = await ResolveActiveAdminAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
        {
            return SupportTicketMutationResult.AdminNotEligible;
        }

        var ticket = await _dbContext.SupportTickets
            .Where(t => t.PublicId == command.TicketPublicId &&
                (command.CanSupervise || t.AssigneeAdminUserId == null || t.AssigneeAdminUserId == command.ActorUserId))
            .SingleOrDefaultAsync(cancellationToken);
        if (ticket is null)
        {
            return SupportTicketMutationResult.NotFound;
        }

        (
            SupportStatusHistory? historyStatus,
            SupportSlaEvent? slaEvent,
            string auditAction,
            string auditReason,
            IReadOnlyCollection<AuditFieldChange> auditChanges) result;
        try
        {
            result = mutate(ticket);
        }
        catch (InvalidOperationException)
        {
            return SupportTicketMutationResult.StateConflict;
        }

        _dbContext.Entry(ticket).Property(t => t.RowVersion).OriginalValue = command.ExpectedRowVersion;
        if (result.historyStatus is not null)
        {
            await _dbContext.SupportStatusHistories.AddAsync(result.historyStatus, cancellationToken);
        }
        if (result.slaEvent is not null)
        {
            await _dbContext.SupportSlaEvents.AddAsync(result.slaEvent, cancellationToken);
        }

        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            AuditActor.Create(AuditActorType.Admin, actor.Value.PublicId, command.ActorRoles),
            result.auditAction,
            AuditResourceTypes.SupportTicket,
            command.TicketPublicId,
            AuditResult.Success,
            errorCode: null,
            result.auditChanges,
            result.auditReason,
            command.CorrelationId,
            command.TraceId,
            jobPublicId: null,
            command.RemoteIpAddress));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SupportTicketMutationResult.ConcurrencyConflict;
        }

        var detail = await GetDetailAsync(command.TicketPublicId, command.ActorUserId, command.CanSupervise, cancellationToken);
        return detail is null
            ? SupportTicketMutationResult.NotFound
            : SupportTicketMutationResult.Success(detail);
    }

    /// <summary>
    /// Appends an internal note. Reuses SupportTicket.RecordActivity (the same named operation
    /// message replies already use to bump LastActivityAtUtc) rather than touching any
    /// private-set field directly — it also naturally rejects a Closed/Cancelled ticket, since
    /// RecordActivity already throws for those. Never touches FirstHumanResponseAtUtc: an
    /// internal note is not a public reply.
    /// </summary>
    public async Task<SupportTicketMutationResult> AddInternalNoteAsync(
        SupportTicketAddInternalNoteCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await ResolveActiveAdminAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
        {
            return SupportTicketMutationResult.AdminNotEligible;
        }

        var ticket = await _dbContext.SupportTickets
            .Where(t => t.PublicId == command.TicketPublicId &&
                (command.CanSupervise || t.AssigneeAdminUserId == null || t.AssigneeAdminUserId == command.ActorUserId))
            .SingleOrDefaultAsync(cancellationToken);
        if (ticket is null)
        {
            return SupportTicketMutationResult.NotFound;
        }

        try
        {
            ticket.RecordActivity(command.OccurredAtUtc);
        }
        catch (InvalidOperationException)
        {
            return SupportTicketMutationResult.StateConflict;
        }

        _dbContext.Entry(ticket).Property(t => t.RowVersion).OriginalValue = command.ExpectedRowVersion;

        await _dbContext.SupportMessages.AddAsync(
            new SupportMessage(
                Guid.CreateVersion7(),
                ticket.Id,
                SupportSenderType.Admin,
                command.ActorUserId,
                command.Body,
                isInternal: true,
                aiGenerated: false,
                replyToMessageId: null,
                language: "zh-TW",
                sentAtUtc: command.OccurredAtUtc),
            cancellationToken);

        // The note's own text is never given to the audit trail — only "a note field changed"
        // is recorded. The full text lives solely in SupportMessages, gated by the same
        // SupportTicket.Handle/Supervise scope as everything else in this file.
        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            AuditActor.Create(AuditActorType.Admin, actor.Value.PublicId, command.ActorRoles),
            AuditActions.SupportTicketInternalNote,
            AuditResourceTypes.SupportTicket,
            command.TicketPublicId,
            AuditResult.Success,
            errorCode: null,
            [AuditFieldChange.Changed("note")],
            "internal_note",
            command.CorrelationId,
            command.TraceId,
            jobPublicId: null,
            command.RemoteIpAddress));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SupportTicketMutationResult.ConcurrencyConflict;
        }

        var detail = await GetDetailAsync(command.TicketPublicId, command.ActorUserId, command.CanSupervise, cancellationToken);
        return detail is null
            ? SupportTicketMutationResult.NotFound
            : SupportTicketMutationResult.Success(detail);
    }

    public async Task<SupportTicketMutationResult> AddPublicReplyAsync(
        SupportTicketAddPublicReplyCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await ResolveActiveAdminAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
        {
            return SupportTicketMutationResult.AdminNotEligible;
        }

        var ticket = await _dbContext.SupportTickets
            .Where(t => t.PublicId == command.TicketPublicId &&
                t.AssigneeAdminUserId != null &&
                (command.CanSupervise || t.AssigneeAdminUserId == command.ActorUserId))
            .SingleOrDefaultAsync(cancellationToken);
        if (ticket is null)
        {
            return SupportTicketMutationResult.NotFound;
        }

        var beforeStatus = ticket.Status;
        var isFirstHumanResponse = ticket.FirstHumanResponseAtUtc is null;
        SupportStatusHistory? statusHistory = null;
        try
        {
            if (beforeStatus is SupportTicketStatus.Resolved or SupportTicketStatus.Closed or SupportTicketStatus.Cancelled)
            {
                return SupportTicketMutationResult.StateConflict;
            }

            if (beforeStatus == SupportTicketStatus.Assigned)
            {
                ticket.Transition(SupportTicketStatus.InProgress, command.OccurredAtUtc);
                statusHistory = new SupportStatusHistory(
                    ticket.Id,
                    beforeStatus,
                    SupportTicketStatus.InProgress,
                    reasonCode: "admin-replied",
                    note: null,
                    actorUserId: command.ActorUserId,
                    occurredAtUtc: command.OccurredAtUtc);
            }
            else
            {
                ticket.RecordActivity(command.OccurredAtUtc);
            }

            ticket.RecordFirstHumanResponse(command.OccurredAtUtc);
        }
        catch (InvalidOperationException)
        {
            return SupportTicketMutationResult.StateConflict;
        }

        _dbContext.Entry(ticket).Property(t => t.RowVersion).OriginalValue = command.ExpectedRowVersion;
        await _dbContext.SupportMessages.AddAsync(
            new SupportMessage(
                Guid.CreateVersion7(),
                ticket.Id,
                SupportSenderType.Admin,
                command.ActorUserId,
                command.Body,
                isInternal: false,
                aiGenerated: false,
                replyToMessageId: null,
                language: "zh-TW",
                sentAtUtc: command.OccurredAtUtc),
            cancellationToken);

        if (statusHistory is not null)
        {
            await _dbContext.SupportStatusHistories.AddAsync(statusHistory, cancellationToken);
        }

        var memberPublicId = await _dbContext.MemberProfiles.AsNoTracking()
            .Where(profile => profile.UserId == ticket.MemberUserId)
            .Select(profile => (Guid?)profile.PublicId)
            .SingleOrDefaultAsync(cancellationToken);
        var emailNotificationPublicId = Guid.CreateVersion7();
        _outboxWriter.Add(OutboxWriteRequest.Create(
            Guid.CreateVersion7(),
            AuditResourceTypes.SupportTicket,
            command.TicketPublicId,
            new EmailNotificationRequestedV1(
                emailNotificationPublicId,
                "support.replied",
                "support.customer",
                AuditResourceTypes.SupportTicket,
                command.TicketPublicId,
                "zh-TW",
                ParameterSetVersion: 1),
            command.OccurredAtUtc,
            command.OccurredAtUtc,
            command.CorrelationId));

        if (memberPublicId is { } publicId)
        {
            _outboxWriter.Add(OutboxWriteRequest.Create(
                Guid.CreateVersion7(),
                AuditResourceTypes.SupportTicket,
                command.TicketPublicId,
                new InAppNotificationRequestedV1(
                    Guid.CreateVersion7(),
                    publicId,
                    "support.replied",
                    AuditResourceTypes.SupportTicket,
                    command.TicketPublicId,
                    "zh-TW",
                    ParameterSetVersion: 1),
                command.OccurredAtUtc,
                command.OccurredAtUtc,
                command.CorrelationId));
        }

        var auditChanges = new List<AuditFieldChange>
        {
            AuditFieldChange.Changed("message"),
        };
        if (isFirstHumanResponse)
        {
            auditChanges.Add(AuditFieldChange.Changed("firstHumanResponse"));
        }
        if (beforeStatus != ticket.Status)
        {
            auditChanges.Add(AuditFieldChange.Code("status", beforeStatus.ToString(), ticket.Status.ToString()));
        }

        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            AuditActor.Create(AuditActorType.Admin, actor.Value.PublicId, command.ActorRoles),
            AuditActions.SupportTicketReply,
            AuditResourceTypes.SupportTicket,
            command.TicketPublicId,
            AuditResult.Success,
            errorCode: null,
            auditChanges,
            "public_reply",
            command.CorrelationId,
            command.TraceId,
            jobPublicId: null,
            command.RemoteIpAddress));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SupportTicketMutationResult.ConcurrencyConflict;
        }

        var detail = await GetDetailAsync(
            command.TicketPublicId,
            command.ActorUserId,
            command.CanSupervise,
            cancellationToken);
        return detail is null
            ? SupportTicketMutationResult.NotFound
            : SupportTicketMutationResult.Success(detail);
    }

    private async Task<(Guid PublicId, string DisplayName)?> ResolveActiveAdminAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var admin = await _dbContext.AdminProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.PublicId, p.DisplayName, p.IsActive })
            .SingleOrDefaultAsync(cancellationToken);
        return admin is { IsActive: true } ? (admin.PublicId, admin.DisplayName) : null;
    }

    private async Task<(string UserId, Guid PublicId, string DisplayName)?> ResolveEligibleTargetAsync(
        Guid targetAdminPublicId,
        CancellationToken cancellationToken)
    {
        var target = await _dbContext.AdminProfiles
            .AsNoTracking()
            .Where(p => p.PublicId == targetAdminPublicId)
            .Select(p => new { p.UserId, p.PublicId, p.DisplayName, p.IsActive })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null || !target.IsActive)
        {
            return null;
        }

        var roles = await (
            from userRole in _dbContext.UserRoles.AsNoTracking()
            join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == target.UserId && role.Name != null
            select role.Name!)
            .ToArrayAsync(cancellationToken);
        var eligible = roles.Contains(AuditRoleNames.CustomerService, StringComparer.Ordinal) ||
            roles.Contains(AuditRoleNames.CustomerServiceSupervisor, StringComparer.Ordinal);
        return eligible ? (target.UserId, target.PublicId, target.DisplayName) : null;
    }

    public async Task<AdminSupportTicketDetail?> GetDetailAsync(
        Guid ticketPublicId,
        string adminUserId,
        bool canSupervise,
        CancellationToken cancellationToken)
    {
        // A single query with left joins onto Orders and active AdminProfiles keeps the ticket
        // shell + assignee + order lookup at one round trip; messages are a second, separately
        // bounded query. Neither scales with the number of messages or historical assignees, so
        // this is a constant query count rather than N+1.
        var row = await (
            from t in _dbContext.SupportTickets.AsNoTracking()
            where t.PublicId == ticketPublicId &&
                (canSupervise || t.AssigneeAdminUserId == null || t.AssigneeAdminUserId == adminUserId)
            join o in _dbContext.Orders.AsNoTracking() on t.OrderId equals (long?)o.Id into orderGroup
            from o in orderGroup.DefaultIfEmpty()
            join a in _dbContext.AdminProfiles.AsNoTracking().Where(p => p.IsActive)
                on t.AssigneeAdminUserId equals a.UserId into assigneeGroup
            from a in assigneeGroup.DefaultIfEmpty()
            select new
            {
                t.Id,
                t.PublicId,
                t.TicketNumber,
                t.Category,
                t.Subject,
                t.Status,
                t.Priority,
                OrderPublicId = o == null ? (Guid?)null : o.PublicId,
                AssigneePublicId = a == null ? (Guid?)null : a.PublicId,
                AssigneeDisplayName = a == null ? null : a.DisplayName,
                t.CreatedAtUtc,
                t.LastActivityAtUtc,
                t.FirstResponseDueAtUtc,
                t.ResolutionDueAtUtc,
                t.FirstHumanResponseAtUtc,
                t.ResolvedAtUtc,
                t.ClosedAtUtc,
                t.ReopenCount,
                t.RowVersion,
            }).SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        // Deterministic order for admin readers: chronological, with PublicId as the stable
        // unique tie-break for messages sharing the same SentAtUtc.
        var messages = await _dbContext.SupportMessages
            .AsNoTracking()
            .Where(m => m.SupportTicketId == row.Id)
            .OrderBy(m => m.SentAtUtc)
            .ThenBy(m => m.PublicId)
            .Select(m => new AdminSupportMessageProjection(
                m.PublicId,
                m.SenderType,
                m.AiGenerated,
                m.IsInternal,
                m.Body,
                m.Language,
                m.SentAtUtc))
            .ToListAsync(cancellationToken);

        var attachments = await _dbContext.SupportAttachments
            .AsNoTracking()
            .Where(a => a.SupportTicketId == row.Id &&
                a.DeletedAtUtc == null &&
                a.ScanStatus == PrivateAttachmentScanStatus.Clean)
            .OrderBy(a => a.CreatedAtUtc)
            .ThenBy(a => a.PublicId)
            .Select(a => new SupportAttachmentDto(
                a.PublicId,
                a.OriginalFileName,
                a.MimeType,
                a.FileSizeBytes,
                a.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new AdminSupportTicketDetail(
            row.PublicId,
            row.TicketNumber,
            row.Category,
            row.Subject,
            row.Status,
            row.Priority,
            row.OrderPublicId,
            row.AssigneePublicId,
            row.AssigneeDisplayName,
            row.CreatedAtUtc,
            row.LastActivityAtUtc,
            row.FirstResponseDueAtUtc,
            row.ResolutionDueAtUtc,
            row.FirstHumanResponseAtUtc,
            row.ResolvedAtUtc,
            row.ClosedAtUtc,
            row.ReopenCount,
            row.RowVersion,
            messages,
            attachments);
    }
}
