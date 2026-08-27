using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Common;
using DoSelect.Application.Support.Dtos;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Support.Admin.Dtos;

/// <summary>
/// DES-23 request DTOs for the SupportTicket.Supervise/​Handle actions dispatched from
/// POST /api/v1/admin/support-tickets/{id}/actions/{action}. Property-init records with
/// property-targeted attributes only (never positional-record constructor parameters) — a
/// positional record's attributes are either dropped from the generated OpenAPI schema or throw
/// InvalidOperationException at request time when mixed with a primary constructor, per the
/// convention already established by ClaimSupportTicketRequest.
/// </summary>
public sealed record AssignSupportTicketRequest
{
    [Required]
    public Guid TargetAdminPublicId { get; init; }

    [NotWhiteSpace]
    [MaxLength(500)]
    public string Reason { get; init; } = string.Empty;

    [RowVersionRequired]
    public byte[] RowVersion { get; init; } = [];
}

public sealed record TransferSupportTicketRequest
{
    [Required]
    public Guid TargetAdminPublicId { get; init; }

    [NotWhiteSpace]
    [MaxLength(500)]
    public string Reason { get; init; } = string.Empty;

    [RowVersionRequired]
    public byte[] RowVersion { get; init; } = [];
}

public sealed record ChangeSupportTicketPriorityRequest
{
    [Required]
    public CasePriority Priority { get; init; }

    [NotWhiteSpace]
    [MaxLength(500)]
    public string Reason { get; init; } = string.Empty;

    [RowVersionRequired]
    public byte[] RowVersion { get; init; } = [];
}

public sealed record ChangeSupportTicketStatusRequest
{
    [Required]
    public SupportTicketStatus Status { get; init; }

    [MaxLength(500)]
    public string? Reason { get; init; }

    [RowVersionRequired]
    public byte[] RowVersion { get; init; } = [];
}

public sealed record CancelSupportTicketByAdminRequest
{
    [NotWhiteSpace]
    [MaxLength(500)]
    public string Reason { get; init; } = string.Empty;

    [RowVersionRequired]
    public byte[] RowVersion { get; init; } = [];
}

public sealed record ReopenSupportTicketRequest
{
    [NotWhiteSpace]
    [MaxLength(500)]
    public string Reason { get; init; } = string.Empty;

    [RowVersionRequired]
    public byte[] RowVersion { get; init; } = [];
}

public sealed record CreateInternalNoteRequest
{
    [NotWhiteSpace]
    [MaxLength(4000)]
    public string Body { get; init; } = string.Empty;

    [RowVersionRequired]
    public byte[] RowVersion { get; init; } = [];
}
