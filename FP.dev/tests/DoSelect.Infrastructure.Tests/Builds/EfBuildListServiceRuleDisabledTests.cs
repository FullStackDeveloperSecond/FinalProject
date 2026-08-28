using DoSelect.Application.Auditing;
using DoSelect.Application.Builds;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Builds;
using DoSelect.Infrastructure.Catalog;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Builds;

// This one scenario needs its own throwaway database rather than sharing
// CompatibilityCheckServiceCollection's: CompatibilityRuleSettings (which rule-activation writes
// to) is a global table, not scoped per-SKU, so disabling CPU_SOCKET here would otherwise leak
// into every other test sharing that collection's one database regardless of run order.
[Trait("Category", "RequiresSqlServer")]
public sealed class EfBuildListServiceRuleDisabledTests : IAsyncLifetime
{
    // 組長 PR #34 review round 3: was hardcoded to the local ".\SQL2025" instance — CI's SQL
    // Server runs in a container reachable only via DOSELECT_SQLSERVER_TEST_CONNECTION.
    private readonly string _connectionString =
        SqlServerTestConnection.Build($"DoSelectBuildListRuleDisabledTests_{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        await CompatibilityCheckServiceFixture.SeedCategoriesAndSpecTemplatesAsync(context);
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

    /// <summary>
    /// PR #34 round-3 review: CompatibilityRuleEngine deliberately never lets a ruleDisabled
    /// finding influence Overall (an admin test tool legitimately wants to see "would have failed
    /// if the rule were active"), but the real purchase flow may never let a hard rule an admin
    /// disabled silently read as purchasable — a disabled CPU_SOCKET rule must not let a genuinely
    /// socket-mismatched build reach the cart.
    /// </summary>
    [Fact]
    public async Task AddToCartAsync_Throws_WhenTheOnlyBlockingFindingComesFromADisabledRule()
    {
        await using var context = CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var adminUserId = await CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync(context, AuditRoleNames.SuperAdmin);
        var components = await EfBuildListServiceTests.SeedCompleteBuildComponentsAsync(context);

        // A second motherboard, identical to the complete baseline's except the socket, so only
        // CPU_SOCKET fires — every other rule still sees fully-specified, matching facts.
        var mismatchedMotherboard = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, CompatibilityCatalogContract.Categories.Motherboard,
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
        await EfBuildListServiceTests.SeedInventoryAsync(context, mismatchedMotherboard.Id, 100);

        var catalogReader = new EfCompatibilityCatalogReader(context);
        var ruleAdminService = new EfCompatibilityRuleAdminService(
            context, new EfCompatibilityCheckService(context, catalogReader), catalogReader,
            new EfAuditWriter(context, TimeProvider.System));
        var beforeList = await ruleAdminService.ListAsync(CancellationToken.None);
        var activationRowVersion = beforeList.Rules
            .Single(rule => rule.RuleCode == CompatibilityRuleCodes.CpuSocket).ActivationRowVersion;
        await ruleAdminService.SetActivationAsync(
            CompatibilityRuleCodes.CpuSocket, adminUserId,
            new SetRuleActivationRequest(false, "test", activationRowVersion),
            CompatibilityRuleAdminServiceFixture.TestAuditContext, CancellationToken.None);

        var service = EfBuildListServiceTests.CreateService(context);
        var items = EfBuildListServiceTests.ToBuildItems(components);
        var motherboardIndex = items.FindIndex(item => item.SkuPublicId == components.Motherboard.PublicId);
        items[motherboardIndex] = new BuildItemInput(mismatchedMotherboard.PublicId, 1);
        var created = await service.CreateAsync(memberUserId, new CreateBuildListRequest("Disabled Rule", items), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.AddToCartAsync(
            memberUserId, created.PublicId, new AddBuildToCartRequest(1, created.RowVersion), "disabled-rule-key", CancellationToken.None));
        Assert.Equal(BuildWriteException.ErrorCodes.BuildIncompatible, exception.ErrorCode);
    }
}
