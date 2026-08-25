using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using DoSelect.Application.Idempotency;
using DoSelect.Domain.Idempotency;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Idempotency;

public sealed class EfIdempotencyExecutor : IIdempotencyExecutor
{
    private const int RetryAfterSeconds = 3;
    private const int MaximumResponseSummaryBytes = 32 * 1024;
    private const int MaximumResponseHeadersBytes = 8_000;
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private readonly DoSelectDbContext _context;
    private readonly string _actorScopePepper;
    private readonly TimeProvider _timeProvider;

    public EfIdempotencyExecutor(
        DoSelectDbContext context,
        IOptions<IdempotencyOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var pepper = options.Value.ActorScopePepper;
        if (Encoding.UTF8.GetByteCount(pepper) < 32)
        {
            throw new InvalidOperationException(
                "Configuration key 'Idempotency:ActorScopePepper' must contain at least 32 UTF-8 bytes.");
        }

        _context = context;
        _actorScopePepper = pepper;
        _timeProvider = timeProvider;
    }

    public async Task<IdempotencyExecutionResult<T>> ExecuteAsync<T>(
        IdempotencyCommand command,
        Func<CancellationToken, Task<IdempotencyResponse<T>>> handler,
        Func<StoredIdempotencyResponse, CancellationToken, Task<T>> replayFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(replayFactory);

        if (_context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "The idempotency executor must own the business transaction.");
        }

        var actorScopeHash = command.ActorScope.ComputeHash(_actorScopePepper);
        var lockResource = CreateLockResource(actorScopeHash, command.Operation, command.Key);

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var transactionFinished = false;

        try
        {
            if (!await TryAcquireTransactionLockAsync(lockResource, transaction, cancellationToken))
            {
                throw new IdempotencyConflictException(
                    IdempotencyErrorCodes.RequestInProgress,
                    RetryAfterSeconds);
            }

            var existing = await _context.IdempotencyRecords.SingleOrDefaultAsync(
                record => record.ActorScopeHash == actorScopeHash &&
                          record.Operation == command.Operation &&
                          record.Key == command.Key,
                cancellationToken);
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            if (existing is not null &&
                existing.Status != IdempotencyStatus.Processing &&
                existing.ExpiresAtUtc <= now)
            {
                _context.IdempotencyRecords.Remove(existing);
                await _context.SaveChangesAsync(cancellationToken);
                existing = null;
            }

            if (existing is not null)
            {
                if (!CryptographicOperations.FixedTimeEquals(
                        existing.RequestHash,
                        command.RequestHash.Span))
                {
                    throw new IdempotencyConflictException(IdempotencyErrorCodes.PayloadConflict);
                }

                if (existing.Status == IdempotencyStatus.Processing)
                {
                    throw new IdempotencyConflictException(
                        IdempotencyErrorCodes.RequestInProgress,
                        RetryAfterSeconds);
                }

                if (existing.Status != IdempotencyStatus.Succeeded ||
                    existing.ResponseStatusCode is null ||
                    existing.ResponseHeadersJson is null ||
                    existing.ResponseSummary is null)
                {
                    throw new InvalidOperationException(
                        "The stored idempotency response is incomplete.");
                }

                var stored = new StoredIdempotencyResponse(
                    existing.ResponseStatusCode.Value,
                    existing.ResponseHeadersJson,
                    existing.ResponseSummary);
                await transaction.CommitAsync(cancellationToken);
                transactionFinished = true;
                var replayBody = await replayFactory(stored, cancellationToken);
                return new IdempotencyExecutionResult<T>(
                    stored.StatusCode,
                    replayBody,
                    stored.ResponseHeadersJson,
                    IsReplay: true);
            }

            var record = new IdempotencyRecord(
                actorScopeHash,
                command.Operation,
                command.Key,
                command.RequestHash.ToArray(),
                now.Add(Retention),
                now);
            _context.IdempotencyRecords.Add(record);
            await _context.SaveChangesAsync(cancellationToken);

            var response = await handler(cancellationToken);
            EnsureStoredResponseFits(response.ResponseHeadersJson, response.ResponseSummary);
            var completedAt = _timeProvider.GetUtcNow().UtcDateTime;
            record.Complete(
                response.StatusCode,
                response.ResponseHeadersJson,
                response.ResponseSummary,
                completedAt);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            transactionFinished = true;

            return new IdempotencyExecutionResult<T>(
                response.StatusCode,
                response.Body,
                response.ResponseHeadersJson,
                IsReplay: false);
        }
        catch
        {
            if (!transactionFinished)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            _context.ChangeTracker.Clear();
            throw;
        }
    }

    private static string CreateLockResource(
        byte[] actorScopeHash,
        string operation,
        string key)
    {
        var operationBytes = Encoding.UTF8.GetBytes(operation);
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var material = new byte[actorScopeHash.Length + operationBytes.Length + keyBytes.Length + 2];
        actorScopeHash.CopyTo(material, 0);
        material[actorScopeHash.Length] = 0;
        operationBytes.CopyTo(material, actorScopeHash.Length + 1);
        material[actorScopeHash.Length + operationBytes.Length + 1] = 0;
        keyBytes.CopyTo(material, actorScopeHash.Length + operationBytes.Length + 2);
        return "doselect:idempotency:" + Convert.ToHexString(SHA256.HashData(material));
    }

    private async Task<bool> TryAcquireTransactionLockAsync(
        string resource,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 0;
            SELECT @result;
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.DbType = DbType.String;
        parameter.Size = 255;
        parameter.Value = resource;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) >= 0;
    }

    private static void EnsureStoredResponseFits(string headersJson, string summary)
    {
        if (string.IsNullOrWhiteSpace(headersJson))
        {
            throw new ArgumentException("Response headers JSON is required.", nameof(headersJson));
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("Response summary is required.", nameof(summary));
        }

        if (Encoding.UTF8.GetByteCount(headersJson) > MaximumResponseHeadersBytes)
        {
            throw new ArgumentException(
                $"Response headers JSON cannot exceed {MaximumResponseHeadersBytes} UTF-8 bytes.",
                nameof(headersJson));
        }

        if (Encoding.UTF8.GetByteCount(summary) > MaximumResponseSummaryBytes)
        {
            throw new ArgumentException(
                $"Response summary cannot exceed {MaximumResponseSummaryBytes} UTF-8 bytes.",
                nameof(summary));
        }
    }
}
