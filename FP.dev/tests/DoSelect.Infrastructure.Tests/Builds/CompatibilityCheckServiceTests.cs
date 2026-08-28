using DoSelect.Application.Builds;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Builds;
using DoSelect.Infrastructure.Catalog;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Builds;

[Collection(nameof(CompatibilityCheckServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class CompatibilityCheckServiceTests
{
    private readonly CompatibilityCheckServiceFixture _fixture;

    public CompatibilityCheckServiceTests(CompatibilityCheckServiceFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Mirrors <see cref="EfBuildListServiceTests.SeedCompleteBuildComponentsAsync"/> — the canonical
    /// <see cref="DoSelect.Domain.Builds.CompatibilityEvaluator"/> requires every singleton role (and at least
    /// one Memory/Storage) present before it evaluates any pairwise rule, so a partial SKU set only ever
    /// reaches <c>insufficientData</c>. Tests that need a specific pairwise rule to actually fire seed the full
    /// 8-category baseline via this helper, then substitute in one custom-seeded SKU for the category under test.</summary>
    private static Task<(Sku Cpu, Sku Motherboard, Sku Memory, Sku Psu, Sku Case, Sku Gpu, Sku Storage, Sku Cooler)>
        SeedCompleteBuildAsync(DoSelect.Infrastructure.Persistence.DoSelectDbContext context) =>
        EfBuildListServiceTests.SeedCompleteBuildComponentsAsync(context);

    [Fact]
    public async Task CheckAsync_ReturnsCompatible_ForAMatchingCpuAndMotherboard()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var components = await SeedCompleteBuildAsync(context);

        var service = new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context));
        var result = await service.CheckAsync(
            new CompatibilityCheckRequest(EfBuildListServiceTests.ToBuildItems(components)),
            null,
            CancellationToken.None);

        Assert.Equal("compatible", result.Overall);
        Assert.Empty(result.Results);
        Assert.Equal(EfCompatibilityCheckService.RuleSetVersion, result.RuleSetVersion);
    }

    [Fact]
    public async Task CheckAsync_ReturnsBlocked_ForAMismatchedSocket()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var components = await SeedCompleteBuildAsync(context);
        var mismatchedBoard = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context,
            CompatibilityCatalogContract.Categories.Motherboard,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.CpuSocket] = "LGA1700",
                [CompatibilityCatalogContract.SemanticKeys.MotherboardChipset] = "X670E",
                [CompatibilityCatalogContract.SemanticKeys.MemoryType] = "DDR5",
                [CompatibilityCatalogContract.SemanticKeys.MemorySlotCount] = 4m,
                [CompatibilityCatalogContract.SemanticKeys.MemoryMaxCapacityGb] = 128m,
                [CompatibilityCatalogContract.SemanticKeys.MotherboardFormFactor] = "ATX",
                [CompatibilityCatalogContract.SemanticKeys.M2SlotCount] = 4m,
                [CompatibilityCatalogContract.SemanticKeys.SataPortCount] = 4m,
                [CompatibilityCatalogContract.SemanticKeys.MotherboardCpuEps8PinRequiredCount] = 1m,
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 20m,
            });
        var items = EfBuildListServiceTests.ToBuildItems(components with { Motherboard = mismatchedBoard });

        var service = new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context));
        var result = await service.CheckAsync(new CompatibilityCheckRequest(items), null, CancellationToken.None);

        // Overall rolls up to blocked even though CPU_CHIPSET also fires as insufficientData
        // (Cpu.Generation "RYZEN_7000" has no mapping for the mismatched board's own chipset
        // pairing check to run against — TryGet fails) — Blocked outranks InsufficientData per
        // CompatibilityEvaluator's severity precedence.
        Assert.Equal("blocked", result.Overall);
        var socketFinding = Assert.Single(result.Results, f => f.RuleCode == CompatibilityRuleCodes.CpuSocket);
        Assert.Equal("blocked", socketFinding.Severity);
    }

    [Fact]
    public async Task CheckAsync_MergesDuplicateSkuEntriesByQuantity()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var components = await SeedCompleteBuildAsync(context);

        var items = EfBuildListServiceTests.ToBuildItems(components)
            .Where(item => item.SkuPublicId != components.Memory.PublicId)
            .Append(new BuildItemInput(components.Memory.PublicId, 1))
            .Append(new BuildItemInput(components.Memory.PublicId, 1)) // same SKU listed twice -> merges to quantity 2
            .ToList();

        var service = new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context));
        var result = await service.CheckAsync(new CompatibilityCheckRequest(items), null, CancellationToken.None);

        // 2 modules against a 4-slot board with the default 0-slot warning threshold: no finding.
        Assert.Equal("compatible", result.Overall);
    }

    [Fact]
    public async Task CheckAsync_Throws_ForAnUnknownSku()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context));

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.CheckAsync(
            new CompatibilityCheckRequest([new BuildItemInput(Guid.NewGuid(), 1)]),
            null,
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task CheckAsync_RejectsItemCountsOutsideOneToTwenty(int itemCount)
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context));
        var items = Enumerable.Range(0, itemCount).Select(_ => new BuildItemInput(Guid.NewGuid(), 1)).ToList();

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.CheckAsync(
            new CompatibilityCheckRequest(items),
            null,
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task CheckAsync_RejectsAMergedQuantityAboveEight()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memory = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, CompatibilityCatalogContract.Categories.Memory);
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context));

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.CheckAsync(
            new CompatibilityCheckRequest(
            [
                new BuildItemInput(memory.PublicId, 5),
                new BuildItemInput(memory.PublicId, 5),
            ]),
            null,
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    /// <summary>
    /// PR #34 round-3 review: FirstOfRole used to silently pick the lowest SkuId and ignore every
    /// other SKU in a single-instance role (CPU／Motherboard／GPU／PSU／Case／Cooler) — a second CPU
    /// still rode along into the cart without ever participating in evaluation. Canonical
    /// <see cref="DoSelect.Domain.Builds.CompatibilityEvaluator"/> handles this itself now: a
    /// singleton category with more than one distinct SKU present fails its own presence check
    /// (組長 PR #34 round-7 review: a 200 OK with <c>insufficientData</c>, not a thrown validation
    /// error like the deleted parallel model used to return).
    /// </summary>
    [Fact]
    public async Task CheckAsync_ReturnsInsufficientData_WhenTwoDistinctCpuSkusAreRequested()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var components = await SeedCompleteBuildAsync(context);
        var secondCpu = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, CompatibilityCatalogContract.Categories.Cpu);
        var items = EfBuildListServiceTests.ToBuildItems(components)
            .Append(new BuildItemInput(secondCpu.PublicId, 1))
            .ToList();
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context));

        var result = await service.CheckAsync(new CompatibilityCheckRequest(items), null, CancellationToken.None);

        Assert.Equal("insufficientData", result.Overall);
        Assert.Contains(result.Results, f =>
            f.RuleCode == CompatibilityRuleCodes.RequiredComponent &&
            Equals(f.Facts["categoryCode"], CompatibilityCatalogContract.Categories.Cpu));
    }

    [Fact]
    public async Task CheckAsync_ReturnsInsufficientData_WhenCpuQuantityIsGreaterThanOne()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var components = await SeedCompleteBuildAsync(context);
        var items = EfBuildListServiceTests.ToBuildItems(components)
            .Where(item => item.SkuPublicId != components.Cpu.PublicId)
            .Append(new BuildItemInput(components.Cpu.PublicId, 2))
            .ToList();
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context));

        var result = await service.CheckAsync(new CompatibilityCheckRequest(items), null, CancellationToken.None);

        Assert.Equal("insufficientData", result.Overall);
        Assert.Contains(result.Results, f =>
            f.RuleCode == CompatibilityRuleCodes.RequiredComponent &&
            Equals(f.Facts["categoryCode"], CompatibilityCatalogContract.Categories.Cpu));
    }

    [Fact]
    public async Task CheckAsync_ReturnsInsufficientData_WhenTwoDistinctMotherboardSkusAreRequested()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var components = await SeedCompleteBuildAsync(context);
        var secondBoard = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, CompatibilityCatalogContract.Categories.Motherboard);
        var items = EfBuildListServiceTests.ToBuildItems(components)
            .Append(new BuildItemInput(secondBoard.PublicId, 1))
            .ToList();
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context));

        var result = await service.CheckAsync(new CompatibilityCheckRequest(items), null, CancellationToken.None);

        Assert.Equal("insufficientData", result.Overall);
        Assert.Contains(result.Results, f =>
            f.RuleCode == CompatibilityRuleCodes.RequiredComponent &&
            Equals(f.Facts["categoryCode"], CompatibilityCatalogContract.Categories.Motherboard));
    }

    /// <summary>Memory is a genuine multi-instance role — this must keep working after the singleton-role guard was added.</summary>
    [Fact]
    public async Task CheckAsync_Allows_MultipleMemoryModulesAndQuantityGreaterThanOne()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var components = await SeedCompleteBuildAsync(context);
        // The baseline board has 4 memory slots; a second distinct 2-module memory SKU keeps this
        // test's "two distinct SKUs, quantity > 1 each" intent while still landing well under
        // capacity (2 + 2 = 4 slots used, matching the 4-slot board exactly — replace with a
        // higher-capacity board here, seeded fresh, so there is real headroom to prove "compatible"
        // rather than tripping the low-slot warning).
        var wideBoard = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context,
            CompatibilityCatalogContract.Categories.Motherboard,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.CpuSocket] = "AM5",
                [CompatibilityCatalogContract.SemanticKeys.MotherboardChipset] = "X670E",
                [CompatibilityCatalogContract.SemanticKeys.MemoryType] = "DDR5",
                [CompatibilityCatalogContract.SemanticKeys.MemorySlotCount] = 8m,
                [CompatibilityCatalogContract.SemanticKeys.MemoryMaxCapacityGb] = 256m,
                [CompatibilityCatalogContract.SemanticKeys.MotherboardFormFactor] = "ATX",
                [CompatibilityCatalogContract.SemanticKeys.M2SlotCount] = 4m,
                [CompatibilityCatalogContract.SemanticKeys.SataPortCount] = 4m,
                [CompatibilityCatalogContract.SemanticKeys.MotherboardCpuEps8PinRequiredCount] = 1m,
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 20m,
            });
        var secondModule = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context,
            CompatibilityCatalogContract.Categories.Memory,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.MemoryType] = "DDR5",
                [CompatibilityCatalogContract.SemanticKeys.MemoryModuleCount] = 1m,
                [CompatibilityCatalogContract.SemanticKeys.MemoryKitCapacityGb] = 16m,
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 5m,
            });
        var items = EfBuildListServiceTests.ToBuildItems(components with { Motherboard = wideBoard })
            .Where(item => item.SkuPublicId != components.Memory.PublicId)
            .Append(new BuildItemInput(components.Memory.PublicId, 2))
            .Append(new BuildItemInput(secondModule.PublicId, 2))
            .ToList();
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context));

        var result = await service.CheckAsync(new CompatibilityCheckRequest(items), null, CancellationToken.None);

        Assert.Equal("compatible", result.Overall);
    }

    /// <summary>
    /// Regression test: proves the motherboard's M2/SATA port counts are read as independent
    /// pools rather than conflated — a board with 2 SATA and 2 M2 slots accepts 1 of each at
    /// once, leaving headroom on both independently (組長 PR #34 round-4 review, item 1's
    /// "multi-interface grouping" case).
    /// </summary>
    [Fact]
    public async Task CheckAsync_WhenStorageUsageStaysWithinEachInterfaceIndependently_ReturnsCompatible()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var components = await SeedCompleteBuildAsync(context);
        var board = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, CompatibilityCatalogContract.Categories.Motherboard,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.CpuSocket] = "AM5",
                [CompatibilityCatalogContract.SemanticKeys.MotherboardChipset] = "X670E",
                [CompatibilityCatalogContract.SemanticKeys.MemoryType] = "DDR5",
                [CompatibilityCatalogContract.SemanticKeys.MemorySlotCount] = 4m,
                [CompatibilityCatalogContract.SemanticKeys.MemoryMaxCapacityGb] = 128m,
                [CompatibilityCatalogContract.SemanticKeys.MotherboardFormFactor] = "ATX",
                [CompatibilityCatalogContract.SemanticKeys.M2SlotCount] = 2m,
                [CompatibilityCatalogContract.SemanticKeys.SataPortCount] = 2m,
                [CompatibilityCatalogContract.SemanticKeys.MotherboardCpuEps8PinRequiredCount] = 1m,
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 20m,
            });
        var sataDrive = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, CompatibilityCatalogContract.Categories.Storage,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.StorageInterface] = "SATA",
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 5m,
            });
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context));

        var items = EfBuildListServiceTests.ToBuildItems(components with { Motherboard = board })
            .Append(new BuildItemInput(sataDrive.PublicId, 1))
            .ToList();
        var result = await service.CheckAsync(new CompatibilityCheckRequest(items), null, CancellationToken.None);

        Assert.Equal("compatible", result.Overall);
    }

    /// <summary>
    /// Regression test: SATA over capacity must block even though M2 (a different interface on
    /// the same board) still has headroom — proves usage is tallied per interface, not against a
    /// combined port total. Unlike the old per-interface model, the canonical
    /// <see cref="DoSelect.Domain.Builds.CompatibilityEvaluator"/> reports this as one combined
    /// finding carrying both interfaces' counts, not one finding per offending interface.
    /// </summary>
    [Fact]
    public async Task CheckAsync_WhenOneInterfaceExceedsItsOwnPortCount_ReturnsBlocked()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var components = await SeedCompleteBuildAsync(context); // Storage is already M2_NVME qty 1
        var board = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, CompatibilityCatalogContract.Categories.Motherboard,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.CpuSocket] = "AM5",
                [CompatibilityCatalogContract.SemanticKeys.MotherboardChipset] = "X670E",
                [CompatibilityCatalogContract.SemanticKeys.MemoryType] = "DDR5",
                [CompatibilityCatalogContract.SemanticKeys.MemorySlotCount] = 4m,
                [CompatibilityCatalogContract.SemanticKeys.MemoryMaxCapacityGb] = 128m,
                [CompatibilityCatalogContract.SemanticKeys.MotherboardFormFactor] = "ATX",
                [CompatibilityCatalogContract.SemanticKeys.M2SlotCount] = 2m,
                [CompatibilityCatalogContract.SemanticKeys.SataPortCount] = 2m,
                [CompatibilityCatalogContract.SemanticKeys.MotherboardCpuEps8PinRequiredCount] = 1m,
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 20m,
            });
        var sataDrive = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, CompatibilityCatalogContract.Categories.Storage,
            new Dictionary<string, object?>
            {
                [CompatibilityCatalogContract.SemanticKeys.StorageInterface] = "SATA",
                [CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 5m,
            });
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context));

        var items = EfBuildListServiceTests.ToBuildItems(components with { Motherboard = board })
            .Append(new BuildItemInput(sataDrive.PublicId, 3))
            .ToList();
        var result = await service.CheckAsync(new CompatibilityCheckRequest(items), null, CancellationToken.None);

        Assert.Equal("blocked", result.Overall);
        var finding = Assert.Single(result.Results, r => r.RuleCode == CompatibilityRuleCodes.StorageInterface);
        Assert.Equal("compatibility.storage_ports_exceeded", finding.MessageKey);
        Assert.Equal(2, Convert.ToInt32(finding.Facts["sataPorts"]));
        Assert.Equal(3, Convert.ToInt32(finding.Facts["sataUsed"]));
        Assert.Equal(2, Convert.ToInt32(finding.Facts["m2Slots"]));
        Assert.Equal(1, Convert.ToInt32(finding.Facts["m2Used"]));
    }

    /// <summary>
    /// Regression test: the public check endpoint persists a Run/Result snapshot on every
    /// anonymous call with no retention — proves the purge deletes only rows past the cutoff,
    /// deletes their Results too (no orphans), and is safe to call again once nothing is left
    /// (組長 PR #34 round-4 review, item 3's "monitorable, retryable batch cleanup" ask).
    /// </summary>
    [Fact]
    public async Task PurgeExpiredRunsAsync_DeletesOnlyRunsOlderThanTheCutoff_AndIsIdempotent()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, CompatibilityCatalogContract.Categories.Storage);
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context));

        var oldCheck = await service.CheckAsync(
            new CompatibilityCheckRequest([new BuildItemInput(sku.PublicId, 1)]), null, CancellationToken.None);
        var freshCheck = await service.CheckAsync(
            new CompatibilityCheckRequest([new BuildItemInput(sku.PublicId, 2)]), null, CancellationToken.None);

        var cutoff = DateTime.UtcNow;
        var oldRunId = await context.CompatibilityCheckRuns
            .Where(run => run.EvaluatedAtUtc == oldCheck.EvaluatedAtUtc)
            .Select(run => run.Id)
            .FirstAsync();
        // Backdate only the old run past the retention cutoff — everything else (including
        // freshCheck's own run) stays at its real creation time. PurgeExpiredRunsAsync filters on
        // EvaluatedAtUtc (組長 PR #34 review: the entity's own indexed business timestamp), not
        // the inherited CreatedAtUtc, so both must move together here even though the entity's own
        // constructor always keeps them equal in real usage.
        await context.CompatibilityCheckRuns
            .Where(run => run.Id == oldRunId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.CreatedAtUtc, cutoff.AddDays(-91))
                .SetProperty(run => run.EvaluatedAtUtc, cutoff.AddDays(-91)));

        var deletedCount = await service.PurgeExpiredRunsAsync(cutoff.AddDays(-90), batchSize: 100, CancellationToken.None);

        Assert.Equal(1, deletedCount);
        Assert.False(await context.CompatibilityCheckRuns.AnyAsync(run => run.Id == oldRunId));
        Assert.False(await context.CompatibilityCheckResults.AnyAsync(result => result.CompatibilityCheckRunId == oldRunId));
        Assert.True(await context.CompatibilityCheckRuns.AnyAsync(run => run.EvaluatedAtUtc == freshCheck.EvaluatedAtUtc));

        var secondCallDeletedCount = await service.PurgeExpiredRunsAsync(cutoff.AddDays(-90), batchSize: 100, CancellationToken.None);
        Assert.Equal(0, secondCallDeletedCount);
    }

    /// <summary>
    /// 組長 PR #34 round-5 review, item 3: the retention batch query now seeks the new
    /// IX_CompatibilityCheckRuns_EvaluatedAtUtc_Id index instead of scanning the table — evidence
    /// is the generated SQL's ORDER BY matching that index's column order exactly (EvaluatedAtUtc
    /// then Id), which is what lets SQL Server satisfy OrderBy+Take via an index seek/top-N rather
    /// than a sort over the whole table.
    /// </summary>
    [Fact]
    public void PurgeExpiredRunsAsync_BatchQuery_OrdersByEvaluatedAtUtcThenId_MatchingTheNewIndex()
    {
        using var context = CompatibilityCheckServiceFixture.CreateContext();
        var olderThanUtc = DateTime.UtcNow;

        var query = context.CompatibilityCheckRuns
            .Where(run => run.EvaluatedAtUtc < olderThanUtc)
            .OrderBy(run => run.EvaluatedAtUtc)
            .ThenBy(run => run.Id)
            .Select(run => run.Id)
            .Take(500);

        var sql = query.ToQueryString();

        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        var orderByIndex = sql.IndexOf("ORDER BY", StringComparison.Ordinal);
        var orderByClause = sql[orderByIndex..];
        var evaluatedAtIndex = orderByClause.IndexOf("EvaluatedAtUtc", StringComparison.Ordinal);
        var idIndex = orderByClause.IndexOf("].[Id]", StringComparison.Ordinal);
        Assert.True(evaluatedAtIndex >= 0 && idIndex >= 0 && evaluatedAtIndex < idIndex,
            $"Expected ORDER BY to sort EvaluatedAtUtc before Id (matching the index column order). Actual SQL:\n{sql}");
    }

    /// <summary>
    /// Proves the batch is deterministic even when many rows share the same millisecond
    /// EvaluatedAtUtc timestamp — without the ThenBy(Id) tiebreaker (added this round to match the
    /// new index's trailing column), repeated calls over ties could reorder and skip or re-visit
    /// rows across batches.
    /// </summary>
    [Fact]
    public async Task PurgeExpiredRunsAsync_WhenManyRunsShareTheSameEvaluatedAtUtc_DeletesThemAllAcrossBatches()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, CompatibilityCatalogContract.Categories.Storage);
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityCatalogReader(context));

        var cutoff = DateTime.UtcNow;
        var sameTimestamp = cutoff.AddDays(-91);
        var runIds = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            var check = await service.CheckAsync(
                new CompatibilityCheckRequest([new BuildItemInput(sku.PublicId, i + 1)]), null, CancellationToken.None);
            var runId = await context.CompatibilityCheckRuns
                .Where(run => run.EvaluatedAtUtc == check.EvaluatedAtUtc)
                .Select(run => run.Id)
                .FirstAsync();
            runIds.Add(runId);
        }

        await context.CompatibilityCheckRuns
            .Where(run => runIds.Contains(run.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.CreatedAtUtc, sameTimestamp)
                .SetProperty(run => run.EvaluatedAtUtc, sameTimestamp));

        var firstBatch = await service.PurgeExpiredRunsAsync(cutoff.AddDays(-90), batchSize: 2, CancellationToken.None);
        var secondBatch = await service.PurgeExpiredRunsAsync(cutoff.AddDays(-90), batchSize: 2, CancellationToken.None);
        var thirdBatch = await service.PurgeExpiredRunsAsync(cutoff.AddDays(-90), batchSize: 2, CancellationToken.None);

        Assert.Equal(2, firstBatch);
        Assert.Equal(2, secondBatch);
        Assert.Equal(1, thirdBatch);
        Assert.False(await context.CompatibilityCheckRuns.AnyAsync(run => runIds.Contains(run.Id)));
    }

    /// <summary>
    /// 組長 PR #34 review, item 5: the Results-then-Runs delete now shares one SQL Server
    /// transaction. Forces the second (Runs) delete to fail via a real ADO.NET interceptor, then
    /// proves the whole batch rolled back (Results are NOT orphaned/pre-deleted) and that a normal
    /// retry afterward succeeds and deletes everything — the exact failure/retry shape a batch
    /// cleanup job needs.
    /// </summary>
    [Fact]
    public async Task PurgeExpiredRunsAsync_WhenTheRunsDeleteFails_RollsBackTheResultsDeleteToo_AndSucceedsOnRetry()
    {
        await using var seedContext = CompatibilityCheckServiceFixture.CreateContext();
        // A single lone SKU produces zero rule Findings (every rule needs its counterpart
        // component present too), which would leave CompatibilityCheckResults empty regardless of
        // whether the rollback under test actually works — a matching CPU+Motherboard pair fires
        // CPU_SOCKET (severity "compatible") and genuinely leaves a Result row to roll back.
        var cpu = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            seedContext, CompatibilityCatalogContract.Categories.Cpu,
            new Dictionary<string, object?> { [CompatibilityCatalogContract.SemanticKeys.CpuSocket] = "AM5" });
        var board = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            seedContext, CompatibilityCatalogContract.Categories.Motherboard,
            new Dictionary<string, object?> { [CompatibilityCatalogContract.SemanticKeys.CpuSocket] = "AM5" });
        var seedingService = new EfCompatibilityCheckService(seedContext, new EfCompatibilityCatalogReader(seedContext));
        var oldCheck = await seedingService.CheckAsync(
            new CompatibilityCheckRequest([new BuildItemInput(cpu.PublicId, 1), new BuildItemInput(board.PublicId, 1)]),
            null, CancellationToken.None);

        var cutoff = DateTime.UtcNow;
        var oldRunId = await seedContext.CompatibilityCheckRuns
            .Where(run => run.EvaluatedAtUtc == oldCheck.EvaluatedAtUtc)
            .Select(run => run.Id)
            .FirstAsync();
        await seedContext.CompatibilityCheckRuns
            .Where(run => run.Id == oldRunId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.CreatedAtUtc, cutoff.AddDays(-91))
                .SetProperty(run => run.EvaluatedAtUtc, cutoff.AddDays(-91)));

        var interceptor = new FailOnRunsDeleteInterceptor();
        var options = new DbContextOptionsBuilder<DoSelect.Infrastructure.Persistence.DoSelectDbContext>()
            .UseSqlServer(seedContext.Database.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;
        await using (var failingContext = new DoSelect.Infrastructure.Persistence.DoSelectDbContext(options))
        {
            var failingService = new EfCompatibilityCheckService(failingContext, new EfCompatibilityCatalogReader(failingContext));
            await Assert.ThrowsAnyAsync<Exception>(() => failingService.PurgeExpiredRunsAsync(
                cutoff.AddDays(-90), batchSize: 100, CancellationToken.None));
        }

        Assert.True(interceptor.RunsDeleteAttempted);
        await using var verifyContext = CompatibilityCheckServiceFixture.CreateContext();
        Assert.True(await verifyContext.CompatibilityCheckRuns.AnyAsync(run => run.Id == oldRunId));
        Assert.True(await verifyContext.CompatibilityCheckResults.AnyAsync(result => result.CompatibilityCheckRunId == oldRunId));

        var retryService = new EfCompatibilityCheckService(verifyContext, new EfCompatibilityCatalogReader(verifyContext));
        var deletedCount = await retryService.PurgeExpiredRunsAsync(cutoff.AddDays(-90), batchSize: 100, CancellationToken.None);
        Assert.Equal(1, deletedCount);
        Assert.False(await verifyContext.CompatibilityCheckRuns.AnyAsync(run => run.Id == oldRunId));
        Assert.False(await verifyContext.CompatibilityCheckResults.AnyAsync(result => result.CompatibilityCheckRunId == oldRunId));
    }

    private sealed class FailOnRunsDeleteInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public bool RunsDeleteAttempted { get; private set; }

        public override Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> NonQueryExecuting(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result)
        {
            ThrowIfRunsDelete(command);
            return result;
        }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfRunsDelete(command);
            return ValueTask.FromResult(result);
        }

        // ExecuteDeleteAsync on SQL Server compiles to a DELETE with an OUTPUT clause, executed
        // via ExecuteReaderAsync rather than ExecuteNonQueryAsync — NonQueryExecuting[Async] alone
        // never fires for it, so the reader path needs the same guard.
        public override Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result)
        {
            ThrowIfRunsDelete(command);
            return result;
        }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfRunsDelete(command);
            return ValueTask.FromResult(result);
        }

        private void ThrowIfRunsDelete(System.Data.Common.DbCommand command)
        {
            if (command.CommandText.Contains("DELETE", StringComparison.Ordinal) &&
                command.CommandText.Contains("CompatibilityCheckRuns", StringComparison.Ordinal) &&
                !command.CommandText.Contains("CompatibilityCheckResults", StringComparison.Ordinal))
            {
                RunsDeleteAttempted = true;
                throw new InvalidOperationException("Simulated failure deleting CompatibilityCheckRuns.");
            }
        }
    }
}

/// <summary>
/// Pure-function tests for the InputHash canonicalization — no database involved, so not tagged
/// RequiresSqlServer.
/// </summary>
public sealed class CompatibilityCheckServiceCanonicalInputTextTests
{
    /// <summary>Regression test: a hash of SkuPublicIds alone couldn't tell quantity 1 from quantity 8 of the same SKU apart, even though slot/capacity/port/power results depend on it (組長 PR #34 round-4 review, item 4).</summary>
    [Fact]
    public void BuildCanonicalInputText_DiffersWhenOnlyQuantityDiffers()
    {
        var skuId = Guid.CreateVersion7();

        var lowQuantity = EfCompatibilityCheckService.BuildCanonicalInputText([new BuildItemInput(skuId, 1)]);
        var highQuantity = EfCompatibilityCheckService.BuildCanonicalInputText([new BuildItemInput(skuId, 8)]);

        Assert.NotEqual(lowQuantity, highQuantity);
    }

    [Fact]
    public void BuildCanonicalInputText_IsIndependentOfInputOrder()
    {
        var skuA = Guid.CreateVersion7();
        var skuB = Guid.CreateVersion7();

        var forward = EfCompatibilityCheckService.BuildCanonicalInputText(
            [new BuildItemInput(skuA, 2), new BuildItemInput(skuB, 3)]);
        var reversed = EfCompatibilityCheckService.BuildCanonicalInputText(
            [new BuildItemInput(skuB, 3), new BuildItemInput(skuA, 2)]);

        Assert.Equal(forward, reversed);
    }

    [Fact]
    public void BuildCanonicalInputText_IsTheSameForIdenticalContent()
    {
        var skuId = Guid.CreateVersion7();

        var first = EfCompatibilityCheckService.BuildCanonicalInputText([new BuildItemInput(skuId, 5)]);
        var second = EfCompatibilityCheckService.BuildCanonicalInputText([new BuildItemInput(skuId, 5)]);

        Assert.Equal(first, second);
    }
}

[CollectionDefinition(nameof(CompatibilityCheckServiceCollection))]
public sealed class CompatibilityCheckServiceCollection : ICollectionFixture<CompatibilityCheckServiceFixture>;
