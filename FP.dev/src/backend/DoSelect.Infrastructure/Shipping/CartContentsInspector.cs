using DoSelect.Application.Shopping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shipping;

internal readonly record struct CartShippingRelevantContents(bool HasAssemblyItem, bool HasPrepaymentRequiredSku);

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
            return new CartShippingRelevantContents(false, false);
        }

        var hasAssemblyItem = cart.Items.Any(item => item.AssemblyGroupKey is not null);

        var skuPublicIds = cart.Items.Select(item => item.SkuPublicId).Distinct().ToList();
        var hasPrepaymentRequiredSku = await dbContext.Skus
            .AsNoTracking()
            .Where(sku => skuPublicIds.Contains(sku.PublicId))
            .AnyAsync(sku => sku.RequiresPrepayment, cancellationToken);

        return new CartShippingRelevantContents(hasAssemblyItem, hasPrepaymentRequiredSku);
    }
}
