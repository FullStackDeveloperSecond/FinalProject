using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;

namespace DoSelect.Api.IntegrationTests.Shopping;

/// <summary>Minimal, safely-generous shipping and catalog fixtures for driving <c>POST /api/v1/orders</c> end to end. Mirrors the seed shape EfCheckoutTransactionGatewayTests already exercises directly against the gateway.</summary>
internal static class CheckoutApiSeeding
{
    /// <summary>
    /// Unlike <see cref="CartApiSeeding.CreatePublishedSkuAsync"/>, this also publishes the parent
    /// Product — Cart's own add-item validation doesn't check Product status, but Checkout's
    /// revalidation does, and rejects an unpublished-Product SKU with cart_item_requires_attention.
    /// </summary>
    public static async Task<Sku> CreatePurchasableSkuAsync(
        DoSelectDbContext context,
        decimal listPrice = 1000m,
        int availableQuantity = 1000)
    {
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var brand = new Brand(Guid.CreateVersion7(), $"BR-{suffix}", "測試品牌", now);
        var category = new Category(
            Guid.CreateVersion7(), $"CAT-{suffix}", $"cat-{suffix.ToLowerInvariant()}", "測試分類", null, now);
        context.AddRange(brand, category);
        await context.SaveChangesAsync();

        var product = new Product(Guid.CreateVersion7(), $"PROD-{suffix}", brand.Id, category.Id, "測試商品", now);
        product.ChangeStatus(ProductStatus.Published, now);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var sku = new Sku(Guid.CreateVersion7(), $"SKU-{suffix}", product.Id, "測試SKU", listPrice, listPrice * 0.6m, now);
        sku.UpdatePackageDimensions(1m, 20m, 15m, 10m, now);
        sku.ChangeStatus(SkuStatus.Published, now);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        context.InventoryBalances.Add(new InventoryBalance(
            Guid.CreateVersion7(), sku.Id, availableQuantity, reorderLevel: 1, now));
        await context.SaveChangesAsync();

        return sku;
    }

    public static async Task<ShippingMethod> SeedHomeDeliveryShippingMethodAsync(
        DoSelectDbContext context,
        decimal baseFee = 150m,
        decimal? freeShippingThreshold = 5_000m)
    {
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var method = new ShippingMethod(
            Guid.CreateVersion7(),
            $"HOME-{suffix}",
            "一般宅配",
            "HomeDeliveryStandard",
            baseFee,
            freeShippingThreshold,
            allowsCod: true,
            requiresPrepayment: false,
            $"PROVIDER-{suffix}",
            now);
        var profile = new ShippingProviderProfile(
            Guid.CreateVersion7(),
            $"PROVIDER-{suffix}",
            1,
            "Published",
            null,
            null,
            "{}",
            1,
            now);
        context.AddRange(method, profile);
        await context.SaveChangesAsync();

        context.PackageLimitVersions.Add(new PackageLimitVersion(
            Guid.CreateVersion7(), profile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m, null, null, now));
        await context.SaveChangesAsync();

        return method;
    }
}
