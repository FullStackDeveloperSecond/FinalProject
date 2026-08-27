using DoSelect.Application.Orders;
using DoSelect.Application.Returns;
using DoSelect.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Returns;

/// <summary>
/// Kafen's own narrow read adapter onto haru's Order aggregate — mirrors the precedent already
/// set by Support's OrderOwnershipLookup. No shared Application-layer Order port exists in
/// origin/dev yet, so this reads Order/OrderItem/AssemblyJob directly (read-only; never writes
/// to another module's tables) rather than blocking on a port haru has not published.
/// </summary>
public sealed class ReturnOrderEligibilityLookup : IReturnOrderEligibilityPort
{
    private readonly DoSelectDbContext _dbContext;

    public ReturnOrderEligibilityLookup(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<OrderEligibilitySnapshot?> FindByPublicIdAsync(Guid orderPublicId, CancellationToken cancellationToken) =>
        BuildAsync(_dbContext.Orders.Where(o => o.PublicId == orderPublicId), cancellationToken);

    public Task<OrderEligibilitySnapshot?> FindByIdAsync(long orderId, CancellationToken cancellationToken) =>
        BuildAsync(_dbContext.Orders.Where(o => o.Id == orderId), cancellationToken);

    private async Task<OrderEligibilitySnapshot?> BuildAsync(IQueryable<Order> orderQuery, CancellationToken cancellationToken)
    {
        var order = await orderQuery.SingleOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            return null;
        }

        var startedAssemblyGroupKeys = await _dbContext.AssemblyJobs
            .Where(j => j.OrderId == order.Id && j.Status != AssemblyJobStatus.Pending)
            .Select(j => j.AssemblyGroupKey)
            .ToListAsync(cancellationToken);
        var startedSet = startedAssemblyGroupKeys.ToHashSet();

        var items = await _dbContext.OrderItems
            .Where(i => i.OrderId == order.Id)
            .Select(i => new EligibleOrderItem(
                i.Id,
                i.PublicId,
                i.SkuCodeSnapshot,
                i.ProductNameSnapshot,
                i.ReturnableQuantity,
                i.ReturnedQuantity,
                i.AssemblyGroupKey,
                i.AssemblyGroupKey != null && startedSet.Contains(i.AssemblyGroupKey.Value),
                i.FinalUnitPrice))
            .ToListAsync(cancellationToken);

        return new OrderEligibilitySnapshot(
            order.Id, order.PublicId, order.OrderNumber, order.MemberUserId,
            order.DeliveredAtUtc, order.ReturnPolicyVersion, order.RowVersion, items);
    }
}

/// <summary>
/// Validates the raw token claim extracted from the protected GuestOrderAccess authentication
/// ticket. Token hashing must use the same HMAC implementation as the mint flow; callers must
/// never pass the encrypted cookie ticket itself as though it were the raw token.
/// </summary>
public sealed class GuestOrderAccessValidator : IGuestOrderAccessValidator
{
    public const string GuestOrderAccessCookieName = ".DoSelect.GuestOrderAccess";

    private readonly DoSelectDbContext _dbContext;
    private readonly IGuestOrderAccessHasher _hasher;

    public GuestOrderAccessValidator(
        DoSelectDbContext dbContext,
        IGuestOrderAccessHasher hasher)
    {
        _dbContext = dbContext;
        _hasher = hasher;
    }

    public async Task<long?> ValidateAsync(
        string rawToken, long requestedOrderId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var tokenHash = _hasher.HashToken(rawToken);

        // GuestOrderAccessTokens has no uniqueness on OrderId alone (a guest may re-verify and
        // mint more than one token for the same order over time) — the hash must be part of the
        // lookup predicate itself, not checked afterward, or SingleOrDefaultAsync could throw on
        // a legitimate multi-token order.
        var token = await _dbContext.Set<GuestOrderAccessToken>()
            .SingleOrDefaultAsync(t => t.OrderId == requestedOrderId && t.TokenHash == tokenHash, cancellationToken);

        if (token is null || token.RevokedAtUtc is not null || token.ExpiresAtUtc <= nowUtc)
        {
            return null;
        }

        return token.OrderId;
    }

    public async Task<long?> ResolveOrderIdAsync(string rawToken, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var tokenHash = _hasher.HashToken(rawToken);
        var token = await _dbContext.Set<GuestOrderAccessToken>()
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (token is null || token.RevokedAtUtc is not null || token.ExpiresAtUtc <= nowUtc)
        {
            return null;
        }

        return token.OrderId;
    }
}
