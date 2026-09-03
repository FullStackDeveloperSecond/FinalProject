using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Shipping;
using DoSelect.Domain.Idempotency;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Shipping;

/// <summary>
/// 批次出貨的冪等（組長 PR #93 review item 1 與裁定 A1：「沿用既有 IdempotencyRecords」）。
///
/// 這裡刻意**不**直接用 <see cref="IIdempotencyExecutor"/>：它會把整個 handler 包在自己開的那一個
/// 交易裡，而批次出貨的核心不變量正好相反——「每筆訂單獨立驗證、獨立交易及獨立回傳結果」「一筆
/// 失敗不回滾其他已成功出貨的訂單」。把逐筆出貨塞進單一交易，等於把規格明令禁止的事做出來。
///
/// 所以這是 review 允許的「等價且維持逐筆獨立交易的設計」：沿用同一張 `IdempotencyRecords`、同一組
/// 鍵（Actor Scope Hash ＋ Operation ＋ Key）、同一個 Request Hash 與同一把 `sp_getapplock`，只是把
/// 交易切成三段——認領、逐筆出貨（各自的交易）、寫回結果。與 executor 的差別只有交易邊界。
///
/// 中斷的代價要講清楚：逐筆出貨跑到一半進程掛掉，記錄會停在 Processing，之後用同一把鍵重送一律
/// 回 `idempotency_request_in_progress`。這是安全的一邊——不會重複出貨，也不會謊報成功。要收拾殘局
/// 就換一把新的冪等鍵重送，已經出貨的那幾筆會被逐筆的「訂單已有出貨」擋下來。
/// </summary>
public sealed class BatchShipmentIdempotency
{
    public const string Operation = "shipping.batch";

    /// <summary>與 EfIdempotencyExecutor 相同的保存期限與回應上限。</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private const int MaximumResponseSummaryBytes = 32 * 1024;
    private const int RetryAfterSeconds = 3;

    private readonly DoSelectDbContext _dbContext;
    private readonly string _actorScopePepper;

    public BatchShipmentIdempotency(DoSelectDbContext dbContext, IOptions<IdempotencyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var pepper = options.Value.ActorScopePepper;
        if (Encoding.UTF8.GetByteCount(pepper) < 32)
        {
            throw new InvalidOperationException(
                "Configuration key 'Idempotency:ActorScopePepper' must contain at least 32 UTF-8 bytes.");
        }

        _dbContext = dbContext;
        _actorScopePepper = pepper;
    }

    /// <summary>
    /// 認領這把鍵。回傳既有結果代表這是重播，呼叫端不該再出任何一次貨；回傳 null 代表這是第一次，
    /// 記錄已經以 Processing 建立，呼叫端跑完之後必須呼叫 <see cref="CompleteAsync"/>。
    /// </summary>
    public async Task<BatchShipmentResultDto?> ClaimAsync(
        IdempotencyCommand command,
        DateTime now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var actorScopeHash = command.ActorScope.ComputeHash(_actorScopePepper);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            // 應用程式鎖擋住同一把鍵的兩個並行請求；沒有它，兩邊都會查不到記錄然後各出一次貨。
            if (!await TryAcquireLockAsync(
                    CreateLockResource(actorScopeHash, command.Operation, command.Key),
                    transaction,
                    cancellationToken))
            {
                throw new IdempotencyConflictException(
                    IdempotencyErrorCodes.RequestInProgress, RetryAfterSeconds);
            }

            var existing = await _dbContext.IdempotencyRecords.SingleOrDefaultAsync(
                record => record.ActorScopeHash == actorScopeHash &&
                          record.Operation == command.Operation &&
                          record.Key == command.Key,
                cancellationToken);

            if (existing is not null &&
                existing.Status != IdempotencyStatus.Processing &&
                existing.ExpiresAtUtc <= now)
            {
                _dbContext.IdempotencyRecords.Remove(existing);
                await _dbContext.SaveChangesAsync(cancellationToken);
                existing = null;
            }

            if (existing is not null)
            {
                // 同一把鍵配不同 payload：呼叫端把兩份不同的請求當成同一次，必須讓它知道。
                if (!CryptographicOperations.FixedTimeEquals(existing.RequestHash, command.RequestHash.Span))
                {
                    throw new IdempotencyConflictException(IdempotencyErrorCodes.PayloadConflict);
                }

                if (existing.Status == IdempotencyStatus.Processing)
                {
                    throw new IdempotencyConflictException(
                        IdempotencyErrorCodes.RequestInProgress, RetryAfterSeconds);
                }

                if (existing.Status != IdempotencyStatus.Succeeded || existing.ResponseSummary is null)
                {
                    throw new InvalidOperationException("The stored idempotency response is incomplete.");
                }

                var replay = Deserialize(existing.ResponseSummary);
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            _dbContext.IdempotencyRecords.Add(new IdempotencyRecord(
                actorScopeHash,
                command.Operation,
                command.Key,
                command.RequestHash.ToArray(),
                now.Add(Retention),
                now));
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>逐筆出貨全部跑完之後把結果寫回記錄，之後同一把鍵就會重播這一份。</summary>
    public async Task CompleteAsync(
        IdempotencyCommand command,
        BatchShipmentResultDto result,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var actorScopeHash = command.ActorScope.ComputeHash(_actorScopePepper);
        _dbContext.ChangeTracker.Clear();

        var record = await _dbContext.IdempotencyRecords.SingleOrDefaultAsync(
            candidate => candidate.ActorScopeHash == actorScopeHash &&
                         candidate.Operation == command.Operation &&
                         candidate.Key == command.Key,
            cancellationToken);
        if (record is null || record.Status != IdempotencyStatus.Processing)
        {
            // 有人在這批跑的期間清掉或完成了同一筆記錄。已經出的貨不會因此回來，所以不是錯誤；
            // 代價只是這把鍵之後不再重播。
            return;
        }

        record.Complete(StatusCodes.Ok, "{}", Serialize(result), now);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static class StatusCodes
    {
        public const int Ok = 200;
    }

    /// <summary>
    /// 存進 `ResponseSummary` 的形狀。用短鍵是因為那個欄位有 32 KB 上限，而一批最多 100 筆；
    /// 逐筆的列號、訂單、狀態、單號與錯誤碼都要原樣重播，那些是結果 CSV 的內容。
    /// </summary>
    private sealed record StoredItem(
        int R, Guid O, string? N, string S, string? T, string? E, string? M);

    private sealed record StoredResult(Guid B, DateTime C, IReadOnlyList<StoredItem> I);

    private static string Serialize(BatchShipmentResultDto result)
    {
        var stored = new StoredResult(
            result.BatchPublicId,
            result.CreatedAtUtc,
            result.Items.Select(item => new StoredItem(
                item.SourceRowNumber, item.OrderPublicId, item.OrderNumber,
                item.Status, item.TrackingNumber, item.ErrorCode, item.Message)).ToArray());

        var json = JsonSerializer.Serialize(stored);
        if (Encoding.UTF8.GetByteCount(json) <= MaximumResponseSummaryBytes)
        {
            return json;
        }

        // 一百筆全帶著長訊息時會超過欄位上限。錯誤碼是穩定契約、訊息只是輔助說明，所以先放掉
        // 訊息而不是讓整批在最後一步失敗——貨都已經出了。
        return JsonSerializer.Serialize(stored with
        {
            I = stored.I.Select(item => item with { M = null }).ToArray(),
        });
    }

    private static BatchShipmentResultDto Deserialize(string summary)
    {
        var stored = JsonSerializer.Deserialize<StoredResult>(summary)
            ?? throw new InvalidOperationException("The stored batch shipment result could not be read.");
        var items = stored.I
            .Select(item => new BatchShipmentItemResultDto(item.R, item.O, item.N, item.S, item.T, item.E, item.M))
            .ToArray();
        var succeeded = items.Count(item => item.ErrorCode is null);

        return new BatchShipmentResultDto(
            stored.B, items.Length, succeeded, items.Length - succeeded, items, stored.C, IsReplay: true);
    }

    /// <summary>與 EfIdempotencyExecutor 相同的鎖資源命名，兩邊對同一把鍵才會互斥。</summary>
    private static string CreateLockResource(byte[] actorScopeHash, string operation, string key)
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

    private async Task<bool> TryAcquireLockAsync(
        string resource,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
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
}
