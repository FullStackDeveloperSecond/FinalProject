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

    /// <summary>組長 PR #73 round-2 review (P2): 兩個管理員同時為同一 Provider 建立 Draft，版本配置
    /// 必須在交易內序列化——兩個請求都要成功拿到不同版本號，不得 500、不得產生重複版本。拿掉
    /// provider-scoped lock 後，兩邊會同時讀到「尚無版本」而都配到 1，輸家死在
    /// UX_ProviderProfiles_ProviderCode_Version 上。</summary>
    [Fact]
    public async Task CreateDraftAsync_TwoConcurrentDraftsForTheSameProvider_AllocateDistinctVersions()
    {
        await using var setupContext = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(setupContext);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(setupContext);

        var january = ValidStorePickupRequest(
            from: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            to: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var march = ValidStorePickupRequest(
            from: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            to: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));

        // Two service instances over two independent connections, released by the same signal so
        // both are in flight together — the provider lock must line them up, not let them race.
        var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<PackageLimitVersionDto> CreateAsync(CreatePackageLimitVersionRequest request)
        {
            await using var context = ShippingServiceFixture.CreateContext();
            await startSignal.Task;
            return await CreateService(context).CreateDraftAsync(
                ShippingServiceFixture.TestAuditContext, request, actorId, CancellationToken.None);
        }

        var first = CreateAsync(january);
        var second = CreateAsync(march);
        startSignal.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Equal([1, 2], results.Select(result => result.Version).OrderBy(version => version).ToArray());
        var storedVersions = await setupContext.ShippingProviderProfiles.AsNoTracking()
            .Where(profile => profile.ProviderCode == ShippingProviderCodes.StorePickup)
            .Select(profile => profile.Version)
            .ToListAsync();
        Assert.Equal([1, 2], storedVersions.OrderBy(version => version).ToArray());
    }

    /// <summary>組長 PR #73 round-3, item 1 (裁定 B1)：正式／Seed 的目前版本是開放式窗口
    /// (EffectiveToUtc = null)，舊的「對所有版本做重疊比對」讓任何後續版本都必然被判重疊，於是這個
    /// Provider 的限制永遠更新不了。目前版本會在新版本發布時被收窗，它是前一棒不是衝突。</summary>
    [Fact]
    public async Task CreateDraftAsync_WhenTheCurrentVersionIsOpenEnded_AllowsAScheduledReplacement()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        // The seeded shape production ships with: Published, EffectiveFromUtc/ToUtc both null.
        await ShippingServiceFixture.EnsureProviderWithLimitAsync(context, ShippingProviderCodes.StorePickup);

        var scheduled = await CreateService(context).CreateDraftAsync(
            ShippingServiceFixture.TestAuditContext,
            ValidStorePickupRequest(from: DateTime.UtcNow.AddDays(7)),
            actorId,
            CancellationToken.None);

        Assert.Equal(ShippingProviderProfileStatuses.Draft, scheduled.Status);
        Assert.Equal(2, scheduled.Version);
    }

    /// <summary>「立即生效」的替代版本同樣不能被開放式的目前版本封鎖（From = null）。</summary>
    [Fact]
    public async Task CreateDraftAsync_WhenTheReplacementTakesEffectImmediately_IsStillAllowed()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        await ShippingServiceFixture.EnsureProviderWithLimitAsync(context, ShippingProviderCodes.StorePickup);

        var immediate = await CreateService(context).CreateDraftAsync(
            ShippingServiceFixture.TestAuditContext, ValidStorePickupRequest(), actorId, CancellationToken.None);

        Assert.Equal(2, immediate.Version);
    }

    /// <summary>放寬只針對「會被收窗的前一棒」。兩個排在未來、窗口互撞的 Draft 仍必須被拒——發布流程
    /// 不會替 Draft 收窗，硬寫下去同一瞬間會有兩個有效版本。</summary>
    [Fact]
    public async Task CreateDraftAsync_WhenAnotherDraftAlreadyCoversTheWindow_StillRejectsTheOverlap()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        await ShippingServiceFixture.EnsureProviderWithLimitAsync(context, ShippingProviderCodes.StorePickup);
        var service = CreateService(context);
        var from = DateTime.UtcNow.AddDays(7);
        await service.CreateDraftAsync(
            ShippingServiceFixture.TestAuditContext,
            ValidStorePickupRequest(from: from, to: from.AddDays(30)),
            actorId,
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(() => service.CreateDraftAsync(
            ShippingServiceFixture.TestAuditContext,
            ValidStorePickupRequest(from: from.AddDays(10), to: from.AddDays(40)),
            actorId,
            CancellationToken.None));

        Assert.Equal(ShippingAdminErrorCodes.PackageLimitPeriodOverlap, exception.ErrorCode);
    }

    /// <summary>新窗口必須排在目前版本「之後」。起點不晚於目前版本起點的是插隊，不是接班。</summary>
    [Fact]
    public async Task CreateDraftAsync_WhenTheNewWindowStartsBeforeTheCurrentVersion_RejectsTheOverlap()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var currentFrom = DateTime.UtcNow.AddDays(-1);
        await SeedPublishedVersionAsync(context, currentFrom, effectiveToUtc: null);

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(() => CreateService(context).CreateDraftAsync(
            ShippingServiceFixture.TestAuditContext,
            ValidStorePickupRequest(from: currentFrom.AddHours(-1)),
            actorId,
            CancellationToken.None));

        Assert.Equal(ShippingAdminErrorCodes.PackageLimitPeriodOverlap, exception.ErrorCode);
    }

    /// <summary>組長 PR #73 round-3, item 2 (裁定 B1)：提前發布未來版本後，cutoff 之前舊版本仍須是唯一
    /// 有效版本。舊碼把舊 profile 立刻改成 Superseded，而解析點只認 Status=Published，因此 cutoff 前
    /// 完全找不到有效 profile／limit——物流整段空窗。</summary>
    [Fact]
    public async Task PublishAsync_WhenTheNewVersionIsScheduled_TheOutgoingVersionStaysResolvableUntilCutoff()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        await ShippingServiceFixture.EnsureProviderWithLimitAsync(context, ShippingProviderCodes.StorePickup);
        var service = CreateService(context);
        var cutoff = DateTime.UtcNow.AddDays(7);
        var scheduled = await service.CreateDraftAsync(
            ShippingServiceFixture.TestAuditContext, ValidStorePickupRequest(from: cutoff), actorId, CancellationToken.None);

        await service.PublishAsync(
            ShippingProviderCodes.StorePickup, ShippingServiceFixture.TestAuditContext,
            scheduled.PublicId, new PublishPackageLimitVersionRequest(scheduled.RowVersion), actorId, CancellationToken.None);

        // Resolve exactly the way Checkout and Shipping Options do: not-Draft plus window contains now.
        await using var verify = ShippingServiceFixture.CreateContext();
        var now = DateTime.UtcNow;
        var effective = await (
            from profile in verify.ShippingProviderProfiles.AsNoTracking()
            join limit in verify.PackageLimitVersions.AsNoTracking() on profile.Id equals limit.ProviderProfileId
            where profile.ProviderCode == ShippingProviderCodes.StorePickup &&
                profile.Status != ShippingProviderProfileStatuses.Draft &&
                (profile.EffectiveFromUtc == null || profile.EffectiveFromUtc <= now) &&
                (profile.EffectiveToUtc == null || now < profile.EffectiveToUtc) &&
                (limit.EffectiveFromUtc == null || limit.EffectiveFromUtc <= now) &&
                (limit.EffectiveToUtc == null || now < limit.EffectiveToUtc)
            select new { profile.Version, profile.Status }).ToListAsync();

        // Exactly one — the outgoing v1, still serving until the scheduled switch-over.
        var single = Assert.Single(effective);
        Assert.Equal(1, single.Version);
        Assert.Equal(ShippingProviderProfileStatuses.Superseded, single.Status);

        // And its limit row was truncated to the same cutoff as its profile (windows stay consistent).
        var outgoing = await verify.ShippingProviderProfiles.AsNoTracking()
            .SingleAsync(candidate => candidate.ProviderCode == ShippingProviderCodes.StorePickup && candidate.Version == 1);
        var outgoingLimit = await verify.PackageLimitVersions.AsNoTracking()
            .SingleAsync(candidate => candidate.ProviderProfileId == outgoing.Id);
        Assert.Equal(outgoing.EffectiveToUtc, outgoingLimit.EffectiveToUtc);
        Assert.NotNull(outgoing.EffectiveToUtc);
    }

    /// <summary>切換時刻起，新版本接手且仍然「恰好一個」。</summary>
    [Fact]
    public async Task PublishAsync_AfterTheCutoffPasses_TheIncomingVersionIsTheOnlyEffectiveOne()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        await ShippingServiceFixture.EnsureProviderWithLimitAsync(context, ShippingProviderCodes.StorePickup);
        var service = CreateService(context);
        // Publish a version whose window already started five minutes ago: "now" is past the cutoff.
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        var incoming = await service.CreateDraftAsync(
            ShippingServiceFixture.TestAuditContext, ValidStorePickupRequest(from: cutoff), actorId, CancellationToken.None);

        await service.PublishAsync(
            ShippingProviderCodes.StorePickup, ShippingServiceFixture.TestAuditContext,
            incoming.PublicId, new PublishPackageLimitVersionRequest(incoming.RowVersion), actorId, CancellationToken.None);

        await using var verify = ShippingServiceFixture.CreateContext();
        var now = DateTime.UtcNow;
        var effective = await verify.ShippingProviderProfiles.AsNoTracking()
            .Where(profile => profile.ProviderCode == ShippingProviderCodes.StorePickup &&
                profile.Status != ShippingProviderProfileStatuses.Draft &&
                (profile.EffectiveFromUtc == null || profile.EffectiveFromUtc <= now) &&
                (profile.EffectiveToUtc == null || now < profile.EffectiveToUtc))
            .ToListAsync();

        var single = Assert.Single(effective);
        Assert.Equal(2, single.Version);
        Assert.Equal(ShippingProviderProfileStatuses.Published, single.Status);
    }

    /// <summary>組長 PR #73 round-3, item 3：Publish 也要走同一把 provider-scoped lock。沒有既有
    /// Published 列時，兩個不同 Draft 各自通過自己的 RowVersion，輸家會撞
    /// UX_ProviderProfiles_ProviderCode_Published 變成未映射的 500。</summary>
    [Fact]
    public async Task PublishAsync_TwoConcurrentPublishesForTheSameProvider_NeverReturnAnUnmappedFailure()
    {
        await using var setupContext = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(setupContext);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(setupContext);
        var setupService = CreateService(setupContext);
        // No Published row yet: both drafts are independently publishable, which is the race.
        var first = await setupService.CreateDraftAsync(
            ShippingServiceFixture.TestAuditContext,
            ValidStorePickupRequest(from: DateTime.UtcNow.AddDays(1), to: DateTime.UtcNow.AddDays(2)),
            actorId, CancellationToken.None);
        var second = await setupService.CreateDraftAsync(
            ShippingServiceFixture.TestAuditContext,
            ValidStorePickupRequest(from: DateTime.UtcNow.AddDays(3), to: DateTime.UtcNow.AddDays(4)),
            actorId, CancellationToken.None);

        var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> PublishAsync(PackageLimitVersionDto version)
        {
            await using var context = ShippingServiceFixture.CreateContext();
            await startSignal.Task;
            try
            {
                await CreateService(context).PublishAsync(
                    ShippingProviderCodes.StorePickup, ShippingServiceFixture.TestAuditContext,
                    version.PublicId, new PublishPackageLimitVersionRequest(version.RowVersion),
                    actorId, CancellationToken.None);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var firstTask = PublishAsync(first);
        var secondTask = PublishAsync(second);
        startSignal.SetResult();
        var outcomes = await Task.WhenAll(firstTask, secondTask);

        // At least one succeeds, and every failure is a mapped ShippingAdminWriteException — never
        // a raw DbUpdateException/SqlException that the API would surface as a 500 (that is exactly
        // what the missing publish lock produced).
        //
        // Which of the two stable codes the loser gets depends on who wins the lock, and both are
        // correct production outcomes: if the earlier-window draft publishes first, the second is a
        // normal chronological publish (or loses on the single-Published race → concurrency_conflict);
        // if the later-window draft publishes first, the earlier one is then genuinely out of
        // chronological order and must be refused with validation_failed (round-2, item 5). Pinning
        // one specific code here would be pinning a coin flip, so the assertion is the property that
        // actually matters: mapped, stable, and never a 500.
        Assert.Contains(outcomes, outcome => outcome is null);
        foreach (var failure in outcomes.Where(outcome => outcome is not null))
        {
            var writeFailure = Assert.IsType<ShippingAdminWriteException>(failure);
            Assert.Contains(
                writeFailure.ErrorCode,
                new[] { ShippingAdminErrorCodes.ConcurrencyConflict, ShippingAdminErrorCodes.ValidationFailed });
        }

        // And the single-Published invariant held.
        await using var verify = ShippingServiceFixture.CreateContext();
        Assert.Equal(1, await verify.ShippingProviderProfiles.AsNoTracking().CountAsync(
            profile => profile.ProviderCode == ShippingProviderCodes.StorePickup &&
                profile.Status == ShippingProviderProfileStatuses.Published));
    }

    /// <summary>組長 PR #73 round-3, item 5：Domain 建構子要求 DateTimeKind.Utc，但沒有 Z 的 JSON 值
    /// 綁成 Unspecified、帶 offset 的綁成 Local——輸入錯誤不可以變成 500。</summary>
    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public async Task CreateDraftAsync_WhenTheEffectiveTimeIsNotUtc_ThrowsValidationFailed(DateTimeKind kind)
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var from = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(7), kind);

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(() => CreateService(context).CreateDraftAsync(
            ShippingServiceFixture.TestAuditContext, ValidStorePickupRequest(from: from), actorId, CancellationToken.None));

        Assert.Equal(ShippingAdminErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    /// <summary>同一類輸入錯誤：結束時間不晚於開始時間，Domain 也會丟例外，必須先擋成 400。</summary>
    [Fact]
    public async Task CreateDraftAsync_WhenTheWindowEndsBeforeItStarts_ThrowsValidationFailed()
    {
        await using var context = ShippingServiceFixture.CreateContext();
        await ShippingServiceFixture.ClearPackageLimitDataAsync(context);
        var actorId = await ShippingServiceFixture.SeedShippingAdminAsync(context);
        var from = DateTime.UtcNow.AddDays(7);

        var exception = await Assert.ThrowsAsync<ShippingAdminWriteException>(() => CreateService(context).CreateDraftAsync(
            ShippingServiceFixture.TestAuditContext,
            ValidStorePickupRequest(from: from, to: from.AddHours(-1)),
            actorId,
            CancellationToken.None));

        Assert.Equal(ShippingAdminErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    /// <summary>Seeds one Published profile+limit pair with an explicit window.</summary>
    private static async Task SeedPublishedVersionAsync(
        DoSelectDbContext context, DateTime? effectiveFromUtc, DateTime? effectiveToUtc)
    {
        var now = DateTime.UtcNow;
        var profile = new ShippingProviderProfile(
            Guid.CreateVersion7(), ShippingProviderCodes.StorePickup, 1,
            ShippingProviderProfileStatuses.Published, effectiveFromUtc, effectiveToUtc, "{}", 1, now);
        context.ShippingProviderProfiles.Add(profile);
        await context.SaveChangesAsync();
        context.PackageLimitVersions.Add(new PackageLimitVersion(
            Guid.CreateVersion7(), profile.Id, 1, 5m, 45m, 45m, 45m, 105m, 20000m,
            effectiveFromUtc, effectiveToUtc, now));
        await context.SaveChangesAsync();
    }

    private static EfPackageLimitService CreateService(DoSelectDbContext context) =>
        new(context, new EfAuditWriter(context, TimeProvider.System));
}
