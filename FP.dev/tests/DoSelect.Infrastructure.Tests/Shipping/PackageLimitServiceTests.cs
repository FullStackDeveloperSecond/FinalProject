using DoSelect.Application.Shipping;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);

        var result = await CreateService(context).CreateDraftAsync(ShippingServiceFixture.TestAuditContext, ValidStorePickupRequest(), actorId, CancellationToken.None);

        Assert.Equal(ShippingProviderProfileStatuses.Draft, result.Status);
        Assert.Equal(1, result.Version);
    }

    /// <summary>購物車、訂單、付款與物流.md: 超商 Profile 安全範圍單邊 1～45cm — 超出即拒絕，不可由一般管理員突破。</summary>
    [Fact]
    public async Task CreateDraftAsync_WhenASideExceedsTheSafeRange_ThrowsValidationFailed()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var request = ValidStorePickupRequest() with { MaxLengthCm = 46m };

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(
            () => CreateService(context).CreateDraftAsync(ShippingServiceFixture.TestAuditContext, request, actorId, CancellationToken.None));
        Assert.Equal(ShippingAdminErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    /// <summary>"管理員設定值需通過...單邊不大於三邊和等跨欄位驗證".</summary>
    [Fact]
    public async Task CreateDraftAsync_WhenASideExceedsMaxTotalCm_ThrowsValidationFailed()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var request = ValidStorePickupRequest() with { MaxLengthCm = 45m, MaxTotalCm = 40m };

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(
            () => CreateService(context).CreateDraftAsync(ShippingServiceFixture.TestAuditContext, request, actorId, CancellationToken.None));
        Assert.Equal(ShippingAdminErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenAnUnknownProviderCodeIsGiven_ThrowsValidationFailed()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var request = ValidStorePickupRequest() with { ProviderCode = "DoesNotExist" };

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(
            () => CreateService(context).CreateDraftAsync(ShippingServiceFixture.TestAuditContext, request, actorId, CancellationToken.None));
        Assert.Equal(ShippingAdminErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    /// <summary>非重疊區間規則: 新草稿的生效期間不得與同一 provider 現有版本重疊。</summary>
    [Fact]
    public async Task CreateDraftAsync_WhenEffectivePeriodOverlapsAnExistingVersion_ThrowsPeriodOverlap()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var service = CreateService(context);
        var baseline = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await service.CreateDraftAsync(ShippingServiceFixture.TestAuditContext,
            ValidStorePickupRequest(baseline, baseline.AddMonths(6)), actorId, CancellationToken.None);

        var overlapping = ValidStorePickupRequest(baseline.AddMonths(3), baseline.AddMonths(9));
        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(
            () => service.CreateDraftAsync(ShippingServiceFixture.TestAuditContext, overlapping, actorId, CancellationToken.None));
        Assert.Equal(ShippingAdminErrorCodes.PackageLimitPeriodOverlap, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenEffectivePeriodDoesNotOverlap_SucceedsWithTheNextVersionNumber()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var service = CreateService(context);
        var baseline = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await service.CreateDraftAsync(ShippingServiceFixture.TestAuditContext,
            ValidStorePickupRequest(baseline, baseline.AddMonths(6)), actorId, CancellationToken.None);

        var second = await service.CreateDraftAsync(ShippingServiceFixture.TestAuditContext,
            ValidStorePickupRequest(baseline.AddMonths(6), baseline.AddMonths(12)), actorId, CancellationToken.None);

        Assert.Equal(2, second.Version);
    }

    [Fact]
    public async Task PublishAsync_MarksTheDraftPublished()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var service = CreateService(context);
        var draft = await service.CreateDraftAsync(ShippingServiceFixture.TestAuditContext, ValidStorePickupRequest(), actorId, CancellationToken.None);

        var published = await service.PublishAsync(ShippingProviderCodes.StorePickup, ShippingServiceFixture.TestAuditContext,
            draft.PublicId, new PublishPackageLimitVersionRequest(draft.RowVersion), actorId, CancellationToken.None);

        Assert.Equal(ShippingProviderProfileStatuses.Published, published.Status);
    }

    /// <summary>"同一物流服務在任一時間只有一個有效版本" — publishing a second version supersedes the first.</summary>
    [Fact]
    public async Task PublishAsync_SupersedesThePreviouslyPublishedVersionForTheSameProvider()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var service = CreateService(context);
        var baseline = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var first = await service.CreateDraftAsync(ShippingServiceFixture.TestAuditContext,
            ValidStorePickupRequest(baseline, baseline.AddMonths(6)), actorId, CancellationToken.None);
        await service.PublishAsync(ShippingProviderCodes.StorePickup, ShippingServiceFixture.TestAuditContext, first.PublicId, new PublishPackageLimitVersionRequest(first.RowVersion), actorId, CancellationToken.None);
        var second = await service.CreateDraftAsync(ShippingServiceFixture.TestAuditContext,
            ValidStorePickupRequest(baseline.AddMonths(6), baseline.AddMonths(12)), actorId, CancellationToken.None);

        await service.PublishAsync(ShippingProviderCodes.StorePickup, ShippingServiceFixture.TestAuditContext, second.PublicId, new PublishPackageLimitVersionRequest(second.RowVersion), actorId, CancellationToken.None);

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
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var service = CreateService(context);
        var draft = await service.CreateDraftAsync(ShippingServiceFixture.TestAuditContext, ValidStorePickupRequest(), actorId, CancellationToken.None);
        var staleRowVersion = (byte[])draft.RowVersion.Clone();
        staleRowVersion[0] = unchecked((byte)(staleRowVersion[0] + 1));

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(() => service.PublishAsync(ShippingProviderCodes.StorePickup, ShippingServiceFixture.TestAuditContext,
            draft.PublicId, new PublishPackageLimitVersionRequest(staleRowVersion), actorId, CancellationToken.None));
        Assert.Equal(ShippingAdminErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }

    /// <summary>組長 PR #73 review item 4: the route provider must own the version.</summary>
    [Fact]
    public async Task PublishAsync_WhenTheVersionBelongsToAnotherProvider_ThrowsResourceNotFound()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var service = CreateService(context);
        var draft = await service.CreateDraftAsync(ShippingServiceFixture.TestAuditContext, ValidStorePickupRequest(), actorId, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(() => service.PublishAsync(
            ShippingProviderCodes.HomeDelivery, ShippingServiceFixture.TestAuditContext,
            draft.PublicId, new PublishPackageLimitVersionRequest(draft.RowVersion), actorId, CancellationToken.None));
        Assert.Equal(ShippingAdminErrorCodes.ResourceNotFound, exception.ErrorCode);

        var all = await service.ListAsync(ShippingProviderCodes.StorePickup, CancellationToken.None);
        Assert.Equal(ShippingProviderProfileStatuses.Draft, all.Single().Status);
    }

    /// <summary>組長 PR #73 review item 5: out-of-chronological-order publish used to surface as an
    /// unhandled CK_ShippingProviderProfiles_Period SqlException (500).</summary>
    [Fact]
    public async Task PublishAsync_WhenTheNewWindowStartsBeforeThePublishedOnes_ThrowsAStableValidationError()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var service = CreateService(context);
        var baseline = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        // Later window published first; the earlier window then published out of order.
        var later = await service.CreateDraftAsync(ShippingServiceFixture.TestAuditContext,
            ValidStorePickupRequest(baseline.AddMonths(6), baseline.AddMonths(12)), actorId, CancellationToken.None);
        await service.PublishAsync(ShippingProviderCodes.StorePickup, ShippingServiceFixture.TestAuditContext,
            later.PublicId, new PublishPackageLimitVersionRequest(later.RowVersion), actorId, CancellationToken.None);
        var earlier = await service.CreateDraftAsync(ShippingServiceFixture.TestAuditContext,
            ValidStorePickupRequest(baseline, baseline.AddMonths(6)), actorId, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(() => service.PublishAsync(
            ShippingProviderCodes.StorePickup, ShippingServiceFixture.TestAuditContext,
            earlier.PublicId, new PublishPackageLimitVersionRequest(earlier.RowVersion), actorId, CancellationToken.None));
        Assert.Equal(ShippingAdminErrorCodes.ValidationFailed, exception.ErrorCode);

        // No partial state: the previously published window is untouched, the draft stays Draft.
        var all = await service.ListAsync(ShippingProviderCodes.StorePickup, CancellationToken.None);
        Assert.Equal(ShippingProviderProfileStatuses.Published, all.Single(version => version.Version == 1).Status);
        Assert.Equal(ShippingProviderProfileStatuses.Draft, all.Single(version => version.Version == 2).Status);
    }

    /// <summary>組長 PR #73 review item 2: create and publish land audit rows in the same
    /// transaction; a publish that loses its concurrency check rolls the audit back too.</summary>
    [Fact]
    public async Task CreateAndPublish_WriteCentralAuditEntries_AndAFailedPublishLeavesNoAuditRow()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var service = CreateService(context);
        var draft = await service.CreateDraftAsync(ShippingServiceFixture.TestAuditContext, ValidStorePickupRequest(), actorId, CancellationToken.None);

        var stale = (byte[])draft.RowVersion.Clone();
        stale[0] = unchecked((byte)(stale[0] + 1));
        await Assert.ThrowsAsync<ShippingAdminWriteException>(() => service.PublishAsync(
            ShippingProviderCodes.StorePickup, ShippingServiceFixture.TestAuditContext,
            draft.PublicId, new PublishPackageLimitVersionRequest(stale), actorId, CancellationToken.None));

        var auditsAfterFailedPublish = await context.AuditLogs.AsNoTracking()
            .Where(log => log.ResourcePublicId == draft.PublicId)
            .Select(log => log.Action)
            .ToListAsync();
        Assert.Equal(["shipping.package_limit.create"], auditsAfterFailedPublish);

        // The rolled-back publish leaves the *tracked* profile mutated in this context's change
        // tracker; production never sees that (each request gets a fresh scoped DbContext), so the
        // retry below uses a fresh context the same way a second HTTP request would.
        await using var retryContext = ShippingServiceFixture.CreateContext();
        var current = await retryContext.ShippingProviderProfiles.AsNoTracking()
            .SingleAsync(candidate => candidate.ProviderCode == ShippingProviderCodes.StorePickup);
        await CreateService(retryContext).PublishAsync(ShippingProviderCodes.StorePickup, ShippingServiceFixture.TestAuditContext,
            draft.PublicId, new PublishPackageLimitVersionRequest(current.RowVersion), actorId, CancellationToken.None);

        var auditsAfterPublish = await retryContext.AuditLogs.AsNoTracking()
            .Where(log => log.ResourcePublicId == draft.PublicId)
            .OrderBy(log => log.Id)
            .Select(log => log.Action)
            .ToListAsync();
        Assert.Equal(["shipping.package_limit.create", "shipping.package_limit.publish"], auditsAfterPublish);
    }

    private static EfPackageLimitService CreateService(DoSelectDbContext context) =>
        new(context, new EfAuditWriter(context, TimeProvider.System));
}
