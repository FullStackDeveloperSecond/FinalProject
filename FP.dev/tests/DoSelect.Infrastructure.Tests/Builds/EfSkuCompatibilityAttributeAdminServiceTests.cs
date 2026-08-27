using DoSelect.Application.Auditing;
using DoSelect.Application.Builds;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Builds;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Builds;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Builds;

/// <summary>
/// 組長 PR #34 review, item 4: proves a SKU that was NOT created by the dev seeder — one built
/// the same way a real catalog admin would create it — can still get real compatibility facts
/// through this admin write path and be evaluated correctly, closing the gap the review flagged
/// ("固定的 DEV-COMPAT-* 商品可以評為 compatible，不代表真實商品不會持續得到 insufficientData").
/// </summary>
[Collection(nameof(CompatibilityCheckServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfSkuCompatibilityAttributeAdminServiceTests
{
    private static readonly AuditRequestContext AuditContext = CompatibilityCheckServiceFixture.TestAuditContext;

    private readonly CompatibilityCheckServiceFixture _fixture;

    public EfSkuCompatibilityAttributeAdminServiceTests(CompatibilityCheckServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SetAsync_PersistsAttributesAndStoragePorts_AndTheEngineReadsThemBackCorrectly()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        // No `attributes`/`storagePorts` passed here — this is exactly what a real catalog admin
        // creating a SKU through the ordinary flow ends up with today: zero compatibility facts.
        var cooler = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Cooler,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.CoolerHeightMm] = 150m });
        var adminUserId = await CompatibilityCheckServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);

        var service = new EfSkuCompatibilityAttributeAdminService(context, new EfAuditWriter(context, TimeProvider.System));
        var before = await service.GetAsync(cooler.PublicId, CancellationToken.None);
        Assert.Empty(before.Attributes);

        var updated = await service.SetAsync(
            cooler.PublicId,
            adminUserId,
            new SetSkuCompatibilityAttributesRequest(
                new Dictionary<string, IReadOnlyList<string>>
                {
                    // Deliberately lowercase — proves normalization, not just pass-through.
                    [CompatibilityAttributeKeys.CoolerSupportedSockets] = ["am5", "lga1700"],
                },
                new Dictionary<string, int>(),
                before.RowVersion),
            AuditContext,
            CancellationToken.None);

        Assert.Equal(["AM5", "LGA1700"], updated.Attributes[CompatibilityAttributeKeys.CoolerSupportedSockets].OrderBy(v => v));
        Assert.NotEqual(before.RowVersion, updated.RowVersion);

        var factsReader = new EfCompatibilityFactsReader(context);
        var resolution = await factsReader.ResolveAsync(
            [new BuildItemInput(cooler.PublicId, 1)], CancellationToken.None);
        Assert.Contains("AM5", resolution.Components.Cooler!.SupportedSockets);
    }

    [Fact]
    public async Task SetAsync_ReplacesRatherThanAppends_OnASecondCall()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var motherboard = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Motherboard);
        var adminUserId = await CompatibilityCheckServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = new EfSkuCompatibilityAttributeAdminService(context, new EfAuditWriter(context, TimeProvider.System));
        var before = await service.GetAsync(motherboard.PublicId, CancellationToken.None);

        var afterFirst = await service.SetAsync(
            motherboard.PublicId,
            adminUserId,
            new SetSkuCompatibilityAttributesRequest(
                new Dictionary<string, IReadOnlyList<string>>(),
                new Dictionary<string, int> { ["SATA"] = 4 },
                before.RowVersion),
            AuditContext,
            CancellationToken.None);
        var afterSecond = await service.SetAsync(
            motherboard.PublicId,
            adminUserId,
            new SetSkuCompatibilityAttributesRequest(
                new Dictionary<string, IReadOnlyList<string>>(),
                new Dictionary<string, int> { ["NVME"] = 2 },
                afterFirst.RowVersion),
            AuditContext,
            CancellationToken.None);

        var port = Assert.Single(afterSecond.StoragePorts);
        Assert.Equal("NVME", port.Key);
        Assert.Equal(2, port.Value);
    }

    [Fact]
    public async Task SetAsync_Throws_ConcurrencyConflict_ForAStaleRowVersion()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var motherboard = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Motherboard);
        var adminUserId = await CompatibilityCheckServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = new EfSkuCompatibilityAttributeAdminService(context, new EfAuditWriter(context, TimeProvider.System));
        var before = await service.GetAsync(motherboard.PublicId, CancellationToken.None);

        await service.SetAsync(
            motherboard.PublicId,
            adminUserId,
            new SetSkuCompatibilityAttributesRequest(
                new Dictionary<string, IReadOnlyList<string>>(),
                new Dictionary<string, int> { ["SATA"] = 4 },
                before.RowVersion),
            AuditContext,
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.SetAsync(
            motherboard.PublicId,
            adminUserId,
            new SetSkuCompatibilityAttributesRequest(
                new Dictionary<string, IReadOnlyList<string>>(),
                new Dictionary<string, int> { ["NVME"] = 2 },
                before.RowVersion),
            AuditContext,
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }

    /// <summary>組長 PR #34 round-4 review: two admins racing the SAME SKU's compatibility facts — one wins, the other gets a stable stale-token conflict, and the winner's data is fully intact afterward.</summary>
    [Fact]
    public async Task SetAsync_WhenTwoAdminsConcurrentlyEditTheSameSku_OnlyOneSucceeds_AndItsDataSurvives()
    {
        await using var seedContext = CompatibilityCheckServiceFixture.CreateContext();
        var motherboard = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            seedContext, BuildComponentCategoryCodes.Motherboard);
        var adminUserId = await CompatibilityCheckServiceFixture.SeedAdminUserIdAsync(seedContext, AuditRoleNames.CatalogManager);
        var seedingService = new EfSkuCompatibilityAttributeAdminService(seedContext, new EfAuditWriter(seedContext, TimeProvider.System));
        var startingRowVersion = (await seedingService.GetAsync(motherboard.PublicId, CancellationToken.None)).RowVersion;

        await using var contextA = CompatibilityCheckServiceFixture.CreateContext();
        await using var contextB = CompatibilityCheckServiceFixture.CreateContext();
        var serviceA = new EfSkuCompatibilityAttributeAdminService(contextA, new EfAuditWriter(contextA, TimeProvider.System));
        var serviceB = new EfSkuCompatibilityAttributeAdminService(contextB, new EfAuditWriter(contextB, TimeProvider.System));

        var results = await Task.WhenAll(
            RunOrCaptureConflictAsync(() => serviceA.SetAsync(
                motherboard.PublicId,
                adminUserId,
                new SetSkuCompatibilityAttributesRequest(
                    new Dictionary<string, IReadOnlyList<string>>(), new Dictionary<string, int> { ["SATA"] = 4 },
                    startingRowVersion),
                AuditContext,
                CancellationToken.None)),
            RunOrCaptureConflictAsync(() => serviceB.SetAsync(
                motherboard.PublicId,
                adminUserId,
                new SetSkuCompatibilityAttributesRequest(
                    new Dictionary<string, IReadOnlyList<string>>(), new Dictionary<string, int> { ["NVME"] = 2 },
                    startingRowVersion),
                AuditContext,
                CancellationToken.None)));

        Assert.Single(results, succeeded => succeeded);
        Assert.Single(results, succeeded => !succeeded);

        var final = await seedingService.GetAsync(motherboard.PublicId, CancellationToken.None);
        var winningPort = Assert.Single(final.StoragePorts);
        Assert.Contains(winningPort.Key, new[] { "SATA", "NVME" });
    }

    private static async Task<bool> RunOrCaptureConflictAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (BuildWriteException exception) when (exception.ErrorCode == BuildWriteException.ErrorCodes.ConcurrencyConflict)
        {
            return false;
        }
    }

    [Fact]
    public async Task SetAsync_Throws_ValidationFailed_ForAnUnknownAttributeKey()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var gpu = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.GraphicsCard);
        var adminUserId = await CompatibilityCheckServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = new EfSkuCompatibilityAttributeAdminService(context, new EfAuditWriter(context, TimeProvider.System));
        var before = await service.GetAsync(gpu.PublicId, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.SetAsync(
            gpu.PublicId,
            adminUserId,
            new SetSkuCompatibilityAttributesRequest(
                new Dictionary<string, IReadOnlyList<string>> { ["not_a_real_key"] = ["x"] },
                new Dictionary<string, int>(),
                before.RowVersion),
            AuditContext,
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    /// <summary>組長 PR #34 round-4 review, item 2: an attribute key that only makes sense for a different component role must be rejected, not silently persisted-but-never-read.</summary>
    [Fact]
    public async Task SetAsync_Throws_ValidationFailed_ForAnAttributeKeyThatDoesNotMatchTheSkusCategory()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var gpu = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.GraphicsCard);
        var adminUserId = await CompatibilityCheckServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = new EfSkuCompatibilityAttributeAdminService(context, new EfAuditWriter(context, TimeProvider.System));
        var before = await service.GetAsync(gpu.PublicId, CancellationToken.None);

        // CoolerSupportedSockets only makes sense on a Cooler SKU, not a GraphicsCard one.
        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.SetAsync(
            gpu.PublicId,
            adminUserId,
            new SetSkuCompatibilityAttributesRequest(
                new Dictionary<string, IReadOnlyList<string>>
                {
                    [CompatibilityAttributeKeys.CoolerSupportedSockets] = ["AM5"],
                },
                new Dictionary<string, int>(),
                before.RowVersion),
            AuditContext,
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task SetAsync_Throws_ValidationFailed_ForStoragePortsOnANonMotherboardSku()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var storage = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.StorageDevice);
        var adminUserId = await CompatibilityCheckServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = new EfSkuCompatibilityAttributeAdminService(context, new EfAuditWriter(context, TimeProvider.System));
        var before = await service.GetAsync(storage.PublicId, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.SetAsync(
            storage.PublicId,
            adminUserId,
            new SetSkuCompatibilityAttributesRequest(
                new Dictionary<string, IReadOnlyList<string>>(),
                new Dictionary<string, int> { ["NVME"] = 4 },
                before.RowVersion),
            AuditContext,
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task SetAsync_Throws_ValidationFailed_ForDuplicateValuesAfterNormalization()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var cooler = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Cooler);
        var adminUserId = await CompatibilityCheckServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = new EfSkuCompatibilityAttributeAdminService(context, new EfAuditWriter(context, TimeProvider.System));
        var before = await service.GetAsync(cooler.PublicId, CancellationToken.None);

        // "am5" and "AM5" are the same normalized code — must be caught as a clean
        // validation_failed, not left to hit the database's own unique index as a 500.
        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.SetAsync(
            cooler.PublicId,
            adminUserId,
            new SetSkuCompatibilityAttributesRequest(
                new Dictionary<string, IReadOnlyList<string>>
                {
                    [CompatibilityAttributeKeys.CoolerSupportedSockets] = ["am5", "AM5"],
                },
                new Dictionary<string, int>(),
                before.RowVersion),
            AuditContext,
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task SetAsync_Throws_ValidationFailed_ForAnOutOfRangePortCount()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var motherboard = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Motherboard);
        var adminUserId = await CompatibilityCheckServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = new EfSkuCompatibilityAttributeAdminService(context, new EfAuditWriter(context, TimeProvider.System));
        var before = await service.GetAsync(motherboard.PublicId, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.SetAsync(
            motherboard.PublicId,
            adminUserId,
            new SetSkuCompatibilityAttributesRequest(
                new Dictionary<string, IReadOnlyList<string>>(),
                new Dictionary<string, int> { ["NVME"] = 0 },
                before.RowVersion),
            AuditContext,
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task SetAsync_Throws_ResourceNotFound_ForAnUnknownSku()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var adminUserId = await CompatibilityCheckServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = new EfSkuCompatibilityAttributeAdminService(context, new EfAuditWriter(context, TimeProvider.System));

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.SetAsync(
            Guid.NewGuid(),
            adminUserId,
            new SetSkuCompatibilityAttributesRequest(
                new Dictionary<string, IReadOnlyList<string>>(), new Dictionary<string, int>(), []),
            AuditContext,
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);
    }

    /// <summary>PR #34 round-6 review, A1 裁定: a successful write leaves exactly one central Audit Log row, on action SkuCompatibilityAttributesReplace and resource Sku/skuPublicId.</summary>
    [Fact]
    public async Task SetAsync_WritesExactlyOneAuditLogRow_OnSuccess()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var cooler = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Cooler);
        var adminUserId = await CompatibilityCheckServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = new EfSkuCompatibilityAttributeAdminService(context, new EfAuditWriter(context, TimeProvider.System));
        var before = await service.GetAsync(cooler.PublicId, CancellationToken.None);

        await service.SetAsync(
            cooler.PublicId,
            adminUserId,
            new SetSkuCompatibilityAttributesRequest(
                new Dictionary<string, IReadOnlyList<string>>
                {
                    [CompatibilityAttributeKeys.CoolerSupportedSockets] = ["AM5"],
                },
                new Dictionary<string, int>(),
                before.RowVersion),
            AuditContext,
            CancellationToken.None);

        // The collection-shared fixture's database accumulates Audit rows from every test in this
        // class, so "exactly one" is scoped to this SKU's own resource, not the whole table.
        var audit = Assert.Single(await context.AuditLogs.AsNoTracking()
            .Where(log => log.ResourcePublicId == cooler.PublicId)
            .ToListAsync());
        Assert.Equal(AuditActions.SkuCompatibilityAttributesReplace, audit.Action);
        Assert.Equal(AuditResourceTypes.Sku, audit.ResourceType);
        Assert.Equal(AuditResult.Success, audit.Result);
    }

    /// <summary>PR #34 round-6 review, A1 裁定: an Audit write failure must roll back the entire attribute/port replace — no core data change and no Audit row may survive a failed Audit write.</summary>
    [Fact]
    public async Task SetAsync_WhenAuditWriteFails_RollsBackTheCoreAttributeAndPortWrites()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var motherboard = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Motherboard);
        var adminUserId = await CompatibilityCheckServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);
        var service = new EfSkuCompatibilityAttributeAdminService(context, new ThrowingAuditWriter());
        var before = await service.GetAsync(motherboard.PublicId, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAsync(
            motherboard.PublicId,
            adminUserId,
            new SetSkuCompatibilityAttributesRequest(
                new Dictionary<string, IReadOnlyList<string>>(),
                new Dictionary<string, int> { ["SATA"] = 4 },
                before.RowVersion),
            AuditContext,
            CancellationToken.None));

        context.ChangeTracker.Clear();
        Assert.Empty(await context.SkuStorageInterfacePorts.Where(port => port.SkuId == motherboard.Id).ToListAsync());
        Assert.Empty(await context.AuditLogs.Where(log => log.ResourcePublicId == motherboard.PublicId).ToListAsync());
        var untouched = await context.Skus.AsNoTracking().SingleAsync(sku => sku.Id == motherboard.Id);
        Assert.Equal(before.RowVersion, untouched.RowVersion);
    }

    private sealed class ThrowingAuditWriter : IAuditWriter
    {
        public AuditLog Add(AuditWriteRequest request) =>
            throw new InvalidOperationException("Injected audit failure.");
    }
}
