using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoSelect.Application.Builds;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Shopping;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DoSelect.Infrastructure.Builds;

/// <summary>UC-BUILD-01: build-list CRUD for a member's own saved PC configurations.</summary>
public sealed class EfBuildListService : IBuildListService
{
    /// <summary>
    /// 組裝服務費，固定 NT$300／台（見 商品、組裝與相容性.md，定義見
    /// <see cref="AssemblyPricingPolicy.FeePerUnit"/>）。A build list represents one physical
    /// unit for preview purposes — actions/add-to-cart multiplies this per purchased quantity,
    /// but that belongs to the add-to-cart slice, not this CRUD surface.
    /// </summary>
    private const decimal AssemblyFeePerUnit = AssemblyPricingPolicy.FeePerUnit;

    private const int MaxActiveBuildListsPerMember = 50;

    /// <summary>Stable command name for 資料一致性、Outbox與冪等設計.md's IdempotencyRecord.Operation.</summary>
    private const string AddToCartOperation = "buildlist.add_to_cart";

    /// <summary>
    /// PR #34 review round 2: a build with only, say, a storage device has zero applicable
    /// compatibility rules (every rule short-circuits when its counterpart slot is empty) and was
    /// being reported "compatible" — 組長's V1 ruling is that all 8 categories are required for a
    /// purchasable build, not just the 5 that have direct compatibility rules between them.
    /// </summary>
    private static readonly IReadOnlyList<string> RequiredComponentCategoryCodes =
        CompatibilityCatalogContract.Categories.All;

    private readonly DoSelectDbContext _dbContext;
    private readonly ICompatibilityCheckService _compatibilityCheckService;
    private readonly ICompatibilityCatalogReader _catalogReader;
    private readonly ICartService _cartService;
    private readonly IIdempotencyExecutor _idempotencyExecutor;

    public EfBuildListService(
        DoSelectDbContext dbContext,
        ICompatibilityCheckService compatibilityCheckService,
        ICompatibilityCatalogReader catalogReader,
        ICartService cartService,
        IIdempotencyExecutor idempotencyExecutor)
    {
        _dbContext = dbContext;
        _compatibilityCheckService = compatibilityCheckService;
        _catalogReader = catalogReader;
        _cartService = cartService;
        _idempotencyExecutor = idempotencyExecutor;
    }

    public async Task<PageResult<BuildListSummaryDto>> ListAsync(
        string memberUserId,
        BuildListListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var now = DateTime.UtcNow;

        var baseQuery = _dbContext.BuildLists.AsNoTracking()
            .Where(list => list.OwnerUserId == memberUserId && list.Status == BuildListStatusCodes.Active);

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        // (pageNumber - 1) * pageSize can overflow int for an extreme page number — computed in
        // long first, matching the fix already established for catalog's own paged list
        // (EfProductSearchService). A skip beyond int.MaxValue can never land on a real row, so
        // it's a legal empty page, not an error.
        var skip = (long)(query.PageNumber - 1) * query.PageSize;
        var page = skip > int.MaxValue
            ? []
            : await baseQuery
                // datetime2(3) can tie across rows updated in the same millisecond (e.g. a bulk
                // re-check); Id is immutable and unique, so it breaks the tie deterministically
                // instead of leaving row order across pages undefined (組長 PR #34 round-4
                // review, item 6).
                .OrderByDescending(list => list.UpdatedAtUtc)
                .ThenByDescending(list => list.Id)
                .Skip((int)skip)
                .Take(query.PageSize)
                .ToListAsync(cancellationToken);

        if (page.Count == 0)
        {
            return new PageResult<BuildListSummaryDto>([], query.PageNumber, query.PageSize, totalCount);
        }

        var buildListIds = page.Select(list => list.Id).ToArray();

        var itemRows = await _dbContext.BuildListItems.AsNoTracking()
            .Where(item => buildListIds.Contains(item.BuildListId))
            .Select(item => new { item.BuildListId, item.SkuId, item.Quantity })
            .ToListAsync(cancellationToken);

        var skuIds = itemRows.Select(row => row.SkuId).Distinct().ToArray();
        var priceBySkuId = await LoadEffectivePricesAsync(skuIds, now, cancellationToken);

        var sharedBuildListIds = await _dbContext.BuildShareTokens.AsNoTracking()
            .Where(token => buildListIds.Contains(token.BuildListId) &&
                token.RevokedAtUtc == null &&
                (token.ExpiresAtUtc == null || token.ExpiresAtUtc > now))
            .Select(token => token.BuildListId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var sharedBuildListIdSet = sharedBuildListIds.ToHashSet();

        var items = page.Select(list =>
        {
            var listItems = itemRows.Where(row => row.BuildListId == list.Id).ToList();
            var merchandise = listItems.Sum(row => priceBySkuId.GetValueOrDefault(row.SkuId, 0m) * row.Quantity);

            return new BuildListSummaryDto(
                list.PublicId,
                list.Name,
                listItems.Count,
                OverallToToken(list.CompatibilityStatus ?? CompatibilityOverall.InsufficientData),
                merchandise + AssemblyFeePerUnit,
                sharedBuildListIdSet.Contains(list.Id),
                list.UpdatedAtUtc,
                list.RowVersion);
        }).ToList();

        return new PageResult<BuildListSummaryDto>(items, query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<BuildListDto> GetAsync(
        string memberUserId,
        Guid buildListPublicId,
        CancellationToken cancellationToken)
    {
        var buildList = await FindOwnedActiveAsync(memberUserId, buildListPublicId, cancellationToken);

        var storedItems = await _dbContext.BuildListItems.AsNoTracking()
            .Where(item => item.BuildListId == buildList.Id)
            .OrderBy(item => item.SortOrder)
            .ToListAsync(cancellationToken);

        var skusById = await LoadSkusAsync(storedItems.Select(item => item.SkuId), cancellationToken);
        var rows = storedItems
            .Select(item => (item.PublicId, Sku: skusById[item.SkuId], item.Quantity, item.SortOrder))
            .ToList();

        return await ComposeDtoAsync(buildList, rows, cancellationToken);
    }

    public async Task<BuildListDto> CreateAsync(
        string memberUserId,
        CreateBuildListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTime.UtcNow;
        var mergedItems = EfCompatibilityCheckService.MergeAndValidateItems(request.Items);
        var skusByPublicId = await LoadSkusByPublicIdAsync(mergedItems, cancellationToken);

        // PR #34 review: this used to insert the BuildList (SaveChanges #1), then add items and
        // record compatibility in a second SaveChanges — a failure in between left an empty
        // Active BuildList behind, still counting against the 50-list quota. One transaction
        // spans the whole create so a mid-way failure leaves nothing behind at all. The quota
        // Count also moved inside this transaction, behind a per-member exclusive lock (see
        // TryAcquireLockAsync's doc comment) — two concurrent creates for the same member could
        // otherwise both read 49 and both pass, landing at 51.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (!await TryAcquireLockAsync(transaction, $"build-list-quota:{memberUserId}", cancellationToken))
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ConcurrencyConflict,
                "Another build list operation for this member is in progress. Try again shortly.");
        }

        var activeCount = await _dbContext.BuildLists.CountAsync(
            list => list.OwnerUserId == memberUserId && list.Status == BuildListStatusCodes.Active,
            cancellationToken);
        if (activeCount >= MaxActiveBuildListsPerMember)
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ValidationFailed,
                $"A member may have at most {MaxActiveBuildListsPerMember} active build lists.");
        }

        var buildList = new BuildList(Guid.CreateVersion7(), memberUserId, request.Name, BuildListStatusCodes.Active, now);
        _dbContext.BuildLists.Add(buildList);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var rows = AddItems(buildList.Id, mergedItems, skusByPublicId, now);

        var compatibility = await RecordCompatibilityAndSaveAsync(buildList, mergedItems, now, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await ComposeDtoAsync(buildList, rows, cancellationToken, compatibility);
    }

    public async Task<BuildListDto> UpdateAsync(
        string memberUserId,
        Guid buildListPublicId,
        UpdateBuildListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTime.UtcNow;
        var buildList = await FindOwnedActiveAsync(memberUserId, buildListPublicId, cancellationToken);
        var mergedItems = EfCompatibilityCheckService.MergeAndValidateItems(request.Items);
        var skusByPublicId = await LoadSkusByPublicIdAsync(mergedItems, cancellationToken);

        _dbContext.Entry(buildList).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;

        var existingItems = await _dbContext.BuildListItems
            .Where(item => item.BuildListId == buildList.Id)
            .ToListAsync(cancellationToken);
        _dbContext.BuildListItems.RemoveRange(existingItems);

        buildList.Rename(request.Name, now);

        var rows = AddItems(buildList.Id, mergedItems, skusByPublicId, now);

        // Same reasoning as CreateAsync's own transaction (組長 PR #34 review): RecordCompatibilityAsync
        // now persists a CompatibilityCheckRun/Result snapshot as a side effect of CheckAsync, so a
        // RowVersion conflict on the final SaveWithConcurrencyCheckAsync must roll that back too,
        // not leave an orphaned Run behind for an update that never actually landed.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var compatibility = await RecordCompatibilityAndSaveAsync(buildList, mergedItems, now, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await ComposeDtoAsync(buildList, rows, cancellationToken, compatibility);
    }

    public async Task DeleteAsync(
        string memberUserId,
        Guid buildListPublicId,
        byte[] rowVersion,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var buildList = await FindOwnedActiveAsync(memberUserId, buildListPublicId, cancellationToken);

        _dbContext.Entry(buildList).Property(candidate => candidate.RowVersion).OriginalValue = rowVersion;
        buildList.ChangeStatus(BuildListStatusCodes.Deleted, now);

        // 分享清單設計: 來源清單被刪除，連結必須立即失效 — revoke any still-live share tokens
        // in the same save rather than leaving them readable against a deleted list.
        var liveShareTokens = await _dbContext.BuildShareTokens
            .Where(token => token.BuildListId == buildList.Id && token.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var token in liveShareTokens)
        {
            token.Revoke(now);
        }

        await SaveWithConcurrencyCheckAsync(cancellationToken);
    }

    private async Task<BuildList> FindOwnedActiveAsync(
        string memberUserId,
        Guid buildListPublicId,
        CancellationToken cancellationToken)
    {
        var buildList = await _dbContext.BuildLists.FirstOrDefaultAsync(
            list => list.PublicId == buildListPublicId &&
                list.OwnerUserId == memberUserId &&
                list.Status == BuildListStatusCodes.Active,
            cancellationToken);

        return buildList ?? throw new BuildWriteException(
            BuildWriteException.ErrorCodes.ResourceNotFound,
            $"Build list '{buildListPublicId}' was not found.");
    }

    private async Task<Dictionary<Guid, Sku>> LoadSkusByPublicIdAsync(
        IReadOnlyList<BuildItemInput> mergedItems,
        CancellationToken cancellationToken)
    {
        var requestedPublicIds = mergedItems.Select(item => item.SkuPublicId).ToArray();
        var skus = await _dbContext.Skus
            .Where(sku => requestedPublicIds.Contains(sku.PublicId))
            .ToListAsync(cancellationToken);

        var unresolved = requestedPublicIds.Except(skus.Select(sku => sku.PublicId)).ToList();
        if (unresolved.Count > 0)
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ValidationFailed,
                $"Unknown SKU(s): {string.Join(", ", unresolved)}.");
        }

        return skus.ToDictionary(sku => sku.PublicId);
    }

    private async Task<Dictionary<long, Sku>> LoadSkusAsync(IEnumerable<long> skuIds, CancellationToken cancellationToken)
    {
        var ids = skuIds.Distinct().ToArray();
        return await _dbContext.Skus.AsNoTracking()
            .Where(sku => ids.Contains(sku.Id))
            .ToDictionaryAsync(sku => sku.Id, cancellationToken);
    }

    /// <summary>Constructs and stages <see cref="BuildListItem"/> rows for a (now-persisted) BuildList, returning the same shape <see cref="ComposeDtoAsync"/> needs for the response.</summary>
    private List<(Guid ItemPublicId, Sku Sku, int Quantity, int SortOrder)> AddItems(
        long buildListId,
        IReadOnlyList<BuildItemInput> mergedItems,
        IReadOnlyDictionary<Guid, Sku> skusByPublicId,
        DateTime now)
    {
        var rows = new List<(Guid, Sku, int, int)>();
        for (var sortOrder = 0; sortOrder < mergedItems.Count; sortOrder++)
        {
            var input = mergedItems[sortOrder];
            var sku = skusByPublicId[input.SkuPublicId];
            var itemPublicId = Guid.CreateVersion7();
            var item = new BuildListItem(itemPublicId, buildListId, sku.Id, input.Quantity, sortOrder, now);
            _dbContext.BuildListItems.Add(item);
            rows.Add((itemPublicId, sku, input.Quantity, sortOrder));
        }

        return rows;
    }

    /// <summary>
    /// CheckAsync now saves as a side effect (its CompatibilityCheckRun/Result snapshot), so a
    /// stale RowVersion on <paramref name="buildList"/> (already Modified by Rename/AddItems by
    /// the time this runs) can surface as a raw DbUpdateConcurrencyException from *inside*
    /// CheckAsync's own SaveChangesAsync — before the caller's later SaveWithConcurrencyCheckAsync
    /// ever runs. Wrapping both calls here catches it at either point. Returns the computed
    /// result so the caller's response DTO can reuse it (組長 PR #34 round-4 review, item 2)
    /// instead of calling CheckAsync a second time after commit — a second call after commit
    /// persists a second CompatibilityCheckRun/Result outside this transaction, so a failure
    /// there would return an error for a create/update that had already succeeded, and a rule
    /// settings change landing between the two calls could make the response disagree with what
    /// was actually just saved.
    /// </summary>
    private async Task<CompatibilityCheckDto> RecordCompatibilityAndSaveAsync(
        BuildList buildList,
        IReadOnlyList<BuildItemInput> mergedItems,
        DateTime now,
        CancellationToken cancellationToken)
    {
        CompatibilityCheckDto compatibility;
        try
        {
            compatibility = await RecordCompatibilityAsync(buildList, mergedItems, now, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ConcurrencyConflict,
                "The build list was updated by someone else. Reload and try again.");
        }

        await SaveWithConcurrencyCheckAsync(cancellationToken);
        return compatibility;
    }

    private async Task<CompatibilityCheckDto> RecordCompatibilityAsync(
        BuildList buildList,
        IReadOnlyList<BuildItemInput> mergedItems,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var compatibility = await _compatibilityCheckService.CheckAsync(
            new CompatibilityCheckRequest(mergedItems), buildList.Id, cancellationToken);
        buildList.RecordCompatibility(TokenToOverall(compatibility.Overall), now);
        return compatibility;
    }

    /// <summary>
    /// 組長 PR #34 review — two check-then-write races: (1) CreateAsync's 50-active-list quota
    /// Count then Insert can both pass for two concurrent creates by the same member, landing at
    /// 51+; (2) CreateShareAsync's revoke-then-insert can leave two simultaneously "active" share
    /// tokens for the same build list if two requests race. Both are the identical shape as
    /// EfCompatibilityRuleAdminService's own SettingsVersion race — a sys.sp_getapplock exclusive
    /// lock scoped to the specific resource (member id, or build-list id) serializes the
    /// read-check-write sequence per-resource without needing a lock across all members/lists.
    /// </summary>
    private async Task<bool> TryAcquireLockAsync(
        IDbContextTransaction transaction, string resource, CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 0;
            SELECT @result;
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.DbType = DbType.String;
        parameter.Size = 255;
        parameter.Value = resource;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) >= 0;
    }

    private async Task<BuildListDto> ComposeDtoAsync(
        BuildList buildList,
        IReadOnlyList<(Guid ItemPublicId, Sku Sku, int Quantity, int SortOrder)> rows,
        CancellationToken cancellationToken,
        CompatibilityCheckDto? precomputedCompatibility = null)
    {
        var (itemDtos, compatibilityDto, totals) = await ComposeItemsAsync(
            rows, buildList.Id, cancellationToken, precomputedCompatibility);

        return new BuildListDto(
            buildList.PublicId,
            buildList.Name,
            itemDtos,
            compatibilityDto,
            totals,
            buildList.UpdatedAtUtc,
            buildList.RowVersion);
    }

    /// <summary>
    /// Shared by <see cref="ComposeDtoAsync"/> and <see cref="GetSharedBuildAsync"/> — both render
    /// the same item/compatibility/totals projection, just wrapped in a different envelope.
    /// <paramref name="precomputedCompatibility"/> lets a caller that just ran its own CheckAsync
    /// moments ago (Create/Update, inside the same transaction) reuse that result here instead of
    /// triggering a second persisted CompatibilityCheckRun/Result — omit it (as GetAsync and the
    /// share-view path do) to always re-check fresh, e.g. share pages must revalidate compatibility
    /// every time they're opened.
    /// </summary>
    private async Task<(List<BuildItemDto> Items, BuildCompatibilitySummaryDto Compatibility, BuildTotalsDto Totals)> ComposeItemsAsync(
        IReadOnlyList<(Guid ItemPublicId, Sku Sku, int Quantity, int SortOrder)> rows,
        long? buildListId,
        CancellationToken cancellationToken,
        CompatibilityCheckDto? precomputedCompatibility = null)
    {
        var now = DateTime.UtcNow;
        var skuIds = rows.Select(row => row.Sku.Id).ToArray();
        var priceBySkuId = await LoadEffectivePricesAsync(skuIds, now, cancellationToken);
        var availableQuantityBySkuId = await _dbContext.InventoryBalances.AsNoTracking()
            .Where(balance => skuIds.Contains(balance.SkuId))
            .ToDictionaryAsync(balance => balance.SkuId, balance => balance.AvailableQuantity, cancellationToken);

        var itemDtos = rows.Select(row =>
        {
            var unitPrice = priceBySkuId.GetValueOrDefault(row.Sku.Id, row.Sku.ListPrice);
            var availableQuantity = availableQuantityBySkuId.TryGetValue(row.Sku.Id, out var quantity)
                ? quantity
                : (int?)null;

            return new BuildItemDto(
                row.ItemPublicId,
                row.Sku.PublicId,
                row.Sku.SkuCode,
                row.Sku.NameZhTw,
                row.Quantity,
                row.SortOrder,
                unitPrice,
                unitPrice * row.Quantity,
                AvailabilityToken(row.Sku, row.Quantity, availableQuantity));
        }).ToList();

        var compatibility = precomputedCompatibility;
        if (compatibility is null)
        {
            var mergedItems = rows.Select(row => new BuildItemInput(row.Sku.PublicId, row.Quantity)).ToList();
            compatibility = await _compatibilityCheckService.CheckAsync(
                new CompatibilityCheckRequest(mergedItems), buildListId, cancellationToken);
        }

        var merchandise = itemDtos.Sum(item => item.LineTotal);
        var totals = new BuildTotalsDto(merchandise, AssemblyFeePerUnit, merchandise + AssemblyFeePerUnit, "TWD");
        var compatibilityDto = new BuildCompatibilitySummaryDto(
            compatibility.Overall, compatibility.RuleSetVersion, compatibility.SettingsVersion, compatibility.Results);

        return (itemDtos, compatibilityDto, totals);
    }

    public async Task<BuildShareDto> CreateShareAsync(
        string memberUserId,
        Guid buildListPublicId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var buildList = await FindOwnedActiveAsync(memberUserId, buildListPublicId, cancellationToken);

        // Two concurrent regenerate-link requests for the same build list could otherwise both
        // read "no active token yet" and both insert one, leaving two simultaneously "active"
        // shares — violating "一次只有一個有效分享連結" (組長 PR #34 review). Locked per build list,
        // not globally, so unrelated members' share creations never serialize against each other.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await TryAcquireLockAsync(transaction, $"build-list-share:{buildList.Id}", cancellationToken))
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ConcurrencyConflict,
                "Another share-link operation for this build list is in progress. Try again shortly.");
        }

        // POST regenerates the link: a build list has at most one live share at a time, so any
        // still-active token is revoked before the new one is created.
        await RevokeActiveSharesAsync(buildList.Id, now, cancellationToken);

        var (rawToken, tokenHash) = GenerateShareToken();
        // 分享清單設計/組長定版修正: ExpiresAtUtc defaults to NULL (no auto-expiry) — only an
        // explicit owner revoke or the source list being deleted invalidates a share link.
        var shareToken = new BuildShareToken(Guid.CreateVersion7(), buildList.Id, tokenHash, expiresAtUtc: null, now);
        _dbContext.BuildShareTokens.Add(shareToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new BuildShareDto(shareToken.PublicId, $"/api/v1/build-shares/{rawToken}", shareToken.ExpiresAtUtc);
    }

    public async Task RevokeShareAsync(
        string memberUserId,
        Guid buildListPublicId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var buildList = await FindOwnedActiveAsync(memberUserId, buildListPublicId, cancellationToken);

        // 組長 PR #34 review: this used to revoke-then-save with no lock or transaction at all,
        // unlike CreateShareAsync's own build-list-share:{id} lock — a concurrent revoke and
        // regenerate-link could interleave so the revoke's SaveChanges lands *after* the create's,
        // silently un-revoking the create's freshly minted token and leaving the "revoked" link
        // still live. Same lock/transaction shape as CreateShareAsync serializes the two against
        // each other per build list.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await TryAcquireLockAsync(transaction, $"build-list-share:{buildList.Id}", cancellationToken))
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ConcurrencyConflict,
                "Another share-link operation for this build list is in progress. Try again shortly.");
        }

        await RevokeActiveSharesAsync(buildList.Id, now, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SharedBuildDto> GetSharedBuildAsync(string rawToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);

        var now = DateTime.UtcNow;
        var tokenHash = HashShareToken(rawToken);

        var shareToken = await _dbContext.BuildShareTokens.FirstOrDefaultAsync(
            token => token.TokenHash == tokenHash, cancellationToken);
        if (shareToken is null || shareToken.RevokedAtUtc is not null ||
            (shareToken.ExpiresAtUtc is not null && shareToken.ExpiresAtUtc <= now))
        {
            throw NotFoundShare();
        }

        var buildList = await _dbContext.BuildLists.FirstOrDefaultAsync(
            list => list.Id == shareToken.BuildListId && list.Status == BuildListStatusCodes.Active,
            cancellationToken);
        if (buildList is null)
        {
            // 分享清單設計: 來源清單被刪除，連結立即失效 — even a not-yet-revoked token must 404
            // once its BuildList is gone (Delete already revokes live tokens too, but this
            // covers a list deleted before that codepath existed, or any other edge case).
            throw NotFoundShare();
        }

        // PR #34 review: a suspended/disabled/anonymized owner's share link must invalidate
        // immediately too, not just a deleted list — the token/list checks above don't see this.
        // PendingEmailVerification is intentionally still allowed: nothing else in this codebase
        // gates member feature usage on having confirmed email, so a fresh signup's build list
        // shares normally, same as everywhere else.
        var ownerAccountStatus = await _dbContext.Users
            .Where(user => user.Id == buildList.OwnerUserId)
            .Select(user => user.AccountStatus)
            .SingleAsync(cancellationToken);
        if (ownerAccountStatus is AccountStatus.Suspended or AccountStatus.Disabled or AccountStatus.Anonymized)
        {
            throw NotFoundShare();
        }

        shareToken.RecordAccess(now);

        var storedItems = await _dbContext.BuildListItems.AsNoTracking()
            .Where(item => item.BuildListId == buildList.Id)
            .OrderBy(item => item.SortOrder)
            .ToListAsync(cancellationToken);
        var skusById = await LoadSkusAsync(storedItems.Select(item => item.SkuId), cancellationToken);
        var rows = storedItems
            .Select(item => (item.PublicId, Sku: skusById[item.SkuId], item.Quantity, item.SortOrder))
            .ToList();

        // 每次分享頁開啟需重新驗證: recompute live and refresh the owner's cache too, unlike the
        // owner's own read-only GetAsync.
        var (itemDtos, compatibility, totals) = await ComposeItemsAsync(rows, buildList.Id, cancellationToken);
        buildList.RecordCompatibility(TokenToOverall(compatibility.Overall), now);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // PR #34 review: this is a best-effort cache refresh on a public, unauthenticated
            // read — the response below was already computed live regardless of whether this
            // write lands. Two concurrent opens of the same link racing on BuildList's RowVersion
            // must not turn a read into a 500; the loser's cache write is simply dropped, and the
            // next successful read corrects it.
        }

        // 商品、組裝與相容性.md: InsufficientData, not just Blocked, still blocks "一鍵加入整套清單";
        // "組合內任何必要零件缺貨或不可售時，整組不能直接結帳" — insufficient_stock blocks it too,
        // not just unavailable, so every item must be fully available. A ruleDisabled finding
        // blocks it too — see ExecuteAddToCartAsync's matching check (組長 PR #34 review).
        var canAddToCart = compatibility.Overall is "compatible" or "warning" &&
            itemDtos.All(item => item.Availability == "available") &&
            compatibility.Results.All(finding => finding.Severity != CompatibilitySeverityTokens.RuleDisabled);

        return new SharedBuildDto(shareToken.PublicId, buildList.Name, itemDtos, compatibility, totals, CanCopy: true, canAddToCart);
    }

    public async Task<CartDto> AddToCartAsync(
        string memberUserId,
        Guid buildListPublicId,
        AddBuildToCartRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ValidationFailed, "An Idempotency-Key header is required.");
        }

        var userPublicId = await _dbContext.Users
            .Where(user => user.Id == memberUserId)
            .Select(user => user.PublicId)
            .SingleAsync(cancellationToken);

        var command = IdempotencyCommand.Create(
            IdempotencyActorScope.ForUser(userPublicId),
            AddToCartOperation,
            idempotencyKey,
            new { buildListPublicId, request.Quantity, BuildRowVersion = Convert.ToBase64String(request.BuildRowVersion) });

        var result = await _idempotencyExecutor.ExecuteAsync(
            command,
            handler: ct => ExecuteAddToCartAsync(memberUserId, buildListPublicId, request, ct),
            replayFactory: (stored, _) => Task.FromResult(
                JsonSerializer.Deserialize<CartDto>(stored.ResponseSummary)!),
            cancellationToken);

        return result.Body;
    }

    private async Task<IdempotencyResponse<CartDto>> ExecuteAddToCartAsync(
        string memberUserId,
        Guid buildListPublicId,
        AddBuildToCartRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var buildList = await FindOwnedActiveAsync(memberUserId, buildListPublicId, cancellationToken);
        _dbContext.Entry(buildList).Property(candidate => candidate.RowVersion).OriginalValue = request.BuildRowVersion;

        var storedItems = await _dbContext.BuildListItems.AsNoTracking()
            .Where(item => item.BuildListId == buildList.Id)
            .ToListAsync(cancellationToken);
        var skusById = await LoadSkusAsync(storedItems.Select(item => item.SkuId), cancellationToken);

        foreach (var item in storedItems)
        {
            var sku = skusById[item.SkuId];
            if (sku.Status != SkuStatus.Published)
            {
                throw new BuildWriteException(
                    BuildWriteException.ErrorCodes.BuildUnavailableItem,
                    $"SKU '{sku.PublicId}' is no longer available for purchase.");
            }
        }

        // 加入購物車前重新檢查: never trust BuildList.CompatibilityStatus — recompute live, and
        // treat InsufficientData the same as Blocked (see the SharedBuildDto.CanAddToCart note).
        var mergedItems = storedItems
            .Select(item => new BuildItemInput(skusById[item.SkuId].PublicId, item.Quantity))
            .ToList();

        var catalogResult = await _catalogReader.ReadAsync(
            mergedItems.Select(item => new CompatibilityItemReference(item.SkuPublicId, item.Quantity)).ToArray(),
            cancellationToken);
        var presentCategoryCodes = catalogResult.Components.Select(component => component.CategoryCode).ToHashSet();
        var missingCategoryCodes = RequiredComponentCategoryCodes
            .Where(categoryCode => !presentCategoryCodes.Contains(categoryCode))
            .ToList();
        if (missingCategoryCodes.Count > 0)
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.BuildIncomplete,
                $"This build is missing required component(s): {string.Join(", ", missingCategoryCodes)}.");
        }

        var compatibility = await _compatibilityCheckService.CheckAsync(
            new CompatibilityCheckRequest(mergedItems), buildList.Id, cancellationToken);
        // 組長 PR #34 review: Overall never reflects a ruleDisabled finding by design
        // (EfCompatibilityCheckService.ApplyDisabledRules keeps the rollup at whatever the
        // remaining active findings say) — that is fine for the admin test tool, but the real
        // purchase flow may never let a hard rule an admin turned off silently read as
        // "compatible". A disabled rule that would have fired blocks the add here too.
        if (compatibility.Overall is "blocked" or "insufficientData" ||
            compatibility.Results.Any(finding => finding.Severity == CompatibilitySeverityTokens.RuleDisabled))
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.BuildIncompatible,
                $"This build is not ready to add to cart (compatibility overall: {compatibility.Overall}).");
        }

        var skuIds = storedItems.Select(item => item.SkuId).Distinct().ToArray();
        var availableQuantityBySkuId = await _dbContext.InventoryBalances.AsNoTracking()
            .Where(balance => skuIds.Contains(balance.SkuId))
            .ToDictionaryAsync(balance => balance.SkuId, balance => balance.AvailableQuantity, cancellationToken);

        foreach (var item in storedItems)
        {
            var requiredTotal = item.Quantity * request.Quantity;
            var availableQuantity = availableQuantityBySkuId.GetValueOrDefault(item.SkuId, 0);
            if (availableQuantity < requiredTotal)
            {
                var sku = skusById[item.SkuId];
                throw new BuildWriteException(
                    BuildWriteException.ErrorCodes.InventoryInsufficient,
                    $"SKU '{sku.PublicId}' has insufficient available stock ({availableQuantity} < {requiredTotal} required).");
            }
        }

        buildList.RecordCompatibility(TokenToOverall(compatibility.Overall), now);
        await SaveWithConcurrencyCheckAsync(cancellationToken);

        var perUnitItems = storedItems
            .Select(item => new AssemblyGroupItemInput(skusById[item.SkuId].PublicId, item.Quantity))
            .ToList();
        var cartDto = await _cartService.AddAssemblyGroupsAsync(
            new CartIdentity(memberUserId, null), perUnitItems, request.Quantity, cancellationToken);

        return new IdempotencyResponse<CartDto>(
            StatusCode: 200, Body: cartDto, ResponseSummary: JsonSerializer.Serialize(cartDto));
    }

    private async Task RevokeActiveSharesAsync(long buildListId, DateTime now, CancellationToken cancellationToken)
    {
        var activeTokens = await _dbContext.BuildShareTokens
            .Where(token => token.BuildListId == buildListId && token.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var token in activeTokens)
        {
            token.Revoke(now);
        }
    }

    private static BuildWriteException NotFoundShare() => new(
        BuildWriteException.ErrorCodes.ResourceNotFound,
        "The share link was not found or is no longer valid.");

    private static (string RawToken, byte[] Hash) GenerateShareToken()
    {
        var rawToken = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (rawToken, HashShareToken(rawToken));
    }

    private static byte[] HashShareToken(string rawToken) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<Dictionary<long, decimal>> LoadEffectivePricesAsync(
        IReadOnlyList<long> skuIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var listPriceBySkuId = await _dbContext.Skus.AsNoTracking()
            .Where(sku => skuIds.Contains(sku.Id))
            .ToDictionaryAsync(sku => sku.Id, sku => sku.ListPrice, cancellationToken);

        var salePriceBySkuId = await _dbContext.SalePrices.AsNoTracking()
            .Where(salePrice => skuIds.Contains(salePrice.SkuId) &&
                salePrice.Status == SalePriceStatus.Active &&
                salePrice.StartsAtUtc <= now &&
                salePrice.EndsAtUtc > now)
            .GroupBy(salePrice => salePrice.SkuId)
            .Select(group => new
            {
                SkuId = group.Key,
                Price = group.OrderByDescending(salePrice => salePrice.StartsAtUtc).First().Price,
            })
            .ToDictionaryAsync(row => row.SkuId, row => row.Price, cancellationToken);

        foreach (var (skuId, price) in salePriceBySkuId)
        {
            listPriceBySkuId[skuId] = price;
        }

        return listPriceBySkuId;
    }

    private static string AvailabilityToken(Sku sku, int quantity, int? availableQuantity)
    {
        if (sku.Status != SkuStatus.Published)
        {
            return "unavailable";
        }

        if (availableQuantity is null || availableQuantity.Value < quantity)
        {
            return "insufficient_stock";
        }

        return "available";
    }

    private static string OverallToToken(CompatibilityOverall overall) => overall switch
    {
        CompatibilityOverall.Compatible => "compatible",
        CompatibilityOverall.Warning => "warning",
        CompatibilityOverall.Blocked => "blocked",
        CompatibilityOverall.InsufficientData => "insufficientData",
        _ => throw new ArgumentOutOfRangeException(nameof(overall)),
    };

    private static CompatibilityOverall TokenToOverall(string token) => token switch
    {
        "compatible" => CompatibilityOverall.Compatible,
        "warning" => CompatibilityOverall.Warning,
        "blocked" => CompatibilityOverall.Blocked,
        "insufficientData" => CompatibilityOverall.InsufficientData,
        _ => throw new ArgumentOutOfRangeException(nameof(token)),
    };

    private async Task SaveWithConcurrencyCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ConcurrencyConflict,
                "The build list was updated by someone else. Reload and try again.");
        }
    }
}
