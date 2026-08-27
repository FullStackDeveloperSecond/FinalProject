using DoSelect.Application.Support.Dtos;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Support.Admin;

/// <summary>
/// Persistence port for admin-facing support ticket actions. Kept separate from
/// ISupportTicketStore (which is scoped to a member's own tickets) because admin actions
/// operate across all tickets and need the conditional-update/conflict-classification
/// semantics ISupportTicketStore's generic SaveChangesAsync does not provide.
/// </summary>
public interface IAdminSupportTicketStore
{
    /// <summary>
    /// Atomically claims an unassigned Open ticket for <paramref name="adminUserId"/> and
    /// appends one SupportAssignmentHistory(Action=Claim) row, conditioned on the ticket still
    /// matching <paramref name="expectedRowVersion"/>. A tracked SaveChangesAsync cannot
    /// reliably classify a claim race, so this uses a conditional UPDATE whose affected-row
    /// count is observable: exactly one concurrent caller sees 1 row affected, and the loser
    /// is classified by re-reading the now-current row (already claimed/not open vs. merely a
    /// stale RowVersion). <paramref name="adminUserId"/> must resolve to an active AdminProfile;
    /// if it does not, AdminNotEligible is returned before any ticket mutation is attempted.
    /// </summary>
    Task<SupportTicketClaimResult> ClaimAsync(
        Guid ticketPublicId,
        string adminUserId,
        byte[] expectedRowVersion,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the admin-facing detail projection for one ticket, including internal notes, in a
    /// bounded query shape (ticket/assignee/order in one query, messages in a second) rather
    /// than lazily loading per-message or per-assignee data. Returns null when the ticket does
    /// not exist or is outside the actor assignment scope, so both map to the standard 404.
    /// </summary>
    Task<AdminSupportTicketDetail?> GetDetailAsync(
        Guid ticketPublicId,
        string adminUserId,
        bool canSupervise,
        CancellationToken cancellationToken);

    /// <summary>
    /// DES-23 SupportTicket.Supervise: assigns a currently-unassigned, non-terminal ticket to the
    /// command's target admin. Like ClaimAsync, uses a conditional UPDATE so the affected-row
    /// count — not a tracked SaveChangesAsync — is the source of truth for whether this specific
    /// caller's precondition (still unassigned, matching RowVersion) held at commit time. The
    /// target admin's eligibility (active + CustomerService/CustomerServiceSupervisor) is
    /// resolved and rejected before the ticket is touched.
    /// </summary>
    Task<SupportTicketAssignResult> AssignAsync(
        SupportTicketAssignCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// DES-23 SupportTicket.Supervise: moves a currently-assigned, non-terminal ticket from its
    /// present assignee to a different qualified admin. The conditional UPDATE's WHERE clause
    /// itself encodes "assignee IS NOT NULL AND assignee &lt;&gt; target", so a self-transfer (or
    /// a race that lands on the same target another caller just assigned) can never commit — it
    /// always falls through to AssignmentConflict classification.
    /// </summary>
    Task<SupportTicketAssignResult> TransferAsync(
        SupportTicketAssignCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// General adjustment (SupportTicket.Handle, actor-scoped) or supervisor override
    /// (SupportTicket.Supervise, any ticket) of case priority. Uses a tracked
    /// load+RowVersion-OriginalValue+SaveChangesAsync pattern (not the conditional-UPDATE race
    /// pattern ClaimAsync/AssignAsync/TransferAsync need) because this is a single-actor field
    /// mutation, not a multi-actor race over who owns the ticket; EF's optimistic-concurrency
    /// check on RowVersion is sufficient to reject a stale write.
    /// </summary>
    Task<SupportTicketMutationResult> ChangePriorityAsync(
        SupportTicketChangePriorityCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// General status change through the ticket's existing Transition state machine — never
    /// expands the allowed-transition graph, only exposes edges Transition already permits.
    /// </summary>
    Task<SupportTicketMutationResult> ChangeStatusAsync(
        SupportTicketChangeStatusCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// Admin-initiated cancel, mirroring the member-facing cancel window (Open/Assigned only,
    /// before any human has publicly responded) via the same Transition(Cancelled) edge.
    /// </summary>
    Task<SupportTicketMutationResult> CancelAsync(
        SupportTicketReasonCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reopens a Resolved ticket back to InProgress via the existing Transition(InProgress) edge
    /// (the same edge a customer reply implicitly uses) — Closed cannot be reopened, matching the
    /// existing state machine exactly with no new edges added.
    /// </summary>
    Task<SupportTicketMutationResult> ReopenAsync(
        SupportTicketReasonCommand command,
        CancellationToken cancellationToken);
}

/// <summary>
/// Context common to every DES-23 admin action: who is acting, with which role snapshot (used
/// both for actor-scope checks and for the AuditActor role snapshot), against which ticket and
/// expected RowVersion, and the observability fields (CorrelationId/TraceId/RemoteIpAddress)
/// the central Audit contract requires. CanSupervise is precomputed by the caller (controller)
/// from the same role claims already used for [Authorize], not re-derived here.
/// </summary>
public abstract record SupportTicketActionCommand(
    Guid TicketPublicId,
    string ActorUserId,
    IReadOnlyCollection<string> ActorRoles,
    bool CanSupervise,
    byte[] ExpectedRowVersion,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string TraceId,
    System.Net.IPAddress? RemoteIpAddress);

public sealed record SupportTicketAssignCommand(
    Guid TicketPublicId,
    string ActorUserId,
    IReadOnlyCollection<string> ActorRoles,
    bool CanSupervise,
    byte[] ExpectedRowVersion,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string TraceId,
    System.Net.IPAddress? RemoteIpAddress,
    Guid TargetAdminPublicId,
    string Reason)
    : SupportTicketActionCommand(TicketPublicId, ActorUserId, ActorRoles, CanSupervise, ExpectedRowVersion,
        OccurredAtUtc, CorrelationId, TraceId, RemoteIpAddress);

public sealed record SupportTicketChangePriorityCommand(
    Guid TicketPublicId,
    string ActorUserId,
    IReadOnlyCollection<string> ActorRoles,
    bool CanSupervise,
    byte[] ExpectedRowVersion,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string TraceId,
    System.Net.IPAddress? RemoteIpAddress,
    CasePriority Priority,
    string Reason)
    : SupportTicketActionCommand(TicketPublicId, ActorUserId, ActorRoles, CanSupervise, ExpectedRowVersion,
        OccurredAtUtc, CorrelationId, TraceId, RemoteIpAddress);

public sealed record SupportTicketChangeStatusCommand(
    Guid TicketPublicId,
    string ActorUserId,
    IReadOnlyCollection<string> ActorRoles,
    bool CanSupervise,
    byte[] ExpectedRowVersion,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string TraceId,
    System.Net.IPAddress? RemoteIpAddress,
    SupportTicketStatus TargetStatus,
    string? Reason)
    : SupportTicketActionCommand(TicketPublicId, ActorUserId, ActorRoles, CanSupervise, ExpectedRowVersion,
        OccurredAtUtc, CorrelationId, TraceId, RemoteIpAddress);

/// <summary>Shared shape for cancel/reopen: no extra payload beyond the required reason.</summary>
public sealed record SupportTicketReasonCommand(
    Guid TicketPublicId,
    string ActorUserId,
    IReadOnlyCollection<string> ActorRoles,
    bool CanSupervise,
    byte[] ExpectedRowVersion,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string TraceId,
    System.Net.IPAddress? RemoteIpAddress,
    string Reason)
    : SupportTicketActionCommand(TicketPublicId, ActorUserId, ActorRoles, CanSupervise, ExpectedRowVersion,
        OccurredAtUtc, CorrelationId, TraceId, RemoteIpAddress);

public enum SupportTicketAssignOutcome
{
    Success,
    NotFound,
    TargetNotEligible,
    AssignmentConflict,
    ConcurrencyConflict,
    AdminNotEligible,
}

public sealed record SupportTicketAssignResult(SupportTicketAssignOutcome Outcome, ClaimedSupportTicket? Ticket)
{
    public static SupportTicketAssignResult Success(ClaimedSupportTicket ticket) =>
        new(SupportTicketAssignOutcome.Success, ticket);

    public static readonly SupportTicketAssignResult NotFound =
        new(SupportTicketAssignOutcome.NotFound, null);

    public static readonly SupportTicketAssignResult TargetNotEligible =
        new(SupportTicketAssignOutcome.TargetNotEligible, null);

    public static readonly SupportTicketAssignResult AssignmentConflict =
        new(SupportTicketAssignOutcome.AssignmentConflict, null);

    public static readonly SupportTicketAssignResult ConcurrencyConflict =
        new(SupportTicketAssignOutcome.ConcurrencyConflict, null);

    public static readonly SupportTicketAssignResult AdminNotEligible =
        new(SupportTicketAssignOutcome.AdminNotEligible, null);
}

public enum SupportTicketMutationOutcome
{
    Success,
    NotFound,
    StateConflict,
    ConcurrencyConflict,
    AdminNotEligible,
}

/// <summary>
/// Result of change-priority/change-status/cancel/reopen. The successful payload is the full
/// admin detail projection (not just the claimed-ticket shape) because, unlike assign/transfer,
/// these actions do not guarantee the ticket ends up assigned to anyone in particular.
/// </summary>
public sealed record SupportTicketMutationResult(SupportTicketMutationOutcome Outcome, AdminSupportTicketDetail? Ticket)
{
    public static SupportTicketMutationResult Success(AdminSupportTicketDetail ticket) =>
        new(SupportTicketMutationOutcome.Success, ticket);

    public static readonly SupportTicketMutationResult NotFound =
        new(SupportTicketMutationOutcome.NotFound, null);

    public static readonly SupportTicketMutationResult StateConflict =
        new(SupportTicketMutationOutcome.StateConflict, null);

    public static readonly SupportTicketMutationResult ConcurrencyConflict =
        new(SupportTicketMutationOutcome.ConcurrencyConflict, null);

    public static readonly SupportTicketMutationResult AdminNotEligible =
        new(SupportTicketMutationOutcome.AdminNotEligible, null);
}

public enum SupportTicketClaimOutcome
{
    Claimed,
    NotFound,
    AssignmentConflict,
    ConcurrencyConflict,
    AdminNotEligible,
}

public sealed record SupportTicketClaimResult(SupportTicketClaimOutcome Outcome, ClaimedSupportTicket? Ticket)
{
    public static SupportTicketClaimResult Claimed(ClaimedSupportTicket ticket) =>
        new(SupportTicketClaimOutcome.Claimed, ticket);

    public static readonly SupportTicketClaimResult NotFound =
        new(SupportTicketClaimOutcome.NotFound, null);

    public static readonly SupportTicketClaimResult AssignmentConflict =
        new(SupportTicketClaimOutcome.AssignmentConflict, null);

    public static readonly SupportTicketClaimResult ConcurrencyConflict =
        new(SupportTicketClaimOutcome.ConcurrencyConflict, null);

    /// <summary>
    /// The acting admin has no active AdminProfile. Returned before any ticket mutation is
    /// attempted; distinct from the ticket-state conflicts above.
    /// </summary>
    public static readonly SupportTicketClaimResult AdminNotEligible =
        new(SupportTicketClaimOutcome.AdminNotEligible, null);
}

/// <summary>
/// The claimed ticket's admin-facing detail. Carries only public-safe identifiers (PublicId,
/// not the internal numeric Id or the Identity string Id) so the Application layer never needs
/// to re-scrub internal ids before handing this to a caller.
/// </summary>
public sealed record ClaimedSupportTicket(
    Guid PublicId,
    string TicketNumber,
    SupportTicketCategory Category,
    string Subject,
    SupportTicketStatus Status,
    CasePriority Priority,
    Guid? OrderPublicId,
    Guid AssigneeAdminPublicId,
    string AssigneeAdminDisplayName,
    DateTime CreatedAtUtc,
    DateTime LastActivityAtUtc,
    DateTime FirstResponseDueAtUtc,
    DateTime ResolutionDueAtUtc,
    DateTime? FirstHumanResponseAtUtc,
    DateTime? ResolvedAtUtc,
    DateTime? ClosedAtUtc,
    int ReopenCount,
    byte[] RowVersion);

/// <summary>
/// Admin-facing ticket detail projection. Like ClaimedSupportTicket, carries only public-safe
/// identifiers. AssigneeAdminPublicId/AssigneeAdminDisplayName are both null when the ticket is
/// unassigned or when the assignee has no active public AdminProfile to project.
/// </summary>
public sealed record AdminSupportTicketDetail(
    Guid PublicId,
    string TicketNumber,
    SupportTicketCategory Category,
    string Subject,
    SupportTicketStatus Status,
    CasePriority Priority,
    Guid? OrderPublicId,
    Guid? AssigneeAdminPublicId,
    string? AssigneeAdminDisplayName,
    DateTime CreatedAtUtc,
    DateTime LastActivityAtUtc,
    DateTime FirstResponseDueAtUtc,
    DateTime ResolutionDueAtUtc,
    DateTime? FirstHumanResponseAtUtc,
    DateTime? ResolvedAtUtc,
    DateTime? ClosedAtUtc,
    int ReopenCount,
    byte[] RowVersion,
    IReadOnlyList<AdminSupportMessageProjection> Messages,
    IReadOnlyList<SupportAttachmentDto> Attachments);

/// <summary>
/// A single message or internal note as projected for admin use. Never carries SenderUserId,
/// the internal bigint Id, or any storage key — only what AdminSupportMessageDto exposes.
/// </summary>
public sealed record AdminSupportMessageProjection(
    Guid PublicId,
    SupportSenderType SenderType,
    bool AiGenerated,
    bool IsInternal,
    string Body,
    string Language,
    DateTime SentAtUtc);
