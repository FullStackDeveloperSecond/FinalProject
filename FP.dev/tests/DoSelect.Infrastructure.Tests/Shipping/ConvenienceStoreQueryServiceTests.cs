using DoSelect.Application.Shipping;
using DoSelect.Infrastructure.Shipping;

namespace DoSelect.Infrastructure.Tests.Shipping;

[Collection(nameof(ShippingServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ConvenienceStoreQueryServiceTests
{
    [Fact]
    public async Task ListRegionsAsync_UsesActiveProviderStoresAndDistinctPagedRegions()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
        await ShippingServiceFixture.SeedStoreAsync(context, "7-11", "REGION-1", "臺北市", "中正區");
        await ShippingServiceFixture.SeedStoreAsync(context, "7-11", "REGION-2", "臺北市", "中正區");
        await ShippingServiceFixture.SeedStoreAsync(context, "7-11", "REGION-3", "臺北市", "大安區");
        await ShippingServiceFixture.SeedStoreAsync(context, "7-11", "REGION-4", "臺中市", "西區");
        await ShippingServiceFixture.SeedStoreAsync(context, "FamilyMart", "REGION-5", "高雄市", "苓雅區");
        await ShippingServiceFixture.SeedStoreAsync(context, "7-11", "REGION-6", "桃園市", "桃園區", isActive: false);
        var service = new EfConvenienceStoreQueryService(context);
        var cities = await service.ListRegionsAsync(new("7-11", null, 1, 1), CancellationToken.None);
        Assert.Equal(2, cities.TotalCount);
        Assert.Single(cities.Items);
        var next = await service.ListRegionsAsync(new("7-11", null, 2, 1), CancellationToken.None);
        Assert.NotEqual(cities.Items[0], next.Items[0]);
        var districts = await service.ListRegionsAsync(new("7-11", "臺北市", 1, 100), CancellationToken.None);
        Assert.Equal(2, districts.TotalCount);
        Assert.Contains("中正區", districts.Items);
        Assert.Contains("大安區", districts.Items);
        Assert.Empty((await service.ListRegionsAsync(new("7-11", "高雄市", 1, 100), CancellationToken.None)).Items);
        Assert.Empty((await service.ListRegionsAsync(new("7-11", null, int.MaxValue, 100), CancellationToken.None)).Items);
    }

    [Fact]
    public async Task ListAsync_FiltersByCityAndDistrict()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
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
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
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
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
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
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
        var matching = await ShippingServiceFixture.SeedStoreAsync(context, "7-11", "SEARCHABLE-CODE", "桃園市", "中壢區");
        await ShippingServiceFixture.SeedStoreAsync(context, "7-11", ShippingServiceFixture.UniqueCode("S"), "桃園市", "中壢區");

        var result = await new EfConvenienceStoreQueryService(context).ListAsync(
            new ConvenienceStoreQuery(null, null, null, "SEARCHABLE", 1, 20), CancellationToken.None);

        var store = Assert.Single(result.Items);
        Assert.Equal(matching.PublicId, store.PublicId);
    }

    /// <summary>組長 PR #73 round-3, item 4：pageNumber = int.MaxValue 通過 [Range]，而
    /// (pageNumber - 1) * pageSize 會溢位成負值，SQL Server 直接拒絕 → 500。offset 改用 long 計算，
    /// 超出資料範圍就回空頁。</summary>
    [Fact]
    public async Task ListAsync_WithAnExtremePageNumber_ReturnsAnEmptyPageInsteadOfOverflowing()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
        await ShippingServiceFixture.SeedStoreAsync(
            context, "7-11", ShippingServiceFixture.UniqueCode("S"), "台北市", "大安區");

        var page = await new EfConvenienceStoreQueryService(context).ListAsync(
            new ConvenienceStoreQuery(null, null, null, null, int.MaxValue, 20), CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
    }
}
