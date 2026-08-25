using DoSelect.Application.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Shipping;

namespace DoSelect.Infrastructure.Tests.Shipping;

[Collection(nameof(ShippingServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ConvenienceStoreAdminServiceTests
{
    [Fact]
    public async Task CreateAsync_PersistsAsDemoData()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
        var code = ShippingServiceFixture.UniqueCode("STORE");

        var created = await CreateService(context).CreateAsync(
            new CreateConvenienceStoreRequest("7-11", code, "測試門市", "測試路 1 號", "台北市", "大安區"),
            "actor-1", CancellationToken.None);

        Assert.True(created.IsDemoData);
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task CreateAsync_WhenProviderAndStoreCodeAlreadyExist_ThrowsStoreCodeDuplicate()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
        var code = ShippingServiceFixture.UniqueCode("STORE");
        var service = CreateService(context);
        await service.CreateAsync(
            new CreateConvenienceStoreRequest("7-11", code, "測試門市", "測試路 1 號", "台北市", "大安區"),
            "actor-1", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(() => service.CreateAsync(
            new CreateConvenienceStoreRequest("7-11", code, "另一個名稱", "另一條路 2 號", "台北市", "大安區"),
            "actor-1", CancellationToken.None));
        Assert.Equal(ShippingAdminErrorCodes.StoreCodeDuplicate, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_AllowsTheSameStoreCodeUnderADifferentProvider()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
        var code = ShippingServiceFixture.UniqueCode("STORE");
        var service = CreateService(context);
        await service.CreateAsync(
            new CreateConvenienceStoreRequest("7-11", code, "測試門市", "測試路 1 號", "台北市", "大安區"),
            "actor-1", CancellationToken.None);

        var created = await service.CreateAsync(
            new CreateConvenienceStoreRequest("FamilyMart", code, "測試門市", "測試路 1 號", "台北市", "大安區"),
            "actor-1", CancellationToken.None);

        Assert.Equal(code, created.StoreCode);
    }

    /// <summary>購物車、訂單、付款與物流.md: "已被購物車或訂單引用的門市不得實體刪除；停用後不可供新訂單選擇" — deactivate via Update.</summary>
    [Fact]
    public async Task UpdateAsync_CanDeactivateAStore()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
        var service = CreateService(context);
        var created = await service.CreateAsync(
            new CreateConvenienceStoreRequest("7-11", ShippingServiceFixture.UniqueCode("STORE"), "測試門市", "測試路 1 號", "台北市", "大安區"),
            "actor-1", CancellationToken.None);

        var updated = await service.UpdateAsync(
            created.PublicId,
            new UpdateConvenienceStoreRequest("測試門市", "測試路 1 號", "台北市", "大安區", IsActive: false, created.RowVersion),
            "actor-1", CancellationToken.None);

        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_WithAStaleRowVersion_ThrowsConcurrencyConflict()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
        var service = CreateService(context);
        var created = await service.CreateAsync(
            new CreateConvenienceStoreRequest("7-11", ShippingServiceFixture.UniqueCode("STORE"), "測試門市", "測試路 1 號", "台北市", "大安區"),
            "actor-1", CancellationToken.None);
        var staleRowVersion = (byte[])created.RowVersion.Clone();
        staleRowVersion[0] = unchecked((byte)(staleRowVersion[0] + 1));

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(() => service.UpdateAsync(
            created.PublicId,
            new UpdateConvenienceStoreRequest("測試門市", "測試路 1 號", "台北市", "大安區", true, staleRowVersion),
            "actor-1", CancellationToken.None));
        Assert.Equal(ShippingAdminErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }

    [Fact]
    public async Task ListAsync_FiltersByIsActive()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
        var service = CreateService(context);
        var active = await service.CreateAsync(
            new CreateConvenienceStoreRequest("7-11", ShippingServiceFixture.UniqueCode("STORE"), "門市A", "路1號", "台北市", "大安區"),
            "actor-1", CancellationToken.None);
        var toDeactivate = await service.CreateAsync(
            new CreateConvenienceStoreRequest("7-11", ShippingServiceFixture.UniqueCode("STORE"), "門市B", "路2號", "台北市", "大安區"),
            "actor-1", CancellationToken.None);
        await service.UpdateAsync(
            toDeactivate.PublicId,
            new UpdateConvenienceStoreRequest("門市B", "路2號", "台北市", "大安區", false, toDeactivate.RowVersion),
            "actor-1", CancellationToken.None);

        var result = await service.ListAsync(
            new AdminConvenienceStoreQuery(null, null, null, IsActive: true, 1, 20), CancellationToken.None);

        Assert.Contains(result.Items, store => store.PublicId == active.PublicId);
        Assert.DoesNotContain(result.Items, store => store.PublicId == toDeactivate.PublicId);
    }

    private static EfConvenienceStoreAdminService CreateService(DoSelectDbContext context) => new(context);
}
