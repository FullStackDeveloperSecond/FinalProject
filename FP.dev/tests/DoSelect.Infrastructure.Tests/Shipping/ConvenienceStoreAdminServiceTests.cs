using DoSelect.Application.Shipping;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var code = ShippingServiceFixture.UniqueCode("STORE");

        var created = await CreateService(context).CreateAsync(
            ShippingServiceFixture.TestAuditContext,
            new CreateConvenienceStoreRequest("7-11", code, "測試門市", "測試路 1 號", "台北市", "大安區"),
            actorId, CancellationToken.None);

        Assert.True(created.IsDemoData);
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task CreateAsync_WhenProviderAndStoreCodeAlreadyExist_ThrowsStoreCodeDuplicate()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var code = ShippingServiceFixture.UniqueCode("STORE");
        var service = CreateService(context);
        await service.CreateAsync(
            ShippingServiceFixture.TestAuditContext,
            new CreateConvenienceStoreRequest("7-11", code, "測試門市", "測試路 1 號", "台北市", "大安區"),
            actorId, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(() => service.CreateAsync(
            ShippingServiceFixture.TestAuditContext,
            new CreateConvenienceStoreRequest("7-11", code, "另一個名稱", "另一條路 2 號", "台北市", "大安區"),
            actorId, CancellationToken.None));
        Assert.Equal(ShippingAdminErrorCodes.StoreCodeDuplicate, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_AllowsTheSameStoreCodeUnderADifferentProvider()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var code = ShippingServiceFixture.UniqueCode("STORE");
        var service = CreateService(context);
        await service.CreateAsync(
            ShippingServiceFixture.TestAuditContext,
            new CreateConvenienceStoreRequest("7-11", code, "測試門市", "測試路 1 號", "台北市", "大安區"),
            actorId, CancellationToken.None);

        var created = await service.CreateAsync(
            ShippingServiceFixture.TestAuditContext,
            new CreateConvenienceStoreRequest("FamilyMart", code, "測試門市", "測試路 1 號", "台北市", "大安區"),
            actorId, CancellationToken.None);

        Assert.Equal(code, created.StoreCode);
    }

    /// <summary>購物車、訂單、付款與物流.md: "已被購物車或訂單引用的門市不得實體刪除；停用後不可供新訂單選擇" — deactivate via Update.</summary>
    [Fact]
    public async Task UpdateAsync_CanDeactivateAStore()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var service = CreateService(context);
        var created = await service.CreateAsync(
            ShippingServiceFixture.TestAuditContext,
            new CreateConvenienceStoreRequest("7-11", ShippingServiceFixture.UniqueCode("STORE"), "測試門市", "測試路 1 號", "台北市", "大安區"),
            actorId, CancellationToken.None);

        var updated = await service.UpdateAsync(
            ShippingServiceFixture.TestAuditContext,
            created.PublicId,
            new UpdateConvenienceStoreRequest("測試門市", "測試路 1 號", "台北市", "大安區", IsActive: false, created.RowVersion),
            actorId, CancellationToken.None);

        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_WithAStaleRowVersion_ThrowsConcurrencyConflict()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var service = CreateService(context);
        var created = await service.CreateAsync(
            ShippingServiceFixture.TestAuditContext,
            new CreateConvenienceStoreRequest("7-11", ShippingServiceFixture.UniqueCode("STORE"), "測試門市", "測試路 1 號", "台北市", "大安區"),
            actorId, CancellationToken.None);
        var staleRowVersion = (byte[])created.RowVersion.Clone();
        staleRowVersion[0] = unchecked((byte)(staleRowVersion[0] + 1));

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(() => service.UpdateAsync(
            ShippingServiceFixture.TestAuditContext,
            created.PublicId,
            new UpdateConvenienceStoreRequest("測試門市", "測試路 1 號", "台北市", "大安區", true, staleRowVersion),
            actorId, CancellationToken.None));
        Assert.Equal(ShippingAdminErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }

    [Fact]
    public async Task ListAsync_FiltersByIsActive()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var service = CreateService(context);
        var active = await service.CreateAsync(
            ShippingServiceFixture.TestAuditContext,
            new CreateConvenienceStoreRequest("7-11", ShippingServiceFixture.UniqueCode("STORE"), "門市A", "路1號", "台北市", "大安區"),
            actorId, CancellationToken.None);
        var toDeactivate = await service.CreateAsync(
            ShippingServiceFixture.TestAuditContext,
            new CreateConvenienceStoreRequest("7-11", ShippingServiceFixture.UniqueCode("STORE"), "門市B", "路2號", "台北市", "大安區"),
            actorId, CancellationToken.None);
        await service.UpdateAsync(
            ShippingServiceFixture.TestAuditContext,
            toDeactivate.PublicId,
            new UpdateConvenienceStoreRequest("門市B", "路2號", "台北市", "大安區", false, toDeactivate.RowVersion),
            actorId, CancellationToken.None);

        var result = await service.ListAsync(
            new AdminConvenienceStoreQuery(null, null, null, IsActive: true, 1, 20), CancellationToken.None);

        Assert.Contains(result.Items, store => store.PublicId == active.PublicId);
        Assert.DoesNotContain(result.Items, store => store.PublicId == toDeactivate.PublicId);
    }

    /// <summary>組長 PR #73 review item 2: every store write must land its audit row in the same
    /// transaction as the write itself.</summary>
    [Fact]
    public async Task CreateAndUpdate_WriteCentralAuditEntriesWithChangedFieldNamesOnly()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var service = CreateService(context);
        var created = await service.CreateAsync(
            ShippingServiceFixture.TestAuditContext,
            new CreateConvenienceStoreRequest("7-11", ShippingServiceFixture.UniqueCode("STORE"), "測試門市", "測試路 1 號", "台北市", "大安區"),
            actorId, CancellationToken.None);

        await service.UpdateAsync(
            ShippingServiceFixture.TestAuditContext,
            created.PublicId,
            new UpdateConvenienceStoreRequest("改名門市", "測試路 1 號", "台北市", "大安區", IsActive: false, created.RowVersion),
            actorId, CancellationToken.None);

        var audits = await context.AuditLogs.AsNoTracking()
            .Where(log => log.ResourcePublicId == created.PublicId)
            .OrderBy(log => log.Id)
            .ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Equal("shipping.store.create", audits[0].Action);
        Assert.Equal("shipping.store.update", audits[1].Action);
        // Free-text values must never enter the safe-code audit fields — the update records the
        // changed field *name* (storeName) and the isActive transition only.
        Assert.DoesNotContain("改名門市", audits[1].ChangedFieldsJson + audits[1].Reason);
        Assert.Contains("storeName", audits[1].ChangedFieldsJson);
        Assert.Contains("isActive", audits[1].ChangedFieldsJson);
    }

    private static EfConvenienceStoreAdminService CreateService(DoSelectDbContext context) =>
        new(context, new EfAuditWriter(context, TimeProvider.System));

    /// <summary>組長 PR #73 round-3, item 4：後台列表同樣不得因大頁碼溢位成 500。</summary>
    [Fact]
    public async Task ListAsync_WithAnExtremePageNumber_ReturnsAnEmptyPageInsteadOfOverflowing()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearConvenienceStoresAsync(context);
        await ShippingServiceFixture.SeedStoreAsync(
            context, "7-11", ShippingServiceFixture.UniqueCode("S"), "台北市", "大安區");

        var page = await CreateService(context).ListAsync(
            new AdminConvenienceStoreQuery(null, null, null, null, int.MaxValue, 20), CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
    }
}
