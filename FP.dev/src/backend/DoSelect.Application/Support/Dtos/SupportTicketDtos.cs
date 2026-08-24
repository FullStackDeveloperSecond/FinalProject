using System.ComponentModel.DataAnnotations;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Support.Dtos;

public sealed record CreateSupportTicketRequest
{
    [Required]
    public SupportTicketCategory Category { get; init; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Subject { get; init; } = string.Empty;

    [Required]
    [StringLength(4000, MinimumLength = 1)]
    public string Message { get; init; } = string.Empty;

    public Guid? OrderPublicId { get; init; }
}

public sealed record CreateSupportMessageRequest
{
    [Required]
    [StringLength(4000, MinimumLength = 1)]
    public string Body { get; init; } = string.Empty;

    [RowVersionRequired]
    public byte[] RowVersion { get; init; } = [];
}

public sealed record CancelSupportTicketRequest
{
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public string ReasonCode { get; init; } = string.Empty;

    [RowVersionRequired]
    public byte[] RowVersion { get; init; } = [];
}

public sealed record SupportTicketQuery
{
    public IReadOnlyList<SupportTicketStatus>? Statuses { get; init; }

    public SupportTicketCategory? Category { get; init; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

public sealed record SupportMessageDto(
    Guid PublicId,
    SupportSenderType SenderType,
    bool AiGenerated,
    string Body,
    DateTime SentAtUtc);

public sealed record SupportTicketSummaryDto(
    Guid PublicId,
    string TicketNumber,
    SupportTicketCategory Category,
    string Subject,
    SupportTicketStatus Status,
    CasePriority Priority,
    bool IsWaitingForCustomer,
    DateTime LastActivityAtUtc,
    byte[] RowVersion);

public sealed record SupportTicketDto(
    Guid PublicId,
    string TicketNumber,
    SupportTicketCategory Category,
    string Subject,
    SupportTicketStatus Status,
    CasePriority Priority,
    Guid? OrderPublicId,
    DateTime CreatedAtUtc,
    DateTime LastActivityAtUtc,
    DateTime FirstResponseDueAtUtc,
    DateTime ResolutionDueAtUtc,
    bool IsOverdue,
    DateTime? FirstHumanResponseAtUtc,
    DateTime? ResolvedAtUtc,
    DateTime? ClosedAtUtc,
    int ReopenCount,
    IReadOnlyList<string> AvailableActions,
    IReadOnlyList<SupportMessageDto> Messages,
    byte[] RowVersion);
