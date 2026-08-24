using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Persistence;

namespace DoSelect.Api.IntegrationTests.Shopping;

internal static class CartApiSeeding
{
    /// <summary>
    /// <paramref name="availableQuantity"/> defaults to a generous 1000 (not null): after PR
    /// #28's review a missing InventoryBalance row means "insufficient_stock", so any test
    /// that isn't specifically exercising that edge case needs a real balance row to avoid
    /// tripping over it incidentally. Pass <c>null</c> explicitly to test the no-balance-row
    /// case itself.
    /// </summary>
    public static async Task<Sku> CreatePublishedSkuAsync(
        DoSelectDbContext context,
        decimal listPrice = 1000m,
        int? availableQuantity = 1000)
    {
        var now = DateTime.UtcNow;
        var brand = new Brand(Guid.CreateVersion7(), CartApiFixture.UniqueCode("BRAND"), "測試品牌", now);
        context.Brands.Add(brand);
        var category = new Category(
            Guid.CreateVersion7(),
            CartApiFixture.UniqueCode("CAT"),
            "cat-" + Guid.NewGuid().ToString("N")[..12],
            "測試分類",
            null,
            now);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product(Guid.CreateVersion7(), CartApiFixture.UniqueCode("PROD"), brand.Id, category.Id, "測試商品", now);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var sku = new Sku(Guid.CreateVersion7(), CartApiFixture.UniqueCode("SKU"), product.Id, "測試SKU", listPrice, listPrice * 0.6m, now);
        sku.ChangeStatus(SkuStatus.Published, now);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        if (availableQuantity.HasValue)
        {
            context.InventoryBalances.Add(new InventoryBalance(
                Guid.CreateVersion7(), sku.Id, onHandQuantity: availableQuantity.Value, reorderLevel: 0, now));
            await context.SaveChangesAsync();
        }

        return sku;
    }
}
