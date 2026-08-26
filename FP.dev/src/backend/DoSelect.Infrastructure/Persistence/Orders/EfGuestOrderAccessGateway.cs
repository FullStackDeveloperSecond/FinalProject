using System.Data;
using DoSelect.Application.Common;
using DoSelect.Application.Orders;
using DoSelect.Domain.Orders;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Orders;

public sealed class EfGuestOrderAccessGateway(DoSelectDbContext dbContext) : IGuestOrderAccessGateway
{
    /// <summary>SQL Server 的死結受害者錯誤碼（比照 <c>RefundExecutor</c> 的既有判斷）。</summary>
    private const int DeadlockVictimErrorNumber = 1205;

    public async Task<GuestOrderLookup?> FindGuestOrderAsync(
        string orderNumber, string emailNormalized, CancellationToken cancellationToken = default)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Where(o =>
                o.OrderNumber == orderNumber &&
                o.GuestEmailNormalized != null &&
                o.GuestEmailNormalized == emailNormalized)
            .Select(o => new { o.Id, o.PublicId, o.OrderNumber, o.GuestEmailNormalized })
            .FirstOrDefaultAsync(cancellationToken);

        return order is null
            ? null
            : new GuestOrderLookup(order.Id, order.PublicId, order.OrderNumber, order.GuestEmailNormalized);
    }

    public async Task<GuestOrderLookup?> FindGuestOrderByIdAsync(
        long orderId, CancellationToken cancellationToken = default)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.Id, o.PublicId, o.OrderNumber, o.GuestEmailNormalized })
            .FirstOrDefaultAsync(cancellationToken);

        return order is null
            ? null
            : new GuestOrderLookup(order.Id, order.PublicId, order.OrderNumber, order.GuestEmailNormalized);
    }

    /// <summary>
    /// 在單一 Serializable 交易內原子核對三 Scope 15 分鐘視窗既有筆數（沿用既有的三組
    /// (Hash, CreatedAtUtc) 索引，DEC-P266，不新增限流表）；任一達到上限就整個 rollback、
    /// 不寫入，回傳 false。三者都通過才撤銷 <paramref name="requestToRevoke"/>（若不為
    /// null）、新增 <paramref name="newRequest"/> 並 commit。Serializable 隔離讓範圍查詢
    /// 期間不會被其他交易插入同一段索引範圍，同一組 Hash 的並行建立／重寄會彼此等待而不是
    /// 都讀到同一個舊計數——真的發生 SQL Server 死結／並行衝突時，改拋
    /// <see cref="DomainProblemException"/>（Code＝ConcurrencyConflict）交由呼叫端比照
    /// <see cref="ReloadRequestAsync"/> 既有的重試慣例處理，不在這裡默默重試（重試需要用
    /// 最新狀態重新跑一次 Domain 層的資格判斷，那是 Application／Domain 的責任）。
    /// </summary>
    public async Task<bool> TryCreateRequestWithinRateLimitAsync(
        GuestOrderAccessRateLimitWindow window,
        GuestOrderAccessRequest newRequest,
        GuestOrderAccessRequest? requestToRevoke,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        try
        {
            var ipCount = await dbContext.GuestOrderAccessRequests
                .Where(r => r.RequesterIpHash == window.IpHash && r.CreatedAtUtc > window.WindowStartUtc)
                .CountAsync(cancellationToken);
            var emailCount = await dbContext.GuestOrderAccessRequests
                .Where(r => r.EmailKeyHash == window.EmailHash && r.CreatedAtUtc > window.WindowStartUtc)
                .CountAsync(cancellationToken);
            var orderLookupCount = await dbContext.GuestOrderAccessRequests
                .Where(r =>
                    r.OrderLookupKeyHash == window.OrderLookupHash && r.CreatedAtUtc > window.WindowStartUtc)
                .CountAsync(cancellationToken);

            if (ipCount >= window.IpPermitLimit ||
                emailCount >= window.EmailPermitLimit ||
                orderLookupCount >= window.OrderLookupPermitLimit)
            {
                return false;
            }

            requestToRevoke?.Revoke(newRequest.CreatedAtUtc);
            await dbContext.GuestOrderAccessRequests.AddAsync(newRequest, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (IsRetryableConflict(exception))
        {
            // 交易已經（或即將）rollback——ChangeTracker 裡的異動（含 requestToRevoke 的
            // Revoke）要一併丟掉，不然呼叫端重試時會疊加在一個「以為已經改過」的追蹤實例上。
            dbContext.ChangeTracker.Clear();
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict,
                "The guest order access rate limit check was interrupted by a concurrent request.")
                .WithInnerException(exception);
        }
    }

    /// <summary>
    /// Resend 對查無此 PublicId／已完全失效的呼叫：沒有 Row 可寫入，也沒有其他 Scope 的
    /// Hash 可用，只唯讀核對 IP 這一個 Scope，不需要交易。
    /// </summary>
    public async Task<bool> IsIpWithinRateLimitAsync(
        byte[] ipHash, int permitLimit, DateTime windowStartUtc, CancellationToken cancellationToken = default)
    {
        var count = await dbContext.GuestOrderAccessRequests
            .AsNoTracking()
            .Where(r => r.RequesterIpHash == ipHash && r.CreatedAtUtc > windowStartUtc)
            .CountAsync(cancellationToken);
        return count < permitLimit;
    }

    /// <summary>
    /// 值得讓呼叫端重試整段操作的並行衝突：SQL Server 死結受害者，或樂觀鎖（RowVersion）
    /// 失敗——比照既有 <c>RefundExecutor</c> 的判斷，死結的 <see cref="SqlException"/>
    /// 會被層層包裝，因此往內層找。
    /// </summary>
    private static bool IsRetryableConflict(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return true;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: DeadlockVictimErrorNumber })
            {
                return true;
            }
        }

        return false;
    }

    public Task<GuestOrderAccessRequest?> FindActiveRequestAsync(
        Guid requestPublicId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        dbContext.GuestOrderAccessRequests
            .Where(r =>
                r.PublicId == requestPublicId &&
                r.ExpiresAtUtc > nowUtc &&
                r.ConsumedAtUtc == null &&
                r.LockedAtUtc == null &&
                r.RevokedAtUtc == null &&
                r.AttemptCount < GuestOrderAccessRequest.MaximumAttempts)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddTokenAsync(
        GuestOrderAccessToken token, CancellationToken cancellationToken = default) =>
        await dbContext.GuestOrderAccessTokens.AddAsync(token, cancellationToken);

    public async Task<GuestOrderAccessTokenContext?> FindTokenByHashAsync(
        byte[] tokenHash, CancellationToken cancellationToken = default)
    {
        var token = await dbContext.GuestOrderAccessTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
        if (token is null)
        {
            return null;
        }

        var orderPublicId = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.Id == token.OrderId)
            .Select(o => o.PublicId)
            .FirstAsync(cancellationToken);

        return new GuestOrderAccessTokenContext(token, orderPublicId);
    }

    public Task IncrementScopeViolationAsync(
        long tokenId, CancellationToken cancellationToken = default) =>
        dbContext.GuestOrderAccessTokens
            .Where(t => t.Id == tokenId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    t => t.ScopeViolationCount, t => t.ScopeViolationCount + 1),
                cancellationToken);

    public async Task<int> PurgeExpiredAsync(
        DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken = default)
    {
        var expiredTokenIds = await dbContext.GuestOrderAccessTokens
            .Where(t => t.ExpiresAtUtc < cutoffUtc)
            .OrderBy(t => t.Id)
            .Select(t => t.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var deletedTokenCount = 0;
        if (expiredTokenIds.Count > 0)
        {
            deletedTokenCount = await dbContext.GuestOrderAccessTokens
                .Where(t => expiredTokenIds.Contains(t.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        // Token 對 Request 有外鍵（RequestId），先刪 Token 再刪 Request 才不會違反約束——
        // 到期的 Request 底下的 Token 理論上也早就到期，兩批清理最終會把兩邊都清乾淨,
        // 只是可能跨好幾天的執行周期。DEC-P267：單一 batch 的 Request＋Token 刪除總量
        // 不得超過 batchSize，所以 Request 只能用 Token 花剩的預算，不能各自獨立取滿。
        var remainingBudget = batchSize - deletedTokenCount;
        var deletedRequestCount = 0;
        if (remainingBudget > 0)
        {
            var deletableRequestIds = await dbContext.GuestOrderAccessRequests
                .Where(r => r.ExpiresAtUtc < cutoffUtc)
                .Where(r => !dbContext.GuestOrderAccessTokens.Any(t => t.RequestId == r.Id))
                .OrderBy(r => r.Id)
                .Select(r => r.Id)
                .Take(remainingBudget)
                .ToListAsync(cancellationToken);

            if (deletableRequestIds.Count > 0)
            {
                deletedRequestCount = await dbContext.GuestOrderAccessRequests
                    .Where(r => deletableRequestIds.Contains(r.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }

        return deletedTokenCount + deletedRequestCount;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict,
                "The guest order access request was modified by another request.")
                .WithInnerException(exception);
        }
    }

    public Task ReloadRequestAsync(
        GuestOrderAccessRequest request, CancellationToken cancellationToken = default) =>
        dbContext.Entry(request).ReloadAsync(cancellationToken);
}
