using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Domain.Auditing;
using DoSelect.Infrastructure.Persistence;

namespace DoSelect.Infrastructure.Auditing;

public sealed class EfAuditWriter : IAuditWriter
{
    private const int ChangedFieldsSchemaVersion = 1;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(365);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly DoSelectDbContext _context;
    private readonly TimeProvider _timeProvider;

    public EfAuditWriter(DoSelectDbContext context, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _context = context;
        _timeProvider = timeProvider;
    }

    public AuditLog Add(AuditWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var occurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var rolesJson = JsonSerializer.Serialize(request.Actor.Roles, JsonOptions);
        var changedFieldsJson = JsonSerializer.Serialize(
            new ChangedFieldsEnvelope(
                ChangedFieldsSchemaVersion,
                request.Changes.Select(change => new ChangedField(
                    change.Field,
                    change.BeforeCode,
                    change.AfterCode,
                    change.ChangedOnly)).ToArray()),
            JsonOptions);
        var audit = new AuditLog(
            request.AuditPublicId,
            request.Actor.Type,
            request.Actor.PublicId,
            rolesJson,
            request.Action,
            request.ResourceType,
            request.ResourcePublicId,
            request.Result,
            request.ErrorCode,
            changedFieldsJson,
            ChangedFieldsSchemaVersion,
            request.Reason,
            request.CorrelationId,
            request.TraceId,
            request.JobPublicId,
            AuditNetworkMasker.Mask(request.RemoteIpAddress),
            occurredAtUtc,
            occurredAtUtc.Add(Retention),
            isLegalHold: false,
            holdReason: null);
        _context.AuditLogs.Add(audit);
        return audit;
    }

    private sealed record ChangedFieldsEnvelope(
        int SchemaVersion,
        IReadOnlyList<ChangedField> Changes);

    private sealed record ChangedField(
        string Field,
        string? BeforeCode,
        string? AfterCode,
        bool ChangedOnly);
}

public static class AuditNetworkMasker
{
    public static string? Mask(IPAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            bytes[3] = 0;
            return $"{new IPAddress(bytes)}/24";
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            Array.Clear(bytes, 6, bytes.Length - 6);
            return $"{new IPAddress(bytes)}/48";
        }

        throw new ArgumentOutOfRangeException(nameof(address), "Unsupported IP address family.");
    }
}
