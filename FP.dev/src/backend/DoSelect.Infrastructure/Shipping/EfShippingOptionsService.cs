using DoSelect.Application.Shipping;
using DoSelect.Application.Shopping;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shipping;

public sealed class EfShippingOptionsService : IShippingOptionsService
{
    private readonly DoSelectDbContext _dbContext;
    private readonly ICartService _cartService;

    public EfShippingOptionsService(DoSelectDbContext dbContext, ICartService cartService)
    {
        _dbContext = dbContext;
        _cartService = cartService;
    }

    public async Task<ShippingOptionsDto> GetOptionsForCartAsync(
        CartIdentity identity,
        CancellationToken cancellationToken)
    {
        var cart = await _cartService.GetCartAsync(identity, cancellationToken);
        var contents = await CartContentsInspector.InspectAsync(_dbContext, cart, cancellationToken);

        var methods = await _dbContext.ShippingMethods
            .AsNoTracking()
            .Where(method => method.IsActive)
            .OrderBy(method => method.SortOrder)
            .ThenBy(method => method.Code)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var packageEvaluations = await EvaluatePackageAgainstLimitsAsync(methods, contents, now, cancellationToken);

        // Eligible-items subtotal per 購物車、訂單、付款與物流.md's 免運門檻 rule ("使用優惠券折扣後的
        // 符合資格商品小計判斷,不包含運費及組裝費") — ItemDiscount/CouponDiscount are always 0 today
        // (coupon integration is yinyin's slice, not wired into Cart yet), so this is currently
        // just Subtotal, but stays correct once those stop being hardcoded zero.
        var eligibleSubtotal = cart.Amounts.Subtotal - cart.Amounts.ItemDiscount - cart.Amounts.CouponDiscount;

        var options = methods
            .Select(method => BuildOption(method, cart, eligibleSubtotal, contents, packageEvaluations))
            .ToList();

        return new ShippingOptionsDto(cart.PublicId, options, now, cart.RowVersion);
    }

    private sealed record PackageEvaluation(bool IsAllowed, string ReasonCode);

    /// <summary>
    /// 組長 PR #73 review item 3: the options screen must apply the same effective
    /// PackageLimitVersion checkout enforces, or oversized/overweight carts see "超取可用" right up
    /// until checkout rejects them. Reuses the canonical PackageSnapshotCalculator／
    /// PackageConstraintEvaluator pair checkout itself calls, resolving each method's effective
    /// limit exactly the way EfCheckoutTransactionGateway does (one Published profile in window,
    /// one effective PackageLimitVersion). A method whose provider cannot resolve that pair is
    /// reported ineligible here for the same reason checkout would refuse it.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, PackageEvaluation>> EvaluatePackageAgainstLimitsAsync(
        IReadOnlyList<ShippingMethod> methods,
        CartShippingRelevantContents contents,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var evaluations = new Dictionary<string, PackageEvaluation>(StringComparer.Ordinal);
        if (contents.PackageItems.Count == 0)
        {
            return evaluations;
        }

        var calculation = PackageSnapshotCalculator.Calculate(contents.PackageItems);

        var providerCodes = methods
            .Where(method => method.ProviderCode is not null)
            .Select(method => method.ProviderCode!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var limitsByProvider = await (
            from profile in _dbContext.ShippingProviderProfiles.AsNoTracking()
            join limit in _dbContext.PackageLimitVersions.AsNoTracking()
                on profile.Id equals limit.ProviderProfileId
            where providerCodes.Contains(profile.ProviderCode) &&
                profile.Status == ShippingProviderProfileStatuses.Published &&
                (profile.EffectiveFromUtc == null || profile.EffectiveFromUtc <= now) &&
                (profile.EffectiveToUtc == null || now < profile.EffectiveToUtc) &&
                (limit.EffectiveFromUtc == null || limit.EffectiveFromUtc <= now) &&
                (limit.EffectiveToUtc == null || now < limit.EffectiveToUtc)
            select new { profile.ProviderCode, limit })
            .ToListAsync(cancellationToken);

        foreach (var method in methods)
        {
            if (method.ProviderCode is null)
            {
                // Checkout refuses a method with no provider outright.
                evaluations[method.Code] = new PackageEvaluation(false, ShippingErrorCodes.ShippingMethodNotAllowed);
                continue;
            }

            var matching = limitsByProvider.Where(pair => pair.ProviderCode == method.ProviderCode).ToList();
            if (matching.Count != 1)
            {
                // Zero or ambiguous effective limits — checkout would throw
                // shipping_method_not_allowed for the same reason.
                evaluations[method.Code] = new PackageEvaluation(false, ShippingErrorCodes.ShippingMethodNotAllowed);
                continue;
            }

            if (!calculation.IsComplete)
            {
                // Checkout rejects incomplete dimensions for every limited method; surface that
                // here instead of letting the shopper discover it at submit.
                evaluations[method.Code] = new PackageEvaluation(false, ShippingErrorCodes.ShippingMethodNotAllowed);
                continue;
            }

            var limit = matching[0].limit;
            var allowed = PackageConstraintEvaluator.Evaluate(
                calculation.Package!,
                new PackageLimits(
                    limit.MaxWeightKg,
                    limit.MaxLengthCm,
                    limit.MaxWidthCm,
                    limit.MaxHeightCm,
                    limit.MaxTotalCm,
                    limit.MaxDeclaredValue));
            evaluations[method.Code] = allowed.IsAllowed
                ? new PackageEvaluation(true, string.Empty)
                // API錯誤碼目錄.md: shipping_constraint_exceeded — 超過有效包裹尺寸或重量限制.
                : new PackageEvaluation(false, "shipping_constraint_exceeded");
        }

        return evaluations;
    }

    private static ShippingOptionDto BuildOption(
        ShippingMethod method,
        CartDto cart,
        decimal eligibleSubtotal,
        CartShippingRelevantContents contents,
        IReadOnlyDictionary<string, PackageEvaluation> packageEvaluations)
    {
        var fee = method.FreeShippingThreshold.HasValue && eligibleSubtotal >= method.FreeShippingThreshold.Value
            ? 0m
            : method.BaseFee;

        // 組裝擋超取 (購物車、訂單、付款與物流.md: "組裝電腦、螢幕及任何超過尺寸或重量限制的商品只能
        // 選擇宅配") takes precedence in the reported reason; otherwise the package-limit
        // evaluation computed against the effective PackageLimitVersion decides.
        var isStorePickupBlockedByAssembly = method.Kind == ShippingMethodKinds.StorePickup && contents.HasAssemblyItem;
        packageEvaluations.TryGetValue(method.Code, out var packageEvaluation);
        var isPackageBlocked = packageEvaluation is { IsAllowed: false };

        var isEligible = method.IsActive && !isStorePickupBlockedByAssembly && !isPackageBlocked;
        var ineligibleReasonCode = isStorePickupBlockedByAssembly
            ? ShippingErrorCodes.ShippingMethodNotAllowed
            : isPackageBlocked ? packageEvaluation!.ReasonCode : null;

        var finalPayableAmount = cart.Amounts.TotalEstimate + fee;
        // The COD go/no-go shown here must be the same decision Checkout enforces at order
        // creation, so this delegates to the canonical PaymentAttemptPolicy (yinyin's #9, the
        // one EfCheckoutTransactionGateway already calls) instead of keeping a parallel rule
        // set - the same class of duplication 組長 had removed in PR #34 for compatibility.
        var codRejection = PaymentAttemptPolicy.FindCashOnDeliveryRejection(
            new CashOnDeliveryEligibility(
                method.AllowsCod,
                contents.HasAssemblyItem,
                contents.HasPrepaymentRequiredSku),
            finalPayableAmount);
        var allowedPaymentMethods = codRejection is null
            ? new[] { "prepaid", "cashOnDelivery" }
            : new[] { "prepaid" };

        return new ShippingOptionDto(
            method.Code,
            method.NameZhTw,
            fee,
            isEligible,
            ineligibleReasonCode,
            method.FreeShippingThreshold,
            RequiresAddress: method.Kind != ShippingMethodKinds.StorePickup,
            RequiresStore: method.Kind == ShippingMethodKinds.StorePickup,
            allowedPaymentMethods);
    }

}

