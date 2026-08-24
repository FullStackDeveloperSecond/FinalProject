using DoSelect.Application.Shipping;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Shipping;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Shipping;

[CollectionDefinition(nameof(ShippingServiceCollection))]
public sealed class ShippingServiceCollection : ICollectionFixture<ShippingServiceFixture>;

[Collection(nameof(ShippingServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfShippingOptionsQueryServiceTests
{
    [Fact]
    public async Task GetShippingOptionsAsync_OnlyReturnsActiveMethods_OrderedBySortOrderThenCode()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.SeedShippingMethodAsync(context, ShippingProviderCodes.HomeDelivery);
        await ShippingServiceFixture.SeedShippingMethodAsync(context, ShippingProviderCodes.ConvenienceStore);
        await ShippingServiceFixture.SeedShippingMethodAsync(context, ShippingProviderCodes.HomeDelivery, isActive: false);
        var service = new EfShippingOptionsQueryService(context);

        var options = await service.GetShippingOptionsAsync(CancellationToken.None);

        Assert.Equal(2, options.Methods.Count);
    }

    [Fact]
    public async Task SearchConvenienceStoresAsync_FiltersByCityAndKeyword()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        var store = await ShippingServiceFixture.SeedConvenienceStoreAsync(context, "7-ELEVEN");
        var service = new EfShippingOptionsQueryService(context);

        var byCode = await service.SearchConvenienceStoresAsync(
            new ConvenienceStoreQuery(store.StoreCode, null, null), CancellationToken.None);
        var byWrongCity = await service.SearchConvenienceStoresAsync(
            new ConvenienceStoreQuery(store.StoreCode, "高雄市", null), CancellationToken.None);

        Assert.Single(byCode.Items);
        Assert.Equal(store.PublicId, byCode.Items[0].PublicId);
        Assert.Empty(byWrongCity.Items);
    }
}
