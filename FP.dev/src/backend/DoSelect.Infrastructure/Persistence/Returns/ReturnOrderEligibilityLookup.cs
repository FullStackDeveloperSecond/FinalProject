using System.Security.Cryptography;
using System.Text;
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
/// Reads the already-finalized GuestOrderAccessTokens schema directly (haru's
/// Haru-會員登入訂單與訪客存取最終Schema.md §5.2). The mint flow (C-17 /guest-orders/verify,
/// UC-GUEST-ORDER-01) does not exist anywhere in origin/dev — only the schema and EF
/// configuration are merged — so no cookie NAME is fixed by any code yet. What the schema doc
/// *does* pin down, and this class follows exactly: `TokenHash` is "高熵 Token 的
/// SHA-256／HMAC-SHA-256，不存明文" (plain SHA-256 of the raw high-entropy token is explicitly
/// allowed, no shared pepper/secret required, unlike RequesterIpHash/EmailKeyHash/
/// OrderLookupKeyHash which are server-secret HMACs used only for rate-limiting), and the token
/// is valid for 30 minutes after issuance and reusable within that window. Only the literal
/// cookie name string remains unconfirmed pending haru's actual C-17 controller — update
/// <see cref="GuestOrderAccessCookieName"/> once that lands; see the implementation report for
/// the current status of this gap.
/// </summary>
public sealed class GuestOrderAccessValidator : IGuestOrderAccessValidator
{
    /// <summary>Provisional — no C-17 mint endpoint exists yet to define the real cookie name.</summary>
    public const string GuestOrderAccessCookieName = ".DoSelect.GuestOrderAccess";

    private readonly DoSelectDbContext _dbContext;

    public GuestOrderAccessValidator(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<long?> ValidateAsync(
        string rawToken, long requestedOrderId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));

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

        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var token = await _dbContext.Set<GuestOrderAccessToken>()
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (token is null || token.RevokedAtUtc is not null || token.ExpiresAtUtc <= nowUtc)
        {
            return null;
        }

        return token.OrderId;
    }
}
