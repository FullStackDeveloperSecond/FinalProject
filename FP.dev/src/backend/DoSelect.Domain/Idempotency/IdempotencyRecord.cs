using DoSelect.Domain.Common;

namespace DoSelect.Domain.Idempotency;

public enum IdempotencyStatus
{
    Processing,
    Succeeded,
    Failed,
}

public sealed class IdempotencyRecord : MutableEntity
{
    private IdempotencyRecord()
    {
    }

    public IdempotencyRecord(
        byte[] actorScopeHash,
        string operation,
        string key,
        byte[] requestHash,
        DateTime expiresAtUtc,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        ActorScopeHash = RequireHash(actorScopeHash, nameof(actorScopeHash));
        Operation = RequireText(operation, nameof(operation));
        Key = RequireText(key, nameof(key));
        RequestHash = RequireHash(requestHash, nameof(requestHash));
        ExpiresAtUtc = RequireUtc(expiresAtUtc, nameof(expiresAtUtc));

        if (ExpiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        Status = IdempotencyStatus.Processing;
    }

    public byte[] ActorScopeHash { get; private set; } = [];
    public string Operation { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public byte[] RequestHash { get; private set; } = [];
    public IdempotencyStatus Status { get; private set; }
    public int? ResponseStatusCode { get; private set; }
    public string? ResponseHeadersJson { get; private set; }
    public string? ResponseSummary { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    public void Complete(
        int responseStatusCode,
        string responseHeadersJson,
        string responseSummary,
        DateTime completedAtUtc)
    {
        if (Status != IdempotencyStatus.Processing)
        {
            throw new InvalidOperationException("Only a processing idempotency record can complete.");
        }

        if (responseStatusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(responseStatusCode));
        }

        ResponseStatusCode = responseStatusCode;
        ResponseHeadersJson = RequireText(responseHeadersJson, nameof(responseHeadersJson));
        ResponseSummary = RequireText(responseSummary, nameof(responseSummary));
        Status = IdempotencyStatus.Succeeded;
        MarkUpdated(completedAtUtc);
    }

    private static byte[] RequireHash(byte[] value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length != 32)
        {
            throw new ArgumentException("The hash must contain 32 bytes.", parameterName);
        }

        return value.ToArray();
    }
}
