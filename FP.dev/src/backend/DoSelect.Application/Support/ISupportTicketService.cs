using DoSelect.Application.Common;
using DoSelect.Application.Support.Dtos;

namespace DoSelect.Application.Support;

/// <summary>
/// Customer-facing support ticket use cases. Every method is scoped to the calling
/// member's own tickets (Actor Scope) — callers never see or affect another member's case.
/// </summary>
public interface ISupportTicketService
{
    Task<SupportTicketDto> CreateAsync(
        string memberUserId,
        CreateSupportTicketRequest request,
        CancellationToken cancellationToken);

    Task<PageResult<SupportTicketSummaryDto>> ListMyTicketsAsync(
        string memberUserId,
        SupportTicketQuery query,
        CancellationToken cancellationToken);

    Task<SupportTicketDto> GetDetailAsync(
        string memberUserId,
        Guid ticketPublicId,
        CancellationToken cancellationToken);

    Task<SupportTicketDto> AddMessageAsync(
        string memberUserId,
        Guid ticketPublicId,
        CreateSupportMessageRequest request,
        CancellationToken cancellationToken);

    Task<SupportTicketDto> CancelAsync(
        string memberUserId,
        Guid ticketPublicId,
        CancelSupportTicketRequest request,
        CancellationToken cancellationToken);
}
