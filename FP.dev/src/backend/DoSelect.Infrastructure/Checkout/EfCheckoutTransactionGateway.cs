using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoSelect.Application.Builds;
using DoSelect.Application.Checkout;
using DoSelect.Application.Common;
using DoSelect.Application.Promotions;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Promotions;
using DoSelect.Domain.Shopping;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DoSelect.Infrastructure.Checkout;

/// <summary>
/// Performs the authoritative Checkout read/revalidate/write operation inside the SQL transaction
/// owned by the central idempotency executor. This gateway deliberately never starts or commits a
/// transaction: order, stock reservation, coupon seat, payment attempt and cart conversion either
/// all persist or all roll back with the idempotency record.
/// </summary>
public sealed class EfCheckoutTransactionGateway : ICheckoutTransactionGateway
{
    private const decimal AssemblyFeePerGroup = 300m;
    private const string PublishedProviderStatus = "Published";
    private const string AssemblyShippingKind = "HomeDeliveryAssembly";
    private const string StorePickupShippingKind = "ConvenienceStorePickup";
    private const string PaymentProviderCode = "SIMULATED";
    private static readonly TimeSpan OrderPaymentLifetime = TimeSpan.FromDays(3);
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly DoSelectDbContext _context;
    private readonly ICompatibilityCatalogReader _compatibilityCatalogReader;
    private readonly ICouponRuleReader _couponRuleReader;
    private readonly IOrderNumberGenerator _orderNumberGenerator;
    private readonly TimeProvider _timeProvider;

    public EfCheckoutTransactionGateway(
        DoSelectDbContext context,
        ICompatibilityCatalogReader compatibilityCatalogReader,
        ICouponRuleReader couponRuleReader,
        IOrderNumberGenerator orderNumberGenerator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(compatibilityCatalogReader);
        ArgumentNullException.ThrowIfNull(couponRuleReader);
        ArgumentNullException.ThrowIfNull(orderNumberGenerator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _context = context;
        _compatibilityCatalogReader = compatibilityCatalogReader;
        _couponRuleReader = couponRuleReader;
        _orderNumberGenerator = orderNumberGenerator;
        _timeProvider = timeProvider;
    }

    public async Task<CheckoutCreatedOrder> ExecuteAsync(
        CheckoutCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureExistingTransaction();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var cart = await LoadAndValidateCartAsync(command, now, cancellationToken);
        var lines = await LoadAuthoritativeLinesAsync(cart.Id, now, cancellationToken);
        if (lines.Count == 0)
        {
            throw Conflict("cart_item_requires_attention", "The cart does not contain purchasable items.");
        }

        var assemblyGroups = lines
            .Where(line => line.AssemblyGroupKey.HasValue)
            .GroupBy(line => line.AssemblyGroupKey!.Value)
            .ToArray();
        await ValidateCompatibilityAsync(assemblyGroups, cancellationToken);

        var shipping = await ResolveShippingAsync(
            command,
            assemblyGroups.Length > 0,
            now,
            cancellationToken);
        var package = CalculateAndValidatePackage(lines, shipping.PackageLimit);
        var guestHash = command.Actor.IsMember ? null : HashGuestKey(command.Actor.GuestCartKey!);
        var coupon = await CalculateCouponAsync(
            command,
            lines,
            shipping.Method.Kind == AssemblyShippingKind,
            guestHash,
            now,
            cancellationToken);

        var merchandiseSubtotal = RoundMoney(lines.Sum(line => line.LineSubtotal));
        var itemDiscountTotal = coupon?.Result.DiscountAmount ?? 0m;
        var freeShippingBasis = coupon is null
            ? merchandiseSubtotal
            : RoundMoney(coupon.Result.EligibleSubtotal - coupon.Result.DiscountAmount);
        var thresholdReached = shipping.Method.FreeShippingThreshold is { } threshold &&
            freeShippingBasis >= threshold;
        var couponGrantsFreeShipping = coupon?.Result.IsFreeShipping == true ||
            coupon?.Result.IsAssemblyFreeShipping == true;
        var shippingFee = thresholdReached || couponGrantsFreeShipping
            ? 0m
            : shipping.Method.BaseFee;
        var assemblyFee = assemblyGroups.Length * AssemblyFeePerGroup;
        var grandTotal = Math.Round(
            merchandiseSubtotal - itemDiscountTotal + shippingFee + assemblyFee,
            0,
            MidpointRounding.AwayFromZero);
        if (grandTotal < 1m)
        {
            throw Conflict(
                DomainErrorCodes.OrderTotalBelowMinimum,
                "The order total must be at least TWD 1 after discounts and fees.");
        }

        ValidatePaymentMethod(
            command.PaymentMethod,
            shipping.Method,
            assemblyGroups.Length > 0,
            lines.Any(line => line.RequiresPrepayment),
            grandTotal);

        var isCashOnDelivery = PaymentMethodPolicy.KindOf(command.PaymentMethod) ==
            PaymentSettlementKind.CashOnDelivery;
        DateTime? paymentDueAtUtc = isCashOnDelivery ? null : now.Add(OrderPaymentLifetime);
        var orderStatus = isCashOnDelivery ? OrderStatus.Confirmed : OrderStatus.PendingPayment;
        var orderNumber = await _orderNumberGenerator.NextAsync(now, cancellationToken);
        var order = CreateOrder(
            command,
            lines,
            shipping,
            package,
            coupon,
            merchandiseSubtotal,
            itemDiscountTotal,
            shippingFee,
            assemblyFee,
            grandTotal,
            orderStatus,
            paymentDueAtUtc,
            orderNumber,
            now);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        await AddOrderItemsAsync(order.Id, lines, coupon, now, cancellationToken);
        await AddInitialOrderHistoriesAsync(order, now, cancellationToken);
        await AddAssemblyJobsAsync(order.Id, assemblyGroups, now, cancellationToken);
        await ReserveInventoryAsync(order, lines, paymentDueAtUtc, now, cancellationToken);
        await AddCouponReservationAsync(
            order,
            command,
            coupon,
            guestHash,
            paymentDueAtUtc,
            now,
            cancellationToken);

        var paymentAttempt = AddPaymentAttempt(
            order,
            command.PaymentMethod,
            paymentDueAtUtc,
            command.IdempotencyKey,
            now);
        cart.ChangeStatus(CartStatus.Converted, now);
        await _context.SaveChangesAsync(cancellationToken);

        return new CheckoutCreatedOrder(
            order.PublicId,
            order.OrderNumber,
            order.GrandTotal,
            order.Currency,
            paymentAttempt.PublicId,
            order.PaymentDueAtUtc);
    }

    public async Task<CheckoutCreatedOrder?> FindCreatedOrderAsync(
        Guid orderPublicId,
        CancellationToken cancellationToken = default)
    {
        var result = await (
                from order in _context.Orders.AsNoTracking()
                where order.PublicId == orderPublicId
                join attempt in _context.PaymentAttempts.AsNoTracking()
                    on order.Id equals attempt.OrderId
                orderby attempt.CreatedAtUtc
                select new CheckoutCreatedOrder(
                    order.PublicId,
                    order.OrderNumber,
                    order.GrandTotal,
                    order.Currency,
                    attempt.PublicId,
                    order.PaymentDueAtUtc))
            .FirstOrDefaultAsync(cancellationToken);
        return result;
    }

    private void EnsureExistingTransaction()
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Checkout must execute inside the transaction owned by the idempotency executor.");
        }
    }

    private async Task<Cart> LoadAndValidateCartAsync(
        CheckoutCommand command,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var cart = await _context.Carts
            .FromSqlInterpolated($"SELECT * FROM [Carts] WITH (UPDLOCK, HOLDLOCK) WHERE [PublicId] = {command.CartPublicId}")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw DomainProblemException.NotFound("The cart was not found.");

        if (cart.Status != CartStatus.Active || cart.ExpiresAtUtc <= now)
        {
            throw Conflict("cart_item_requires_attention", "The cart is no longer active.");
        }

        var ownsCart = command.Actor.IsMember
            ? string.Equals(cart.OwnerUserId, command.Actor.MemberUserId, StringComparison.Ordinal)
            : cart.GuestCartKeyHash is not null &&
              CryptographicOperations.FixedTimeEquals(
                  cart.GuestCartKeyHash,
                  HashGuestKey(command.Actor.GuestCartKey!));
        if (!ownsCart)
        {
            throw DomainProblemException.Forbidden("The cart does not belong to the current actor.");
        }

        if (!cart.RowVersion.AsSpan().SequenceEqual(command.CartRowVersion))
        {
            throw Conflict(PaymentErrorCodes.ConcurrencyConflict, "The cart has changed. Refresh it before Checkout.");
        }

        var hasBlockingMergeConflict = await _context.CartMergeConflicts.AsNoTracking()
            .AnyAsync(conflict => conflict.MemberCartId == cart.Id && conflict.ResolvedAtUtc == null,
                cancellationToken);
        if (hasBlockingMergeConflict)
        {
            throw Conflict("cart_item_requires_attention", "The cart has unresolved merge conflicts.");
        }

        return cart;
    }

    private async Task<IReadOnlyList<CheckoutLine>> LoadAuthoritativeLinesAsync(
        long cartId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from item in _context.CartItems.AsNoTracking()
                join sku in _context.Skus.AsNoTracking() on item.SkuId equals sku.Id
                join product in _context.Products.AsNoTracking() on sku.ProductId equals product.Id
                where item.CartId == cartId
                select new
                {
                    Item = item,
                    Sku = sku,
                    Product = product,
                })
            .ToListAsync(cancellationToken);

        if (rows.Any(row => row.Sku.Status != SkuStatus.Published ||
                            row.Product.Status != ProductStatus.Published))
        {
            throw Conflict("cart_item_requires_attention", "One or more cart items are no longer published.");
        }

        var skuIds = rows.Select(row => row.Sku.Id).Distinct().ToArray();
        var salePrices = await _context.SalePrices.AsNoTracking()
            .Where(price => skuIds.Contains(price.SkuId) &&
                            price.Status == SalePriceStatus.Active &&
                            price.StartsAtUtc <= now && now < price.EndsAtUtc)
            .ToListAsync(cancellationToken);
        if (salePrices.GroupBy(price => price.SkuId).Any(group => group.Count() > 1))
        {
            throw Conflict("sale_price_period_overlap", "A SKU has overlapping active sale prices.");
        }

        var saleBySku = salePrices.ToDictionary(price => price.SkuId);
        return rows.Select(row =>
        {
            var sale = saleBySku.GetValueOrDefault(row.Sku.Id);
            var finalUnitPrice = sale?.Price ?? row.Sku.ListPrice;
            return new CheckoutLine(
                row.Item.PublicId,
                row.Sku.Id,
                row.Sku.PublicId,
                row.Sku.SkuCode,
                row.Sku.NameZhTw,
                row.Sku.ListPrice,
                row.Sku.UnitCost,
                row.Sku.RequiresPrepayment,
                row.Sku.WeightKg,
                row.Sku.LengthCm,
                row.Sku.WidthCm,
                row.Sku.HeightCm,
                row.Product.Id,
                row.Product.CategoryId,
                row.Product.NameZhTw,
                row.Item.Quantity,
                row.Item.AssemblyGroupKey,
                finalUnitPrice,
                sale is not null);
        }).ToArray();
    }

    private async Task ValidateCompatibilityAsync(
        IReadOnlyCollection<IGrouping<Guid, CheckoutLine>> assemblyGroups,
        CancellationToken cancellationToken)
    {
        if (assemblyGroups.Count == 0)
        {
            return;
        }

        var settings = await ResolveCompatibilitySettingsAsync(cancellationToken);
        var catalog = CompatibilityRuleCatalog.CreateVersion1();
        foreach (var group in assemblyGroups)
        {
            var read = await _compatibilityCatalogReader.ReadAsync(
                group.Select(line => new CompatibilityItemReference(line.SkuPublicId, line.Quantity))
                    .ToArray(),
                cancellationToken);
            if (read.MissingSkuPublicIds.Count > 0)
            {
                throw Conflict("cart_item_requires_attention", "An assembly component is unavailable.");
            }

            var evaluation = CompatibilityEvaluator.Evaluate(read.Components, settings, catalog);
            if (evaluation.Overall is CompatibilityOverall.Blocked or
                CompatibilityOverall.InsufficientData)
            {
                throw Conflict("cart_item_requires_attention", "An assembly group is incompatible or lacks required specifications.");
            }
        }
    }

    private async Task<CompatibilityWarningSettings> ResolveCompatibilitySettingsAsync(
        CancellationToken cancellationToken)
    {
        var rows = await _context.CompatibilityRuleSettings.AsNoTracking()
            .OrderByDescending(setting => setting.SettingsVersion)
            .ToListAsync(cancellationToken);
        decimal Decimal(string code, decimal fallback) => rows
            .FirstOrDefault(setting => setting.SettingCode == code)?.DecimalValue ?? fallback;

        return new CompatibilityWarningSettings(
            Decimal("GpuClearanceWarningMm", 20m),
            Decimal("CoolerClearanceWarningMm", 10m),
            Decimal("PsuReserveWarningPercent", 35m),
            decimal.ToInt32(Decimal("RemainingRamSlotWarningCount", 0m)),
            decimal.ToInt32(Decimal("RemainingStoragePortWarningCount", 0m)));
    }

    private async Task<ResolvedShipping> ResolveShippingAsync(
        CheckoutCommand command,
        bool containsAssembly,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var method = await _context.ShippingMethods.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Code == command.ShippingMethodCode &&
                                               candidate.IsActive,
                cancellationToken)
            ?? throw Conflict("shipping_method_not_allowed", "The shipping method is unavailable.");
        if (string.IsNullOrWhiteSpace(method.ProviderCode))
        {
            throw Conflict("shipping_method_not_allowed", "The shipping method has no provider profile.");
        }

        if (containsAssembly != (method.Kind == AssemblyShippingKind))
        {
            throw Conflict("shipping_method_not_allowed", "The shipping method does not match the assembly contents.");
        }

        ConvenienceStore? store = null;
        if (method.Kind == StorePickupShippingKind)
        {
            if (!command.StorePublicId.HasValue)
            {
                throw Conflict("shipping_method_not_allowed", "A convenience store is required.");
            }

            store = await _context.ConvenienceStores.AsNoTracking()
                .SingleOrDefaultAsync(candidate =>
                        candidate.PublicId == command.StorePublicId.Value &&
                        candidate.ProviderCode == method.ProviderCode,
                    cancellationToken)
                ?? throw DomainProblemException.NotFound("The convenience store was not found.");
            if (!store.IsActive)
            {
                throw Conflict("shipping_store_inactive", "The convenience store is inactive.");
            }
        }
        else if (command.StorePublicId.HasValue)
        {
            throw Conflict("shipping_method_not_allowed", "A home-delivery address is required.");
        }

        var profiles = await _context.ShippingProviderProfiles.AsNoTracking()
            .Where(profile => profile.ProviderCode == method.ProviderCode &&
                              profile.Status == PublishedProviderStatus &&
                              (profile.EffectiveFromUtc == null || profile.EffectiveFromUtc <= now) &&
                              (profile.EffectiveToUtc == null || now < profile.EffectiveToUtc))
            .ToListAsync(cancellationToken);
        if (profiles.Count != 1)
        {
            throw Conflict("shipping_method_not_allowed", "The shipping provider profile is unavailable or ambiguous.");
        }

        var profile = profiles[0];
        var limits = await _context.PackageLimitVersions.AsNoTracking()
            .Where(limit => limit.ProviderProfileId == profile.Id &&
                            (limit.EffectiveFromUtc == null || limit.EffectiveFromUtc <= now) &&
                            (limit.EffectiveToUtc == null || now < limit.EffectiveToUtc))
            .ToListAsync(cancellationToken);
        if (limits.Count != 1)
        {
            throw Conflict("shipping_method_not_allowed", "The package-limit version is unavailable or ambiguous.");
        }

        return new ResolvedShipping(method, profile, limits[0], store);
    }

    private static CalculatedPackage CalculateAndValidatePackage(
        IReadOnlyCollection<CheckoutLine> lines,
        PackageLimitVersion limit)
    {
        var calculation = PackageSnapshotCalculator.Calculate(lines.Select(line =>
            new PackageItemDimensions(
                line.SkuCode,
                line.Quantity,
                line.WeightKg,
                line.LengthCm,
                line.WidthCm,
                line.HeightCm,
                line.FinalUnitPrice)).ToArray());
        if (!calculation.IsComplete)
        {
            throw Conflict("shipping_method_not_allowed", "Package dimensions are incomplete for one or more items.");
        }

        var package = calculation.Package!;
        var allowed = PackageConstraintEvaluator.Evaluate(
            package,
            new PackageLimits(
                limit.MaxWeightKg,
                limit.MaxLengthCm,
                limit.MaxWidthCm,
                limit.MaxHeightCm,
                limit.MaxTotalCm,
                limit.MaxDeclaredValue));
        if (!allowed.IsAllowed)
        {
            throw Conflict("shipping_constraint_exceeded", "The order exceeds the selected shipping constraints.");
        }

        return package;
    }

    private async Task<CalculatedCoupon?> CalculateCouponAsync(
        CheckoutCommand command,
        IReadOnlyList<CheckoutLine> lines,
        bool isAssemblyDelivery,
        byte[]? guestHash,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (command.CouponCode is null)
        {
            return null;
        }

        await AcquireTransactionLockAsync(
            "doselect:coupon:" + command.CouponCode,
            cancellationToken);
        var snapshot = await _couponRuleReader.FindByCodeAsync(command.CouponCode, cancellationToken)
            ?? throw DomainProblemException.BadRequest(CouponCalculationErrorCodes.CouponInvalid, "The coupon is invalid.");
        var usage = await _couponRuleReader.GetUsageAsync(
            snapshot.CouponId,
            command.Actor.MemberUserId,
            guestHash,
            now,
            cancellationToken);
        var result = CouponCalculator.Calculate(new CouponCalculationRequest(
            snapshot.Rule,
            snapshot.Scope,
            usage,
            lines.Select(line => new CouponCalculationLine(
                    line.CartItemPublicId,
                    line.ProductId,
                    [line.CategoryId],
                    line.Quantity,
                    line.FinalUnitPrice,
                    line.IsOnSale))
                .ToArray(),
            command.Actor.IsMember,
            isAssemblyDelivery,
            now));
        if (!result.IsSuccess)
        {
            throw Conflict(result.ErrorCode!, "The coupon cannot be applied to this Checkout.");
        }

        var eligibleLineIds = lines
            .Where(line => IsCouponEligible(line, snapshot.Rule, snapshot.Scope))
            .Select(line => line.CartItemPublicId)
            .ToHashSet();
        return new CalculatedCoupon(snapshot, result, eligibleLineIds);
    }

    private static bool IsCouponEligible(
        CheckoutLine line,
        CouponRule rule,
        CouponScopeRules scope)
    {
        if (scope.ExcludedProductIds.Contains(line.ProductId) ||
            rule.ExcludeSaleItems && line.IsOnSale)
        {
            return false;
        }

        return rule.ScopeType == CouponScopeType.All ||
            scope.IncludedProductIds.Contains(line.ProductId) ||
            scope.IncludedCategoryIds.Contains(line.CategoryId);
    }

    private static void ValidatePaymentMethod(
        PaymentMethod paymentMethod,
        ShippingMethod shippingMethod,
        bool containsAssembly,
        bool containsPrepaymentOnlySku,
        decimal grandTotal)
    {
        if (shippingMethod.RequiresPrepayment &&
            PaymentMethodPolicy.KindOf(paymentMethod) == PaymentSettlementKind.CashOnDelivery)
        {
            throw Conflict(PaymentErrorCodes.PaymentMethodNotAllowed, "The shipping method requires prepayment.");
        }

        if (PaymentMethodPolicy.KindOf(paymentMethod) != PaymentSettlementKind.CashOnDelivery)
        {
            return;
        }

        var rejection = PaymentAttemptPolicy.FindCashOnDeliveryRejection(
            new CashOnDeliveryEligibility(
                shippingMethod.AllowsCod,
                containsAssembly,
                containsPrepaymentOnlySku),
            grandTotal);
        if (rejection is not null)
        {
            throw Conflict(rejection, "Cash on delivery is not available for this order.");
        }
    }

    private static Order CreateOrder(
        CheckoutCommand command,
        IReadOnlyCollection<CheckoutLine> lines,
        ResolvedShipping shipping,
        CalculatedPackage package,
        CalculatedCoupon? coupon,
        decimal merchandiseSubtotal,
        decimal itemDiscountTotal,
        decimal shippingFee,
        decimal assemblyFee,
        decimal grandTotal,
        OrderStatus orderStatus,
        DateTime? paymentDueAtUtc,
        string orderNumber,
        DateTime now) =>
        Order.Create(
            Guid.CreateVersion7(),
            new OrderCreation(
                orderNumber,
                command.Actor.MemberUserId,
                command.Actor.IsMember ? null : command.Recipient.Email,
                orderStatus,
                PaymentStatus.AwaitingPayment,
                FulfillmentStatus.Pending,
                assemblyFee > 0m ? AssemblyStatus.Pending : AssemblyStatus.NotRequired,
                merchandiseSubtotal,
                itemDiscountTotal,
                shippingFee,
                assemblyFee,
                grandTotal,
                command.Recipient.Name,
                command.Recipient.Phone,
                command.Recipient.Email,
                command.Recipient.PostalCode,
                command.Recipient.City,
                command.Recipient.District,
                command.Recipient.AddressLine1,
                command.Recipient.AddressLine2,
                shipping.Method.Code,
                shipping.Profile.Id,
                shipping.Store?.StoreCode,
                shipping.Store?.StoreName,
                shipping.Store?.Address,
                command.PolicyVersions.ShippingConstraint,
                command.PolicyVersions.Return,
                coupon?.Snapshot.Rule.RuleVersion,
                paymentDueAtUtc,
                command.IdempotencyKey,
                command.CartPublicId,
                command.PolicyVersions.Terms,
                command.PolicyVersions.Privacy,
                command.InvoicePreference.ToDomain(),
                shipping.Method.FreeShippingThreshold,
                command.DeliveryNote,
                new OrderPackageSnapshot(
                    shipping.PackageLimit.Id,
                    package.WeightKg,
                    package.LengthCm,
                    package.WidthCm,
                    package.HeightCm,
                    package.TotalCm,
                    package.DeclaredValue),
                shipping.Method.BaseFee),
            now);

    private async Task AddOrderItemsAsync(
        long orderId,
        IReadOnlyList<CheckoutLine> lines,
        CalculatedCoupon? coupon,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var snapshots = await ReadSpecificationSnapshotsAsync(
            lines.Select(line => line.SkuId).ToArray(),
            cancellationToken);
        var allocations = coupon?.Result.Allocations.ToDictionary(item => item.LineId, item => item.Amount) ??
            new Dictionary<Guid, decimal>();
        var eligibleIds = coupon?.EligibleLineIds ?? new HashSet<Guid>();

        foreach (var line in lines)
        {
            var allocation = allocations.GetValueOrDefault(line.CartItemPublicId);
            var subtotal = RoundMoney(line.LineSubtotal);
            _context.OrderItems.Add(new OrderItem(
                Guid.CreateVersion7(),
                orderId,
                line.SkuId,
                line.SkuCode,
                line.ProductName,
                line.SkuName,
                line.Quantity,
                line.ListUnitPrice,
                line.FinalUnitPrice,
                line.FinalUnitPrice,
                line.UnitCost,
                subtotal,
                allocation,
                subtotal - allocation,
                line.AssemblyGroupKey,
                line.Quantity,
                now,
                eligibleIds.Contains(line.CartItemPublicId),
                snapshots.GetValueOrDefault(line.SkuId) ?? EmptySpecificationSnapshot));
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyDictionary<long, OrderItemSpecificationSnapshot>> ReadSpecificationSnapshotsAsync(
        long[] skuIds,
        CancellationToken cancellationToken)
    {
        var scalar = await (
                from value in _context.SkuSpecificationValues.AsNoTracking()
                join definition in _context.SpecificationDefinitions.AsNoTracking()
                    on value.SpecificationDefinitionId equals definition.Id
                join option in _context.SpecificationOptions.AsNoTracking()
                    on value.OptionId equals option.Id into optionJoin
                from option in optionJoin.DefaultIfEmpty()
                where skuIds.Contains(value.SkuId) && definition.IsActive
                select new SpecificationSnapshotValue(
                    value.SkuId,
                    definition.SemanticKey,
                    value.StringValue,
                    value.DecimalValue,
                    value.BooleanValue,
                    option == null ? null : option.Code))
            .ToListAsync(cancellationToken);
        var multi = await (
                from selection in _context.SkuSpecificationOptionSelections.AsNoTracking()
                join option in _context.SpecificationOptions.AsNoTracking()
                    on selection.SpecificationOptionId equals option.Id
                join definition in _context.SpecificationDefinitions.AsNoTracking()
                    on option.SpecificationDefinitionId equals definition.Id
                where skuIds.Contains(selection.SkuId) && definition.IsActive && option.IsActive
                select new { selection.SkuId, definition.SemanticKey, option.Code })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<long, OrderItemSpecificationSnapshot>();
        foreach (var skuId in skuIds.Distinct())
        {
            var values = scalar.Where(value => value.SkuId == skuId)
                .Select(value => new
                {
                    semanticKey = value.SemanticKey,
                    value = value.OptionCode ?? value.StringValue ??
                        value.DecimalValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ??
                        value.BooleanValue?.ToString(),
                })
                .Concat(multi.Where(value => value.SkuId == skuId)
                    .GroupBy(value => value.SemanticKey)
                    .Select(group => new
                    {
                        semanticKey = group.Key,
                        value = (string?)string.Join(",", group.Select(value => value.Code).Order(StringComparer.Ordinal)),
                    }))
                .OrderBy(value => value.semanticKey, StringComparer.Ordinal)
                .ToArray();
            var summary = values.Length == 0
                ? "無規格快照"
                : string.Join("；", values.Select(value => $"{value.semanticKey}:{value.value}"));
            if (summary.Length > 1000)
            {
                summary = summary[..1000];
            }

            var json = JsonSerializer.Serialize(
                new { schemaVersion = 1, items = values },
                SnapshotJsonOptions);
            if (json.Length > 4000)
            {
                throw Conflict("cart_item_requires_attention", "The specification snapshot is too large.");
            }

            result[skuId] = new OrderItemSpecificationSnapshot(summary, json, 1);
        }

        return result;
    }

    private async Task AddInitialOrderHistoriesAsync(
        Order order,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        var actor = order.MemberUserId;
        foreach (var entry in new[]
                 {
                     (OrderStateDimension.OrderStatus, order.OrderStatus.ToString()),
                     (OrderStateDimension.PaymentStatus, order.PaymentStatus.ToString()),
                     (OrderStateDimension.FulfillmentStatus, order.FulfillmentStatus.ToString()),
                     (OrderStateDimension.AssemblyStatus, order.AssemblyStatus.ToString()),
                     (OrderStateDimension.OrderRefundStatus, order.OrderRefundStatus.ToString()),
                 })
        {
            _context.OrderStatusHistories.Add(new OrderStatusHistory(
                Guid.CreateVersion7(), order.Id, entry.Item1, null, entry.Item2,
                "checkout_created", actor, now, traceId));
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task AddAssemblyJobsAsync(
        long orderId,
        IReadOnlyCollection<IGrouping<Guid, CheckoutLine>> assemblyGroups,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (assemblyGroups.Count == 0)
        {
            return;
        }

        var jobs = assemblyGroups.Select(group =>
            new AssemblyJob(Guid.CreateVersion7(), orderId, group.Key, now)).ToArray();
        _context.AssemblyJobs.AddRange(jobs);
        await _context.SaveChangesAsync(cancellationToken);
        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        _context.AssemblyJobStatusHistories.AddRange(jobs.Select(job =>
            new AssemblyJobStatusHistory(
                Guid.CreateVersion7(), job.Id, null, AssemblyJobStatus.Pending,
                "checkout_created", null, now, traceId)));
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task ReserveInventoryAsync(
        Order order,
        IReadOnlyCollection<CheckoutLine> lines,
        DateTime? expiresAtUtc,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (var line in lines.OrderBy(line => line.SkuId))
        {
            var balance = await _context.InventoryBalances
                .FromSqlInterpolated($"SELECT * FROM [InventoryBalances] WITH (UPDLOCK, HOLDLOCK) WHERE [SkuId] = {line.SkuId}")
                .SingleOrDefaultAsync(cancellationToken);
            if (balance is null || balance.AvailableQuantity < line.Quantity)
            {
                throw Conflict("inventory_insufficient", $"Inventory is insufficient for SKU {line.SkuCode}.");
            }

            var beforeReserved = balance.ReservedQuantity;
            balance.ApplyQuantities(
                balance.OnHandQuantity,
                checked(balance.ReservedQuantity + line.Quantity),
                now);
            var reservation = new InventoryReservation(
                Guid.CreateVersion7(), line.SkuId, order.Id, line.Quantity, expiresAtUtc, now);
            _context.InventoryReservations.Add(reservation);
            await _context.SaveChangesAsync(cancellationToken);
            _context.InventoryMovements.Add(new InventoryMovement(
                Guid.CreateVersion7(),
                line.SkuId,
                reservation.Id,
                "Reserve",
                0,
                line.Quantity,
                balance.OnHandQuantity,
                balance.OnHandQuantity,
                beforeReserved,
                balance.ReservedQuantity,
                line.UnitCost,
                "checkout_created",
                "Order",
                order.PublicId,
                order.MemberUserId,
                now));
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task AddCouponReservationAsync(
        Order order,
        CheckoutCommand command,
        CalculatedCoupon? coupon,
        byte[]? guestHash,
        DateTime? expiresAtUtc,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (coupon is null)
        {
            return;
        }

        var redemption = new CouponRedemption(
            Guid.CreateVersion7(),
            coupon.Snapshot.CouponId,
            order.Id,
            command.Actor.MemberUserId,
            guestHash,
            now,
            expiresAtUtc,
            now);
        _context.CouponRedemptions.Add(redemption);
        await _context.SaveChangesAsync(cancellationToken);
        var rule = coupon.Snapshot.Rule;
        _context.OrderCoupons.Add(new OrderCoupon(
            Guid.CreateVersion7(),
            order.Id,
            coupon.Snapshot.CouponId,
            redemption.Id,
            rule.Code,
            coupon.Snapshot.NameZhTw,
            rule.DiscountType,
            rule.RuleVersion,
            rule.DiscountValue,
            rule.MinimumSpend,
            coupon.Result.DiscountAmount,
            coupon.Result.EligibleSubtotal,
            coupon.Result.IsFreeShipping || coupon.Result.IsAssemblyFreeShipping,
            now));
        await _context.SaveChangesAsync(cancellationToken);
    }

    private PaymentAttempt AddPaymentAttempt(
        Order order,
        PaymentMethod method,
        DateTime? orderPaymentDueAtUtc,
        string checkoutIdempotencyKey,
        DateTime now)
    {
        var attempt = new PaymentAttempt(
            Guid.CreateVersion7(),
            order.Id,
            method,
            order.GrandTotal,
            PaymentProviderCode,
            checkoutIdempotencyKey + ":initial-payment",
            PaymentMethodPolicy.ResolveInstructionExpiry(method, now, orderPaymentDueAtUtc),
            now);
        attempt.SetPaymentInstruction("SIM-" + attempt.PublicId.ToString("N"), now);
        _context.PaymentAttempts.Add(attempt);
        return attempt;
    }

    private async Task AcquireTransactionLockAsync(
        string resource,
        CancellationToken cancellationToken)
    {
        var transaction = _context.Database.CurrentTransaction ??
            throw new InvalidOperationException("An active SQL transaction is required.");
        var connection = _context.Database.GetDbConnection();
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
        if (Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) < 0)
        {
            throw Conflict("coupon_usage_exhausted", "The coupon is being used by another Checkout. Retry shortly.");
        }
    }

    private static byte[] HashGuestKey(string guestCartKey) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(guestCartKey));

    private static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static DomainProblemException Conflict(string code, string message) =>
        DomainProblemException.Conflict(code, message);

    private static OrderItemSpecificationSnapshot EmptySpecificationSnapshot { get; } =
        new("無規格快照", "{\"schemaVersion\":1,\"items\":[]}", 1);

    private sealed record CheckoutLine(
        Guid CartItemPublicId,
        long SkuId,
        Guid SkuPublicId,
        string SkuCode,
        string SkuName,
        decimal ListUnitPrice,
        decimal UnitCost,
        bool RequiresPrepayment,
        decimal? WeightKg,
        decimal? LengthCm,
        decimal? WidthCm,
        decimal? HeightCm,
        long ProductId,
        long CategoryId,
        string ProductName,
        int Quantity,
        Guid? AssemblyGroupKey,
        decimal FinalUnitPrice,
        bool IsOnSale)
    {
        public decimal LineSubtotal => FinalUnitPrice * Quantity;
    }

    private sealed record ResolvedShipping(
        ShippingMethod Method,
        ShippingProviderProfile Profile,
        PackageLimitVersion PackageLimit,
        ConvenienceStore? Store);

    private sealed record CalculatedCoupon(
        CouponRuleSnapshot Snapshot,
        CouponCalculationResult Result,
        IReadOnlySet<Guid> EligibleLineIds);

    private sealed record SpecificationSnapshotValue(
        long SkuId,
        string SemanticKey,
        string? StringValue,
        decimal? DecimalValue,
        bool? BooleanValue,
        string? OptionCode);
}
