using System.Data.Common;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using DoSelect.Application.Ai;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Ai;

public sealed class EfAiSupportContextReader(DoSelectDbContext dbContext)
    : IAiSupportContextReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public async Task<AiSupportContextReadResult> ReadAsync(
        Guid memberId,
        Guid? conversationPublicId,
        IReadOnlyList<Guid> referencedOrderPublicIds,
        IReadOnlyList<Guid> referencedSupportTicketPublicIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(referencedOrderPublicIds);
        ArgumentNullException.ThrowIfNull(referencedSupportTicketPublicIds);
        if (memberId == Guid.Empty)
        {
            throw new ArgumentException("A member identifier is required.", nameof(memberId));
        }

        var requestedIds = referencedOrderPublicIds.Distinct().ToArray();
        var requestedTicketIds = referencedSupportTicketPublicIds.Distinct().ToArray();
        try
        {
            var memberUserId = memberId.ToString("D");
            if (conversationPublicId.HasValue)
            {
                var ownsConversation = await dbContext.AiConversations
                    .AsNoTracking()
                    .AnyAsync(
                        conversation =>
                            conversation.PublicId == conversationPublicId.Value &&
                            conversation.MemberUserId == memberUserId &&
                            conversation.Status == Domain.Ai.AiConversationStatus.Active,
                        cancellationToken);
                if (!ownsConversation)
                {
                    return new AiSupportContextReadResult(
                        AiSupportContextStatus.ResourceNotFound,
                        DataItems: []);
                }
            }

            if (requestedIds.Length == 0 && requestedTicketIds.Length == 0)
            {
                return new AiSupportContextReadResult(
                    AiSupportContextStatus.Allowed,
                    DataItems: []);
            }

            var dataItems = new List<AiSupportContextItem>(requestedIds.Length + requestedTicketIds.Length);
            var orders = await dbContext.Orders
                .AsNoTracking()
                .Where(order =>
                    requestedIds.Contains(order.PublicId) &&
                    order.MemberUserId == memberUserId)
                .Select(order => new OrderRow(
                    order.Id,
                    order.PublicId,
                    order.OrderNumber,
                    order.OrderStatus,
                    order.PaymentStatus,
                    order.FulfillmentStatus,
                    order.UpdatedAtUtc))
                .ToListAsync(cancellationToken);
            if (orders.Count != requestedIds.Length)
            {
                return new AiSupportContextReadResult(
                    AiSupportContextStatus.ResourceNotFound,
                    DataItems: []);
            }

            var orderIds = orders.Select(order => order.Id).ToArray();
            var items = await dbContext.OrderItems
                .AsNoTracking()
                .Where(item => orderIds.Contains(item.OrderId))
                .OrderBy(item => item.Id)
                .Select(item => new OrderItemRow(
                    item.OrderId,
                    item.ProductNameSnapshot,
                    item.SkuNameSnapshot,
                    item.Quantity))
                .ToListAsync(cancellationToken);

            var byPublicId = orders.ToDictionary(order => order.PublicId);
            foreach (var publicId in requestedIds)
            {
                var order = byPublicId[publicId];
                var payload = new
                {
                    orderStatus = order.OrderStatus.ToString(),
                    paymentStatus = order.PaymentStatus.ToString(),
                    shippingStatus = order.FulfillmentStatus.ToString(),
                    items = items
                        .Where(item => item.OrderId == order.Id)
                        .Select(item => new
                        {
                            productName = item.ProductName,
                            skuName = item.SkuName,
                            item.Quantity,
                        })
                        .ToArray(),
                };
                var json = JsonSerializer.Serialize(payload, JsonOptions);
                var outboundProductContent = items
                    .Where(item => item.OrderId == order.Id)
                    .SelectMany(item => new[] { item.ProductName, item.SkuName })
                    .ToArray();
                if (!AiOutboundContentGuard.Inspect(outboundProductContent).IsAllowed)
                {
                    return new AiSupportContextReadResult(
                        AiSupportContextStatus.Unavailable,
                        DataItems: []);
                }

                dataItems.Add(new AiSupportContextItem(
                    SourceType: "order",
                    SourceId: order.PublicId.ToString("D"),
                    Title: order.OrderNumber,
                    VersionOrUpdatedAt: order.UpdatedAtUtc.ToString("O"),
                    Content: json));
            }

            var tickets = await dbContext.SupportTickets
                .AsNoTracking()
                .Where(ticket =>
                    requestedTicketIds.Contains(ticket.PublicId) &&
                    ticket.MemberUserId == memberUserId)
                .Select(ticket => new SupportTicketRow(
                    ticket.Id,
                    ticket.PublicId,
                    ticket.TicketNumber,
                    ticket.Subject,
                    ticket.Status,
                    ticket.UpdatedAtUtc))
                .ToListAsync(cancellationToken);
            if (tickets.Count != requestedTicketIds.Length)
            {
                return new AiSupportContextReadResult(
                    AiSupportContextStatus.ResourceNotFound,
                    DataItems: []);
            }

            var ticketIds = tickets.Select(ticket => ticket.Id).ToArray();
            var messages = await dbContext.SupportMessages
                .AsNoTracking()
                .Where(message =>
                    ticketIds.Contains(message.SupportTicketId) &&
                    !message.IsInternal)
                .OrderByDescending(message => message.SentAtUtc)
                .ThenByDescending(message => message.Id)
                .Select(message => new SupportMessageRow(
                    message.SupportTicketId,
                    message.SenderType,
                    message.Body,
                    message.SentAtUtc))
                .ToListAsync(cancellationToken);
            var ticketsByPublicId = tickets.ToDictionary(ticket => ticket.PublicId);
            foreach (var publicId in requestedTicketIds)
            {
                var ticket = ticketsByPublicId[publicId];
                var ticketMessages = messages
                    .Where(message => message.SupportTicketId == ticket.Id)
                    .Take(20)
                    .OrderBy(message => message.SentAtUtc)
                    .Select(message => new
                    {
                        senderType = message.SenderType.ToString(),
                        message.Body,
                        message.SentAtUtc,
                    })
                    .ToArray();
                var payload = new
                {
                    ticket.Subject,
                    status = ticket.Status.ToString(),
                    messages = ticketMessages,
                };
                var json = JsonSerializer.Serialize(payload, JsonOptions);
                var outboundTicketContent = ticketMessages
                    .Select(message => message.Body)
                    .Prepend(ticket.Subject)
                    .ToArray();
                if (!AiOutboundContentGuard.Inspect(outboundTicketContent).IsAllowed)
                {
                    return new AiSupportContextReadResult(
                        AiSupportContextStatus.Unavailable,
                        DataItems: []);
                }

                dataItems.Add(new AiSupportContextItem(
                    SourceType: "support_ticket",
                    SourceId: ticket.PublicId.ToString("D"),
                    Title: ticket.TicketNumber,
                    VersionOrUpdatedAt: ticket.UpdatedAtUtc.ToString("O"),
                    Content: json));
            }

            return new AiSupportContextReadResult(
                AiSupportContextStatus.Allowed,
                dataItems);
        }
        catch (DbException)
        {
            return new AiSupportContextReadResult(
                AiSupportContextStatus.Unavailable,
                DataItems: []);
        }
        catch (InvalidOperationException exception) when (exception.InnerException is DbException)
        {
            return new AiSupportContextReadResult(
                AiSupportContextStatus.Unavailable,
                DataItems: []);
        }
    }

    private sealed record OrderRow(
        long Id,
        Guid PublicId,
        string OrderNumber,
        Domain.Orders.OrderStatus OrderStatus,
        Domain.Orders.PaymentStatus PaymentStatus,
        Domain.Orders.FulfillmentStatus FulfillmentStatus,
        DateTime UpdatedAtUtc);

    private sealed record OrderItemRow(
        long OrderId,
        string ProductName,
        string SkuName,
        int Quantity);

    private sealed record SupportTicketRow(
        long Id,
        Guid PublicId,
        string TicketNumber,
        string Subject,
        Domain.Support.SupportTicketStatus Status,
        DateTime UpdatedAtUtc);

    private sealed record SupportMessageRow(
        long SupportTicketId,
        Domain.Support.SupportSenderType SenderType,
        string Body,
        DateTime SentAtUtc);
}
