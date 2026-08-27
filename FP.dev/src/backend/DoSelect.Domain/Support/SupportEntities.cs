using DoSelect.Domain.Common;

namespace DoSelect.Domain.Support;

public sealed class SupportTicket : MutablePublicEntity
{
    private static readonly IReadOnlyDictionary<SupportTicketStatus, SupportTicketStatus[]> AllowedTransitions = new Dictionary<SupportTicketStatus, SupportTicketStatus[]>
    {
        [SupportTicketStatus.Open] = [SupportTicketStatus.Assigned, SupportTicketStatus.Cancelled],
        [SupportTicketStatus.Assigned] = [SupportTicketStatus.InProgress, SupportTicketStatus.Cancelled],
        [SupportTicketStatus.InProgress] = [SupportTicketStatus.WaitingForCustomer, SupportTicketStatus.WaitingForInternal, SupportTicketStatus.Resolved, SupportTicketStatus.Cancelled],
        [SupportTicketStatus.WaitingForCustomer] = [SupportTicketStatus.InProgress],
        [SupportTicketStatus.WaitingForInternal] = [SupportTicketStatus.InProgress],
        [SupportTicketStatus.Resolved] = [SupportTicketStatus.InProgress, SupportTicketStatus.Closed],
        [SupportTicketStatus.Closed] = [],
        [SupportTicketStatus.Cancelled] = [],
    };

    private SupportTicket() { }
    public SupportTicket(Guid publicId, string ticketNumber, string memberUserId, long? orderId,
        SupportTicketCategory category, string subject, CasePriority priority,
        DateTime firstResponseDueAtUtc, DateTime resolutionDueAtUtc, DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (orderId is <= 0 || firstResponseDueAtUtc <= createdAtUtc || resolutionDueAtUtc <= firstResponseDueAtUtc) throw new ArgumentOutOfRangeException(nameof(orderId));
        TicketNumber = RequireText(ticketNumber, nameof(ticketNumber)); MemberUserId = RequireText(memberUserId, nameof(memberUserId)); OrderId = orderId;
        Category = category; Subject = RequireText(subject, nameof(subject)); Priority = priority; Status = SupportTicketStatus.Open;
        FirstResponseDueAtUtc = RequireUtc(firstResponseDueAtUtc, nameof(firstResponseDueAtUtc)); ResolutionDueAtUtc = RequireUtc(resolutionDueAtUtc, nameof(resolutionDueAtUtc)); LastActivityAtUtc = createdAtUtc;
    }
    public string TicketNumber { get; private set; } = string.Empty;
    public string MemberUserId { get; private set; } = string.Empty;
    public long? OrderId { get; private set; }
    public SupportTicketCategory Category { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public SupportTicketStatus Status { get; private set; }
    public CasePriority Priority { get; private set; }
    public string? AssigneeAdminUserId { get; private set; }
    public DateTime FirstResponseDueAtUtc { get; private set; }
    public DateTime ResolutionDueAtUtc { get; private set; }
    public DateTime? FirstHumanResponseAtUtc { get; private set; }
    public DateTime? WaitingForCustomerStartedAtUtc { get; private set; }
    public int PausedSeconds { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public DateTime LastActivityAtUtc { get; private set; }
    public int ReopenCount { get; private set; }

    public void Assign(string adminUserId, DateTime occurredAtUtc)
    {
        if (AssigneeAdminUserId is not null || Status != SupportTicketStatus.Open) throw new InvalidOperationException("The ticket is already assigned.");
        AssigneeAdminUserId = RequireText(adminUserId, nameof(adminUserId)); Transition(SupportTicketStatus.Assigned, occurredAtUtc);
    }

    /// <summary>
    /// Supervisor-driven handoff: unlike Assign (self-claim of an unassigned Open ticket only),
    /// this sets the assignee regardless of whether the ticket is currently unassigned (a
    /// supervisor "assign") or already held by someone else (a "transfer"). The caller
    /// (Application/Infrastructure) is responsible for enforcing which of those two shapes is
    /// legal for the specific action being performed and for target eligibility; this method only
    /// enforces that a terminal ticket can no longer change hands. Does not expand the status
    /// state machine: Open still transitions to Assigned via the existing Transition table, and
    /// every other non-terminal status is left untouched aside from LastActivityAtUtc/RowVersion.
    /// </summary>
    public void AssignTo(string adminUserId, DateTime occurredAtUtc)
    {
        if (Status is SupportTicketStatus.Closed or SupportTicketStatus.Cancelled)
        {
            throw new InvalidOperationException($"A {Status} ticket cannot be assigned or transferred.");
        }

        AssigneeAdminUserId = RequireText(adminUserId, nameof(adminUserId));
        if (Status == SupportTicketStatus.Open)
        {
            Transition(SupportTicketStatus.Assigned, occurredAtUtc);
        }
        else
        {
            LastActivityAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            MarkUpdated(occurredAtUtc);
        }
    }

    /// <summary>
    /// General adjustment or supervisor override of the case priority. Does not itself change
    /// Status or SLA due dates (SLA recompute-on-priority-change is a separate, not-yet-built
    /// concern); a terminal ticket's priority is frozen.
    /// </summary>
    public void ChangePriority(CasePriority priority, DateTime occurredAtUtc)
    {
        if (Status is SupportTicketStatus.Closed or SupportTicketStatus.Cancelled)
        {
            throw new InvalidOperationException($"A {Status} ticket's priority cannot change.");
        }

        Priority = priority;
        LastActivityAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        MarkUpdated(occurredAtUtc);
    }

    /// <summary>
    /// Reopens a Resolved ticket back to InProgress, but only within 3 days of ResolvedAtUtc —
    /// Closed/Cancelled (and any other non-Resolved status) can never reopen, and a Resolved
    /// ticket past its 3-day window can no longer reopen either. Uses the existing
    /// Resolved-&gt;InProgress Transition edge (so ReopenCount increments exactly the way it
    /// already does today) and then additionally recomputes ResolutionDueAtUtc from
    /// <paramref name="resolutionTarget"/> — the caller (Application/Infrastructure) supplies
    /// this from the ticket's current Priority via SupportSlaPolicy, since Domain must not
    /// depend on Application. FirstHumanResponseAtUtc is untouched: a reopen is not a first
    /// response event.
    /// </summary>
    public void Reopen(DateTime occurredAtUtc, TimeSpan resolutionTarget)
    {
        if (Status != SupportTicketStatus.Resolved)
        {
            throw new InvalidOperationException($"A {Status} ticket cannot be reopened.");
        }

        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        if (ResolvedAtUtc is null || occurredAtUtc > ResolvedAtUtc.Value.AddDays(3))
        {
            throw new InvalidOperationException("The ticket can only be reopened within 3 days of being resolved.");
        }

        Transition(SupportTicketStatus.InProgress, occurredAtUtc);
        ResolutionDueAtUtc = occurredAtUtc.Add(resolutionTarget);
    }

    public void Transition(SupportTicketStatus next, DateTime occurredAtUtc)
    {
        if (!AllowedTransitions[Status].Contains(next)) throw new InvalidOperationException($"Support ticket cannot move from {Status} to {next}.");
        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        var previousStatus = Status;
        if (previousStatus == SupportTicketStatus.Resolved && next == SupportTicketStatus.InProgress) ReopenCount++;
        Status = next;
        WaitingForCustomerStartedAtUtc = next == SupportTicketStatus.WaitingForCustomer
            ? occurredAtUtc
            : previousStatus == SupportTicketStatus.WaitingForCustomer
                ? null
                : WaitingForCustomerStartedAtUtc;
        ResolvedAtUtc = next == SupportTicketStatus.Resolved ? occurredAtUtc : ResolvedAtUtc; ClosedAtUtc = next == SupportTicketStatus.Closed ? occurredAtUtc : ClosedAtUtc;
        LastActivityAtUtc = occurredAtUtc; MarkUpdated(occurredAtUtc);
    }
    public int ResumeFromCustomerWait(DateTime occurredAtUtc)
    {
        if (Status != SupportTicketStatus.WaitingForCustomer || WaitingForCustomerStartedAtUtc is null)
        {
            throw new InvalidOperationException("The ticket is not waiting for the customer.");
        }

        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        var elapsedSeconds = Math.Max(
            0L,
            (long)(occurredAtUtc - WaitingForCustomerStartedAtUtc.Value).TotalSeconds);
        var addedSeconds = (int)Math.Min(elapsedSeconds, 259_200 - PausedSeconds);

        Transition(SupportTicketStatus.InProgress, occurredAtUtc);
        if (addedSeconds > 0)
        {
            AddPausedSeconds(addedSeconds, occurredAtUtc);
        }

        return addedSeconds;
    }

    public void RecordFirstHumanResponse(DateTime occurredAtUtc)
    {
        if (FirstHumanResponseAtUtc.HasValue) return;
        FirstHumanResponseAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc)); LastActivityAtUtc = occurredAtUtc; MarkUpdated(occurredAtUtc);
    }

    public void AddPausedSeconds(int seconds, DateTime occurredAtUtc)
    {
        if (seconds < 0 || PausedSeconds + seconds > 259_200)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        PausedSeconds += seconds;
        LastActivityAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        MarkUpdated(occurredAtUtc);
    }

    /// <summary>
    /// Bumps LastActivityAtUtc for events that do not change Status, such as a new
    /// message on a ticket that is already InProgress. Closed/Cancelled are terminal —
    /// no further activity should be recorded against them.
    /// </summary>
    public void RecordActivity(DateTime occurredAtUtc)
    {
        if (Status is SupportTicketStatus.Closed or SupportTicketStatus.Cancelled)
        {
            throw new InvalidOperationException($"A {Status} ticket cannot record new activity.");
        }

        LastActivityAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        MarkUpdated(occurredAtUtc);
    }
}

public sealed class SupportMessage : PublicEntity
{
    private SupportMessage() { }
    public SupportMessage(Guid publicId, long supportTicketId, SupportSenderType senderType, string? senderUserId,
        string body, bool isInternal, bool aiGenerated, long? replyToMessageId, string language, DateTime sentAtUtc)
        : base(publicId, sentAtUtc)
    {
        if (supportTicketId <= 0 || replyToMessageId is <= 0 || senderType != SupportSenderType.System && string.IsNullOrWhiteSpace(senderUserId) || isInternal && senderType != SupportSenderType.Admin) throw new ArgumentException("The support message sender is invalid.");
        SupportTicketId = supportTicketId; SenderType = senderType; SenderUserId = Optional(senderUserId); Body = RequireText(body, nameof(body)); IsInternal = isInternal; AiGenerated = aiGenerated;
        ReplyToMessageId = replyToMessageId; Language = RequireText(language, nameof(language)); SentAtUtc = RequireUtc(sentAtUtc, nameof(sentAtUtc));
    }
    public long SupportTicketId { get; private set; }
    public SupportSenderType SenderType { get; private set; }
    public string? SenderUserId { get; private set; }
    public string Body { get; private set; } = string.Empty; public bool IsInternal { get; private set; }
    public bool AiGenerated { get; private set; }
    public long? ReplyToMessageId { get; private set; }
    public string Language { get; private set; } = "zh-TW"; public DateTime SentAtUtc { get; private set; }
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class SupportAssignmentHistory
{
    private SupportAssignmentHistory() { }
    public SupportAssignmentHistory(long supportTicketId, string? fromAdminUserId, string? toAdminUserId, AssignmentAction action, string? reason, string? actorUserId, DateTime occurredAtUtc)
    {
        if (supportTicketId <= 0 || occurredAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Invalid assignment history.");
        Id = 0; SupportTicketId = supportTicketId; FromAdminUserId = Optional(fromAdminUserId); ToAdminUserId = Optional(toAdminUserId); Action = action; Reason = Optional(reason); ActorUserId = Optional(actorUserId); OccurredAtUtc = occurredAtUtc;
    }
    public long Id { get; private set; }
    public long SupportTicketId { get; private set; }
    public string? FromAdminUserId { get; private set; }
    public string? ToAdminUserId { get; private set; }
    public AssignmentAction Action { get; private set; }
    public string? Reason { get; private set; }
    public string? ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class SupportStatusHistory
{
    private SupportStatusHistory() { }
    public SupportStatusHistory(long supportTicketId, SupportTicketStatus? fromStatus, SupportTicketStatus toStatus, string? reasonCode, string? note, string? actorUserId, DateTime occurredAtUtc)
    { if (supportTicketId <= 0 || occurredAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Invalid status history."); SupportTicketId = supportTicketId; FromStatus = fromStatus; ToStatus = toStatus; ReasonCode = Optional(reasonCode); Note = Optional(note); ActorUserId = Optional(actorUserId); OccurredAtUtc = occurredAtUtc; }
    public long Id { get; private set; }
    public long SupportTicketId { get; private set; }
    public SupportTicketStatus? FromStatus { get; private set; }
    public SupportTicketStatus ToStatus { get; private set; }
    public string? ReasonCode { get; private set; }
    public string? Note { get; private set; }
    public string? ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class SupportSlaEvent
{
    private SupportSlaEvent() { }
    public SupportSlaEvent(long supportTicketId, SupportSlaEventType eventType, SupportSlaTargetType targetType, DateTime? dueAtUtc, int? durationSeconds, DateTime occurredAtUtc, string? metadataJson)
    { if (supportTicketId <= 0 || durationSeconds is < 0 || occurredAtUtc.Kind != DateTimeKind.Utc || dueAtUtc?.Kind is not (null or DateTimeKind.Utc)) throw new ArgumentException("Invalid SLA event."); SupportTicketId = supportTicketId; EventType = eventType; TargetType = targetType; DueAtUtc = dueAtUtc; DurationSeconds = durationSeconds; OccurredAtUtc = occurredAtUtc; MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson.Trim(); }
    public long Id { get; private set; }
    public long SupportTicketId { get; private set; }
    public SupportSlaEventType EventType { get; private set; }
    public SupportSlaTargetType TargetType { get; private set; }
    public DateTime? DueAtUtc { get; private set; }
    public int? DurationSeconds { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? MetadataJson { get; private set; }
}

public sealed class SupportSummary : MutablePublicEntity
{
    private SupportSummary() { }
    public SupportSummary(Guid publicId, long supportTicketId, long sourceLastMessageId, string summary, string model, string promptVersion, DateTime createdAtUtc) : base(publicId, createdAtUtc)
    { if (supportTicketId <= 0 || sourceLastMessageId <= 0) throw new ArgumentOutOfRangeException(nameof(supportTicketId)); SupportTicketId = supportTicketId; SourceLastMessageId = sourceLastMessageId; Summary = RequireText(summary, nameof(summary)); Model = RequireText(model, nameof(model)); PromptVersion = RequireText(promptVersion, nameof(promptVersion)); Status = SupportSummaryStatus.Pending; }
    public long SupportTicketId { get; private set; }
    public long SourceLastMessageId { get; private set; }
    public string Summary { get; private set; } = string.Empty; public string Model { get; private set; } = string.Empty;
    public string PromptVersion { get; private set; } = string.Empty; public SupportSummaryStatus Status { get; private set; }
    public DateTime? GeneratedAtUtc { get; private set; }
    public void Complete(DateTime generatedAtUtc)
    { if (Status != SupportSummaryStatus.Pending) throw new InvalidOperationException("Only a pending summary can complete."); GeneratedAtUtc = RequireUtc(generatedAtUtc, nameof(generatedAtUtc)); Status = SupportSummaryStatus.Completed; MarkUpdated(generatedAtUtc); }
    public void Fail(DateTime occurredAtUtc)
    {
        if (Status != SupportSummaryStatus.Pending) throw new InvalidOperationException("Only a pending summary can fail.");
        Status = SupportSummaryStatus.Failed; MarkUpdated(occurredAtUtc);
    }
    public void MarkObsolete(DateTime occurredAtUtc)
    {
        if (Status is not (SupportSummaryStatus.Completed or SupportSummaryStatus.Failed)) throw new InvalidOperationException("Only a finished summary can become obsolete.");
        Status = SupportSummaryStatus.Obsolete; MarkUpdated(occurredAtUtc);
    }
}

public sealed class SupportAttachment : MutablePublicEntity
{
    private SupportAttachment() { }
    public SupportAttachment(Guid publicId, long supportTicketId, long? supportMessageId, string uploadedByUserId, string originalFileName, string storageKey, string extension, string mimeType, long fileSizeBytes, byte[] sha256, DateTime createdAtUtc) : base(publicId, createdAtUtc)
    { AttachmentRules.Validate(fileSizeBytes, sha256); if (supportTicketId <= 0 || supportMessageId is <= 0) throw new ArgumentOutOfRangeException(nameof(supportTicketId)); SupportTicketId = supportTicketId; SupportMessageId = supportMessageId; UploadedByUserId = RequireText(uploadedByUserId, nameof(uploadedByUserId)); OriginalFileName = RequireText(originalFileName, nameof(originalFileName)); StorageKey = RequireText(storageKey, nameof(storageKey)); Extension = RequireText(extension, nameof(extension)); MimeType = RequireText(mimeType, nameof(mimeType)); FileSizeBytes = fileSizeBytes; Sha256 = sha256.ToArray(); ScanStatus = PrivateAttachmentScanStatus.Pending; }
    public long SupportTicketId { get; private set; }
    public long? SupportMessageId { get; private set; }
    public string UploadedByUserId { get; private set; } = string.Empty; public string OriginalFileName { get; private set; } = string.Empty;
    public string StorageKey { get; private set; } = string.Empty; public string Extension { get; private set; } = string.Empty; public string MimeType { get; private set; } = string.Empty; public long FileSizeBytes { get; private set; }
    public byte[] Sha256 { get; private set; } = []; public PrivateAttachmentScanStatus ScanStatus { get; private set; }
    public DateTime? ScannedAtUtc { get; private set; }
    public DateTime? RetentionUntilUtc { get; private set; }
    public bool LegalHold { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public void RecordScan(PrivateAttachmentScanStatus result, DateTime scannedAtUtc)
    { if (result == PrivateAttachmentScanStatus.Pending) throw new ArgumentException("A completed scan result is required.", nameof(result)); ScanStatus = result; ScannedAtUtc = RequireUtc(scannedAtUtc, nameof(scannedAtUtc)); MarkUpdated(scannedAtUtc); }
    public void ScheduleRetention(DateTime retentionUntilUtc, DateTime occurredAtUtc)
    { RetentionUntilUtc = RequireUtc(retentionUntilUtc, nameof(retentionUntilUtc)); MarkUpdated(occurredAtUtc); }
    public void SetLegalHold(bool enabled, DateTime occurredAtUtc)
    { LegalHold = enabled; MarkUpdated(occurredAtUtc); }
    public void SoftDelete(DateTime deletedAtUtc)
    { if (LegalHold) throw new InvalidOperationException("An attachment under legal hold cannot be deleted."); DeletedAtUtc = RequireUtc(deletedAtUtc, nameof(deletedAtUtc)); MarkUpdated(deletedAtUtc); }
}

internal static class AttachmentRules
{
    public static void Validate(long size, byte[] hash) { if (size is < 1 or > 10_485_760 || hash.Length != 32) throw new ArgumentOutOfRangeException(nameof(size)); }
}
