using DoSelect.Application.Common;
using DoSelect.Application.Orders;
using DoSelect.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Orders;

public sealed class EfGuestOrderAccessGateway(DoSelectDbContext dbContext) : IGuestOrderAccessGateway
{
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

    public async Task AddRequestAsync(
        GuestOrderAccessRequest request, CancellationToken cancellationToken = default) =>
        await dbContext.GuestOrderAccessRequests.AddAsync(request, cancellationToken);

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

    public async Task<int> PurgeExpiredAsync(
        DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken = default)
    {
        var expiredRequestIds = await dbContext.GuestOrderAccessRequests
            .Where(r => r.ExpiresAtUtc < cutoffUtc)
            .OrderBy(r => r.Id)
            .Select(r => r.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var expiredTokenIds = await dbContext.GuestOrderAccessTokens
            .Where(t => t.ExpiresAtUtc < cutoffUtc)
            .OrderBy(t => t.Id)
            .Select(t => t.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (expiredTokenIds.Count > 0)
        {
            await dbContext.GuestOrderAccessTokens
                .Where(t => expiredTokenIds.Contains(t.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        // Token 對 Request 有外鍵（RequestId），先刪 Token 再刪 Request 才不會違反約束——
        // 到期的 Request 底下的 Token 理論上也早就到期，兩批清理最終會把兩邊都清乾淨,
        // 只是可能跨好幾天的執行周期。
        var deletedRequestCount = 0;
        if (expiredRequestIds.Count > 0)
        {
            var deletableRequestIds = await dbContext.GuestOrderAccessRequests
                .Where(r => expiredRequestIds.Contains(r.Id))
                .Where(r => !dbContext.GuestOrderAccessTokens.Any(t => t.RequestId == r.Id))
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);

            if (deletableRequestIds.Count > 0)
            {
                deletedRequestCount = await dbContext.GuestOrderAccessRequests
                    .Where(r => deletableRequestIds.Contains(r.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }

        return expiredTokenIds.Count + deletedRequestCount;
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
