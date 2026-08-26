using DoSelect.Domain.Common;
using System.Text.Json;

namespace DoSelect.Domain.Auditing;

public enum AuditActorType
{
    Member,
    Admin,
    Guest,
    System,
}

public enum AuditResult
{
    Success,
    Rejected,
    Conflict,
    Failed,
}

/// <summary>
/// Append-only security and business audit event. Mutable business entities and secrets are never
/// serialized into this record; callers must use the central writer's structured whitelist.
/// </summary>
public sealed class AuditLog : PublicEntity
{
    private AuditLog()
    {
    }

    internal AuditLog(
        Guid publicId,
        AuditActorType actorType,
        Guid? actorPublicId,
        string actorRolesJson,
        string action,
        string resourceType,
        Guid resourcePublicId,
        AuditResult result,
        string? errorCode,
        string changedFieldsJson,
        int changedFieldsSchemaVersion,
        string reason,
        string correlationId,
        string traceId,
        Guid? jobPublicId,
        string? maskedIpAddress,
        DateTime occurredAtUtc,
        DateTime retentionUntilUtc,
        bool isLegalHold,
        string? holdReason)
        : base(publicId, occurredAtUtc)
    {
        if (actorType == AuditActorType.System)
        {
            if (actorPublicId is not null)
            {
                throw new ArgumentException(
                    "A system audit actor must not have a user PublicId.",
                    nameof(actorPublicId));
            }
        }
        else if (actorPublicId is null || actorPublicId == Guid.Empty)
        {
            throw new ArgumentException(
                "A non-system audit actor requires a PublicId.",
                nameof(actorPublicId));
        }

        actorRolesJson = RequireBoundedText(actorRolesJson, nameof(actorRolesJson), 1_000);
        var actorRoleCount = RequireRoleSnapshot(actorRolesJson);
        if (actorType == AuditActorType.Admin && actorRoleCount == 0)
        {
            throw new ArgumentException(
                "An administrator audit actor requires a role snapshot.",
                nameof(actorRolesJson));
        }

        if (actorType != AuditActorType.Admin && actorRoleCount != 0)
        {
            throw new ArgumentException(
                "Only an administrator audit actor can have a role snapshot.",
                nameof(actorRolesJson));
        }

        if (resourcePublicId == Guid.Empty)
        {
            throw new ArgumentException("Resource PublicId is required.", nameof(resourcePublicId));
        }

        errorCode = NormalizeOptional(errorCode, nameof(errorCode), 128);
        if (result == AuditResult.Success ? errorCode is not null : errorCode is null)
        {
            throw new ArgumentException(
                "Successful audit results cannot have an error code; other results require one.",
                nameof(errorCode));
        }

        if (changedFieldsSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(changedFieldsSchemaVersion));
        }

        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        retentionUntilUtc = RequireUtc(retentionUntilUtc, nameof(retentionUntilUtc));
        if (retentionUntilUtc < occurredAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionUntilUtc));
        }

        holdReason = NormalizeOptional(holdReason, nameof(holdReason), 500);
        if (isLegalHold != (holdReason is not null))
        {
            throw new ArgumentException(
                "Legal hold and hold reason must either both be present or both be absent.",
                nameof(holdReason));
        }

        ActorType = actorType;
        ActorPublicId = actorPublicId;
        ActorRolesJson = actorRolesJson;
        Action = RequireBoundedText(action, nameof(action), 128);
        ResourceType = RequireBoundedText(resourceType, nameof(resourceType), 64);
        ResourcePublicId = resourcePublicId;
        Result = result;
        ErrorCode = errorCode;
        ChangedFieldsJson = RequireBoundedText(
            changedFieldsJson,
            nameof(changedFieldsJson),
            4_000);
        ChangedFieldsSchemaVersion = changedFieldsSchemaVersion;
        Reason = RequireBoundedText(reason, nameof(reason), 64);
        CorrelationId = RequireBoundedText(correlationId, nameof(correlationId), 64);
        TraceId = RequireBoundedText(traceId, nameof(traceId), 64);
        JobPublicId = jobPublicId;
        MaskedIpAddress = NormalizeOptional(maskedIpAddress, nameof(maskedIpAddress), 64);
        OccurredAtUtc = occurredAtUtc;
        RetentionUntilUtc = retentionUntilUtc;
        IsLegalHold = isLegalHold;
        HoldReason = holdReason;
    }

    public AuditActorType ActorType { get; private set; }
    public Guid? ActorPublicId { get; private set; }
    public string ActorRolesJson { get; private set; } = "[]";
    public string Action { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public Guid ResourcePublicId { get; private set; }
    public AuditResult Result { get; private set; }
    public string? ErrorCode { get; private set; }
    public string ChangedFieldsJson { get; private set; } = string.Empty;
    public int ChangedFieldsSchemaVersion { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public string TraceId { get; private set; } = string.Empty;
    public Guid? JobPublicId { get; private set; }
    public string? MaskedIpAddress { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime RetentionUntilUtc { get; private set; }
    public bool IsLegalHold { get; private set; }
    public string? HoldReason { get; private set; }

    private static string RequireBoundedText(
        string value,
        string parameterName,
        int maximumLength)
    {
        value = RequireText(value, parameterName);
        return value.Length <= maximumLength
            ? value
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }

    private static int RequireRoleSnapshot(string actorRolesJson)
    {
        try
        {
            using var document = JsonDocument.Parse(actorRolesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.EnumerateArray().Any(role =>
                    role.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(role.GetString())))
            {
                throw new ArgumentException(
                    "The actor role snapshot must be a JSON array of role names.",
                    nameof(actorRolesJson));
            }

            return document.RootElement.GetArrayLength();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The actor role snapshot must be valid JSON.",
                nameof(actorRolesJson),
                exception);
        }
    }

    private static string? NormalizeOptional(
        string? value,
        string parameterName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        return value.Length <= maximumLength
            ? value
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }
}
