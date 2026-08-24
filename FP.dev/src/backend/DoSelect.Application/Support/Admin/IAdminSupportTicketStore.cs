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
