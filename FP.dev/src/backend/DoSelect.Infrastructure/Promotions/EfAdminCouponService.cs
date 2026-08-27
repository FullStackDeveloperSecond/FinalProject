using DoSelect.Application.Common;
using DoSelect.Application.Promotions;
using DoSelect.Domain.Promotions;
using DoSelect.Infrastructure.Catalog;
using DoSelect.Infrastructure.Persistence;
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

    private readonly DoSelectDbContext _context;
    private readonly TimeProvider _timeProvider;

    public EfAdminCouponService(DoSelectDbContext context, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _context = context;
        _timeProvider = timeProvider;
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AdminCouponQueryValidator.RequireValidRule(
            request.DiscountType,
            request.DiscountValue,
            request.MaximumDiscount,
            request.ScopeType,
            request.CategoryPublicIds,
            request.ProductPublicIds,
            request.ExcludedProductPublicIds);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var categoryIds = await ResolveCategoryIdsAsync(request.CategoryPublicIds, cancellationToken);
        var productIds = await ResolveProductIdsAsync(request.ProductPublicIds, cancellationToken);
        var excludedProductIds =
            await ResolveProductIdsAsync(request.ExcludedProductPublicIds, cancellationToken);

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

        // 先查一次是為了在正常情況給出明確的 409；真正的保證是 UX_Coupons_Code，
        // 兩個並行建立都可能通過這個 SELECT，輸的那個由下方的唯一索引接住。
        var normalizedCode = coupon.Code;
        if (await _context.Coupons.AnyAsync(
                candidate => candidate.Code == normalizedCode, cancellationToken))
        {
            throw DuplicateCode(normalizedCode);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _context.Coupons.Add(coupon);
            await _context.SaveChangesAsync(cancellationToken);

            AddScope(coupon.Id, categoryIds, productIds, excludedProductIds, now);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);

            // 只翻譯 UX_Coupons_Code；其他唯一索引或連線失敗照原樣往上拋，
            // 不能被誤標成優惠碼重複。
            if (exception is DbUpdateException dbUpdateException &&
                SqlUniqueIndexViolations.Matches(dbUpdateException, CouponCodeIndexName))
            {
                throw DuplicateCode(normalizedCode);
            }

            throw;
        }

        return (await FindByPublicIdAsync(coupon.PublicId, cancellationToken))!;
    }

    public async Task<CouponDto> UpdateAsync(
        Guid publicId,
        UpdateCouponRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AdminCouponQueryValidator.RequireValidRule(
            request.DiscountType,
            request.DiscountValue,
            request.MaximumDiscount,
            request.ScopeType,
            request.CategoryPublicIds,
            request.ProductPublicIds,
            request.ExcludedProductPublicIds);

        var coupon = await _context.Coupons
            .SingleOrDefaultAsync(candidate => candidate.PublicId == publicId, cancellationToken)
            ?? throw DomainProblemException.NotFound($"Coupon '{publicId}' was not found.");

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var hasRedemptions = await _context.CouponRedemptions
            .AnyAsync(redemption => redemption.CouponId == coupon.Id, cancellationToken);

        var categoryIds = await ResolveCategoryIdsAsync(request.CategoryPublicIds, cancellationToken);
        var productIds = await ResolveProductIdsAsync(request.ProductPublicIds, cancellationToken);
        var excludedProductIds =
            await ResolveProductIdsAsync(request.ExcludedProductPublicIds, cancellationToken);

        _context.Entry(coupon).Property(entity => entity.RowVersion).OriginalValue =
            request.RowVersion;

        try
        {
            coupon.UpdateRules(
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

        await ReplaceScopeAsync(
            coupon.Id, categoryIds, productIds, excludedProductIds, now, cancellationToken);

        await SaveWithConflictMappingAsync(coupon.Code, cancellationToken);
        return (await FindByPublicIdAsync(coupon.PublicId, cancellationToken))!;
    }

    /// <summary>
    /// 執行 <c>activate</c>／<c>pause</c>／<c>disable</c>。
    /// </summary>
    /// <remarks>
    /// **已知缺口：<see cref="CouponActionRequest.ReasonCode"/> 與
    /// <see cref="CouponActionRequest.Note"/> 目前沒有寫進任何地方。**
    /// 依 DEC-P289 的原則，執行理由只能寫中央 Audit、不得在 <see cref="Coupon"/>
    /// 新增欄位，但 <c>AuditWritePolicy</c> 的動作白名單目前沒有任何 <c>coupon.*</c>
    /// 項目，而該白名單屬 alex 的共用 Audit 元件，不由本工程包新增。
    /// 兩個值仍照契約驗證（必填、長度上限），待白名單補上 <c>coupon.activate</c>／
    /// <c>coupon.pause</c>／<c>coupon.disable</c> 後於此處接上 <c>IAuditWriter</c>，
    /// 與狀態變更同一交易提交。
    /// </remarks>
    public async Task<CouponDto> ExecuteActionAsync(
        Guid publicId,
        string action,
        CouponActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AdminCouponActions.IsAllowed(action))
        {
            throw DomainProblemException.NotFound($"Action '{action}' is not supported.");
        }

        var coupon = await _context.Coupons
            .SingleOrDefaultAsync(candidate => candidate.PublicId == publicId, cancellationToken)
            ?? throw DomainProblemException.NotFound($"Coupon '{publicId}' was not found.");

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _context.Entry(coupon).Property(entity => entity.RowVersion).OriginalValue =
            request.RowVersion;

        try
        {
            switch (action.Trim())
            {
                case AdminCouponActions.Activate:
                    await ActivateAsync(coupon, now, cancellationToken);
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

        await SaveWithConflictMappingAsync(coupon.Code, cancellationToken);
        return (await FindByPublicIdAsync(coupon.PublicId, cancellationToken))!;
    }

    /// <summary>
    /// 把單一個 `activate` 動作分派到 Entity 上正確的那一條轉移。
    /// </summary>
    /// <remarks>
    /// Entity 依來源狀態提供三條不同的啟用路徑，各自要重新驗證的條件不同；
    /// Action 白名單卻只有一個 `activate`（API Endpoint目錄第 113 行）。
    /// 若一律呼叫 <see cref="Coupon.ActivateNow"/>，`Scheduled` 與 `Exhausted`
    /// 的券永遠無法啟用。
    /// <para>
    /// 尚未進入有效期間的 `Draft` 走 <see cref="Coupon.ScheduleForLaterStart"/>：
    /// 白名單沒有 `schedule`，若在此直接拒絕，`Scheduled` 將是一個 API 到不了的狀態。
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
            case CouponStatus.Scheduled:
                coupon.ActivateScheduled(usage, now);
                break;
            case CouponStatus.Exhausted:
                coupon.ReactivateAfterQuotaRelease(usage, now);
                break;
            default:
                throw new InvalidOperationException(
                    $"A {coupon.Status} coupon cannot be activated.");
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
