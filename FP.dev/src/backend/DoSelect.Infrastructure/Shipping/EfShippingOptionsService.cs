using DoSelect.Application.Shipping;
using DoSelect.Application.Shopping;
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

        // Eligible-items subtotal per 購物車、訂單、付款與物流.md's 免運門檻 rule ("使用優惠券折扣後的
        // 符合資格商品小計判斷,不包含運費及組裝費") — ItemDiscount/CouponDiscount are always 0 today
        // (coupon integration is yinyin's slice, not wired into Cart yet), so this is currently
        // just Subtotal, but stays correct once those stop being hardcoded zero.
        var eligibleSubtotal = cart.Amounts.Subtotal - cart.Amounts.ItemDiscount - cart.Amounts.CouponDiscount;

        var options = methods
            .Select(method => BuildOption(method, cart, eligibleSubtotal, contents))
            .ToList();

        return new ShippingOptionsDto(cart.PublicId, options, DateTime.UtcNow, cart.RowVersion);
    }

    private static ShippingOptionDto BuildOption(
        ShippingMethod method,
        CartDto cart,
        decimal eligibleSubtotal,
        CartShippingRelevantContents contents)
    {
        var fee = method.FreeShippingThreshold.HasValue && eligibleSubtotal >= method.FreeShippingThreshold.Value
            ? 0m
            : method.BaseFee;

        // Storage/package-limit-based ineligibility (size/weight exceeding the currently
        // published PackageLimitVersion) is deferred to the shipping-admin slice, which owns
        // publishing that config — until it exists there's nothing to check it against. Only
        // the assembly-item exclusion from 購物車、訂單、付款與物流.md line 199 ("組裝電腦、螢幕及任何
        // 超過尺寸或重量限制的商品只能選擇宅配") is enforced here so far.
        var isStorePickupBlockedByAssembly = method.Kind == ShippingMethodKinds.StorePickup && contents.HasAssemblyItem;

        var isEligible = method.IsActive && !isStorePickupBlockedByAssembly;
        var ineligibleReasonCode = isStorePickupBlockedByAssembly ? ShippingErrorCodes.ShippingMethodNotAllowed : null;

        var finalPayableAmount = cart.Amounts.TotalEstimate + fee;
        var codEligibility = CodEligibilityRules.Evaluate(
            method,
            finalPayableAmount,
            contents.HasAssemblyItem,
            contents.HasPrepaymentRequiredSku);
        var allowedPaymentMethods = codEligibility.IsEligible
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

internal static class ShippingMethodKinds
{
    internal const string StorePickup = "StorePickup";
    internal const string HomeDelivery = "HomeDelivery";
    internal const string HomeDeliveryAssembly = "HomeDeliveryAssembly";
}
