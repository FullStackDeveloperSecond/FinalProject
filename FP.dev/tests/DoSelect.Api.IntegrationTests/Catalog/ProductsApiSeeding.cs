using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Catalog;

internal static class ProductsApiSeeding
{
    public static async Task<(Product Product, Sku DefaultSku, Category Category)> CreatePublishedProductAsync(
        DoSelectDbContext context,
        int onHandQuantity = 10,
        long? categoryId = null,
        long? brandId = null)
    {
        var now = DateTime.UtcNow;

        long resolvedBrandId;
        if (brandId is { } existingBrandId)
        {
            resolvedBrandId = existingBrandId;
        }
        else
        {
            var brand = new Brand(Guid.CreateVersion7(), ProductsApiFixture.UniqueCode("BRAND"), "測試品牌", now);
            context.Brands.Add(brand);
            await context.SaveChangesAsync();
            resolvedBrandId = brand.Id;
        }

        Category category;
        if (categoryId is { } existingCategoryId)
        {
            category = await context.Categories.FirstAsync(c => c.Id == existingCategoryId);
        }
        else
        {
            category = new Category(
                Guid.CreateVersion7(),
                ProductsApiFixture.UniqueCode("CAT"),
                "cat-" + Guid.NewGuid().ToString("N")[..12],
                "測試分類",
                null,
                now);
            context.Categories.Add(category);
            await context.SaveChangesAsync();
        }

        var product = new Product(
            Guid.CreateVersion7(),
            ProductsApiFixture.UniqueCode("PROD"),
            resolvedBrandId,
            category.Id,
            "測試商品",
            now);
        product.ChangeStatus(ProductStatus.Published, now);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var sku = new Sku(Guid.CreateVersion7(), ProductsApiFixture.UniqueCode("SKU"), product.Id, "測試SKU", 1000m, 600m, now);
        sku.ChangeStatus(SkuStatus.Published, now);
        sku.UpdateCommercialDetails(sku.NameZhTw, sku.ListPrice, sku.UnitCost, isDefault: true, requiresPrepayment: false, now);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        context.InventoryBalances.Add(
            new InventoryBalance(Guid.CreateVersion7(), sku.Id, onHandQuantity, reorderLevel: 2, now));
        await context.SaveChangesAsync();

        return (product, sku, category);
    }
}
