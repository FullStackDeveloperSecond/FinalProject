using DoSelect.Application.Builds;
using DoSelect.Domain.Builds;
using DoSelect.Infrastructure.Builds;
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

    [Fact]
    public async Task CheckAsync_ReturnsCompatible_ForAMatchingCpuAndMotherboard()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var cpu = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context,
            BuildComponentCategoryCodes.Cpu,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.CpuSocket] = "AM5",
                [CompatibilitySemanticKeys.CpuGeneration] = "Ryzen7000",
                [CompatibilitySemanticKeys.CpuPowerWatts] = 105m,
            });
        var board = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context,
            BuildComponentCategoryCodes.Motherboard,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.BoardSocket] = "AM5",
                [CompatibilitySemanticKeys.BoardChipset] = "X670E",
            });

        var service = new EfCompatibilityCheckService(context, new EfCompatibilityFactsReader(context));
        var result = await service.CheckAsync(
            new CompatibilityCheckRequest(
            [
                new BuildItemInput(cpu.PublicId, 1),
                new BuildItemInput(board.PublicId, 1),
            ]),
            null,
            CancellationToken.None);

        Assert.Equal("compatible", result.Overall);
        Assert.Empty(result.Results);
        Assert.Equal(CompatibilityRuleEngine.RuleSetVersion, result.RuleSetVersion);
    }

    [Fact]
    public async Task CheckAsync_ReturnsBlocked_ForAMismatchedSocket()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var cpu = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context,
            BuildComponentCategoryCodes.Cpu,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.CpuSocket] = "AM5" });
        var board = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context,
            BuildComponentCategoryCodes.Motherboard,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.BoardSocket] = "LGA1700" });

        var service = new EfCompatibilityCheckService(context, new EfCompatibilityFactsReader(context));
        var result = await service.CheckAsync(
            new CompatibilityCheckRequest(
            [
                new BuildItemInput(cpu.PublicId, 1),
                new BuildItemInput(board.PublicId, 1),
            ]),
            null,
            CancellationToken.None);

        // Overall rolls up to blocked even though CHIPSET_CPU_GENERATION also fires as
        // insufficientData (neither Cpu.Generation nor Motherboard.Chipset was seeded here) —
        // Blocked outranks InsufficientData per CompatibilityRuleEngine's severity precedence.
        Assert.Equal("blocked", result.Overall);
        var socketFinding = Assert.Single(result.Results, f => f.RuleCode == BuildCompatibilityRuleCodes.CpuSocket);
        Assert.Equal("blocked", socketFinding.Severity);
    }

    [Fact]
    public async Task CheckAsync_MergesDuplicateSkuEntriesByQuantity()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var board = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context,
            BuildComponentCategoryCodes.Motherboard,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.BoardMemoryGeneration] = "DDR5",
                [CompatibilitySemanticKeys.BoardMemorySlotCount] = 4,
                [CompatibilitySemanticKeys.BoardMaxMemoryCapacityGb] = 128m,
            });
        var memory = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context,
            BuildComponentCategoryCodes.Memory,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.MemoryGeneration] = "DDR5",
                [CompatibilitySemanticKeys.MemoryCapacityGbPerModule] = 16m,
            });

        var service = new EfCompatibilityCheckService(context, new EfCompatibilityFactsReader(context));
        var result = await service.CheckAsync(
            new CompatibilityCheckRequest(
            [
                new BuildItemInput(board.PublicId, 1),
                new BuildItemInput(memory.PublicId, 1),
                new BuildItemInput(memory.PublicId, 1), // same SKU listed twice -> merges to quantity 2
            ]),
            null,
            CancellationToken.None);

        // 2 modules against a 4-slot board with the default 0-slot warning threshold: no finding.
        Assert.Equal("compatible", result.Overall);
    }

    [Fact]
    public async Task CheckAsync_Throws_ForAnUnknownSku()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityFactsReader(context));

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
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityFactsReader(context));
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
            context, BuildComponentCategoryCodes.Memory);
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityFactsReader(context));

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
    /// still rode along into the cart without ever participating in evaluation.
    /// </summary>
    [Fact]
    public async Task CheckAsync_Throws_WhenTwoDistinctCpuSkusAreRequested()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var firstCpu = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Cpu);
        var secondCpu = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Cpu);
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityFactsReader(context));

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.CheckAsync(
            new CompatibilityCheckRequest(
            [
                new BuildItemInput(firstCpu.PublicId, 1),
                new BuildItemInput(secondCpu.PublicId, 1),
            ]),
            null,
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task CheckAsync_Throws_WhenCpuQuantityIsGreaterThanOne()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var cpu = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Cpu);
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityFactsReader(context));

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.CheckAsync(
            new CompatibilityCheckRequest([new BuildItemInput(cpu.PublicId, 2)]),
            null,
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task CheckAsync_Throws_WhenTwoDistinctMotherboardSkusAreRequested()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var firstBoard = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Motherboard);
        var secondBoard = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Motherboard);
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityFactsReader(context));

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.CheckAsync(
            new CompatibilityCheckRequest(
            [
                new BuildItemInput(firstBoard.PublicId, 1),
                new BuildItemInput(secondBoard.PublicId, 1),
            ]),
            null,
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    /// <summary>Memory is a genuine multi-instance role — this must keep working after the singleton-role guard was added.</summary>
    [Fact]
    public async Task CheckAsync_Allows_MultipleMemoryModulesAndQuantityGreaterThanOne()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var firstModule = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Memory);
        var secondModule = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Memory);
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityFactsReader(context));

        var result = await service.CheckAsync(
            new CompatibilityCheckRequest(
            [
                new BuildItemInput(firstModule.PublicId, 2),
                new BuildItemInput(secondModule.PublicId, 2),
            ]),
            null,
            CancellationToken.None);

        Assert.Equal("compatible", result.Overall);
    }

    /// <summary>
    /// Regression test: proves the motherboard's per-interface port counts are read as
    /// independent pools rather than conflated — a board with SATA:2 and NVME:2 accepts 1 of each
    /// at once, leaving headroom on both independently (組長 PR #34 round-4 review, item 1's
    /// "multi-interface grouping" case).
    /// </summary>
    [Fact]
    public async Task CheckAsync_WhenStorageUsageStaysWithinEachInterfaceIndependently_ReturnsCompatible()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var board = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Motherboard,
            storagePorts: new Dictionary<string, int> { ["SATA"] = 2, ["NVME"] = 2 });
        var sataDrive = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.StorageDevice,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.StorageInterface] = "SATA" });
        var nvmeDrive = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.StorageDevice,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.StorageInterface] = "NVME" });
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityFactsReader(context));

        var result = await service.CheckAsync(
            new CompatibilityCheckRequest(
            [
                new BuildItemInput(board.PublicId, 1),
                new BuildItemInput(sataDrive.PublicId, 1),
                new BuildItemInput(nvmeDrive.PublicId, 1),
            ]),
            null,
            CancellationToken.None);

        Assert.Equal("compatible", result.Overall);
    }

    /// <summary>
    /// Regression test: SATA over capacity must block even though NVME (a different interface on
    /// the same board) still has headroom — proves usage is tallied per interface, not against a
    /// combined port total.
    /// </summary>
    [Fact]
    public async Task CheckAsync_WhenOneInterfaceExceedsItsOwnPortCount_ReturnsBlockedForThatInterfaceOnly()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var board = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Motherboard,
            storagePorts: new Dictionary<string, int> { ["SATA"] = 2, ["NVME"] = 2 });
        var sataDrive = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.StorageDevice,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.StorageInterface] = "SATA" });
        var nvmeDrive = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.StorageDevice,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.StorageInterface] = "NVME" });
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityFactsReader(context));

        var result = await service.CheckAsync(
            new CompatibilityCheckRequest(
            [
                new BuildItemInput(board.PublicId, 1),
                new BuildItemInput(sataDrive.PublicId, 3),
                new BuildItemInput(nvmeDrive.PublicId, 1),
            ]),
            null,
            CancellationToken.None);

        Assert.Equal("blocked", result.Overall);
        var finding = Assert.Single(result.Results, r => r.RuleCode == BuildCompatibilityRuleCodes.StorageInterface);
        Assert.Equal("compatibility.storage_interface_port_exceeded", finding.MessageKey);
        var factsJson = System.Text.Json.JsonSerializer.Serialize(finding.Facts);
        Assert.Contains("SATA", factsJson);
        Assert.DoesNotContain("NVME", factsJson);
    }

    /// <summary>
    /// Regression test: the old "{interface}:{portCount}" packed string could hold two rows for
    /// the same interface with different counts (its unique index was on the whole value
    /// string). SkuStorageInterfacePort's unique index on (SkuId, InterfaceCode) makes that
    /// unrepresentable at the schema level (組長 PR #34 round-4 review, item 1).
    /// </summary>
    [Fact]
    public async Task SeedingTwoPortCountsForTheSameSkuAndInterface_ViolatesTheUniqueConstraint()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var board = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Motherboard,
            storagePorts: new Dictionary<string, int> { ["SATA"] = 4 });

        context.SkuStorageInterfacePorts.Add(new SkuStorageInterfacePort(board.Id, "SATA", 6, DateTime.UtcNow));

        await Assert.ThrowsAnyAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
            () => context.SaveChangesAsync());
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
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityFactsReader(context));

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
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);
        var service = new EfCompatibilityCheckService(context, new EfCompatibilityFactsReader(context));

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
            seedContext, BuildComponentCategoryCodes.Cpu,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.CpuSocket] = "AM5" });
        var board = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            seedContext, BuildComponentCategoryCodes.Motherboard,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.BoardSocket] = "AM5" });
        var seedingService = new EfCompatibilityCheckService(seedContext, new EfCompatibilityFactsReader(seedContext));
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
            var failingService = new EfCompatibilityCheckService(failingContext, new EfCompatibilityFactsReader(failingContext));
            await Assert.ThrowsAnyAsync<Exception>(() => failingService.PurgeExpiredRunsAsync(
                cutoff.AddDays(-90), batchSize: 100, CancellationToken.None));
        }

        Assert.True(interceptor.RunsDeleteAttempted);
        await using var verifyContext = CompatibilityCheckServiceFixture.CreateContext();
        Assert.True(await verifyContext.CompatibilityCheckRuns.AnyAsync(run => run.Id == oldRunId));
        Assert.True(await verifyContext.CompatibilityCheckResults.AnyAsync(result => result.CompatibilityCheckRunId == oldRunId));

        var retryService = new EfCompatibilityCheckService(verifyContext, new EfCompatibilityFactsReader(verifyContext));
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
