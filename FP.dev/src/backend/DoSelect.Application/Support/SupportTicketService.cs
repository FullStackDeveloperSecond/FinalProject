using DoSelect.Application.Common;
using DoSelect.Application.Support.Dtos;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Support;

public sealed class SupportTicketService : ISupportTicketService
{
    private readonly ISupportTicketStore _store;
    private readonly IOrderOwnershipLookup _orderLookup;
    private readonly TimeProvider _timeProvider;

    public SupportTicketService(
        ISupportTicketStore store,
        IOrderOwnershipLookup orderLookup,
        TimeProvider timeProvider)
    {
        _store = store;
        _orderLookup = orderLookup;
        _timeProvider = timeProvider;
    }

    public async Task<SupportTicketDto> CreateAsync(
        string memberUserId,
        CreateSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        long? orderId = null;
        Guid? orderPublicId = null;
        if (request.OrderPublicId is { } requestedOrderPublicId)
        {
            var order = await _orderLookup.FindByPublicIdAsync(requestedOrderPublicId, cancellationToken);
            if (order is null || order.MemberUserId != memberUserId)
            {
                // Same 404 whether the order does not exist or belongs to someone else —
                // never reveal that another member's order exists.
                throw DomainProblemException.NotFound("The referenced order was not found.");
            }

            orderId = order.OrderId;
            orderPublicId = order.PublicId;
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        const CasePriority priority = CasePriority.Normal; // Member cannot self-select; CS staff adjusts later with a reason.
        var (firstResponseDueAtUtc, resolutionDueAtUtc) = SupportSlaPolicy.ComputeDueDates(priority, nowUtc);

        // The pre-insert existence check in GenerateCandidateTicketNumberAsync avoids the
        // common case cheaply, but two requests can still race between that check and the
        // insert. CreateTicketWithArtifactsAsync detects the real unique-constraint violation
        // and signals it as SupportTicketNumberCollisionException; this loop is the
        // authoritative safety net that retries with a fresh candidate when that happens.
        const int maxTicketNumberAttempts = 5;
        for (var attempt = 1; attempt <= maxTicketNumberAttempts; attempt++)
        {
            var ticketNumber = await GenerateCandidateTicketNumberAsync(nowUtc, cancellationToken);
            var ticket = new SupportTicket(
                Guid.CreateVersion7(),
                ticketNumber,
                memberUserId,
                orderId,
                request.Category!.Value,
                request.Subject,
                priority,
                firstResponseDueAtUtc,
                resolutionDueAtUtc,
                nowUtc);

            try
            {
                var creation = await _store.CreateTicketWithArtifactsAsync(
                    ticket,
                    ticketId => new SupportMessage(
                        Guid.CreateVersion7(),
                        ticketId,
                        SupportSenderType.Member,
                        memberUserId,
                        request.Message,
                        isInternal: false,
                        aiGenerated: false,
                        replyToMessageId: null,
                        language: "zh-TW",
                        sentAtUtc: nowUtc),
                    ticketId => new SupportStatusHistory(
                        ticketId,
                        fromStatus: null,
                        toStatus: SupportTicketStatus.Open,
                        reasonCode: null,
                        note: "Created by member.",
                        actorUserId: memberUserId,
                        occurredAtUtc: nowUtc),
                    ticketId => new SupportSlaEvent(
                        ticketId,
                        SupportSlaEventType.Started,
                        SupportSlaTargetType.FirstResponse,
                        firstResponseDueAtUtc,
                        durationSeconds: null,
                        occurredAtUtc: nowUtc,
                        metadataJson: null),
                    cancellationToken);

                return ToDto(ticket, orderPublicId, [creation.FirstMessage], nowUtc);
            }
            catch (SupportTicketNumberCollisionException)
            {
                // Another request took this TicketNumber between our existence check and the
                // insert; retry the whole create-with-artifacts transaction on a new number.
                // Caught unconditionally (not gated on `attempt < maxTicketNumberAttempts`) so
                // the last failed attempt still falls through to the Conflict below instead of
                // this internal signal type escaping to the Api layer as an unhandled 500.
            }
        }

        throw DomainProblemException.Conflict(
            DomainErrorCodes.SupportTicketNumberGenerationFailed,
            "Unable to allocate a unique support ticket number after multiple attempts. Please try again.");
    }

    public async Task<PageResult<SupportTicketSummaryDto>> ListMyTicketsAsync(
        string memberUserId,
        SupportTicketQuery query,
        CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(query.PageNumber, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var normalizedQuery = query with { PageNumber = pageNumber, PageSize = pageSize };

        var (items, totalCount) = await _store.ListOwnedAsync(memberUserId, normalizedQuery, cancellationToken);

        var summaries = items
            .Select(t => new SupportTicketSummaryDto(
                t.PublicId,
                t.TicketNumber,
                t.Category,
                t.Subject,
                t.Status,
                t.Priority,
                t.Status == SupportTicketStatus.WaitingForCustomer,
                t.LastActivityAtUtc,
                t.RowVersion))
            .ToList();

        return new PageResult<SupportTicketSummaryDto>(summaries, pageNumber, pageSize, totalCount);
    }

    public async Task<SupportTicketDto> GetDetailAsync(
        string memberUserId,
        Guid ticketPublicId,
        CancellationToken cancellationToken)
    {
        var ticket = await LoadOwnedTicketAsync(memberUserId, ticketPublicId, cancellationToken);
        var messages = await _store.ListPublicMessagesAsync(ticket.Id, cancellationToken);
        var attachments = await _store.ListCleanAttachmentsAsync(ticket.Id, cancellationToken);
        var orderPublicId = await ResolveOrderPublicIdAsync(ticket.OrderId, cancellationToken);
        return ToDto(ticket, orderPublicId, messages, _timeProvider.GetUtcNow().UtcDateTime) with
        {
            Attachments = attachments,
        };
    }

    public async Task<SupportTicketDto> AddMessageAsync(
        string memberUserId,
        Guid ticketPublicId,
        CreateSupportMessageRequest request,
        CancellationToken cancellationToken)
    {
        var ticket = await LoadOwnedTicketAsync(memberUserId, ticketPublicId, cancellationToken);

        if (ticket.Status is SupportTicketStatus.Resolved or SupportTicketStatus.Closed or SupportTicketStatus.Cancelled)
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.SupportTicketStateConflict,
                $"A message cannot be added while the ticket is {ticket.Status}.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var message = new SupportMessage(
            Guid.CreateVersion7(),
            ticket.Id,
            SupportSenderType.Member,
            memberUserId,
            request.Body,
            isInternal: false,
            aiGenerated: false,
            replyToMessageId: null,
            language: "zh-TW",
            sentAtUtc: nowUtc);

        SupportStatusHistory? statusHistory = null;
        SupportSlaEvent? slaEvent = null;
        if (ticket.Status == SupportTicketStatus.WaitingForCustomer)
        {
            var fromStatus = ticket.Status;
            var resumedSeconds = ticket.ResumeFromCustomerWait(nowUtc);
            statusHistory = new SupportStatusHistory(
                ticket.Id,
                fromStatus,
                SupportTicketStatus.InProgress,
                reasonCode: "customer-replied",
                note: "Customer replied while the ticket was waiting for customer.",
                actorUserId: memberUserId,
                occurredAtUtc: nowUtc);
            slaEvent = new SupportSlaEvent(
                ticket.Id,
                SupportSlaEventType.Resumed,
                SupportSlaTargetType.Resolution,
                ticket.ResolutionDueAtUtc.AddSeconds(ticket.PausedSeconds),
                resumedSeconds,
                nowUtc,
                metadataJson: null);
        }
        else
        {
            ticket.RecordActivity(nowUtc);
        }

        await _store.AddMessageAsync(message, cancellationToken);
        if (statusHistory is not null)
        {
            await _store.AddStatusHistoryAsync(statusHistory, cancellationToken);
        }

        if (slaEvent is not null)
        {
            await _store.AddSlaEventAsync(slaEvent, cancellationToken);
        }

        await _store.SaveTicketChangesAsync(ticket, request.RowVersion, cancellationToken);

        var messages = await _store.ListPublicMessagesAsync(ticket.Id, cancellationToken);
        var orderPublicId = await ResolveOrderPublicIdAsync(ticket.OrderId, cancellationToken);
        return ToDto(ticket, orderPublicId, messages, nowUtc);
    }

    public async Task<SupportTicketDto> CancelAsync(
        string memberUserId,
        Guid ticketPublicId,
        CancelSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        var ticket = await LoadOwnedTicketAsync(memberUserId, ticketPublicId, cancellationToken);

        var canCancel = ticket.Status is SupportTicketStatus.Open or SupportTicketStatus.Assigned
            && ticket.FirstHumanResponseAtUtc is null;
        if (!canCancel)
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.SupportTicketCancelNotAllowed,
                "The ticket can only be cancelled while Open or Assigned and before a human has publicly replied.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var fromStatus = ticket.Status;
        ticket.Transition(SupportTicketStatus.Cancelled, nowUtc);

        var history = new SupportStatusHistory(
            ticket.Id,
            fromStatus,
            SupportTicketStatus.Cancelled,
            reasonCode: request.ReasonCode,
            note: "Cancelled by member.",
            actorUserId: memberUserId,
            occurredAtUtc: nowUtc);
        await _store.AddStatusHistoryAsync(history, cancellationToken);
        await _store.SaveTicketChangesAsync(ticket, request.RowVersion, cancellationToken);

        var messages = await _store.ListPublicMessagesAsync(ticket.Id, cancellationToken);
        var orderPublicId = await ResolveOrderPublicIdAsync(ticket.OrderId, cancellationToken);
        return ToDto(ticket, orderPublicId, messages, nowUtc);
    }

    private async Task<SupportTicket> LoadOwnedTicketAsync(
        string memberUserId,
        Guid ticketPublicId,
        CancellationToken cancellationToken)
    {
        var ticket = await _store.FindOwnedTicketAsync(ticketPublicId, memberUserId, cancellationToken);
        if (ticket is null)
        {
            // Same 404 whether the ticket does not exist or belongs to someone else.
            throw DomainProblemException.NotFound("The support ticket was not found.");
        }

        return ticket;
    }

    private async Task<Guid?> ResolveOrderPublicIdAsync(long? orderId, CancellationToken cancellationToken) =>
        orderId is null ? null : await _store.FindOrderPublicIdAsync(orderId.Value, cancellationToken);

    private async Task<string> GenerateCandidateTicketNumberAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var candidate = $"CS-{nowUtc:yyyyMMdd}-{Random.Shared.Next(0, 10_000):D4}";
            if (!await _store.TicketNumberExistsAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unable to generate a unique support ticket number.");
    }

    private static IReadOnlyList<string> ComputeAvailableActions(SupportTicket ticket)
    {
        var actions = new List<string>();
        if (ticket.Status is not (
            SupportTicketStatus.Resolved or
            SupportTicketStatus.Closed or
            SupportTicketStatus.Cancelled))
        {
            actions.Add("addMessage");
        }

        var canCancel = ticket.Status is SupportTicketStatus.Open or SupportTicketStatus.Assigned
            && ticket.FirstHumanResponseAtUtc is null;
        if (canCancel)
        {
            actions.Add("cancel");
        }

        return actions;
    }

    /// <summary>
    /// A ticket is overdue against whichever SLA target is still active: FirstResponse until a
    /// human has replied, then Resolution — and never once the case has left active work
    /// (Resolved/Closed/Cancelled), regardless of how the historical due dates compare to now.
    /// </summary>
    private static bool ComputeIsOverdue(SupportTicket ticket, DateTime nowUtc)
    {
        if (ticket.Status is SupportTicketStatus.Resolved or SupportTicketStatus.Closed or SupportTicketStatus.Cancelled)
        {
            return false;
        }

        var activeDueAtUtc = ticket.FirstHumanResponseAtUtc is null
            ? ticket.FirstResponseDueAtUtc
            : ticket.ResolutionDueAtUtc;
        return nowUtc > activeDueAtUtc;
    }

    private static SupportTicketDto ToDto(
        SupportTicket ticket,
        Guid? orderPublicId,
        IReadOnlyList<SupportMessage> messages,
        DateTime nowUtc) =>
        new(
            ticket.PublicId,
            ticket.TicketNumber,
            ticket.Category,
            ticket.Subject,
            ticket.Status,
            ticket.Priority,
            orderPublicId,
            ticket.CreatedAtUtc,
            ticket.LastActivityAtUtc,
            ticket.FirstResponseDueAtUtc,
            ticket.ResolutionDueAtUtc,
            ComputeIsOverdue(ticket, nowUtc),
            ticket.FirstHumanResponseAtUtc,
            ticket.ResolvedAtUtc,
            ticket.ClosedAtUtc,
            ticket.ReopenCount,
            ComputeAvailableActions(ticket),
            [.. messages.Select(m => new SupportMessageDto(
                m.PublicId,
                m.SenderType,
                m.AiGenerated,
                m.Body,
                m.SentAtUtc))],
            ticket.RowVersion);
}
