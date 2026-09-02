using DoSelect.Application.Promotions;
using DoSelect.Application.Common;
using DoSelect.Application.Shopping;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Promotions;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shopping;

/// <summary>
/// Projects the caller's current cart into the existing coupon-calculation contract. Prices come
/// from <see cref="ICartService"/>, while internal Product/category identities and sale status are
/// resolved server-side; no client amount or ownership input is trusted.
/// </summary>
public sealed class EfCartCouponLineReader(
    DoSelectDbContext context,
    ICartService cartService) : ICartCouponLineReader
{
    public async Task<CartCouponLines?> FindAsync(
        CartIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var cart = await cartService.GetCartAsync(identity, cancellationToken);
        if (cart.Items.Count == 0)
        {
            return new CartCouponLines(
                cart.PublicId,
                cart.RowVersion,
                [],
                IsAssemblyDelivery: false);
        }

        var skuPublicIds = cart.Items
            .Select(item => item.SkuPublicId)
            .Distinct()
            .ToArray();
        var skuFacts = await (
                from sku in context.Skus.AsNoTracking()
                join product in context.Products.AsNoTracking() on sku.ProductId equals product.Id
                where skuPublicIds.Contains(sku.PublicId)
                select new
                {
                    sku.Id,
                    sku.PublicId,
                    sku.ListPrice,
                    ProductId = product.Id,
                    product.CategoryId,
                })
            .ToDictionaryAsync(fact => fact.PublicId, cancellationToken);
        if (skuFacts.Count != skuPublicIds.Length)
        {
            throw DomainProblemException.Conflict(
                "cart_item_requires_attention",
                "One or more cart items no longer resolve to a product.");
        }

        var now = DateTime.UtcNow;
        var skuIds = skuFacts.Values.Select(fact => fact.Id).ToArray();
        var saleSkuIds = await context.SalePrices.AsNoTracking()
            .Where(price => skuIds.Contains(price.SkuId) &&
                price.Status == SalePriceStatus.Active &&
                price.StartsAtUtc <= now &&
                now < price.EndsAtUtc)
            .Select(price => price.SkuId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        var lines = cart.Items.Select(item =>
        {
            var fact = skuFacts[item.SkuPublicId];
            return new CouponCalculationLine(
                item.PublicId,
                fact.ProductId,
                [fact.CategoryId],
                item.Quantity,
                item.UnitPrice,
                saleSkuIds.Contains(fact.Id));
        }).ToArray();

        return new CartCouponLines(
            cart.PublicId,
            cart.RowVersion,
            lines,
            cart.Items.Any(item => item.AssemblyGroupKey is not null));
    }
}
