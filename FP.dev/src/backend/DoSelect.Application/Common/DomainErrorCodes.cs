namespace DoSelect.Application.Common;

/// <summary>
/// Stable snake_case error codes returned to callers via Problem Details "code" extension.
/// Mirrors the project-wide error code catalog (API錯誤碼目錄). Values are append-only.
/// </summary>
public static class DomainErrorCodes
{
    public const string ResourceNotFound = "resource_not_found";
    public const string AuthorizationForbidden = "authorization_forbidden";
    public const string ConcurrencyConflict = "concurrency_conflict";
    public const string ValidationFailed = "validation_failed";

    public const string SupportTicketStateConflict = "support_ticket_state_conflict";
    public const string SupportTicketCancelNotAllowed = "support_ticket_cancel_not_allowed";
    public const string SupportTicketAssignmentConflict = "support_ticket_assignment_conflict";
    public const string SupportTicketNumberGenerationFailed = "support_ticket_number_generation_failed";
}
