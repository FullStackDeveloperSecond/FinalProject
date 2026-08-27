using DoSelect.Application.Builds;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Builds;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Shopping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Builds;

[Collection(nameof(CompatibilityCheckServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfBuildListServiceTests
{
    private readonly CompatibilityCheckServiceFixture _fixture;

    public EfBuildListServiceTests(CompatibilityCheckServiceFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// PR #34 round-3 review: Count-then-Insert for the 50-active-list quota let two concurrent
    /// creates by the same member both read 49 and both pass, landing at 51. The invariant that
    /// actually matters — never exceeding 50 — is what this test proves; which of the two
    /// concurrent requests "wins" is not something the test can control (both a lock-timeout
    /// ConcurrencyConflict and a post-lock re-check ValidationFailed are correct outcomes for the
    /// loser, depending on exact timing).
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenTwoConcurrentCreatesWouldBothExceedTheQuota_NeverExceedsIt()
    {
        await using var seedContext = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(seedContext);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(seedContext, BuildComponentCategoryCodes.StorageDevice);

        // Directly insert 49 (not via CreateAsync — avoids 49x redundant compatibility checks).
        var now = DateTime.UtcNow;
        for (var i = 0; i < 49; i++)
        {
            seedContext.BuildLists.Add(new BuildList(
                Guid.CreateVersion7(), memberUserId, $"Existing {i}", BuildListStatusCodes.Active, now));
        }

        await seedContext.SaveChangesAsync();

        await using var contextA = CompatibilityCheckServiceFixture.CreateContext();
        await using var contextB = CompatibilityCheckServiceFixture.CreateContext();
        var serviceA = CreateService(contextA);
        var serviceB = CreateService(contextB);
        var request = new CreateBuildListRequest("Racer", [new BuildItemInput(sku.PublicId, 1)]);

        var results = await Task.WhenAll(
            RunOrCaptureFailureAsync(() => serviceA.CreateAsync(memberUserId, request, CancellationToken.None)),
            RunOrCaptureFailureAsync(() => serviceB.CreateAsync(memberUserId, request, CancellationToken.None)));

        Assert.Single(results, succeeded => succeeded);
        Assert.Single(results, succeeded => !succeeded);

        var finalCount = await seedContext.BuildLists.CountAsync(
            list => list.OwnerUserId == memberUserId && list.Status == BuildListStatusCodes.Active);
        Assert.Equal(50, finalCount);
    }

    private static async Task<bool> RunOrCaptureFailureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (BuildWriteException exception) when (
            exception.ErrorCode is BuildWriteException.ErrorCodes.ConcurrencyConflict
                or BuildWriteException.ErrorCodes.ValidationFailed)
        {
            return false;
        }
    }

    /// <summary>
    /// PR #34 round-3 review: revoke-then-insert for a new share link let two concurrent
    /// regenerate requests both see "no active token yet" and both insert one, leaving two
    /// simultaneously active shares for the same build list.
    /// </summary>
    [Fact]
    public async Task CreateShareAsync_WhenTwoConcurrentRequestsRace_ExactlyOneActiveShareSurvives()
    {
        await using var seedContext = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(seedContext);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(seedContext, BuildComponentCategoryCodes.StorageDevice);
        var seedService = CreateService(seedContext);
        var created = await seedService.CreateAsync(
            memberUserId, new CreateBuildListRequest("Shared", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);

        await using var contextA = CompatibilityCheckServiceFixture.CreateContext();
        await using var contextB = CompatibilityCheckServiceFixture.CreateContext();
        var serviceA = CreateService(contextA);
        var serviceB = CreateService(contextB);

        var results = await Task.WhenAll(
            RunOrCaptureFailureAsync(() => serviceA.CreateShareAsync(memberUserId, created.PublicId, CancellationToken.None)),
            RunOrCaptureFailureAsync(() => serviceB.CreateShareAsync(memberUserId, created.PublicId, CancellationToken.None)));

        Assert.Contains(results, succeeded => succeeded);

        var buildListId = await seedContext.BuildLists
            .Where(list => list.PublicId == created.PublicId)
            .Select(list => list.Id)
            .SingleAsync();
        var activeShareCount = await seedContext.BuildShareTokens
            .CountAsync(token => token.BuildListId == buildListId && token.RevokedAtUtc == null);
        Assert.Equal(1, activeShareCount);
    }

    [Fact]
    public async Task CreateAsync_CreatesABuildList_WithComputedTotalsAndCompatibility()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        // Two Storage SKUs with no Motherboard present: EvaluateStorageInterface (like every
        // other rule) short-circuits to "no finding" when its counterpart slot is empty, so this
        // is guaranteed "compatible" with zero findings — unlike a Cpu+Motherboard pair, which
        // also fires CHIPSET_CPU_GENERATION as insufficientData unless Generation/Chipset are
        // both seeded too (see CompatibilityCheckServiceTests's same caveat).
        var first = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);
        var second = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);

        var service = CreateService(context);
        var result = await service.CreateAsync(
            memberUserId,
            new CreateBuildListRequest("我的組裝", [new BuildItemInput(first.PublicId, 1), new BuildItemInput(second.PublicId, 2)]),
            CancellationToken.None);

        Assert.Equal("我的組裝", result.Name);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("compatible", result.Compatibility.Overall);
        Assert.Equal(1000m * 1 + 1000m * 2, result.Totals.Merchandise);
        Assert.Equal(result.Totals.Merchandise + 300m, result.Totals.GrandTotal);
        Assert.Equal("TWD", result.Totals.Currency);
        Assert.NotEmpty(result.RowVersion);
    }

    /// <summary>
    /// Regression test: composing the response DTO after commit used to call CheckAsync again,
    /// which persists its own CompatibilityCheckRun/Result as a side effect — so CreateAsync used
    /// to leave two Run rows behind for one request (one from the pre-commit snapshot, one from
    /// building the response), instead of reusing the already-computed result (組長 PR #34
    /// round-4 review, item 2).
    /// </summary>
    [Fact]
    public async Task CreateAsync_PersistsExactlyOneCompatibilityCheckRun_NotASecondOneWhileComposingTheResponse()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);
        var service = CreateService(context);

        var created = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("我的組裝", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);

        var buildListId = await context.BuildLists.Where(list => list.PublicId == created.PublicId)
            .Select(list => list.Id).SingleAsync();
        var runCount = await context.CompatibilityCheckRuns.CountAsync(run => run.BuildListId == buildListId);
        Assert.Equal(1, runCount);
    }

    [Fact]
    public async Task CreateAsync_MergesDuplicateSkuEntries_AndSumsQuantity()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var storage = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.StorageDevice);

        var service = CreateService(context);
        var result = await service.CreateAsync(
            memberUserId,
            new CreateBuildListRequest("Dup", [new BuildItemInput(storage.PublicId, 2), new BuildItemInput(storage.PublicId, 3)]),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(5, item.Quantity);
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenAnItemSkuIsUnknown()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var service = CreateService(context);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.CreateAsync(
            memberUserId,
            new CreateBuildListRequest("Bad", [new BuildItemInput(Guid.NewGuid(), 1)]),
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenMoreThanTwentyItemsAreRequested()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var skus = new List<Guid>();
        for (var i = 0; i < 21; i++)
        {
            var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);
            skus.Add(sku.PublicId);
        }

        var service = CreateService(context);
        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.CreateAsync(
            memberUserId,
            new CreateBuildListRequest("TooMany", skus.Select(id => new BuildItemInput(id, 1)).ToList()),
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenMemberAlreadyHasFiftyActiveBuildLists()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);

        var service = CreateService(context);
        for (var i = 0; i < 50; i++)
        {
            await service.CreateAsync(
                memberUserId,
                new CreateBuildListRequest($"List {i}", [new BuildItemInput(sku.PublicId, 1)]),
                CancellationToken.None);
        }

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.CreateAsync(
            memberUserId,
            new CreateBuildListRequest("One too many", [new BuildItemInput(sku.PublicId, 1)]),
            CancellationToken.None));

        Assert.Equal(BuildWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task GetAsync_Throws_ResourceNotFound_ForAnotherMembersBuildList()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var ownerId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var otherMemberId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);

        var service = CreateService(context);
        var created = await service.CreateAsync(
            ownerId, new CreateBuildListRequest("Mine", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(
            () => service.GetAsync(otherMemberId, created.PublicId, CancellationToken.None));
        Assert.Equal(BuildWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_RenamesAndReplacesItems()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var first = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);
        var second = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);

        var service = CreateService(context);
        var created = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("Before", [new BuildItemInput(first.PublicId, 1)]), CancellationToken.None);

        var updated = await service.UpdateAsync(
            memberUserId,
            created.PublicId,
            new UpdateBuildListRequest("After", [new BuildItemInput(second.PublicId, 4)], created.RowVersion),
            CancellationToken.None);

        Assert.Equal("After", updated.Name);
        var item = Assert.Single(updated.Items);
        Assert.Equal(second.PublicId, item.SkuPublicId);
        Assert.Equal(4, item.Quantity);
    }

    [Fact]
    public async Task UpdateAsync_Throws_ConcurrencyConflict_ForAStaleRowVersion()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);

        var service = CreateService(context);
        var created = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("Original", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);

        var staleRowVersion = (byte[])created.RowVersion.Clone();
        await service.UpdateAsync(
            memberUserId, created.PublicId,
            new UpdateBuildListRequest("First edit", [new BuildItemInput(sku.PublicId, 2)], created.RowVersion),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.UpdateAsync(
            memberUserId, created.PublicId,
            new UpdateBuildListRequest("Second edit", [new BuildItemInput(sku.PublicId, 3)], staleRowVersion),
            CancellationToken.None));
        Assert.Equal(BuildWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesTheBuildList_SoItNoLongerAppearsInListOrGet()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);

        var service = CreateService(context);
        var created = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("ToDelete", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);

        await service.DeleteAsync(memberUserId, created.PublicId, created.RowVersion, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(
            () => service.GetAsync(memberUserId, created.PublicId, CancellationToken.None));
        Assert.Equal(BuildWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);

        var page = await service.ListAsync(memberUserId, new BuildListListQuery(), CancellationToken.None);
        Assert.DoesNotContain(page.Items, item => item.PublicId == created.PublicId);
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyTheOwnersActiveLists_OrderedByMostRecentlyUpdated()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var otherMemberId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);

        var service = CreateService(context);
        var first = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("First", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);
        var second = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("Second", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);
        await service.CreateAsync(
            otherMemberId, new CreateBuildListRequest("NotMine", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);

        var page = await service.ListAsync(memberUserId, new BuildListListQuery(), CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal([second.PublicId, first.PublicId], page.Items.Select(item => item.PublicId));
    }

    /// <summary>
    /// Regression test: sorting by UpdatedAtUtc alone can tie (datetime2(3) resolution, or a bulk
    /// re-check touching several lists at once), so without a tiebreaker, paging through
    /// same-timestamp rows can duplicate or skip entries across pages (組長 PR #34 round-4
    /// review, item 6).
    /// </summary>
    [Fact]
    public async Task ListAsync_WhenMultipleListsShareTheSameUpdatedAtUtc_PagesWithoutDuplicatesOrGaps()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);
        var service = CreateService(context);

        var first = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("A", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);
        var second = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("B", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);
        var third = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("C", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);

        // Force an identical UpdatedAtUtc across all three so the primary sort key alone can't
        // order them — only the Id tiebreaker can produce a stable, gap-free page order.
        var tiedTimestamp = DateTime.UtcNow;
        await context.BuildLists
            .Where(list => list.OwnerUserId == memberUserId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(list => list.UpdatedAtUtc, tiedTimestamp));

        var firstPage = await service.ListAsync(
            memberUserId, new BuildListListQuery(PageNumber: 1, PageSize: 2), CancellationToken.None);
        var secondPage = await service.ListAsync(
            memberUserId, new BuildListListQuery(PageNumber: 2, PageSize: 2), CancellationToken.None);

        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage.Items);
        var allIds = firstPage.Items.Concat(secondPage.Items).Select(item => item.PublicId).ToList();
        Assert.Equal(3, allIds.Distinct().Count());
        Assert.Equal(
            new[] { first.PublicId, second.PublicId, third.PublicId }.OrderBy(id => id),
            allIds.OrderBy(id => id));
    }

    [Fact]
    public async Task CreateShareAsync_ThenGetSharedBuildAsync_ReturnsTheDeidentifiedView()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);

        var service = CreateService(context);
        var created = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("Shared List", [new BuildItemInput(sku.PublicId, 2)]), CancellationToken.None);

        var share = await service.CreateShareAsync(memberUserId, created.PublicId, CancellationToken.None);
        var token = share.Url.Split('/').Last();

        var shared = await service.GetSharedBuildAsync(token, CancellationToken.None);

        Assert.Equal("Shared List", shared.Name);
        Assert.Single(shared.Items);
        Assert.True(shared.CanCopy);
        Assert.Equal("compatible", shared.Compatibility.Overall);
    }

    [Fact]
    public async Task CreateShareAsync_CalledTwice_RevokesThePreviousToken()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);

        var service = CreateService(context);
        var created = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("List", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);

        var firstShare = await service.CreateShareAsync(memberUserId, created.PublicId, CancellationToken.None);
        var firstToken = firstShare.Url.Split('/').Last();
        await service.CreateShareAsync(memberUserId, created.PublicId, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(
            () => service.GetSharedBuildAsync(firstToken, CancellationToken.None));
        Assert.Equal(BuildWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task RevokeShareAsync_InvalidatesTheToken()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);

        var service = CreateService(context);
        var created = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("List", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);
        var share = await service.CreateShareAsync(memberUserId, created.PublicId, CancellationToken.None);
        var token = share.Url.Split('/').Last();

        await service.RevokeShareAsync(memberUserId, created.PublicId, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(
            () => service.GetSharedBuildAsync(token, CancellationToken.None));
        Assert.Equal(BuildWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_RevokesAnyLiveShareToken()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);

        var service = CreateService(context);
        var created = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("List", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);
        var share = await service.CreateShareAsync(memberUserId, created.PublicId, CancellationToken.None);
        var token = share.Url.Split('/').Last();

        await service.DeleteAsync(memberUserId, created.PublicId, created.RowVersion, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(
            () => service.GetSharedBuildAsync(token, CancellationToken.None));
        Assert.Equal(BuildWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task GetSharedBuildAsync_Throws_ForAnUnknownToken()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var service = CreateService(context);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(
            () => service.GetSharedBuildAsync("not-a-real-token", CancellationToken.None));
        Assert.Equal(BuildWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);
    }

    /// <summary>PR #34 review: a suspended owner's share link must invalidate immediately, not just a deleted list.</summary>
    [Fact]
    public async Task GetSharedBuildAsync_Throws_WhenTheOwnerAccountIsSuspended()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);

        var service = CreateService(context);
        var created = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("List", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);
        var share = await service.CreateShareAsync(memberUserId, created.PublicId, CancellationToken.None);
        var token = share.Url.Split('/').Last();

        var owner = await context.Users.SingleAsync(user => user.Id == memberUserId);
        owner.Suspend(DateTime.UtcNow);
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<BuildWriteException>(
            () => service.GetSharedBuildAsync(token, CancellationToken.None));
        Assert.Equal(BuildWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);
    }

    /// <summary>PR #34 review: two concurrent opens of the same share link racing on BuildList's RowVersion cache write must not turn a read into a 500.</summary>
    [Fact]
    public async Task GetSharedBuildAsync_WhenCalledConcurrently_NeitherThrows()
    {
        await using var seedContext = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(seedContext);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(seedContext, BuildComponentCategoryCodes.StorageDevice);
        var seedService = CreateService(seedContext);
        var created = await seedService.CreateAsync(
            memberUserId, new CreateBuildListRequest("List", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);
        var share = await seedService.CreateShareAsync(memberUserId, created.PublicId, CancellationToken.None);
        var token = share.Url.Split('/').Last();

        await using var contextA = CompatibilityCheckServiceFixture.CreateContext();
        await using var contextB = CompatibilityCheckServiceFixture.CreateContext();
        var serviceA = CreateService(contextA);
        var serviceB = CreateService(contextB);

        var results = await Task.WhenAll(
            serviceA.GetSharedBuildAsync(token, CancellationToken.None),
            serviceB.GetSharedBuildAsync(token, CancellationToken.None));

        Assert.Equal("List", results[0].Name);
        Assert.Equal("List", results[1].Name);
    }

    /// <summary>PR #34 review: a build with only, say, a storage device has zero applicable compatibility rules and was being reported "compatible" — the completeness gate blocks a wildly incomplete build.</summary>
    [Fact]
    public async Task AddToCartAsync_Throws_BuildIncomplete_WhenARequiredCategoryIsMissing()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);
        await SeedInventoryAsync(context, sku.Id, 100);

        var service = CreateService(context);
        var created = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("Storage Only", [new BuildItemInput(sku.PublicId, 1)]), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.AddToCartAsync(
            memberUserId, created.PublicId, new AddBuildToCartRequest(1, created.RowVersion), "incomplete-key", CancellationToken.None));
        Assert.Equal(BuildWriteException.ErrorCodes.BuildIncomplete, exception.ErrorCode);
    }

    /// <summary>
    /// PR #34 review round 2: 組長's V1 ruling is that all 8 categories (not just the 5 with
    /// direct compatibility rules) are required for a purchasable build — GPU/StorageDevice/Cooler
    /// used to be silently optional. This starts from a fully complete, compatible 8-category
    /// build and removes exactly one category at a time, so each category is individually proven
    /// to be enforced (not just "some subset is missing").
    /// </summary>
    [Theory]
    [MemberData(nameof(AllRequiredCategoryCodes))]
    public async Task AddToCartAsync_Throws_BuildIncomplete_WhenExactlyOneRequiredCategoryIsMissing(string missingCategoryCode)
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var components = await SeedCompleteBuildComponentsAsync(context);
        var items = ToCategoryMap(components)
            .Where(pair => pair.Key != missingCategoryCode)
            .Select(pair => new BuildItemInput(pair.Value.PublicId, 1))
            .ToList();

        var service = CreateService(context);
        var created = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest($"Missing {missingCategoryCode}", items), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.AddToCartAsync(
            memberUserId, created.PublicId, new AddBuildToCartRequest(1, created.RowVersion),
            $"incomplete-{missingCategoryCode}", CancellationToken.None));
        Assert.Equal(BuildWriteException.ErrorCodes.BuildIncomplete, exception.ErrorCode);
    }

    public static IEnumerable<object[]> AllRequiredCategoryCodes() =>
        BuildComponentCategoryCodes.All.Select(code => new object[] { code });

    /// <summary>PR #34 review round 2 regression: a build with all 8 categories present must pass the completeness gate and reach the compatibility/inventory checks.</summary>
    [Fact]
    public async Task AddToCartAsync_PassesCompletenessGate_WhenAllEightCategoriesArePresent()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var components = await SeedCompleteBuildComponentsAsync(context);

        var service = CreateService(context);
        var created = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("Complete", ToBuildItems(components)), CancellationToken.None);

        var cart = await service.AddToCartAsync(
            memberUserId, created.PublicId, new AddBuildToCartRequest(1, created.RowVersion), "complete-key", CancellationToken.None);

        Assert.Equal(8, cart.Items.Count);
    }

    /// <summary>PR #34 review: canAddToCart must require every item to be fully "available", not just "not unavailable" — insufficient_stock used to still pass.</summary>
    [Fact]
    public async Task GetSharedBuildAsync_ReportsCanAddToCartFalse_WhenAnItemHasInsufficientStock()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var components = await SeedCompleteBuildComponentsAsync(context);
        // Memory (not Cpu — a singleton role since PR #34 round-3, quantity must be exactly 1) is
        // used here: the seeded balance is 100 but the build list asks for 5 — plenty for the
        // completeness gate, but forces "insufficient_stock" for this one component specifically.
        await context.InventoryBalances
            .Where(balance => balance.SkuId == components.Memory.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(balance => balance.OnHandQuantity, 1));

        var service = CreateService(context);
        var items = ToBuildItems(components);
        var memoryIndex = items.FindIndex(item => item.SkuPublicId == components.Memory.PublicId);
        items[memoryIndex] = new BuildItemInput(components.Memory.PublicId, 5);
        var created = await service.CreateAsync(memberUserId, new CreateBuildListRequest("Short Stock", items), CancellationToken.None);
        var share = await service.CreateShareAsync(memberUserId, created.PublicId, CancellationToken.None);
        var token = share.Url.Split('/').Last();

        var shared = await service.GetSharedBuildAsync(token, CancellationToken.None);

        Assert.Contains(shared.Items, item => item.Availability == "insufficient_stock");
        Assert.False(shared.CanAddToCart);
    }

    /// <summary>PR #34 review: (pageNumber - 1) * pageSize can overflow int for an extreme page number.</summary>
    [Fact]
    public async Task ListAsync_WhenPageNumberIsExtreme_ReturnsAnEmptyPageInsteadOfThrowing()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var service = CreateService(context);

        var page = await service.ListAsync(
            memberUserId, new BuildListListQuery(int.MaxValue, 50), CancellationToken.None);

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task AddToCartAsync_CreatesOneAssemblyGroupPerRequestedUnit()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var components = await SeedCompleteBuildComponentsAsync(context);
        var first = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.StorageDevice,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.StorageInterface] = "NVME", [CompatibilitySemanticKeys.StoragePowerWatts] = 5m });
        var second = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.StorageDevice,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.StorageInterface] = "NVME", [CompatibilitySemanticKeys.StoragePowerWatts] = 5m });
        await SeedInventoryAsync(context, first.Id, 100);
        await SeedInventoryAsync(context, second.Id, 100);

        var service = CreateService(context);
        var created = await service.CreateAsync(
            memberUserId,
            new CreateBuildListRequest(
                "Buildable",
                [.. ToBuildItems(components), new BuildItemInput(first.PublicId, 1), new BuildItemInput(second.PublicId, 2)]),
            CancellationToken.None);

        var cart = await service.AddToCartAsync(
            memberUserId, created.PublicId, new AddBuildToCartRequest(3, created.RowVersion), "key-1", CancellationToken.None);

        // 3 units x 10 distinct SKUs (8 required components + 2 extra storage) = 30 rows, spread across 3 AssemblyGroupKeys.
        Assert.Equal(30, cart.Items.Count);
        Assert.Equal(3, cart.Items.Select(item => item.AssemblyGroupKey).Distinct().Count());
        var firstItemRows = cart.Items.Where(item => item.SkuPublicId == first.PublicId).ToList();
        Assert.Equal(3, firstItemRows.Count);
        Assert.All(firstItemRows, item => Assert.Equal(1, item.Quantity));
        var secondItemRows = cart.Items.Where(item => item.SkuPublicId == second.PublicId).ToList();
        Assert.All(secondItemRows, item => Assert.Equal(2, item.Quantity));
    }

    [Fact]
    public async Task AddToCartAsync_Throws_InventoryInsufficient_WhenStockIsTooLow()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var components = await SeedCompleteBuildComponentsAsync(context);
        var sku = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.StorageDevice,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.StorageInterface] = "NVME", [CompatibilitySemanticKeys.StoragePowerWatts] = 5m });
        await SeedInventoryAsync(context, sku.Id, 5);

        var service = CreateService(context);
        var created = await service.CreateAsync(
            memberUserId,
            new CreateBuildListRequest("Short", [.. ToBuildItems(components), new BuildItemInput(sku.PublicId, 2)]),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.AddToCartAsync(
            memberUserId, created.PublicId, new AddBuildToCartRequest(3, created.RowVersion), "key-2", CancellationToken.None));
        Assert.Equal(BuildWriteException.ErrorCodes.InventoryInsufficient, exception.ErrorCode);
    }

    [Fact]
    public async Task AddToCartAsync_Throws_BuildIncompatible_WhenOverallIsBlocked()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var cpu = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Cpu,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.CpuSocket] = "AM5" });
        var board = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Motherboard,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.BoardSocket] = "LGA1700" });
        await SeedInventoryAsync(context, cpu.Id, 100);
        await SeedInventoryAsync(context, board.Id, 100);
        // Blocked (socket mismatch) outranks InsufficientData in severity, so Memory/PSU/Case/
        // Gpu/Storage/Cooler only need to satisfy the completeness gate here (all 8 categories,
        // since the round-2 review requires all of them) — their own compatibility doesn't affect
        // this test's expected Overall.
        var memory = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.Memory);
        var psu = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.PowerSupply);
        var pcCase = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.Case);
        var gpu = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.GraphicsCard);
        var storage = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.StorageDevice);
        var cooler = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(context, BuildComponentCategoryCodes.Cooler);
        await SeedInventoryAsync(context, memory.Id, 100);
        await SeedInventoryAsync(context, psu.Id, 100);
        await SeedInventoryAsync(context, pcCase.Id, 100);
        await SeedInventoryAsync(context, gpu.Id, 100);
        await SeedInventoryAsync(context, storage.Id, 100);
        await SeedInventoryAsync(context, cooler.Id, 100);

        var service = CreateService(context);
        var created = await service.CreateAsync(
            memberUserId,
            new CreateBuildListRequest(
                "Mismatched",
                [
                    new BuildItemInput(cpu.PublicId, 1), new BuildItemInput(board.PublicId, 1),
                    new BuildItemInput(memory.PublicId, 1), new BuildItemInput(psu.PublicId, 1),
                    new BuildItemInput(pcCase.PublicId, 1), new BuildItemInput(gpu.PublicId, 1),
                    new BuildItemInput(storage.PublicId, 1), new BuildItemInput(cooler.PublicId, 1),
                ]),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BuildWriteException>(() => service.AddToCartAsync(
            memberUserId, created.PublicId, new AddBuildToCartRequest(1, created.RowVersion), "key-3", CancellationToken.None));
        Assert.Equal(BuildWriteException.ErrorCodes.BuildIncompatible, exception.ErrorCode);
    }

    [Fact]
    public async Task AddToCartAsync_ReplaysTheCachedResult_ForARepeatedIdempotencyKey()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var components = await SeedCompleteBuildComponentsAsync(context);

        var service = CreateService(context);
        var created = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("Replay", ToBuildItems(components)), CancellationToken.None);

        var first = await service.AddToCartAsync(
            memberUserId, created.PublicId, new AddBuildToCartRequest(1, created.RowVersion), "same-key", CancellationToken.None);
        var second = await service.AddToCartAsync(
            memberUserId, created.PublicId, new AddBuildToCartRequest(1, created.RowVersion), "same-key", CancellationToken.None);

        Assert.Equal(first.PublicId, second.PublicId);
        // The retry must not have inserted a second AssemblyGroupKey's worth of items.
        Assert.Equal(first.Items.Count, second.Items.Count);
    }

    [Fact]
    public async Task AddToCartAsync_Throws_IdempotencyPayloadConflict_ForADifferentPayloadWithTheSameKey()
    {
        await using var context = CompatibilityCheckServiceFixture.CreateContext();
        var memberUserId = await CompatibilityCheckServiceFixture.SeedMemberUserIdAsync(context);
        var components = await SeedCompleteBuildComponentsAsync(context);

        var service = CreateService(context);
        var created = await service.CreateAsync(
            memberUserId, new CreateBuildListRequest("Conflict", ToBuildItems(components)), CancellationToken.None);

        await service.AddToCartAsync(
            memberUserId, created.PublicId, new AddBuildToCartRequest(1, created.RowVersion), "dup-key", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<IdempotencyConflictException>(() => service.AddToCartAsync(
            memberUserId, created.PublicId, new AddBuildToCartRequest(2, created.RowVersion), "dup-key", CancellationToken.None));
        Assert.Equal(IdempotencyErrorCodes.PayloadConflict, exception.ErrorCode);
    }

    internal static async Task SeedInventoryAsync(
        DoSelect.Infrastructure.Persistence.DoSelectDbContext context, long skuId, int onHandQuantity)
    {
        context.InventoryBalances.Add(new DoSelect.Domain.Inventory.InventoryBalance(
            Guid.CreateVersion7(), skuId, onHandQuantity, reorderLevel: 0, DateTime.UtcNow));
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// PR #34 review round 2: add-to-cart now requires all 8 build-component categories to be
    /// present (EfBuildListService.RequiredComponentCategoryCodes = BuildComponentCategoryCodes.All)
    /// — seeds a full, cleanly "compatible" set of all 8 (100 units of stock each) so tests whose
    /// actual subject is something else (inventory, idempotency, assembly grouping) don't trip the
    /// completeness gate incidentally.
    /// </summary>
    internal static async Task<(Sku Cpu, Sku Motherboard, Sku Memory, Sku Psu, Sku Case, Sku Gpu, Sku Storage, Sku Cooler)> SeedCompleteBuildComponentsAsync(
        DoSelect.Infrastructure.Persistence.DoSelectDbContext context)
    {
        var cpu = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Cpu,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.CpuSocket] = "AM5",
                [CompatibilitySemanticKeys.CpuGeneration] = "Ryzen7000",
                [CompatibilitySemanticKeys.CpuPowerWatts] = 105m,
            });
        var motherboard = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Motherboard,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.BoardSocket] = "AM5",
                [CompatibilitySemanticKeys.BoardChipset] = "X670E",
                [CompatibilitySemanticKeys.BoardMemoryGeneration] = "DDR5",
                [CompatibilitySemanticKeys.BoardMemorySlotCount] = 4,
                [CompatibilitySemanticKeys.BoardMaxMemoryCapacityGb] = 128m,
                [CompatibilitySemanticKeys.BoardFormFactor] = "ATX",
            },
            storagePorts: new Dictionary<string, int> { ["NVME"] = 4 });
        var memory = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Memory,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.MemoryGeneration] = "DDR5",
                [CompatibilitySemanticKeys.MemoryCapacityGbPerModule] = 16m,
            });
        var psu = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.PowerSupply,
            new Dictionary<string, object?> { [CompatibilitySemanticKeys.PsuWattage] = 650m });
        var pcCase = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Case,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.CaseMaxGpuLengthMm] = 320m,
                [CompatibilitySemanticKeys.CaseMaxCoolerHeightMm] = 170m,
            },
            attributes: new Dictionary<string, string[]>
            {
                [CompatibilityAttributeKeys.CaseSupportedFormFactors] = ["ATX"],
            });
        var gpu = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.GraphicsCard,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.GpuLengthMm] = 280m,
                [CompatibilitySemanticKeys.GpuRecommendedPsuWatts] = 450m,
                [CompatibilitySemanticKeys.GpuPowerWatts] = 200m,
            });
        var storage = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.StorageDevice,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.StorageInterface] = "NVME",
                [CompatibilitySemanticKeys.StoragePowerWatts] = 5m,
            });
        var cooler = await CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
            context, BuildComponentCategoryCodes.Cooler,
            new Dictionary<string, object?>
            {
                [CompatibilitySemanticKeys.CoolerHeightMm] = 150m,
                [CompatibilitySemanticKeys.CoolerPowerWatts] = 10m,
            },
            attributes: new Dictionary<string, string[]>
            {
                [CompatibilityAttributeKeys.CoolerSupportedSockets] = ["AM5"],
            });

        foreach (var sku in new[] { cpu, motherboard, memory, psu, pcCase, gpu, storage, cooler })
        {
            await SeedInventoryAsync(context, sku.Id, 100);
        }

        return (cpu, motherboard, memory, psu, pcCase, gpu, storage, cooler);
    }

    internal static List<BuildItemInput> ToBuildItems(
        (Sku Cpu, Sku Motherboard, Sku Memory, Sku Psu, Sku Case, Sku Gpu, Sku Storage, Sku Cooler) components) =>
    [
        new BuildItemInput(components.Cpu.PublicId, 1),
        new BuildItemInput(components.Motherboard.PublicId, 1),
        new BuildItemInput(components.Memory.PublicId, 1),
        new BuildItemInput(components.Psu.PublicId, 1),
        new BuildItemInput(components.Case.PublicId, 1),
        new BuildItemInput(components.Gpu.PublicId, 1),
        new BuildItemInput(components.Storage.PublicId, 1),
        new BuildItemInput(components.Cooler.PublicId, 1),
    ];

    private static Dictionary<string, Sku> ToCategoryMap(
        (Sku Cpu, Sku Motherboard, Sku Memory, Sku Psu, Sku Case, Sku Gpu, Sku Storage, Sku Cooler) components) =>
        new()
        {
            [BuildComponentCategoryCodes.Cpu] = components.Cpu,
            [BuildComponentCategoryCodes.Motherboard] = components.Motherboard,
            [BuildComponentCategoryCodes.Memory] = components.Memory,
            [BuildComponentCategoryCodes.PowerSupply] = components.Psu,
            [BuildComponentCategoryCodes.Case] = components.Case,
            [BuildComponentCategoryCodes.GraphicsCard] = components.Gpu,
            [BuildComponentCategoryCodes.StorageDevice] = components.Storage,
            [BuildComponentCategoryCodes.Cooler] = components.Cooler,
        };

    private const string TestActorScopePepper = "build-list-service-tests-actor-scope-pepper-0";

    internal static EfBuildListService CreateService(DoSelect.Infrastructure.Persistence.DoSelectDbContext context)
    {
        var idempotencyExecutor = new EfIdempotencyExecutor(
            context,
            Options.Create(new IdempotencyOptions { ActorScopePepper = TestActorScopePepper }),
            TimeProvider.System);
        var factsReader = new EfCompatibilityFactsReader(context);
        return new EfBuildListService(
            context,
            new EfCompatibilityCheckService(context, factsReader),
            factsReader,
            new EfCartService(context, idempotencyExecutor),
            idempotencyExecutor);
    }
}
