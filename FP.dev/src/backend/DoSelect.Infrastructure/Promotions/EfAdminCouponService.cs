using System.Data;
using System.Globalization;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Promotions;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Promotions;
using DoSelect.Infrastructure.Catalog;
using DoSelect.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Promotions;

/// <summary>
/// 後台優惠券的查詢與寫入（`/api/v1/admin/coupons*`）。
/// </summary>
/// <remarks>
/// 狀態轉移一律交給 <see cref="Coupon"/>，本類別不自行判斷任何生命週期條件；
/// Entity 丟出的 <see cref="InvalidOperationException"/> 在此統一映射為
/// <c>coupon_state_conflict</c>。使用量沿用 <see cref="CouponRuleReader.OccupiesUsageSeatAt"/>，
/// 後台看到的名額與試算引擎採用的必定是同一個定義。
/// </remarks>
public sealed class EfAdminCouponService : IAdminCouponService
{
    private const string CouponCodeIndexName = "UX_Coupons_Code";
    private const int DeadlockVictimErrorNumber = 1205;
    private const int MaximumDeadlockRetries = 1;

    /// <summary>`Coupon.Manage` 的合法角色（DEC-P284）。</summary>
    private static readonly string[] CouponManageRoles =
    [
        AuditRoleNames.FinanceManager,
        AuditRoleNames.MarketingAnalyst,
        AuditRoleNames.SuperAdmin,
    ];

    private readonly DoSelectDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditWriter _auditWriter;

    public EfAdminCouponService(
        DoSelectDbContext context,
        TimeProvider timeProvider,
        IAuditWriter auditWriter)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(auditWriter);

        _context = context;
        _timeProvider = timeProvider;
        _auditWriter = auditWriter;
    }

    public async Task<PageResult<CouponDto>> ListAsync(
        AdminCouponQuery query,
        CancellationToken cancellationToken = default)
    {
        AdminCouponQueryValidator.RequireValid(query);

        var coupons = _context.Coupons.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            coupons = coupons.Where(coupon =>
                coupon.Code.Contains(term) || coupon.NameZhTw.Contains(term));
        }

        if (query.Statuses is { Count: > 0 })
        {
            var statuses = query.Statuses.Distinct().ToArray();
            coupons = coupons.Where(coupon => statuses.Contains(coupon.Status));
        }

        var totalCount = await coupons.CountAsync(cancellationToken);

        // 排序鍵一律加上 Id 收尾：Code 以外的鍵都可能重複，沒有決勝鍵時
        // SQL Server 不保證跨頁穩定，同一筆會在兩頁出現或完全消失。
        coupons = query.Sort switch
        {
            AdminCouponSortOptions.UpdatedAsc =>
                coupons.OrderBy(coupon => coupon.UpdatedAtUtc).ThenBy(coupon => coupon.Id),
            AdminCouponSortOptions.CodeAsc =>
                coupons.OrderBy(coupon => coupon.Code).ThenBy(coupon => coupon.Id),
            AdminCouponSortOptions.CodeDesc =>
                coupons.OrderByDescending(coupon => coupon.Code).ThenBy(coupon => coupon.Id),
            AdminCouponSortOptions.EndsAtAsc =>
                coupons.OrderBy(coupon => coupon.EndsAtUtc).ThenBy(coupon => coupon.Id),
            _ =>
                coupons.OrderByDescending(coupon => coupon.UpdatedAtUtc)
                    .ThenByDescending(coupon => coupon.Id),
        };

        var page = await coupons
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);

        var items = await BuildDtosAsync(page, cancellationToken);
        return new PageResult<CouponDto>(items, query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<CouponDto?> FindByPublicIdAsync(
        Guid publicId,
        CancellationToken cancellationToken = default)
    {
        var coupon = await _context.Coupons
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.PublicId == publicId, cancellationToken);

        if (coupon is null)
        {
            return null;
        }

        var dtos = await BuildDtosAsync([coupon], cancellationToken);
        return dtos[0];
    }

    public async Task<CouponDto> CreateAsync(
        CreateCouponRequest request,
        AdminCouponActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);
        AdminCouponQueryValidator.RequireValidRule(
            request.DiscountType,
            request.DiscountValue,
            request.MaximumDiscount,
            request.ScopeType,
            request.CategoryPublicIds,
            request.ProductPublicIds,
            request.ExcludedProductPublicIds);

        // 最終Schema「範圍規則」：驗證與寫入需於同一 Transaction 完成。
        // 範圍解析（PublicId → 內部主鍵）也是驗證的一部分，因此一併納入。
        var publicIdForReload = await InSerializableTransactionAsync(
            async token =>
            {
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                var categoryIds = await ResolveCategoryIdsAsync(request.CategoryPublicIds, token);
                var productIds = await ResolveProductIdsAsync(request.ProductPublicIds, token);
                var excludedProductIds =
                    await ResolveProductIdsAsync(request.ExcludedProductPublicIds, token);

                Coupon coupon;
                try
                {
                    coupon = new Coupon(
                        Guid.NewGuid(),
                        new CouponCreation(
                            request.Code,
                            request.NameZhTw,
                            request.DiscountType,
                            request.DiscountValue,
                            request.MinimumSpend,
                            request.MaximumDiscount,
                            RequireUtc(request.StartsAtUtc, "startsAtUtc"),
                            RequireUtc(request.EndsAtUtc, "endsAtUtc"),
                            request.TotalUsageLimit,
                            request.PerMemberLimit,
                            request.MemberOnly,
                            request.ExcludeSaleItems,
                            request.ScopeType),
                        now);
                }
                catch (ArgumentException exception)
                {
                    throw DomainProblemException.Validation(exception.Message);
                }

                // 先查一次是為了在正常情況給出明確的 409；真正的保證仍是
                // UX_Coupons_Code，唯一索引違反在下面被翻譯成同一個錯誤碼。
                var normalizedCode = coupon.Code;
                if (await _context.Coupons.AnyAsync(
                        candidate => candidate.Code == normalizedCode, token))
                {
                    throw DuplicateCode(normalizedCode);
                }

                var auditActor = await ResolveActorAsync(actor.AdminUserId, token);

                try
                {
                    _context.Coupons.Add(coupon);
                    await _context.SaveChangesAsync(token);

                    AddScope(coupon.Id, categoryIds, productIds, excludedProductIds, now);
                    WriteAudit(
                        coupon,
                        AuditActions.CouponCreate,
                        auditActor,
                        actor,
                        CouponAuditFields.CreateReasonCode,
                        note: null,
                        [
                            AuditFieldChange.Code(
                                CouponAuditFields.Status, null, coupon.Status.ToString()),
                            AuditFieldChange.Code(
                                CouponAuditFields.RuleVersion,
                                null,
                                coupon.RuleVersion.ToString(CultureInfo.InvariantCulture)),
                        ]);
                    await _context.SaveChangesAsync(token);
                }
                // 只翻譯 UX_Coupons_Code；其他唯一索引或連線失敗照原樣往上拋，
                // 不能被誤標成優惠碼重複。
                catch (DbUpdateException exception)
                    when (SqlUniqueIndexViolations.Matches(exception, CouponCodeIndexName))
                {
                    throw DuplicateCode(normalizedCode);
                }

                return coupon.PublicId;
            },
            cancellationToken);

        return (await FindByPublicIdAsync(publicIdForReload, cancellationToken))!;
    }

    public async Task<CouponDto> UpdateAsync(
        Guid publicId,
        UpdateCouponRequest request,
        AdminCouponActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);
        AdminCouponQueryValidator.RequireValidRule(
            request.DiscountType,
            request.DiscountValue,
            request.MaximumDiscount,
            request.ScopeType,
            request.CategoryPublicIds,
            request.ProductPublicIds,
            request.ExcludedProductPublicIds);

        var publicIdForReload = await InSerializableTransactionAsync(
            async token =>
            {
                var coupon = await _context.Coupons
                    .SingleOrDefaultAsync(candidate => candidate.PublicId == publicId, token)
                    ?? throw DomainProblemException.NotFound($"Coupon '{publicId}' was not found.");

                RequireCurrentRowVersion(request.RowVersion, coupon.RowVersion);

                var now = _timeProvider.GetUtcNow().UtcDateTime;

                // 這一筆讀取必須與寫入同交易。`Coupon` 的 RowVersion 攔不到它 ——
                // Checkout 新增 CouponRedemption 不會動到 Coupons 那一列，所以在
                // ReadCommitted 下「查完沒有 Redemption」到「寫入新 Code」之間
                // 插進來的一筆保留，會讓已凍結的優惠碼被改掉。
                var hasRedemptions = await _context.CouponRedemptions
                    .AnyAsync(redemption => redemption.CouponId == coupon.Id, token);

                var categoryIds = await ResolveCategoryIdsAsync(request.CategoryPublicIds, token);
                var productIds = await ResolveProductIdsAsync(request.ProductPublicIds, token);
                var excludedProductIds =
                    await ResolveProductIdsAsync(request.ExcludedProductPublicIds, token);

                var scopeChanged = await ScopeDiffersAsync(
                    coupon.Id, categoryIds, productIds, excludedProductIds, token);

                _context.Entry(coupon).Property(entity => entity.RowVersion).OriginalValue =
                    request.RowVersion;

                var auditActor = await ResolveActorAsync(actor.AdminUserId, token);

                CouponRuleChange change;
                try
                {
                    change = coupon.UpdateRules(
                        new CouponRuleRevision(
                            request.Code,
                            request.NameZhTw,
                            request.DiscountType,
                            request.DiscountValue,
                            request.MinimumSpend,
                            request.MaximumDiscount,
                            RequireUtc(request.StartsAtUtc, "startsAtUtc"),
                            RequireUtc(request.EndsAtUtc, "endsAtUtc"),
                            request.TotalUsageLimit,
                            request.PerMemberLimit,
                            request.MemberOnly,
                            request.ExcludeSaleItems,
                            request.ScopeType),
                        hasRedemptions,
                        scopeChanged,
                        now);
                }
                catch (InvalidOperationException exception)
                {
                    throw StateConflict(exception.Message);
                }
                catch (ArgumentException exception)
                {
                    throw DomainProblemException.Validation(exception.Message);
                }

                if (scopeChanged)
                {
                    await ReplaceScopeAsync(
                        coupon.Id, categoryIds, productIds, excludedProductIds, now, token);
                }

                // 沒有任何變動時不寫稽核：一筆「什麼都沒改」的紀錄只會稀釋
                // 真正的異動，讓事後追查更難。
                if (change.HasChanges)
                {
                    WriteAudit(
                        coupon,
                        AuditActions.CouponUpdate,
                        auditActor,
                        actor,
                        CouponAuditFields.UpdateReasonCode,
                        note: null,
                        [
                            AuditFieldChange.Code(
                                CouponAuditFields.RuleVersion,
                                null,
                                coupon.RuleVersion.ToString(CultureInfo.InvariantCulture)),
                            AuditFieldChange.Code(
                                CouponAuditFields.ChangedFields,
                                null,
                                CouponAuditFields.Describe(change.ChangedFields)),
                        ]);
                }

                await SaveWithConflictMappingAsync(coupon.Code, token);
                return coupon.PublicId;
            },
            cancellationToken);

        return (await FindByPublicIdAsync(publicIdForReload, cancellationToken))!;
    }

    /// <summary>
    /// 執行 <c>activate</c>／<c>pause</c>／<c>disable</c>。
    /// </summary>
    /// <remarks>
    /// 依 DEC-P289，執行理由只寫中央 Audit，不在 <see cref="Coupon"/> 新增欄位。
    /// <c>ReasonCode</c> 與 <c>Note</c> 分別以 <c>reason</c>／<c>note</c> 兩個獨立參數
    /// 傳給 <c>IAuditWriter</c>，與狀態變更同一交易提交 —— 不得串接成單一字串，
    /// <c>reason</c> 只收 safe-code。
    /// </remarks>
    public async Task<CouponDto> ExecuteActionAsync(
        Guid publicId,
        string action,
        CouponActionRequest request,
        AdminCouponActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);
        if (!AdminCouponActions.IsAllowed(action))
        {
            throw DomainProblemException.NotFound($"Action '{action}' is not supported.");
        }

        var publicIdForReload = await InSerializableTransactionAsync(
            async token =>
            {
                var coupon = await _context.Coupons
                    .SingleOrDefaultAsync(candidate => candidate.PublicId == publicId, token)
                    ?? throw DomainProblemException.NotFound($"Coupon '{publicId}' was not found.");

                RequireCurrentRowVersion(request.RowVersion, coupon.RowVersion);

                var now = _timeProvider.GetUtcNow().UtcDateTime;
                var auditActor = await ResolveActorAsync(actor.AdminUserId, token);
                var statusBefore = coupon.Status;

                _context.Entry(coupon).Property(entity => entity.RowVersion).OriginalValue =
                    request.RowVersion;

                var normalizedAction = action.Trim();
                try
                {
                    switch (normalizedAction)
                    {
                        case AdminCouponActions.Activate:
                            await ActivateAsync(coupon, now, token);
                            break;
                        case AdminCouponActions.Pause:
                            coupon.Pause(now);
                            break;
                        case AdminCouponActions.Disable:
                            coupon.Disable(now);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(action));
                    }
                }
                catch (InvalidOperationException exception)
                {
                    throw StateConflict(exception.Message);
                }

                WriteAudit(
                    coupon,
                    ActionAuditName(normalizedAction),
                    auditActor,
                    actor,
                    request.ReasonCode.Trim(),
                    request.Note,
                    [
                        AuditFieldChange.Code(
                            CouponAuditFields.Status,
                            statusBefore.ToString(),
                            coupon.Status.ToString()),
                    ]);

                await SaveWithConflictMappingAsync(coupon.Code, token);
                return coupon.PublicId;
            },
            cancellationToken);

        return (await FindByPublicIdAsync(publicIdForReload, cancellationToken))!;
    }

    /// <summary>
    /// 把管理員的 `activate` 動作分派到 Entity 上正確的那一條轉移。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 權威狀態機（<c>狀態機設計.md</c>「優惠券狀態」）規定
    /// **`activate` 只接受 `Draft` 或符合條件的 `Paused`**。
    /// </para>
    /// <para>
    /// <c>Scheduled → Active</c>（到達開始時間）與 <c>Exhausted → Active</c>
    /// （名額返還）是**系統事件**，不是管理員操作。讓管理員的 `activate` 去消耗它們，
    /// 會把兩個不同來源的轉移都記成 <c>coupon.activate</c>，稽核就再也分不出
    /// 「誰讓這張券重新生效」。因此這兩個狀態一律回 <c>coupon_state_conflict</c>，
    /// 正式的排程與名額返還路徑仍各自呼叫 <see cref="Coupon.ActivateScheduled"/>
    /// 與 <see cref="Coupon.ReactivateAfterQuotaRelease"/>。
    /// </para>
    /// <para>
    /// 尚未進入有效期間的 `Draft` 走 <see cref="Coupon.ScheduleForLaterStart"/>
    /// 進入 `Scheduled`（alex 已裁定的 B1）。這也是 `Scheduled` 的唯一進入路徑 ——
    /// 先前註解寫「白名單沒有 schedule，所以 Scheduled 會是 API 到不了的狀態」，
    /// 那個理由不成立。
    /// </para>
    /// </remarks>
    private async Task ActivateAsync(Coupon coupon, DateTime now, CancellationToken cancellationToken)
    {
        var usage = await GetUsageStateAsync(coupon.Id, now, cancellationToken);

        switch (coupon.Status)
        {
            case CouponStatus.Draft when !coupon.IsWithinUsagePeriod(now):
                coupon.ScheduleForLaterStart(now);
                break;
            case CouponStatus.Draft:
            case CouponStatus.Paused:
                coupon.ActivateNow(usage, now);
                break;
            default:
                throw StateConflict(
                    $"A {coupon.Status} coupon cannot be activated by an administrator; " +
                    "activate only accepts Draft or an eligible Paused coupon.");
        }
    }

    private async Task<CouponUsageState> GetUsageStateAsync(
        long couponId,
        DateTime evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        var totalRedeemedCount = await _context.CouponRedemptions
            .AsNoTracking()
            .Where(redemption => redemption.CouponId == couponId)
            .Where(CouponRuleReader.OccupiesUsageSeatAt(evaluatedAtUtc))
            .CountAsync(cancellationToken);

        // 後台沒有「某一位會員」的概念，每人限額在此不適用，固定 0。
        return new CouponUsageState(totalRedeemedCount, 0);
    }

    private async Task<IReadOnlyList<CouponDto>> BuildDtosAsync(
        IReadOnlyList<Coupon> coupons,
        CancellationToken cancellationToken)
    {
        if (coupons.Count == 0)
        {
            return [];
        }

        var couponIds = coupons.Select(coupon => coupon.Id).ToArray();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // 每一種關聯各一次查詢，總共固定五次，不隨頁面筆數增加。
        var categories = await _context.CouponCategories
            .AsNoTracking()
            .Where(link => couponIds.Contains(link.CouponId))
            .Join(
                _context.Categories.AsNoTracking(),
                link => link.CategoryId,
                category => category.Id,
                (link, category) => new { link.CouponId, category.PublicId })
            .ToArrayAsync(cancellationToken);

        var products = await _context.CouponProducts
            .AsNoTracking()
            .Where(link => couponIds.Contains(link.CouponId))
            .Join(
                _context.Products.AsNoTracking(),
                link => link.ProductId,
                product => product.Id,
                (link, product) => new { link.CouponId, product.PublicId })
            .ToArrayAsync(cancellationToken);

        var excludedProducts = await _context.CouponExcludedProducts
            .AsNoTracking()
            .Where(link => couponIds.Contains(link.CouponId))
            .Join(
                _context.Products.AsNoTracking(),
                link => link.ProductId,
                product => product.Id,
                (link, product) => new { link.CouponId, product.PublicId })
            .ToArrayAsync(cancellationToken);

        var usage = await _context.CouponRedemptions
            .AsNoTracking()
            .Where(redemption => couponIds.Contains(redemption.CouponId))
            .Where(CouponRuleReader.OccupiesUsageSeatAt(now))
            .GroupBy(redemption => redemption.CouponId)
            .Select(group => new { CouponId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.CouponId, entry => entry.Count, cancellationToken);

        return coupons
            .Select(coupon => ToDto(
                coupon,
                categories.Where(entry => entry.CouponId == coupon.Id)
                    .Select(entry => entry.PublicId).ToArray(),
                products.Where(entry => entry.CouponId == coupon.Id)
                    .Select(entry => entry.PublicId).ToArray(),
                excludedProducts.Where(entry => entry.CouponId == coupon.Id)
                    .Select(entry => entry.PublicId).ToArray(),
                usage.GetValueOrDefault(coupon.Id)))
            .ToArray();
    }

    private static CouponDto ToDto(
        Coupon coupon,
        IReadOnlyList<Guid> categoryPublicIds,
        IReadOnlyList<Guid> productPublicIds,
        IReadOnlyList<Guid> excludedProductPublicIds,
        int totalRedeemedCount) =>
        new(
            coupon.PublicId,
            coupon.Code,
            coupon.NameZhTw,
            coupon.DiscountType,
            coupon.Status,
            coupon.DiscountValue,
            coupon.MinimumSpend,
            coupon.MaximumDiscount,
            AsUtc(coupon.StartsAtUtc),
            AsUtc(coupon.EndsAtUtc),
            coupon.MemberOnly,
            coupon.ExcludeSaleItems,
            new CouponScopeDto(
                coupon.ScopeType,
                categoryPublicIds,
                productPublicIds,
                excludedProductPublicIds),
            new CouponUsageDto(
                totalRedeemedCount,
                coupon.TotalUsageLimit,
                coupon.PerMemberLimit,
                // 無總量上限時是「不限量」，不是「剩 0 張」。
                coupon.TotalUsageLimit is { } limit
                    ? Math.Max(limit - totalRedeemedCount, 0)
                    : null),
            coupon.RuleVersion,
            AsUtc(coupon.CreatedAtUtc),
            AsUtc(coupon.UpdatedAtUtc),
            coupon.RowVersion);

    /// <summary>
    /// 把 SQL Server 讀回來的 <c>datetime2</c> 標記為 UTC。
    /// </summary>
    /// <remarks>
    /// SQL Server 的 <c>datetime2</c> 不保存時區，EF 讀回來一律是
    /// <see cref="DateTimeKind.Unspecified"/>。不標記會有兩個實際後果：
    /// System.Text.Json 序列化時不會加上 <c>Z</c>，客戶端只能猜；而把同一個值原樣
    /// 送回 <c>PUT</c> 時，會被本服務自己的 UTC 檢查擋成 400。
    /// 資料庫存的本來就是 UTC（所有 Entity 的建構子都以 <c>RequireUtc</c> 把關），
    /// 這裡只是把已經成立的事實重新標上。
    /// </remarks>
    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private void AddScope(
        long couponId,
        IReadOnlyList<long> categoryIds,
        IReadOnlyList<long> productIds,
        IReadOnlyList<long> excludedProductIds,
        DateTime now)
    {
        _context.CouponCategories.AddRange(
            categoryIds.Select(categoryId => new CouponCategory(couponId, categoryId, now)));
        _context.CouponProducts.AddRange(
            productIds.Select(productId => new CouponProduct(couponId, productId, now)));
        _context.CouponExcludedProducts.AddRange(
            excludedProductIds.Select(
                productId => new CouponExcludedProduct(couponId, productId, now)));
    }

    private async Task ReplaceScopeAsync(
        long couponId,
        IReadOnlyList<long> categoryIds,
        IReadOnlyList<long> productIds,
        IReadOnlyList<long> excludedProductIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        _context.CouponCategories.RemoveRange(
            await _context.CouponCategories
                .Where(link => link.CouponId == couponId)
                .ToArrayAsync(cancellationToken));
        _context.CouponProducts.RemoveRange(
            await _context.CouponProducts
                .Where(link => link.CouponId == couponId)
                .ToArrayAsync(cancellationToken));
        _context.CouponExcludedProducts.RemoveRange(
            await _context.CouponExcludedProducts
                .Where(link => link.CouponId == couponId)
                .ToArrayAsync(cancellationToken));

        AddScope(couponId, categoryIds, productIds, excludedProductIds, now);
    }

    private async Task<IReadOnlyList<long>> ResolveCategoryIdsAsync(
        IReadOnlyList<Guid>? publicIds,
        CancellationToken cancellationToken)
    {
        if (publicIds is null or { Count: 0 })
        {
            return [];
        }

        var distinct = publicIds.Distinct().ToArray();
        var ids = await _context.Categories
            .AsNoTracking()
            .Where(category => distinct.Contains(category.PublicId))
            .Select(category => category.Id)
            .ToArrayAsync(cancellationToken);

        // 少一筆就代表管理員送了不存在的分類。靜默略過會產出一張適用範圍
        // 與送出內容不同的券，而畫面上看不出來。
        if (ids.Length != distinct.Length)
        {
            throw DomainProblemException.Validation(
                "categoryPublicIds contains an unknown category.");
        }

        return ids;
    }

    private async Task<IReadOnlyList<long>> ResolveProductIdsAsync(
        IReadOnlyList<Guid>? publicIds,
        CancellationToken cancellationToken)
    {
        if (publicIds is null or { Count: 0 })
        {
            return [];
        }

        var distinct = publicIds.Distinct().ToArray();
        var ids = await _context.Products
            .AsNoTracking()
            .Where(product => distinct.Contains(product.PublicId))
            .Select(product => product.Id)
            .ToArrayAsync(cancellationToken);

        if (ids.Length != distinct.Length)
        {
            throw DomainProblemException.Validation(
                "productPublicIds contains an unknown product.");
        }

        return ids;
    }

    private async Task SaveWithConflictMappingAsync(
        string code,
        CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict,
                "The coupon was updated by someone else. Reload and try again.");
        }
        catch (DbUpdateException exception)
            when (SqlUniqueIndexViolations.Matches(exception, CouponCodeIndexName))
        {
            throw DuplicateCode(code);
        }
    }

    /// <summary>
    /// 在一個 <see cref="IsolationLevel.Serializable"/> 交易內執行 <paramref name="work"/>，
    /// 死結受害者重跑一次。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 最終Schema「範圍規則」要求「驗證與寫入需於同一 Transaction 完成」。Serializable
    /// 是必要的而非保守：`hasRedemptions` 與名額計數都是**範圍查詢**，在
    /// <see cref="IsolationLevel.ReadCommitted"/> 下沒有任何防護，讀完之後仍可能被
    /// 插入新的 <c>CouponRedemption</c>。而新增 Redemption 不會更新 <c>Coupons</c>
    /// 那一列，所以 Coupon 的 RowVersion 攔不到這個競爭。
    /// </para>
    /// <para>
    /// 只重試 SQL Server 死結（1205）。<see cref="DbUpdateConcurrencyException"/>
    /// **不重試** —— 那代表呼叫端持有的 RowVersion 已經過期，重跑只會再失敗一次，
    /// 正確行為是回 <c>concurrency_conflict</c> 讓對方重新載入。
    /// </para>
    /// </remarks>
    private async Task<T> InSerializableTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var result = await work(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception exception)
            {
                await transaction.RollbackAsync(CancellationToken.None);

                if (attempt >= MaximumDeadlockRetries || !IsDeadlockVictim(exception))
                {
                    throw;
                }

                // 重跑前必須丟掉追蹤狀態，否則第二次會沿用上一輪已被回滾的修改。
                _context.ChangeTracker.Clear();
            }
        }
    }

    private static bool IsDeadlockVictim(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: DeadlockVictimErrorNumber })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 比對呼叫端持有的版本與資料庫目前版本。
    /// </summary>
    /// <remarks>
    /// EF 的 <c>OriginalValue</c> 只在該實體真的被修改、實際發出 UPDATE 時才會比對。
    /// 一次「只改適用範圍」或完全沒有變動的請求不會修改 <c>Coupons</c> 那一列，
    /// 樂觀鎖因此**從未執行**。這個前置檢查讓過期版本在任何情況下都被擋下。
    /// </remarks>
    private static void RequireCurrentRowVersion(byte[]? presented, byte[]? current)
    {
        if (presented is null || current is null ||
            !presented.AsSpan().SequenceEqual(current))
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict,
                "The coupon was updated by someone else. Reload and try again.");
        }
    }

    /// <summary>
    /// 三個範圍集合是否與資料庫目前保存的不同（集合語意，與順序無關）。
    /// </summary>
    private async Task<bool> ScopeDiffersAsync(
        long couponId,
        IReadOnlyList<long> categoryIds,
        IReadOnlyList<long> productIds,
        IReadOnlyList<long> excludedProductIds,
        CancellationToken cancellationToken)
    {
        var currentCategoryIds = await _context.CouponCategories
            .AsNoTracking()
            .Where(link => link.CouponId == couponId)
            .Select(link => link.CategoryId)
            .ToArrayAsync(cancellationToken);
        var currentProductIds = await _context.CouponProducts
            .AsNoTracking()
            .Where(link => link.CouponId == couponId)
            .Select(link => link.ProductId)
            .ToArrayAsync(cancellationToken);
        var currentExcludedProductIds = await _context.CouponExcludedProducts
            .AsNoTracking()
            .Where(link => link.CouponId == couponId)
            .Select(link => link.ProductId)
            .ToArrayAsync(cancellationToken);

        return !currentCategoryIds.ToHashSet().SetEquals(categoryIds) ||
            !currentProductIds.ToHashSet().SetEquals(productIds) ||
            !currentExcludedProductIds.ToHashSet().SetEquals(excludedProductIds);
    }

    /// <summary>
    /// 在同一交易內把 Identity 的內部 Id 換成管理員 <c>PublicId</c> 與角色快照，
    /// 並重新確認他**執行當下**仍具備 `Coupon.Manage` 的角色。
    /// </summary>
    /// <remarks>
    /// Policy 在請求進入時已經檢查過一次，這裡是第二次：Token 可能簽發於角色被撤銷之前。
    /// 沿用 <c>InvoiceAllowanceWriter</c> 的既有做法，沒有另建一套。
    /// </remarks>
    private async Task<AuditActor> ResolveActorAsync(
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var admin = await _context.Users.AsNoTracking()
            .Where(user => user.Id == adminUserId && user.AccountType == AccountType.Admin)
            .Select(user => new { user.Id, user.PublicId })
            .SingleOrDefaultAsync(cancellationToken);
        if (admin is null)
        {
            throw DomainProblemException.Forbidden("The administrator identity is invalid.");
        }

        var roles = await (
            from userRole in _context.UserRoles.AsNoTracking()
            join role in _context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == admin.Id && role.Name != null
            select role.Name!)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (!roles.Intersect(CouponManageRoles, StringComparer.Ordinal).Any())
        {
            throw DomainProblemException.Forbidden(
                "The administrator no longer has permission to manage coupons.");
        }

        return AuditActor.Create(AuditActorType.Admin, admin.PublicId, roles);
    }

    /// <summary>
    /// 把一筆稽核加入目前的 <see cref="DoSelectDbContext"/>。
    /// </summary>
    /// <remarks>
    /// <see cref="IAuditWriter.Add"/> 只是把實體加入追蹤，實際寫入由呼叫端接下來的
    /// <c>SaveChangesAsync</c> 完成。因此稽核與優惠券、範圍變更在**同一次**
    /// SaveChanges、同一個 Serializable 交易內提交；稽核建構失敗（例如
    /// <c>note</c> 不合規）會在寫入任何資料前就丟出，整筆交易回滾。
    /// </remarks>
    private void WriteAudit(
        Coupon coupon,
        string action,
        AuditActor auditActor,
        AdminCouponActorContext actor,
        string reasonCode,
        string? note,
        IReadOnlyCollection<AuditFieldChange> changes)
    {
        AuditWriteRequest request;
        try
        {
            request = AuditWriteRequest.Create(
                Guid.NewGuid(),
                auditActor,
                action,
                AuditResourceTypes.Coupon,
                coupon.PublicId,
                AuditResult.Success,
                errorCode: null,
                changes,
                reasonCode,
                actor.CorrelationId,
                actor.TraceId,
                jobPublicId: null,
                actor.RemoteIpAddress,
                note);
        }
        // `reasonCode` 與 `note` 直接來自呼叫端，而中央 Audit 對兩者都有格式限制
        // （safe-code、長度上限、禁用字元與敏感詞）。不接住就會變成 500 ——
        // 呼叫端送了一個格式不合的理由，卻看到「伺服器錯誤」。
        // 這裡刻意不複製那些規則：共用契約是唯一判準，另寫一份必然漂移。
        catch (ArgumentException exception)
        {
            throw DomainProblemException.Validation(exception.Message);
        }

        _auditWriter.Add(request);
    }

    private static string ActionAuditName(string action) => action switch
    {
        AdminCouponActions.Activate => AuditActions.CouponActivate,
        AdminCouponActions.Pause => AuditActions.CouponPause,
        AdminCouponActions.Disable => AuditActions.CouponDisable,
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    private static DateTime RequireUtc(DateTime value, string field) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw DomainProblemException.Validation($"{field} must use UTC.");

    private static DomainProblemException DuplicateCode(string code) =>
        DomainProblemException.Conflict(
            CouponCalculationErrorCodes.CouponCodeDuplicate,
            $"Coupon code '{code}' already exists.");

    private static DomainProblemException StateConflict(string message) =>
        DomainProblemException.Conflict(
            CouponCalculationErrorCodes.CouponStateConflict,
            message);
}
