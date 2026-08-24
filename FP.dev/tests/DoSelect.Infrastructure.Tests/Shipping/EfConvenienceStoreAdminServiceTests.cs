using DoSelect.Application.Shipping;
using DoSelect.Infrastructure.Shipping;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Shipping;

[Collection(nameof(ShippingServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfConvenienceStoreAdminServiceTests
{
    [Fact]
    public async Task CreateAsync_WhenProviderAndStoreCodeAreNew_Succeeds()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        var service = new EfConvenienceStoreAdminService(context);
        var storeCode = ShippingServiceFixture.UniqueCode("STORE");

        var dto = await service.CreateAsync(
            new CreateConvenienceStoreRequest("7-ELEVEN", storeCode, "測試門市", "測試地址", "台北市", "信義區"),
            DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(storeCode, dto.StoreCode);
        Assert.True(dto.IsActive);
        Assert.False(dto.IsDemoData);
    }

    [Fact]
    public async Task CreateAsync_WhenProviderAndStoreCodeAlreadyExist_ThrowsStoreCodeDuplicate()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        var service = new EfConvenienceStoreAdminService(context);
        var storeCode = ShippingServiceFixture.UniqueCode("STORE");
        await service.CreateAsync(
            new CreateConvenienceStoreRequest("7-ELEVEN", storeCode, "測試門市", "測試地址", "台北市", "信義區"),
            DateTime.UtcNow, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ShippingWriteException>(() => service.CreateAsync(
            new CreateConvenienceStoreRequest("7-ELEVEN", storeCode, "另一間門市", "另一個地址", "台中市", "西屯區"),
            DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(ShippingWriteException.ErrorCodes.StoreCodeDuplicate, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_WhenSameStoreCodeUsedByADifferentProvider_Succeeds()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        var service = new EfConvenienceStoreAdminService(context);
        var storeCode = ShippingServiceFixture.UniqueCode("STORE");
        await service.CreateAsync(
            new CreateConvenienceStoreRequest("7-ELEVEN", storeCode, "測試門市", "測試地址", "台北市", "信義區"),
            DateTime.UtcNow, CancellationToken.None);

        var dto = await service.CreateAsync(
            new CreateConvenienceStoreRequest("FamilyMart", storeCode, "測試門市", "測試地址", "台北市", "信義區"),
            DateTime.UtcNow, CancellationToken.None);

        Assert.Equal("FamilyMart", dto.ProviderCode);
    }

    [Fact]
    public async Task UpdateAsync_CanDeactivateAndEditDetails()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        var service = new EfConvenienceStoreAdminService(context);
        var created = await service.CreateAsync(
            new CreateConvenienceStoreRequest(
                "7-ELEVEN", ShippingServiceFixture.UniqueCode("STORE"), "測試門市", "測試地址", "台北市", "信義區"),
            DateTime.UtcNow, CancellationToken.None);

        var updated = await service.UpdateAsync(
            created.PublicId,
            new UpdateConvenienceStoreRequest("更新後門市名", "更新後地址", "新北市", "板橋區", false, created.RowVersion),
            DateTime.UtcNow, CancellationToken.None);

        Assert.Equal("更新後門市名", updated.StoreName);
        Assert.Equal("新北市", updated.City);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_WhenRowVersionIsStale_ThrowsConcurrencyConflict()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        var service = new EfConvenienceStoreAdminService(context);
        var created = await service.CreateAsync(
            new CreateConvenienceStoreRequest(
                "7-ELEVEN", ShippingServiceFixture.UniqueCode("STORE"), "測試門市", "測試地址", "台北市", "信義區"),
            DateTime.UtcNow, CancellationToken.None);
        await service.UpdateAsync(
            created.PublicId,
            new UpdateConvenienceStoreRequest("第一次更新", "測試地址", "台北市", "信義區", true, created.RowVersion),
            DateTime.UtcNow, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ShippingWriteException>(() => service.UpdateAsync(
            created.PublicId,
            new UpdateConvenienceStoreRequest("第二次更新(用舊版本)", "測試地址", "台北市", "信義區", true, created.RowVersion),
            DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(ShippingWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }
}
