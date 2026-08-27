using System.Security.Cryptography;
using System.Text;
using DoSelect.Application.Builds;
using DoSelect.Application.Checkout;
using DoSelect.Application.Common;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Promotions;
using DoSelect.Domain.Shopping;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Catalog;
using DoSelect.Infrastructure.Checkout;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Checkout;

[CollectionDefinition(nameof(EfCheckoutTransactionGatewayCollection))]
public sealed class EfCheckoutTransactionGatewayCollection
    : ICollectionFixture<EfCheckoutTransactionGatewayFixture>;

[Collection(nameof(EfCheckoutTransactionGatewayCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfCheckoutTransactionGatewayTests
{
    private static readonly DateTime NowUtc =
        new(2026, 8, 27, 6, 0, 0, DateTimeKind.Utc);

    [global::DoSelect.Infrastructure.Tests.Idempotency.SqlServerFact]
    public async Task ExecuteAsync_CreatesTheAtomicCheckoutAggregate()
    {
        var seed = await SeedAsync(onHandQuantity: 5);
        await using var context = EfCheckoutTransactionGatewayFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var gateway = CreateGateway(context);

        var created = await gateway.ExecuteAsync(seed.Command);
        await transaction.CommitAsync();

        await using var verification = EfCheckoutTransactionGatewayFixture.CreateContext();
        var order = await verification.Orders.SingleAsync(candidate => candidate.PublicId == created.PublicId);
        var reservation = await verification.InventoryReservations.SingleAsync(candidate => candidate.OrderId == order.Id);
        var attempt = await verification.PaymentAttempts.SingleAsync(candidate => candidate.OrderId == order.Id);
        var cart = await verification.Carts.SingleAsync(candidate => candidate.PublicId == seed.Command.CartPublicId);
        var balance = await verification.InventoryBalances.SingleAsync(candidate => candidate.SkuId == reservation.SkuId);

        Assert.Equal(1_150m, order.GrandTotal);
        Assert.Equal(1_000m, order.MerchandiseSubtotal);
        Assert.Equal(150m, order.ShippingFee);
        Assert.Equal(150m, order.ShippingMethodBaseFeeSnapshot);
        Assert.Equal(DoSelect.Domain.Orders.OrderStatus.PendingPayment, order.OrderStatus);
        Assert.Equal(DoSelect.Domain.Orders.PaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Equal(DoSelect.Domain.Inventory.InventoryReservationStatus.Active, reservation.Status);
        Assert.Equal(4, balance.AvailableQuantity);
        Assert.Equal(1, balance.ReservedQuantity);
        Assert.Equal(PaymentAttemptStatus.AwaitingPayment, attempt.Status);
        Assert.Equal(NowUtc.AddMinutes(15), attempt.InstructionExpiresAtUtc);
        Assert.Equal(CartStatus.Converted, cart.Status);
        Assert.Equal(5, await verification.OrderStatusHistories.CountAsync(candidate => candidate.OrderId == order.Id));
        Assert.Single(await verification.OrderItems.Where(candidate => candidate.OrderId == order.Id).ToListAsync());
    }

    [global::DoSelect.Infrastructure.Tests.Idempotency.SqlServerFact]
    public async Task ExecuteAsync_WhenInventoryIsInsufficient_OuterTransactionRollsBackEverything()
    {
        var seed = await SeedAsync(onHandQuantity: 0);
        await using (var context = EfCheckoutTransactionGatewayFixture.CreateContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var exception = await Assert.ThrowsAsync<DoSelect.Application.Common.DomainProblemException>(
                () => CreateGateway(context).ExecuteAsync(seed.Command));

            Assert.Equal("inventory_insufficient", exception.Code);
            await transaction.RollbackAsync();
        }

        await using var verification = EfCheckoutTransactionGatewayFixture.CreateContext();
        Assert.Empty(await verification.Orders.ToListAsync());
        Assert.Empty(await verification.InventoryReservations.ToListAsync());
        Assert.Empty(await verification.PaymentAttempts.ToListAsync());
        Assert.Equal(
            CartStatus.Active,
            (await verification.Carts.SingleAsync(candidate => candidate.PublicId == seed.Command.CartPublicId)).Status);
    }

    [global::DoSelect.Infrastructure.Tests.Idempotency.SqlServerFact]
    public async Task ExecuteAsync_WithFreeShippingCoupon_SnapshotsEligibilityNameAndSeat()
    {
        var seed = await SeedAsync(onHandQuantity: 5, withCoupon: true);
        await using (var context = EfCheckoutTransactionGatewayFixture.CreateContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await CreateGateway(context).ExecuteAsync(seed.Command);
            await transaction.CommitAsync();
        }

        await using var verification = EfCheckoutTransactionGatewayFixture.CreateContext();
        var order = await verification.Orders.SingleAsync(
            candidate => candidate.CheckoutIdempotencyKey == seed.Command.IdempotencyKey);
        var item = await verification.OrderItems.SingleAsync(candidate => candidate.OrderId == order.Id);
        var orderCoupon = await verification.OrderCoupons.SingleAsync(candidate => candidate.OrderId == order.Id);
        var redemption = await verification.CouponRedemptions.SingleAsync(candidate => candidate.OrderId == order.Id);

        Assert.Equal(0m, order.ShippingFee);
        Assert.Equal(150m, order.ShippingMethodBaseFeeSnapshot);
        Assert.Equal(1_000m, order.GrandTotal);
        Assert.True(item.IsCouponEligible);
        Assert.Equal("Checkout 免運券", orderCoupon.NameSnapshot);
        Assert.True(orderCoupon.IsFreeShipping);
        Assert.Equal(CouponRedemptionStatus.Reserved, redemption.Status);
    }

    [global::DoSelect.Infrastructure.Tests.Idempotency.SqlServerFact]
    public async Task ExecuteAsync_WhenCouponReducesGrandTotalBelowOne_RejectsWithoutSideEffects()
    {
        var seed = await SeedAsync(onHandQuantity: 5, zeroTotalCoupon: true);
        await using (var context = EfCheckoutTransactionGatewayFixture.CreateContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var exception = await Assert.ThrowsAsync<DomainProblemException>(
                () => CreateGateway(context).ExecuteAsync(seed.Command));

            Assert.Equal(DomainErrorCodes.OrderTotalBelowMinimum, exception.Code);
            Assert.Equal(409, exception.StatusCode);
            await transaction.RollbackAsync();
        }

        await using var verification = EfCheckoutTransactionGatewayFixture.CreateContext();
        Assert.Empty(await verification.Orders.ToListAsync());
        Assert.Empty(await verification.OrderItems.ToListAsync());
        Assert.Empty(await verification.InventoryReservations.ToListAsync());
        Assert.Empty(await verification.InventoryMovements.ToListAsync());
        Assert.Empty(await verification.CouponRedemptions.ToListAsync());
        Assert.Empty(await verification.OrderCoupons.ToListAsync());
        Assert.Empty(await verification.PaymentAttempts.ToListAsync());

        var cart = await verification.Carts.SingleAsync(
            candidate => candidate.PublicId == seed.Command.CartPublicId);
        var balance = await verification.InventoryBalances.SingleAsync();
        Assert.Equal(CartStatus.Active, cart.Status);
        Assert.Equal(5, balance.AvailableQuantity);
        Assert.Equal(0, balance.ReservedQuantity);
    }

    private static EfCheckoutTransactionGateway CreateGateway(DoSelectDbContext context) =>
        new(
            context,
            new EfCompatibilityCatalogReader(context),
            new CouponRuleReader(context),
            new SqlOrderNumberGenerator(context),
            new FixedTimeProvider(NowUtc));

    private static async Task<CheckoutSeed> SeedAsync(
        int onHandQuantity,
        bool withCoupon = false,
        bool zeroTotalCoupon = false)
    {
        await using var context = EfCheckoutTransactionGatewayFixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var brand = new Brand(Guid.CreateVersion7(), $"BR-{suffix}", "Checkout 品牌", NowUtc);
        var category = new Category(
            Guid.CreateVersion7(), $"CAT-{suffix}", $"category-{suffix.ToLowerInvariant()}", "周邊", null, NowUtc);
        context.AddRange(brand, category);
        await context.SaveChangesAsync();

        var product = new Product(
            Guid.CreateVersion7(), $"PROD-{suffix}", brand.Id, category.Id, "測試商品", NowUtc);
        product.ChangeStatus(ProductStatus.Published, NowUtc);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var sku = new Sku(
            Guid.CreateVersion7(), $"SKU-{suffix}", product.Id, "測試 SKU", 1_000m, 600m, NowUtc);
        sku.UpdatePackageDimensions(1m, 40m, 30m, 20m, NowUtc);
        sku.ChangeStatus(SkuStatus.Published, NowUtc);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();
        context.InventoryBalances.Add(new DoSelect.Domain.Inventory.InventoryBalance(
            Guid.CreateVersion7(), sku.Id, onHandQuantity, 1, NowUtc));

        var method = new ShippingMethod(
            Guid.CreateVersion7(),
            $"HOME-{suffix}",
            "一般宅配",
            "HomeDeliveryStandard",
            zeroTotalCoupon ? 0m : 150m,
            5_000m,
            allowsCod: true,
            requiresPrepayment: false,
            $"PROVIDER-{suffix}",
            NowUtc);
        var profile = new ShippingProviderProfile(
            Guid.CreateVersion7(),
            $"PROVIDER-{suffix}",
            1,
            "Published",
            null,
            null,
            "{}",
            1,
            NowUtc);
        context.AddRange(method, profile);
        await context.SaveChangesAsync();
        context.PackageLimitVersions.Add(new PackageLimitVersion(
            Guid.CreateVersion7(), profile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m,
            null, null, NowUtc));

        string? couponCode = null;
        if (withCoupon || zeroTotalCoupon)
        {
            couponCode = zeroTotalCoupon ? $"ZERO-{suffix}" : $"FREE-{suffix}";
            var coupon = new Coupon(
                Guid.CreateVersion7(),
                new CouponCreation(
                    couponCode,
                    zeroTotalCoupon ? "Checkout 全額折抵券" : "Checkout 免運券",
                    zeroTotalCoupon ? CouponDiscountType.Percentage : CouponDiscountType.FreeShipping,
                    zeroTotalCoupon ? 1m : null,
                    0m,
                    zeroTotalCoupon ? 1_000m : null,
                    NowUtc.AddDays(-1),
                    NowUtc.AddDays(10),
                    100,
                    1,
                    false,
                    false,
                    CouponScopeType.Restricted),
                NowUtc.AddDays(-2));
            coupon.ActivateNow(CouponUsageState.Unused, NowUtc);
            context.Coupons.Add(coupon);
            await context.SaveChangesAsync();
            context.CouponCategories.Add(new CouponCategory(coupon.Id, category.Id, NowUtc));
        }

        var guestKey = "checkout-test-secret-" + suffix;
        var cart = Cart.CreateForGuest(
            Guid.CreateVersion7(),
            SHA256.HashData(Encoding.UTF8.GetBytes(guestKey)),
            NowUtc.AddDays(30),
            NowUtc);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();
        context.CartItems.Add(new CartItem(
            Guid.CreateVersion7(), cart.Id, sku.Id, 1, null, NowUtc));
        cart.Touch(NowUtc);
        await context.SaveChangesAsync();

        var command = new CheckoutCommand(
            CheckoutActor.ForGuest(guestKey),
            cart.PublicId,
            cart.RowVersion.ToArray(),
            new CheckoutRecipientSnapshot(
                "Guest", "0912345678", "guest@example.test", "TW",
                "100", "Taipei", "Zhongzheng", "No. 1", null),
            null,
            method.Code,
            null,
            PaymentMethod.CreditCard,
            couponCode,
            new CheckoutInvoicePreferenceSnapshot(
                SimulatedInvoiceBuyerType.Individual,
                "guest@example.test",
                null,
                null,
                null,
                null),
            new CheckoutPolicySnapshot(1, 1, 1, 1),
            "checkout-gateway-test-" + Guid.NewGuid().ToString("N"));
        return new CheckoutSeed(command);
    }

    private sealed record CheckoutSeed(CheckoutCommand Command);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        private readonly DateTimeOffset _utcNow = new(utcNow);
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}

public sealed class EfCheckoutTransactionGatewayFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        if (!global::DoSelect.Infrastructure.Tests.Idempotency.IdempotencyExecutorFixture.IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (!global::DoSelect.Infrastructure.Tests.Idempotency.IdempotencyExecutorFixture.IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(
                global::DoSelect.Infrastructure.Tests.SqlServerTestConnection.Build(
                    "DoSelectCheckoutGatewayTests"))
            .Options);
}
