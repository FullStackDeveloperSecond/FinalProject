using DoSelect.Application.Shipping;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Shipping;

namespace DoSelect.Infrastructure.Tests.Shipping;

[Collection(nameof(ShippingServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class PackageLimitServiceTests
{
    private static CreatePackageLimitVersionRequest ValidStorePickupRequest(
        DateTime? from = null, DateTime? to = null) => new(
        ShippingProviderCodes.StorePickup,
        MaxWeightKg: 5m,
        MaxLengthCm: 45m,
        MaxWidthCm: 45m,
        MaxHeightCm: 45m,
        MaxTotalCm: 105m,
        MaxDeclaredValue: 20000m,
        EffectiveFromUtc: from,
        EffectiveToUtc: to);

    [Fact]
    public async Task CreateDraftAsync_WithValuesInsideTheSafeRange_Succeeds()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);

        var result = await CreateService(context).CreateDraftAsync(ValidStorePickupRequest(), "actor-1", CancellationToken.None);

        Assert.Equal(ShippingProviderProfileStatuses.Draft, result.Status);
        Assert.Equal(1, result.Version);
    }

    /// <summary>購物車、訂單、付款與物流.md: 超商 Profile 安全範圍單邊 1～45cm — 超出即拒絕，不可由一般管理員突破。</summary>
    [Fact]
    public async Task CreateDraftAsync_WhenASideExceedsTheSafeRange_ThrowsValidationFailed()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var request = ValidStorePickupRequest() with { MaxLengthCm = 46m };

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(
            () => CreateService(context).CreateDraftAsync(request, "actor-1", CancellationToken.None));
        Assert.Equal(ShippingAdminErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    /// <summary>"管理員設定值需通過...單邊不大於三邊和等跨欄位驗證".</summary>
    [Fact]
    public async Task CreateDraftAsync_WhenASideExceedsMaxTotalCm_ThrowsValidationFailed()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var request = ValidStorePickupRequest() with { MaxLengthCm = 45m, MaxTotalCm = 40m };

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(
            () => CreateService(context).CreateDraftAsync(request, "actor-1", CancellationToken.None));
        Assert.Equal(ShippingAdminErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenAnUnknownProviderCodeIsGiven_ThrowsValidationFailed()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var request = ValidStorePickupRequest() with { ProviderCode = "DoesNotExist" };

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(
            () => CreateService(context).CreateDraftAsync(request, "actor-1", CancellationToken.None));
        Assert.Equal(ShippingAdminErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    /// <summary>非重疊區間規則: 新草稿的生效期間不得與同一 provider 現有版本重疊。</summary>
    [Fact]
    public async Task CreateDraftAsync_WhenEffectivePeriodOverlapsAnExistingVersion_ThrowsPeriodOverlap()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var service = CreateService(context);
        var baseline = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await service.CreateDraftAsync(
            ValidStorePickupRequest(baseline, baseline.AddMonths(6)), "actor-1", CancellationToken.None);

        var overlapping = ValidStorePickupRequest(baseline.AddMonths(3), baseline.AddMonths(9));
        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(
            () => service.CreateDraftAsync(overlapping, "actor-1", CancellationToken.None));
        Assert.Equal(ShippingAdminErrorCodes.PackageLimitPeriodOverlap, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenEffectivePeriodDoesNotOverlap_SucceedsWithTheNextVersionNumber()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var service = CreateService(context);
        var baseline = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await service.CreateDraftAsync(
            ValidStorePickupRequest(baseline, baseline.AddMonths(6)), "actor-1", CancellationToken.None);

        var second = await service.CreateDraftAsync(
            ValidStorePickupRequest(baseline.AddMonths(6), baseline.AddMonths(12)), "actor-1", CancellationToken.None);

        Assert.Equal(2, second.Version);
    }

    [Fact]
    public async Task PublishAsync_MarksTheDraftPublished()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var service = CreateService(context);
        var draft = await service.CreateDraftAsync(ValidStorePickupRequest(), "actor-1", CancellationToken.None);

        var published = await service.PublishAsync(
            draft.PublicId, new PublishPackageLimitVersionRequest(draft.RowVersion), "actor-1", CancellationToken.None);

        Assert.Equal(ShippingProviderProfileStatuses.Published, published.Status);
    }

    /// <summary>"同一物流服務在任一時間只有一個有效版本" — publishing a second version supersedes the first.</summary>
    [Fact]
    public async Task PublishAsync_SupersedesThePreviouslyPublishedVersionForTheSameProvider()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var service = CreateService(context);
        var baseline = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var first = await service.CreateDraftAsync(
            ValidStorePickupRequest(baseline, baseline.AddMonths(6)), "actor-1", CancellationToken.None);
        await service.PublishAsync(first.PublicId, new PublishPackageLimitVersionRequest(first.RowVersion), "actor-1", CancellationToken.None);
        var second = await service.CreateDraftAsync(
            ValidStorePickupRequest(baseline.AddMonths(6), baseline.AddMonths(12)), "actor-1", CancellationToken.None);

        await service.PublishAsync(second.PublicId, new PublishPackageLimitVersionRequest(second.RowVersion), "actor-1", CancellationToken.None);

        var all = await service.ListAsync(ShippingProviderCodes.StorePickup, CancellationToken.None);
        var firstAfter = all.Single(version => version.Version == 1);
        var secondAfter = all.Single(version => version.Version == 2);
        Assert.Equal(ShippingProviderProfileStatuses.Superseded, firstAfter.Status);
        Assert.Equal(ShippingProviderProfileStatuses.Published, secondAfter.Status);
    }

    [Fact]
    public async Task PublishAsync_WithAStaleRowVersion_ThrowsConcurrencyConflict()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var service = CreateService(context);
        var draft = await service.CreateDraftAsync(ValidStorePickupRequest(), "actor-1", CancellationToken.None);
        var staleRowVersion = (byte[])draft.RowVersion.Clone();
        staleRowVersion[0] = unchecked((byte)(staleRowVersion[0] + 1));

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(() => service.PublishAsync(
            draft.PublicId, new PublishPackageLimitVersionRequest(staleRowVersion), "actor-1", CancellationToken.None));
        Assert.Equal(ShippingAdminErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }

    private static EfPackageLimitService CreateService(DoSelectDbContext context) => new(context);
}
