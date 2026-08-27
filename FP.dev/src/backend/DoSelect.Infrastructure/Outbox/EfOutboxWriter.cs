using System.Text.Json;
using DoSelect.Application.Outbox;
using DoSelect.Domain.Outbox;
using DoSelect.Infrastructure.Persistence;

namespace DoSelect.Infrastructure.Outbox;

public sealed class EfOutboxWriter : IOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly DoSelectDbContext _context;
    private readonly TimeProvider _timeProvider;

    public EfOutboxWriter(DoSelectDbContext context, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _context = context;
        _timeProvider = timeProvider;
    }

    public OutboxMessage Add(OutboxWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payloadJson = request.Payload switch
        {
            EmailNotificationRequestedV1 payload => JsonSerializer.Serialize(payload, JsonOptions),
            InAppNotificationRequestedV1 payload => JsonSerializer.Serialize(payload, JsonOptions),
            InventoryReconciliationMismatchDetectedV1 payload => JsonSerializer.Serialize(payload, JsonOptions),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                "The outbox payload type is not registered."),
        };

        var message = new OutboxMessage(
            request.PublicId,
            request.Type,
            request.PayloadVersion,
            request.AggregateType,
            request.AggregatePublicId,
            payloadJson,
            request.OccurredAtUtc,
            request.AvailableAtUtc,
            request.CorrelationId,
            _timeProvider.GetUtcNow().UtcDateTime);
        _context.OutboxMessages.Add(message);
        return message;
    }
}
