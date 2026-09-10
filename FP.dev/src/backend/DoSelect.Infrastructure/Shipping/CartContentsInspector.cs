using DoSelect.Application.Shopping;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shipping;

internal sealed record CartShippingRelevantContents(
    bool HasAssemblyItem,
    bool HasPrepaymentRequiredSku,
    IReadOnlyList<PackageItemDimensions> PackageItems);

/// <summary>
/// Used by <see cref="EfShippingOptionsService"/> so
/// the two don't independently re-derive what makes a cart assembly/prepay-restricted.
/// </summary>
internal static class CartContentsInspector
{
    internal static async Task<CartShippingRelevantContents> InspectAsync(
        DoSelectDbContext dbContext,
        CartDto cart,
        CancellationToken cancellationToken)
    {
        if (cart.Items.Count == 0)
        {
            return new CartShippingRelevantContents(false, false, []);
        }

        var hasAssemblyItem = cart.Items.Any(item => item.AssemblyGroupKey is not null);

        var skuPublicIds = cart.Items.Select(item => item.SkuPublicId).Distinct().ToList();
        var skus = await (
                from sku in dbContext.Skus.AsNoTracking()
                join product in dbContext.Products.AsNoTracking() on sku.ProductId equals product.Id
                join category in dbContext.Categories.AsNoTracking() on product.CategoryId equals category.Id
                where skuPublicIds.Contains(sku.PublicId)
                select new
                {
                    sku.PublicId,
                    sku.RequiresPrepayment,
                    sku.WeightKg,
                    sku.LengthCm,
                    sku.WidthCm,
                    sku.HeightCm,
                    CategoryCode = category.Code,
                })
            .ToDictionaryAsync(sku => sku.PublicId, cancellationToken);

        // Same item shape EfCheckoutTransactionGateway.CalculateAndValidatePackage feeds the
        // canonical PackageSnapshotCalculator: sku code as the key, unit price as declared value —
        // so the options screen and checkout evaluate the exact same package (組長 PR #73 item 3).
        var packageItems = cart.Items
            .Select(item =>
            {
                var sku = skus[item.SkuPublicId];
                return new PackageItemDimensions(
                    item.SkuCode,
                    item.Quantity,
                    sku.WeightKg,
                    sku.LengthCm,
                    sku.WidthCm,
                    sku.HeightCm,
                    item.UnitPrice,
                    item.AssemblyGroupKey,
                    string.Equals(
                        sku.CategoryCode,
                        CompatibilityCatalogContract.Categories.Case,
                        StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        return new CartShippingRelevantContents(
            hasAssemblyItem,
            cart.Items.Any(item => skus[item.SkuPublicId].RequiresPrepayment),
            packageItems);
    }
}
