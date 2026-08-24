using DoSelect.Application.Shipping;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Shipping;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Shipping;

// Package-limit-version tests each need their own exclusive "currently Published" row for the two
// fixed provider codes (ShippingProviderProfile enforces at most one Published row per ProviderCode
// via a DB unique filtered index) — sharing ShippingServiceFixture's one collection-wide database
// would make every test collide on that constraint, so each test method gets its own database
// instead (xunit constructs a fresh instance of this class per [Fact]).
[Trait("Category", "RequiresSqlServer")]
public sealed class EfPackageLimitVersionAdminServiceTests : IAsyncLifetime
{
    private readonly string _connectionString =
        $"Server=.\\SQL2025;Database=DoSelectShippingProviderTests_{Guid.NewGuid():N};Trusted_Connection=True;" +
        "TrustServerCertificate=True;";

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    private DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>().UseSqlServer(_connectionString).Options;
        return new DoSelectDbContext(options);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenProviderIsUnknown_ThrowsResourceNotFound()
    {
        await using var context = CreateContext();
        var service = new EfPackageLimitVersionAdminService(context);

        var exception = await Assert.ThrowsAsync<ShippingWriteException>(() => service.CreateDraftAsync(
            "NotAProvider",
            new CreatePackageLimitVersionRequest(1m, 10m, 10m, 10m, 30m, 1000m, null, null),
            DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(ShippingWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenDimensionsExceedTheProviderSafeRange_ThrowsValidationFailed()
    {
        await using var context = CreateContext();
        await ShippingServiceFixture.SeedPublishedProviderAsync(context, ShippingProviderCodes.ConvenienceStore);
        var service = new EfPackageLimitVersionAdminService(context);

        // 超商 Profile 可設定範圍：單邊 1～45cm — 50cm exceeds it.
        var exception = await Assert.ThrowsAsync<ShippingWriteException>(() => service.CreateDraftAsync(
            ShippingProviderCodes.ConvenienceStore,
            new CreatePackageLimitVersionRequest(5m, 50m, 45m, 45m, 105m, 100000m, null, null),
            DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(ShippingWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenNoPeriodOverlapsAnExistingDraft_CreatesTheNextVersionAsDraft()
    {
        await using var context = CreateContext();
        await ShippingServiceFixture.SeedPublishedProviderAsync(context, ShippingProviderCodes.ConvenienceStore);
        var service = new EfPackageLimitVersionAdminService(context);

        var draft = await service.CreateDraftAsync(
            ShippingProviderCodes.ConvenienceStore,
            new CreatePackageLimitVersionRequest(4m, 40m, 40m, 40m, 100m, 90000m, null, null),
            DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(2, draft.Version);
        Assert.Equal(ShippingProviderProfile.DraftStatus, draft.Status);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenPeriodOverlapsAnotherDraft_ThrowsPackageLimitPeriodOverlap()
    {
        await using var context = CreateContext();
        await ShippingServiceFixture.SeedPublishedProviderAsync(context, ShippingProviderCodes.ConvenienceStore);
        var service = new EfPackageLimitVersionAdminService(context);
        var from = DateTime.UtcNow.AddDays(10);
        var to = DateTime.UtcNow.AddDays(20);
        await service.CreateDraftAsync(
            ShippingProviderCodes.ConvenienceStore,
            new CreatePackageLimitVersionRequest(4m, 40m, 40m, 40m, 100m, 90000m, from, to),
            DateTime.UtcNow, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ShippingWriteException>(() => service.CreateDraftAsync(
            ShippingProviderCodes.ConvenienceStore,
            new CreatePackageLimitVersionRequest(4m, 40m, 40m, 40m, 100m, 90000m, from.AddDays(5), to.AddDays(5)),
            DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(ShippingWriteException.ErrorCodes.PackageLimitPeriodOverlap, exception.ErrorCode);
    }

    [Fact]
    public async Task PublishAsync_SupersedesThePreviouslyPublishedVersion()
    {
        await using var context = CreateContext();
        var (initialProfile, _) = await ShippingServiceFixture.SeedPublishedProviderAsync(
            context, ShippingProviderCodes.ConvenienceStore);
        var service = new EfPackageLimitVersionAdminService(context);
        var draft = await service.CreateDraftAsync(
            ShippingProviderCodes.ConvenienceStore,
            new CreatePackageLimitVersionRequest(4m, 40m, 40m, 40m, 100m, 90000m, null, null),
            DateTime.UtcNow, CancellationToken.None);

        var published = await service.PublishAsync(
            ShippingProviderCodes.ConvenienceStore, draft.PublicId,
            new PublishPackageLimitVersionRequest(draft.RowVersion), DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(ShippingProviderProfile.PublishedStatus, published.Status);

        await using var verifyContext = CreateContext();
        var supersededStatus = await verifyContext.ShippingProviderProfiles
            .Where(profile => profile.Id == initialProfile.Id)
            .Select(profile => profile.Status)
            .SingleAsync();
        Assert.Equal(ShippingProviderProfile.SupersededStatus, supersededStatus);
    }

    [Fact]
    public async Task PublishAsync_WhenRowVersionIsStale_ThrowsConcurrencyConflict()
    {
        await using var context = CreateContext();
        await ShippingServiceFixture.SeedPublishedProviderAsync(context, ShippingProviderCodes.ConvenienceStore);
        var service = new EfPackageLimitVersionAdminService(context);
        var draft = await service.CreateDraftAsync(
            ShippingProviderCodes.ConvenienceStore,
            new CreatePackageLimitVersionRequest(4m, 40m, 40m, 40m, 100m, 90000m, null, null),
            DateTime.UtcNow, CancellationToken.None);
        var staleRowVersion = draft.RowVersion;
        await service.PublishAsync(
            ShippingProviderCodes.ConvenienceStore, draft.PublicId,
            new PublishPackageLimitVersionRequest(staleRowVersion), DateTime.UtcNow, CancellationToken.None);

        var secondDraft = await service.CreateDraftAsync(
            ShippingProviderCodes.ConvenienceStore,
            new CreatePackageLimitVersionRequest(4m, 40m, 40m, 40m, 100m, 90000m, null, null),
            DateTime.UtcNow, CancellationToken.None);

        // Reusing the first draft's already-stale RowVersion against a fresh, still-Draft target
        // simulates a lost-update race without needing two concurrent DbContexts.
        var exception = await Assert.ThrowsAsync<ShippingWriteException>(() => service.PublishAsync(
            ShippingProviderCodes.ConvenienceStore, secondDraft.PublicId,
            new PublishPackageLimitVersionRequest(staleRowVersion), DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(ShippingWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }
}
