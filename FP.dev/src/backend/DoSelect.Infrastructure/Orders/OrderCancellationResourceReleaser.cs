using DoSelect.Domain.Inventory;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Promotions;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Orders;

/// <summary>
/// Shared by EfOrderService (member/guest self-service cancel) and EfAdminOrderService (admin
/// cancel) so both cancellation paths release the same resources the same way — extracted after
/// alex's PR #47 review flagged that the admin path only flipped OrderStatus and left Active
/// InventoryReservations and reserved Coupon seats stuck (Alex review, 2026-08-28). Originally
/// EfOrderService-only (Alex PR #43 review A1: cancellation may not commit unless all active
/// reservations and coupon seats are returned in the same SaveChanges transaction as the order
/// transition).
/// </summary>
internal static class OrderCancellationResourceReleaser
{
    public const string InventoryReleaseReason = "order_cancelled";

    /// <summary>Throws <see cref="InvalidOperationException"/> on inconsistent inventory state —
    /// callers map it to their own module's write-exception type (OrderStateConflict).</summary>
    public static async Task ReleaseAsync(
        DoSelectDbContext dbContext,
        Order order,
        string? actorUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var reservations = await dbContext.InventoryReservations
            .Where(candidate => candidate.OrderId == order.Id &&
                candidate.Status == InventoryReservationStatus.Active)
            .OrderBy(candidate => candidate.SkuId)
            .ThenBy(candidate => candidate.Id)
            .ToListAsync(cancellationToken);
        var reservationGroups = reservations.GroupBy(candidate => candidate.SkuId).ToArray();
        var skuIds = reservationGroups.Select(group => group.Key).ToArray();
        var balances = await dbContext.InventoryBalances
            .Where(candidate => skuIds.Contains(candidate.SkuId))
            .ToDictionaryAsync(candidate => candidate.SkuId, cancellationToken);

        foreach (var group in reservationGroups)
        {
            var quantityToRelease = group.Sum(candidate => candidate.Quantity);
            if (!balances.TryGetValue(group.Key, out var balance) ||
                balance.ReservedQuantity < quantityToRelease)
            {
                throw new InvalidOperationException(
                    $"Inventory reservation state is inconsistent for order '{order.PublicId}'.");
            }

            var runningReservedQuantity = balance.ReservedQuantity;
            foreach (var reservation in group)
            {
                var afterReservedQuantity = runningReservedQuantity - reservation.Quantity;
                reservation.Release(InventoryReleaseReason, expired: false, releasedAtUtc: now);
                dbContext.InventoryMovements.Add(new InventoryMovement(
                    Guid.CreateVersion7(),
                    reservation.SkuId,
                    reservation.Id,
                    "Release",
                    onHandDelta: 0,
                    reservedDelta: -reservation.Quantity,
                    beforeOnHand: balance.OnHandQuantity,
                    afterOnHand: balance.OnHandQuantity,
                    beforeReserved: runningReservedQuantity,
                    afterReserved: afterReservedQuantity,
                    reasonCode: InventoryReleaseReason,
                    referenceType: "Order",
                    referencePublicId: order.PublicId,
                    actorUserId,
                    occurredAtUtc: now));
                runningReservedQuantity = afterReservedQuantity;
            }

            balance.ApplyQuantities(balance.OnHandQuantity, runningReservedQuantity, now);
        }

        var redemptions = await dbContext.CouponRedemptions
            .Where(candidate => candidate.OrderId == order.Id &&
                candidate.Status == CouponRedemptionStatus.Reserved)
            .ToListAsync(cancellationToken);
        foreach (var redemption in redemptions)
        {
            redemption.Release(now);
        }

        var releasedRedemptionIds = redemptions.Select(candidate => candidate.Id).ToArray();
        var couponIds = redemptions.Select(candidate => candidate.CouponId).Distinct().ToArray();
        var exhaustedCoupons = await dbContext.Coupons
            .Where(candidate => couponIds.Contains(candidate.Id) &&
                candidate.Status == CouponStatus.Exhausted)
            .ToListAsync(cancellationToken);
        foreach (var coupon in exhaustedCoupons)
        {
            var occupiedCount = await dbContext.CouponRedemptions
                .AsNoTracking()
                .Where(candidate => candidate.CouponId == coupon.Id &&
                    !releasedRedemptionIds.Contains(candidate.Id))
                .Where(CouponRuleReader.OccupiesUsageSeatAt(now))
                .CountAsync(cancellationToken);
            var usage = new CouponUsageState(occupiedCount, MemberRedeemedCount: 0);
            if (coupon.IsWithinUsagePeriod(now) &&
                coupon.HasCompleteDiscountRule &&
                coupon.HasRemainingQuota(usage))
            {
                coupon.ReactivateAfterQuotaRelease(usage, now);
            }
        }
    }
}
