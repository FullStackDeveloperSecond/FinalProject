using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DoSelect.Application.Idempotency;

public readonly record struct IdempotencyActorScope
{
    private IdempotencyActorScope(string value) => Value = value;

    internal string Value { get; }

    public static IdempotencyActorScope ForUser(Guid userPublicId)
    {
        if (userPublicId == Guid.Empty)
        {
            throw new ArgumentException("User PublicId is required.", nameof(userPublicId));
        }

        return new IdempotencyActorScope($"user:{userPublicId:D}");
    }

    public static IdempotencyActorScope ForGuestCart(Guid cartPublicId)
    {
        if (cartPublicId == Guid.Empty)
        {
            throw new ArgumentException("Cart PublicId is required.", nameof(cartPublicId));
        }

        return new IdempotencyActorScope($"guest-cart:{cartPublicId:D}");
    }

    public static IdempotencyActorScope ForGuestOrderAccess(Guid tokenPublicId)
    {
        if (tokenPublicId == Guid.Empty)
        {
            throw new ArgumentException("Guest order access token PublicId is required.", nameof(tokenPublicId));
        }

        return new IdempotencyActorScope($"guest-order-access:{tokenPublicId:D}");
    }

    public static IdempotencyActorScope ForAdmin(Guid adminPublicId)
    {
        if (adminPublicId == Guid.Empty)
        {
            throw new ArgumentException("Admin PublicId is required.", nameof(adminPublicId));
        }

        return new IdempotencyActorScope($"admin:{adminPublicId:D}");
    }

    public byte[] ComputeHash(string pepper)
    {
        if (string.IsNullOrEmpty(Value))
        {
            throw new InvalidOperationException("The idempotency actor scope is not initialized.");
        }

        var key = Encoding.UTF8.GetBytes(pepper);
        var value = Encoding.UTF8.GetBytes(Value);
        return HMACSHA256.HashData(key, value);
    }
}

public sealed class IdempotencyCommand
{
    private readonly byte[] _requestHash;

    private IdempotencyCommand(
        IdempotencyActorScope actorScope,
        string operation,
        string key,
        byte[] requestHash)
    {
        ActorScope = actorScope;
        Operation = operation;
        Key = key;
        _requestHash = requestHash;
    }

    public IdempotencyActorScope ActorScope { get; }
    public string Operation { get; }
    public string Key { get; }
    public ReadOnlyMemory<byte> RequestHash => _requestHash;

    public static IdempotencyCommand Create<TRequest>(
        IdempotencyActorScope actorScope,
        string operation,
        string key,
        TRequest request)
    {
        operation = RequireText(operation, nameof(operation), 128);
        key = RequireText(key, nameof(key), 128);
        ArgumentNullException.ThrowIfNull(request);

        var requestJson = JsonSerializer.SerializeToUtf8Bytes(request);
        return new IdempotencyCommand(
            actorScope,
            operation,
            key,
            SHA256.HashData(requestJson));
    }

    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value is required.", parameterName);
        }

        value = value.Trim();
        if (value.Length > maximumLength)
        {
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
        }

        return value;
    }
}

public sealed record IdempotencyResponse<T>(
    int StatusCode,
    T Body,
    string ResponseSummary,
    string ResponseHeadersJson = "{}");

public sealed record IdempotencyExecutionResult<T>(
    int StatusCode,
    T Body,
    string ResponseHeadersJson,
    bool IsReplay);

public sealed record StoredIdempotencyResponse(
    int StatusCode,
    string ResponseHeadersJson,
    string ResponseSummary);

public interface IIdempotencyExecutor
{
    Task<IdempotencyExecutionResult<T>> ExecuteAsync<T>(
        IdempotencyCommand command,
        Func<CancellationToken, Task<IdempotencyResponse<T>>> handler,
        Func<StoredIdempotencyResponse, CancellationToken, Task<T>> replayFactory,
        CancellationToken cancellationToken,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);
}

public static class IdempotencyErrorCodes
{
    public const string PayloadConflict = "idempotency_payload_conflict";
    public const string RequestInProgress = "idempotency_request_in_progress";
}

public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException(string errorCode, int? retryAfterSeconds = null)
        : base(errorCode)
    {
        ErrorCode = errorCode;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public string ErrorCode { get; }
    public int? RetryAfterSeconds { get; }
}
