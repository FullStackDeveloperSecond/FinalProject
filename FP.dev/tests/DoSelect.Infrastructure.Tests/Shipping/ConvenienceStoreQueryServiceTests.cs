using DoSelect.Application.Shipping;
using DoSelect.Infrastructure.Shipping;

namespace DoSelect.Infrastructure.Tests.Shipping;

[Collection(nameof(ShippingServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ConvenienceStoreQueryServiceTests
{
    [Fact]
    public async Task ListAsync_FiltersByCityAndDistrict()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.SeedStoreAsync(context, "7-11", ShippingServiceFixture.UniqueCode("S"), "台北市", "大安區");
        await ShippingServiceFixture.SeedStoreAsync(context, "7-11", ShippingServiceFixture.UniqueCode("S"), "台北市", "信義區");
        await ShippingServiceFixture.SeedStoreAsync(context, "7-11", ShippingServiceFixture.UniqueCode("S"), "高雄市", "苓雅區");

        var result = await new EfConvenienceStoreQueryService(context).ListAsync(
            new ConvenienceStoreQuery(null, "台北市", "大安區", null, 1, 20), CancellationToken.None);

        var store = Assert.Single(result.Items);
        Assert.Equal("台北市", store.City);
        Assert.Equal("大安區", store.District);
    }

    [Fact]
    public async Task ListAsync_ExcludesDeactivatedStores()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.SeedStoreAsync(context, "7-11", ShippingServiceFixture.UniqueCode("S"), "台中市", "西區");
        await ShippingServiceFixture.SeedStoreAsync(context, "7-11", ShippingServiceFixture.UniqueCode("S"), "台中市", "西區", isActive: false);

        var result = await new EfConvenienceStoreQueryService(context).ListAsync(
            new ConvenienceStoreQuery(null, "台中市", "西區", null, 1, 20), CancellationToken.None);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task ListAsync_PagesWithoutDuplicatesOrGaps()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        for (var i = 0; i < 5; i++)
        {
            await ShippingServiceFixture.SeedStoreAsync(context, "FamilyMart", ShippingServiceFixture.UniqueCode("S"), "新北市", "板橋區");
        }

        var page1 = await new EfConvenienceStoreQueryService(context).ListAsync(
            new ConvenienceStoreQuery("FamilyMart", null, null, null, 1, 2), CancellationToken.None);
        var page2 = await new EfConvenienceStoreQueryService(context).ListAsync(
            new ConvenienceStoreQuery("FamilyMart", null, null, null, 2, 2), CancellationToken.None);
        var page3 = await new EfConvenienceStoreQueryService(context).ListAsync(
            new ConvenienceStoreQuery("FamilyMart", null, null, null, 3, 2), CancellationToken.None);

        Assert.Equal(5, page1.TotalCount);
        var allPublicIds = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(item => item.PublicId).ToList();
        Assert.Equal(5, allPublicIds.Distinct().Count());
    }

    [Fact]
    public async Task ListAsync_FiltersByKeywordAgainstNameOrCode()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        var matching = await ShippingServiceFixture.SeedStoreAsync(context, "7-11", "SEARCHABLE-CODE", "桃園市", "中壢區");
        await ShippingServiceFixture.SeedStoreAsync(context, "7-11", ShippingServiceFixture.UniqueCode("S"), "桃園市", "中壢區");

        var result = await new EfConvenienceStoreQueryService(context).ListAsync(
            new ConvenienceStoreQuery(null, null, null, "SEARCHABLE", 1, 20), CancellationToken.None);

        var store = Assert.Single(result.Items);
        Assert.Equal(matching.PublicId, store.PublicId);
    }
}
