using DoSelect.Domain.Promotions;

namespace DoSelect.Application.Promotions;

/// <summary>
/// Read-only decision used to decide whether an authenticated member should be shown a
/// member-only coupon in the cart. Checkout remains the authority that reserves a redemption.
/// </summary>
public sealed class MemberCouponVisibilityService(
    ICouponRuleReader ruleReader,
    TimeProvider timeProvider)
{
    public async Task<bool> ShouldDisplayAsync(
        string memberUserId,
        string couponCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(memberUserId) ||
            !CouponCode.TryNormalize(couponCode, out var normalizedCode))
        {
            return false;
        }

        var snapshot = await ruleReader.FindByCodeAsync(normalizedCode, cancellationToken);
        if (snapshot is null || !snapshot.Rule.MemberOnly)
        {
            return false;
        }

        var evaluatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (snapshot.Rule.Status != CouponStatus.Active ||
            evaluatedAtUtc < snapshot.Rule.StartsAtUtc ||
            evaluatedAtUtc >= snapshot.Rule.EndsAtUtc)
        {
            return false;
        }

        var usage = await ruleReader.GetUsageAsync(
            snapshot.CouponId,
            memberUserId.Trim(),
            guestUsageKeyHash: null,
            evaluatedAtUtc,
            cancellationToken);

        if ((snapshot.Rule.TotalUsageLimit is { } totalUsageLimit &&
                usage.TotalRedeemedCount >= totalUsageLimit) ||
            (snapshot.Rule.PerMemberLimit is { } perMemberLimit &&
                usage.MemberRedeemedCount >= perMemberLimit))
        {
            return false;
        }

        if (snapshot.Rule.MemberValidityMonths is not { } validityMonths)
        {
            return true;
        }

        var memberCreatedAtUtc = await ruleReader.GetMemberCreatedAtUtcAsync(
            memberUserId.Trim(), cancellationToken);
        return memberCreatedAtUtc is { } createdAtUtc &&
            evaluatedAtUtc < createdAtUtc.AddMonths(validityMonths);
    }
}
