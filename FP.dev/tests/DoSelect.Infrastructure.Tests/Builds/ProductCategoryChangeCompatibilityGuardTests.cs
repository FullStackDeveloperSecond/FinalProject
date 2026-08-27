using System.Data.Common;
using DoSelect.Application.Auditing;
using DoSelect.Application.Builds;
using DoSelect.Application.Catalog;
using DoSelect.Domain.Builds;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Builds;
using DoSelect.Infrastructure.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DoSelect.Infrastructure.Tests.Builds;

/// <summary>
/// 組長 PR #34 round-5 review, item 2: a Product's category change must not silently orphan
/// per-SKU compatibility facts that only made sense under the old category, and a concurrent
/// category change racing a SKU attributes write must not let both succeed against inconsistent
/// state.
/// </summary>
[Collection(nameof(CompatibilityCheckServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ProductCategoryChangeCompatibilityGuardTests
{
    private static readonly AuditRequestContext AuditContext = CompatibilityCheckServiceFixture.TestAuditContext;

    /// <summary>
    /// PR #34 round-6 review: this test's name claims it covers `SkuCompatibilityAttributes`, but
    /// it previously wrote an empty `attributes` dictionary and a `SATA` storage port instead —
    /// duplicating <see cref="UpdateAsync_WhenChangingCategoryAndSkuHasStorageInterfacePorts_ThrowsValidationFailed"/>
    /// and never exercising the `SkuCompatibilityAttributes` guard path at all. Now seeds a
    /// Cooler-category SKU with a real `CoolerSupportedSockets` attribute and no storage ports, so
    /// each of the two `EfProductAdminService.UpdateAsync` guard clauses (attributes / storage
    /// ports) has its own independent test.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_WhenChangingCategoryAndSkuHasCompatibilityAttributes_ThrowsValidationFailed()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var cooler = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Cooler);
        var product = await context.Products.AsNoTracking().FirstAsync(p => p.Id == cooler.ProductId);
        var adminUserId = await CompatibilityCheckServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);

        var attributeService = new EfSkuCompatibilityAttributeAdminService(context, new EfAuditWriter(context, TimeProvider.System));
        var before = await attributeService.GetAsync(cooler.PublicId, CancellationToken.None);
        await attributeService.SetAsync(
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

        var targetCategory = await context.Categories.AsNoTracking()
            .FirstAsync(c => c.Code == BuildComponentCategoryCodes.Case);
        var brand = await context.Brands.AsNoTracking().FirstAsync(b => b.Id == product.BrandId);

        var productService = new EfProductAdminService(context);
        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => productService.UpdateAsync(
            product.PublicId,
            new UpdateProductRequest(
                "測試商品", brand.PublicId, targetCategory.PublicId, null, null, [], "Draft", product.RowVersion),
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenChangingCategoryAndSkuHasStorageInterfacePorts_ThrowsValidationFailed()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var motherboard = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Motherboard);
        var product = await context.Products.AsNoTracking().FirstAsync(p => p.Id == motherboard.ProductId);
        var adminUserId = await CompatibilityCheckServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.CatalogManager);

        var attributeService = new EfSkuCompatibilityAttributeAdminService(context, new EfAuditWriter(context, TimeProvider.System));
        var before = await attributeService.GetAsync(motherboard.PublicId, CancellationToken.None);
        await attributeService.SetAsync(
            motherboard.PublicId,
            adminUserId,
            new SetSkuCompatibilityAttributesRequest(
                new Dictionary<string, IReadOnlyList<string>>(),
                new Dictionary<string, int> { ["SATA"] = 4 },
                before.RowVersion),
            AuditContext,
            CancellationToken.None);

        var targetCategory = await context.Categories.AsNoTracking()
            .FirstAsync(c => c.Code == BuildComponentCategoryCodes.Case);
        var brand = await context.Brands.AsNoTracking().FirstAsync(b => b.Id == product.BrandId);

        var productService = new EfProductAdminService(context);
        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => productService.UpdateAsync(
            product.PublicId,
            new UpdateProductRequest(
                "測試商品", brand.PublicId, targetCategory.PublicId, null, null, [], "Draft", product.RowVersion),
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    /// <summary>
    /// A Product category change (Catalog) and a SKU compatibility attributes PUT (Builds)
    /// racing the same Product/SKU pair must not both succeed: EfSkuCompatibilityAttributeAdminService
    /// now loads Product tracked and Touches it in the same SaveChanges as the attribute write, so
    /// it shares Product.RowVersion as a concurrency boundary with EfProductAdminService.UpdateAsync
    /// without either service needing to know about the other's request shape.
    ///
    /// PR #34 round-6 review: neither side has any pre-existing compatibility data to race over in
    /// this scenario (both start from a freshly-seeded SKU), so without a write-level barrier one
    /// side could run its entire guard-read-through-SaveChanges sequence before the other even
    /// starts, which would prove nothing about the concurrency boundary. <see cref="TwoPartyWriteBarrier"/>
    /// forces both sides to complete every one of their own reads/guards first, and only then lets
    /// either side's first actual write reach SQL Server — so the loser can only ever be caught by
    /// the database's own RowVersion check (a stable, deterministic `concurrency_conflict`), never
    /// by a guard query that happened to observe the winner's not-yet-issued write.
    /// </summary>
    [Fact]
    public async Task ConcurrentCategoryChangeAndAttributesWrite_OnlyOneSucceeds_AndFinalStateMatchesTheWinner()
    {
        await using var seedContext = CompatibilityCheckServiceFixture.CreateContext();
        var motherboard = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            seedContext, BuildComponentCategoryCodes.Motherboard);
        var product = await seedContext.Products.AsNoTracking().FirstAsync(p => p.Id == motherboard.ProductId);
        var brand = await seedContext.Brands.AsNoTracking().FirstAsync(b => b.Id == product.BrandId);
        var caseCategory = await seedContext.Categories.AsNoTracking()
            .FirstAsync(c => c.Code == BuildComponentCategoryCodes.Case);
        var adminUserId = await CompatibilityCheckServiceFixture.SeedAdminUserIdAsync(seedContext, AuditRoleNames.CatalogManager);

        var attributeSeeder = new EfSkuCompatibilityAttributeAdminService(seedContext, new EfAuditWriter(seedContext, TimeProvider.System));
        var startingRowVersion = (await attributeSeeder.GetAsync(motherboard.PublicId, CancellationToken.None)).RowVersion;

        var writeBarrier = new TwoPartyWriteBarrier();
        await using var contextA = CompatibilityCheckServiceFixture.CreateContext(writeBarrier);
        await using var contextB = CompatibilityCheckServiceFixture.CreateContext(writeBarrier);
        var attributeService = new EfSkuCompatibilityAttributeAdminService(contextA, new EfAuditWriter(contextA, TimeProvider.System));
        var productService = new EfProductAdminService(contextB);

        var attributesTask = RunOrCaptureConflictAsync(() => attributeService.SetAsync(
            motherboard.PublicId,
            adminUserId,
            new SetSkuCompatibilityAttributesRequest(
                new Dictionary<string, IReadOnlyList<string>>(), new Dictionary<string, int> { ["SATA"] = 4 },
                startingRowVersion),
            AuditContext,
            CancellationToken.None));
        var categoryChangeTask = RunOrCaptureConflictAsync(() => productService.UpdateAsync(
            product.PublicId,
            new UpdateProductRequest(
                "測試商品", brand.PublicId, caseCategory.PublicId, null, null, [], "Draft", product.RowVersion),
            CancellationToken.None));

        var results = await Task.WhenAll(attributesTask, categoryChangeTask);

        Assert.Equal(2, writeBarrier.Arrivals);
        Assert.Single(results, succeeded => succeeded);
        Assert.Single(results, succeeded => !succeeded);

        var attributesWon = results[0];
        await using var verifyContext = CompatibilityCheckServiceFixture.CreateContext();
        var finalProduct = await verifyContext.Products.AsNoTracking().FirstAsync(p => p.Id == motherboard.ProductId);
        var finalCategory = await verifyContext.Categories.AsNoTracking().FirstAsync(c => c.Id == finalProduct.CategoryId);

        if (attributesWon)
        {
            // The category change lost: the Product must still be in its original category, and
            // the attributes write's data (which the category-change guard would have rejected
            // had it gone through the other order) is intact.
            Assert.Equal(BuildComponentCategoryCodes.Motherboard, finalCategory.Code);
            var finalAttributes = await verifyContext.SkuStorageInterfacePorts.AsNoTracking()
                .Where(port => port.SkuId == motherboard.Id)
                .ToListAsync();
            var port = Assert.Single(finalAttributes);
            Assert.Equal("SATA", port.InterfaceCode);
        }
        else
        {
            // The category change won: the Product moved to Case, and no orphaned Motherboard
            // storage-port row was left behind by the losing attributes write.
            Assert.Equal(BuildComponentCategoryCodes.Case, finalCategory.Code);
            var finalAttributes = await verifyContext.SkuStorageInterfacePorts.AsNoTracking()
                .Where(port => port.SkuId == motherboard.Id)
                .ToListAsync();
            Assert.Empty(finalAttributes);
        }
    }

    /// <summary>
    /// PR #34 round-6 review: forced down to exactly `concurrency_conflict` for the loser — the
    /// write barrier (see the test above) rules out the loser instead seeing the winner's
    /// already-committed state through a guard query, so a `validation_failed` here would indicate
    /// the barrier failed to engage, not a legitimately different-but-still-safe outcome.
    /// </summary>
    private static async Task<bool> RunOrCaptureConflictAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (BuildWriteException exception) when (
            exception.ErrorCode is BuildWriteException.ErrorCodes.ConcurrencyConflict)
        {
            return false;
        }
        catch (CatalogWriteException exception) when (
            exception.ErrorCode is CatalogWriteException.ErrorCodes.ConcurrencyConflict)
        {
            return false;
        }
    }

    /// <summary>
    /// Forces both sides of a two-party race to complete every one of their own reads before
    /// either side's first actual write (INSERT/UPDATE/DELETE/MERGE) command reaches SQL Server —
    /// mirrors <c>CatalogAdminServiceTests.TwoPartyExistsCheckBarrier</c>'s rendezvous shape, but
    /// keyed on "this is a write command" rather than one specific read fragment, since the two
    /// racing operations here run different queries rather than the same duplicate-check shape.
    /// EF Core's SQL Server provider may send an UPDATE/INSERT as a reader-, scalar-, or
    /// non-query-returning command depending on whether it needs an OUTPUT clause back (e.g. a
    /// rowversion column), so all three *Executing overrides are hooked. Subsequent write commands
    /// from either side after the two-party rendezvous has already happened pass straight through
    /// (arrivals beyond the first two are a no-op), which is correct — a single SaveChangesAsync
    /// can batch more than one DML statement.
    /// </summary>
    private sealed class TwoPartyWriteBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _firstArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public int Arrivals => _arrivals;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await WaitForBothAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            await WaitForBothAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await WaitForBothAsync(command, cancellationToken);
            return result;
        }

        private async Task WaitForBothAsync(DbCommand command, CancellationToken cancellationToken)
        {
            var text = command.CommandText;
            var isWrite =
                text.Contains("INSERT ", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("DELETE ", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("MERGE ", StringComparison.OrdinalIgnoreCase);
            if (!isWrite)
            {
                return;
            }

            if (Interlocked.Increment(ref _arrivals) == 1)
            {
                await _firstArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            else
            {
                _firstArrived.TrySetResult();
            }
        }
    }
}
