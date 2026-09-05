using DoSelect.Application.Shipping;
using DoSelect.Application.Shopping;
using DoSelect.Application.Promotions;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Promotions;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shipping;

public sealed class EfShippingOptionsService : IShippingOptionsService
{
    private readonly DoSelectDbContext _dbContext;
    private readonly ICartService _cartService;
    private readonly ApplyCartCouponService _couponService;

    public EfShippingOptionsService(
        DoSelectDbContext dbContext,
        ICartService cartService,
        ApplyCartCouponService couponService)
    {
        _dbContext = dbContext;
        _cartService = cartService;
        _couponService = couponService;
    }

    public async Task<ShippingOptionsDto> GetOptionsForCartAsync(
        CartIdentity identity,
        CancellationToken cancellationToken,
        string? couponCode = null)
    {
        var cart = await _cartService.GetCartAsync(identity, cancellationToken);
        var methods = await _dbContext.ShippingMethods
            .AsNoTracking()
            .Where(method => method.IsActive)
            .OrderBy(method => method.SortOrder)
            .ThenBy(method => method.Code)
            .ToListAsync(cancellationToken);
        var contents = await CartContentsInspector.InspectAsync(_dbContext, cart, cancellationToken);

        CartCouponQuote? couponQuote = null;
        if (!string.IsNullOrWhiteSpace(couponCode))
        {
            var couponRequest = new ApplyCartCouponRequest(couponCode, cart.RowVersion);
            couponQuote = await _couponService.QuoteAsync(
                identity,
                couponRequest,
                cancellationToken,
                isAssemblyDeliveryOverride: contents.HasAssemblyItem);
            cart = couponQuote.Cart;
        }

        var now = DateTime.UtcNow;
        var packageEvaluations = await EvaluatePackageAgainstLimitsAsync(methods, contents, now, cancellationToken);

        // Eligible-items subtotal per 購物車、訂單、付款與物流.md's 免運門檻 rule ("使用優惠券折扣後的
        // 免運門檻只使用優惠券計算後的符合資格商品小計，不包含運費與組裝費。
        // 未提供優惠券時，沿用購物車既有的折扣欄位。
        var eligibleSubtotal = couponQuote is null
            ? cart.Amounts.Subtotal - cart.Amounts.ItemDiscount - cart.Amounts.CouponDiscount
            : couponQuote.Calculation.EligibleSubtotal - couponQuote.Calculation.DiscountAmount;

        var options = methods
            .Select(method => BuildOption(
                method,
                cart,
                eligibleSubtotal,
                couponQuote?.Calculation,
                contents,
                packageEvaluations))
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
            // 組長 PR #73 round-3, item 2 (裁定 B1)：可用性看時間窗，不看 Published 這個狀態字。排定
            // 未來生效的版本一發布，舊版本立刻變 Superseded 但窗口還沒到 cutoff——只篩 Published 會
            // 讓 cutoff 之前完全找不到有效 profile，物流整段空窗。Draft 以外＋窗內，同一瞬間至多一個。
            where providerCodes.Contains(profile.ProviderCode) &&
                profile.Status != ShippingProviderProfileStatuses.Draft &&
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
        CouponCalculationResult? coupon,
        CartShippingRelevantContents contents,
        IReadOnlyDictionary<string, PackageEvaluation> packageEvaluations)
    {
        var isAssemblyDelivery = method.Kind == ShippingMethodKinds.HomeDeliveryAssembly;
        var couponGrantsFreeShipping = coupon?.IsFreeShipping == true && !isAssemblyDelivery ||
            coupon?.IsAssemblyFreeShipping == true && isAssemblyDelivery;
        var fee = couponGrantsFreeShipping ||
            method.FreeShippingThreshold.HasValue && eligibleSubtotal >= method.FreeShippingThreshold.Value
            ? 0m
            : method.BaseFee;

        // Checkout requires an exact match between the cart's assembly state and the selected
        // shipping kind. Apply that same rule here so the options screen never offers a method
        // that EfCheckoutTransactionGateway will reject at submit time.
        var isAssemblyMismatch = contents.HasAssemblyItem != isAssemblyDelivery;
        packageEvaluations.TryGetValue(method.Code, out var packageEvaluation);
        var isPackageBlocked = packageEvaluation is { IsAllowed: false };

        var isEligible = method.IsActive &&
            !isAssemblyMismatch &&
            !isPackageBlocked;
        var ineligibleReasonCode = isAssemblyMismatch
            ? ShippingErrorCodes.ShippingMethodNotAllowed
            : isPackageBlocked
                ? packageEvaluation!.ReasonCode
                : null;

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
            ? PaymentMethodPolicy.PrepaidMethods.Append(PaymentMethod.CashOnDelivery).ToArray()
            : PaymentMethodPolicy.PrepaidMethods;

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

