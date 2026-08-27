using DoSelect.Application.Common;
using DoSelect.Application.Promotions;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Promotions;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests;

/// <summary>
/// 後台優惠券服務的 SQL Server Provider-backed 測試環境。
/// </summary>
/// <remarks>
/// 環境變數只決定**伺服器**，資料庫名稱強制為這組測試專屬的名稱，
/// 避免與其他 SQL Server 測試互相 <c>EnsureDeleted</c>。
/// </remarks>
public sealed class AdminCouponSqlFixture : IAsyncLifetime
{
    public const string ConnectionStringEnvironmentVariable =
        "DOSELECT_SQLSERVER_TEST_CONNECTION";

    private const string DatabaseName = "DoSelectAdminCouponTests";

    private const string LocalServer = "Server=.\\SQL2025;";

    public static bool IsEnabled =>
        !string.IsNullOrWhiteSpace(GetConfiguredConnectionString()) ||
        (OperatingSystem.IsWindows() &&
         !string.Equals(
             Environment.GetEnvironmentVariable("CI"),
             "true",
             StringComparison.OrdinalIgnoreCase));

    public async Task InitializeAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(BuildConnectionString())
            .Options);

    private static string BuildConnectionString()
    {
        var configured = GetConfiguredConnectionString();
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
            string.IsNullOrWhiteSpace(configured) ? LocalServer : configured)
        {
            InitialCatalog = DatabaseName,
            TrustServerCertificate = true,
        };

        if (string.IsNullOrWhiteSpace(configured))
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    private static string? GetConfiguredConnectionString() =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
}

public sealed class AdminCouponSqlFactAttribute : FactAttribute
{
    public AdminCouponSqlFactAttribute()
    {
        if (!AdminCouponSqlFixture.IsEnabled)
        {
            Skip = "Set " + AdminCouponSqlFixture.ConnectionStringEnvironmentVariable +
                   " to run SQL Server integration tests.";
        }
    }
}

[CollectionDefinition(nameof(AdminCouponSqlCollection))]
public sealed class AdminCouponSqlCollection : ICollectionFixture<AdminCouponSqlFixture>;

/// <summary>
/// 後台優惠券 CRUD 與生命週期動作，對真實 SQL Server 執行。
/// </summary>
/// <remarks>
/// 這裡要證明的是資料庫層才看得到的行為：`UX_Coupons_Code` 唯一索引、
/// RowVersion 樂觀併發、範圍表的實際取代，以及使用量與試算引擎共用同一個名額定義。
/// </remarks>
[Collection(nameof(AdminCouponSqlCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class AdminCouponServiceSqlServerTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StartsAtUtc = NowUtc.AddDays(-1);
    private static readonly DateTime EndsAtUtc = NowUtc.AddDays(30);

    [AdminCouponSqlFact]
    public async Task CreatingACouponPersistsItAsADraft()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var service = CreateService(context);

        var created = await service.CreateAsync(CreateRequest(UniqueCode()));

        Assert.Equal(CouponStatus.Draft, created.Status);
        Assert.Equal(1, created.RuleVersion);
        Assert.NotEmpty(created.RowVersion);
        Assert.Equal(0, created.Usage.TotalRedeemedCount);
    }

    [AdminCouponSqlFact]
    public async Task TheCodeIsNormalizedToUppercaseOnTheWayIn()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var service = CreateService(context);
        var code = UniqueCode();

        var created = await service.CreateAsync(CreateRequest(code.ToLowerInvariant()));

        Assert.Equal(code, created.Code);
    }

    [AdminCouponSqlFact]
    public async Task ADuplicateCodeIsRejectedByTheUniqueIndex()
    {
        // 這條走的是資料庫的 UX_Coupons_Code，不是記憶體裡的預先檢查：
        // 第二次建立時先前那筆已經 commit，SELECT 會抓到，但真正的保證仍是索引。
        await using var context = AdminCouponSqlFixture.CreateContext();
        var service = CreateService(context);
        var code = UniqueCode();
        await service.CreateAsync(CreateRequest(code));

        await using var second = AdminCouponSqlFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => CreateService(second).CreateAsync(CreateRequest(code)));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal(CouponCalculationErrorCodes.CouponCodeDuplicate, exception.Code);
    }

    [AdminCouponSqlFact]
    public async Task ADuplicateCodeThatDiffersOnlyInCaseIsAlsoRejected()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var code = UniqueCode();
        await CreateService(context).CreateAsync(CreateRequest(code));

        await using var second = AdminCouponSqlFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => CreateService(second).CreateAsync(CreateRequest(code.ToLowerInvariant())));

        Assert.Equal(CouponCalculationErrorCodes.CouponCodeDuplicate, exception.Code);
    }

    [AdminCouponSqlFact]
    public async Task AnUnknownCategoryIsRejectedInsteadOfSilentlyDropped()
    {
        // 靜默略過會產出一張適用範圍與送出內容不同的券，而畫面上看不出來。
        await using var context = AdminCouponSqlFixture.CreateContext();

        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => CreateService(context).CreateAsync(
                CreateRequest(UniqueCode()) with
                {
                    ScopeType = CouponScopeType.Restricted,
                    CategoryPublicIds = [Guid.NewGuid()],
                }));

        Assert.Equal(400, exception.StatusCode);
    }

    [AdminCouponSqlFact]
    public async Task AFailedScopeResolutionLeavesNoCouponBehind()
    {
        // 建立與範圍寫入在同一個交易；範圍失敗時不能留下一張沒有範圍的孤兒券。
        await using var context = AdminCouponSqlFixture.CreateContext();
        var code = UniqueCode();

        await Assert.ThrowsAsync<DomainProblemException>(
            () => CreateService(context).CreateAsync(
                CreateRequest(code) with
                {
                    ScopeType = CouponScopeType.Restricted,
                    ProductPublicIds = [Guid.NewGuid()],
                }));

        await using var verify = AdminCouponSqlFixture.CreateContext();
        Assert.False(await verify.Coupons.AnyAsync(coupon => coupon.Code == code));
    }

    [AdminCouponSqlFact]
    public async Task TheScopeRoundTripsThroughTheThreeLinkTables()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var (category, product, excluded) = await SeedCatalogAsync(context);

        var created = await CreateService(context).CreateAsync(
            CreateRequest(UniqueCode()) with
            {
                ScopeType = CouponScopeType.Restricted,
                CategoryPublicIds = [category],
                ProductPublicIds = [product],
                ExcludedProductPublicIds = [excluded],
            });

        await using var verify = AdminCouponSqlFixture.CreateContext();
        var reloaded = await CreateService(verify).FindByPublicIdAsync(created.PublicId);

        Assert.NotNull(reloaded);
        Assert.Equal([category], reloaded!.Scope.CategoryPublicIds);
        Assert.Equal([product], reloaded.Scope.ProductPublicIds);
        Assert.Equal([excluded], reloaded.Scope.ExcludedProductPublicIds);
    }

    [AdminCouponSqlFact]
    public async Task UpdatingReplacesTheScopeRatherThanAppendingToIt()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var (category, product, other) = await SeedCatalogAsync(context);
        var created = await CreateService(context).CreateAsync(
            CreateRequest(UniqueCode()) with
            {
                ScopeType = CouponScopeType.Restricted,
                ProductPublicIds = [product],
            });

        await using var update = AdminCouponSqlFixture.CreateContext();
        var updated = await CreateService(update).UpdateAsync(
            created.PublicId,
            UpdateRequest(created) with
            {
                ScopeType = CouponScopeType.Restricted,
                CategoryPublicIds = [category],
                ProductPublicIds = [other],
            });

        Assert.Equal([other], updated.Scope.ProductPublicIds);
        Assert.Equal([category], updated.Scope.CategoryPublicIds);
    }

    [AdminCouponSqlFact]
    public async Task AScopeOnlyUpdateAdvancesTheVersionInTheDatabase()
    {
        // 只換適用商品、ScopeType 不變。修好之前這條更新不會修改 Coupons 那一列，
        // RuleVersion 與 RowVersion 都不動。
        await using var context = AdminCouponSqlFixture.CreateContext();
        var (_, product, other) = await SeedCatalogAsync(context);
        var created = await CreateService(context).CreateAsync(
            CreateRequest(UniqueCode()) with
            {
                ScopeType = CouponScopeType.Restricted,
                ProductPublicIds = [product],
            });

        await using var update = AdminCouponSqlFixture.CreateContext();
        var updated = await CreateService(update).UpdateAsync(
            created.PublicId,
            UpdateRequest(created) with { ProductPublicIds = [other] });

        Assert.Equal(created.RuleVersion + 1, updated.RuleVersion);
        Assert.NotEqual(created.RowVersion, updated.RowVersion);
        Assert.Equal([other], updated.Scope.ProductPublicIds);
    }

    [AdminCouponSqlFact]
    public async Task AStaleScopeOnlyUpdateIsRejected()
    {
        // 這是上面那條缺陷的實際後果：拿過期版本做純範圍修改會覆蓋別人的變更。
        await using var context = AdminCouponSqlFixture.CreateContext();
        var (category, product, other) = await SeedCatalogAsync(context);
        var created = await CreateService(context).CreateAsync(
            CreateRequest(UniqueCode()) with
            {
                ScopeType = CouponScopeType.Restricted,
                ProductPublicIds = [product],
            });

        await using var first = AdminCouponSqlFixture.CreateContext();
        await CreateService(first).UpdateAsync(
            created.PublicId,
            UpdateRequest(created) with { CategoryPublicIds = [category] });

        await using var second = AdminCouponSqlFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => CreateService(second).UpdateAsync(
                created.PublicId,
                UpdateRequest(created) with { ProductPublicIds = [other] }));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal(DomainErrorCodes.ConcurrencyConflict, exception.Code);
    }

    [AdminCouponSqlFact]
    public async Task ChangingSeveralScopeCollectionsAdvancesTheVersionOnlyOnce()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var (category, product, other) = await SeedCatalogAsync(context);
        var created = await CreateService(context).CreateAsync(
            CreateRequest(UniqueCode()) with
            {
                ScopeType = CouponScopeType.Restricted,
                ProductPublicIds = [product],
            });

        await using var update = AdminCouponSqlFixture.CreateContext();
        var updated = await CreateService(update).UpdateAsync(
            created.PublicId,
            UpdateRequest(created) with
            {
                CategoryPublicIds = [category],
                ProductPublicIds = [other],
                ExcludedProductPublicIds = [product],
            });

        Assert.Equal(created.RuleVersion + 1, updated.RuleVersion);
    }

    [AdminCouponSqlFact]
    public async Task ReorderingTheSameScopeIsNotTreatedAsAChange()
    {
        // 集合語意比較：順序不同不是變更，不該平白推進版本。
        await using var context = AdminCouponSqlFixture.CreateContext();
        var (_, product, other) = await SeedCatalogAsync(context);
        var created = await CreateService(context).CreateAsync(
            CreateRequest(UniqueCode()) with
            {
                ScopeType = CouponScopeType.Restricted,
                ProductPublicIds = [product, other],
            });

        await using var update = AdminCouponSqlFixture.CreateContext();
        var updated = await CreateService(update).UpdateAsync(
            created.PublicId,
            UpdateRequest(created) with { ProductPublicIds = [other, product] });

        Assert.Equal(created.RuleVersion, updated.RuleVersion);
    }

    [AdminCouponSqlFact]
    public async Task TheRedemptionRangeIsLockedWhileTheUpdateTransactionIsOpen()
    {
        // 這條證明「有 Redemption 後 Code 凍結」的競態確實被關上，而不是靠時序碰運氣。
        //
        // 缺陷長這樣：`hasRedemptions` 查完為 false，Checkout 在另一個交易插入一筆
        // Redemption，管理端接著寫入新的 Code —— 已凍結的優惠碼就被改掉了。
        // 新增 Redemption 不會更新 Coupons 那一列，所以 Coupon 的 RowVersion 攔不到。
        //
        // 在 Serializable 下，那個 AnyAsync 會對這個 CouponId 的範圍取得 range lock，
        // 第二個連線的 INSERT 必須等待。這裡用短的 LOCK_TIMEOUT 讓「被擋住」
        // 變成一個確定的失敗，而不是不確定的延遲。
        await using var context = AdminCouponSqlFixture.CreateContext();
        var created = await CreateService(context).CreateAsync(CreateRequest(UniqueCode()));
        var couponId = await context.Coupons
            .Where(coupon => coupon.PublicId == created.PublicId)
            .Select(coupon => coupon.Id)
            .SingleAsync();

        // 訂單與物流設定檔在鎖定範圍外先建好：它們與 CouponRedemptions 無關，
        // 留在裡面只會讓這條測試變慢，也模糊了究竟是哪一個 INSERT 被擋住。
        var orderId = (await SeedOrderAsync(context)).Id;

        await using var holder = AdminCouponSqlFixture.CreateContext();
        await using var transaction = await holder.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        // 與 UpdateAsync 內完全相同的範圍查詢。
        await holder.CouponRedemptions
            .AnyAsync(redemption => redemption.CouponId == couponId);

        await using var intruder = AdminCouponSqlFixture.CreateContext();

        // 連線必須顯式開著：`SET LOCK_TIMEOUT` 只作用於當下這條連線，
        // 讓 EF 自行開關會把連線還給 pool，下一個語句可能拿到另一條而失去設定。
        await intruder.Database.OpenConnectionAsync();
        try
        {
            await intruder.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 3000;");

            var blocked = await Assert.ThrowsAnyAsync<Exception>(
                () => InsertRedemptionAsync(intruder, couponId, orderId));

            Assert.True(
                IsLockTimeout(blocked),
                $"Expected a lock timeout, got: {blocked.GetType().Name}: {blocked.Message}");
        }
        finally
        {
            await intruder.Database.CloseConnectionAsync();
        }

        await transaction.RollbackAsync();
    }

    private static async Task InsertRedemptionAsync(
        DoSelectDbContext context,
        long couponId,
        long orderId)
    {
        var hash = new byte[32];
        Random.Shared.NextBytes(hash);

        context.CouponRedemptions.Add(new CouponRedemption(
            Guid.NewGuid(),
            couponId,
            orderId,
            memberUserId: null,
            guestUsageKeyHash: hash,
            reservedAtUtc: NowUtc.AddHours(-1),
            expiresAtUtc: null,
            createdAtUtc: NowUtc.AddHours(-1)));

        await context.SaveChangesAsync();
    }

    private static bool IsLockTimeout(Exception exception)
    {
        // 1222 = Lock request time out period exceeded.
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Microsoft.Data.SqlClient.SqlException { Number: 1222 })
            {
                return true;
            }
        }

        return false;
    }

    [AdminCouponSqlFact]
    public async Task AStaleRowVersionIsRejectedAsAConcurrencyConflict()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var created = await CreateService(context).CreateAsync(CreateRequest(UniqueCode()));

        // 第一次修改成功，RowVersion 因此前進。
        await using var first = AdminCouponSqlFixture.CreateContext();
        await CreateService(first).UpdateAsync(
            created.PublicId,
            UpdateRequest(created) with { NameZhTw = "第一次改名" });

        // 第二個呼叫端還拿著建立當下的版本。
        await using var second = AdminCouponSqlFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => CreateService(second).UpdateAsync(
                created.PublicId,
                UpdateRequest(created) with { NameZhTw = "第二次改名" }));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal(DomainErrorCodes.ConcurrencyConflict, exception.Code);
    }

    [AdminCouponSqlFact]
    public async Task ARejectedUpdateDoesNotAdvanceTheRuleVersionInTheDatabase()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var created = await CreateService(context).CreateAsync(CreateRequest(UniqueCode()));

        await using var first = AdminCouponSqlFixture.CreateContext();
        await CreateService(first).UpdateAsync(
            created.PublicId,
            UpdateRequest(created) with { DiscountValue = 400m });

        await using var second = AdminCouponSqlFixture.CreateContext();
        await Assert.ThrowsAsync<DomainProblemException>(
            () => CreateService(second).UpdateAsync(
                created.PublicId,
                UpdateRequest(created) with { DiscountValue = 500m }));

        await using var verify = AdminCouponSqlFixture.CreateContext();
        var reloaded = await CreateService(verify).FindByPublicIdAsync(created.PublicId);
        Assert.Equal(2, reloaded!.RuleVersion);
        Assert.Equal(400m, reloaded.DiscountValue);
    }

    [AdminCouponSqlFact]
    public async Task TheCodeIsFrozenOnceARedemptionExistsInTheDatabase()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var created = await CreateService(context).CreateAsync(CreateRequest(UniqueCode()));
        await SeedRedemptionAsync(context, created.PublicId, CouponRedemptionStatus.Consumed, null);

        await using var update = AdminCouponSqlFixture.CreateContext();
        var reloaded = await CreateService(update).FindByPublicIdAsync(created.PublicId);
        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => CreateService(update).UpdateAsync(
                created.PublicId,
                UpdateRequest(reloaded!) with { Code = UniqueCode() }));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal(CouponCalculationErrorCodes.CouponStateConflict, exception.Code);
    }

    [AdminCouponSqlFact]
    public async Task OtherFieldsStillChangeAfterARedemptionExists()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var created = await CreateService(context).CreateAsync(CreateRequest(UniqueCode()));
        await SeedRedemptionAsync(context, created.PublicId, CouponRedemptionStatus.Consumed, null);

        await using var update = AdminCouponSqlFixture.CreateContext();
        var reloaded = await CreateService(update).FindByPublicIdAsync(created.PublicId);
        var updated = await CreateService(update).UpdateAsync(
            created.PublicId,
            UpdateRequest(reloaded!) with { MinimumSpend = 5000m });

        Assert.Equal(5000m, updated.MinimumSpend);
    }

    [AdminCouponSqlFact]
    public async Task ActivateThenPauseThenDisableMovesThroughTheDocumentedStates()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var service = CreateService(context);
        var current = await service.CreateAsync(CreateRequest(UniqueCode()));

        current = await service.ExecuteActionAsync(
            current.PublicId, AdminCouponActions.Activate, ActionRequest(current));
        Assert.Equal(CouponStatus.Active, current.Status);

        current = await service.ExecuteActionAsync(
            current.PublicId, AdminCouponActions.Pause, ActionRequest(current));
        Assert.Equal(CouponStatus.Paused, current.Status);

        current = await service.ExecuteActionAsync(
            current.PublicId, AdminCouponActions.Disable, ActionRequest(current));
        Assert.Equal(CouponStatus.Disabled, current.Status);
    }

    [AdminCouponSqlFact]
    public async Task ActivatingACouponThatHasNotStartedSchedulesItInstead()
    {
        // Action 白名單沒有 `schedule`；若在此拒絕，Scheduled 會是 API 到不了的狀態。
        await using var context = AdminCouponSqlFixture.CreateContext();
        var service = CreateService(context);
        var created = await service.CreateAsync(
            CreateRequest(UniqueCode()) with { StartsAtUtc = NowUtc.AddDays(3) });

        var scheduled = await service.ExecuteActionAsync(
            created.PublicId, AdminCouponActions.Activate, ActionRequest(created));

        Assert.Equal(CouponStatus.Scheduled, scheduled.Status);
    }

    [AdminCouponSqlFact]
    public async Task DisablingIsTerminalAndCannotBeUndone()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var service = CreateService(context);
        var created = await service.CreateAsync(CreateRequest(UniqueCode()));
        var disabled = await service.ExecuteActionAsync(
            created.PublicId, AdminCouponActions.Disable, ActionRequest(created));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => service.ExecuteActionAsync(
                disabled.PublicId, AdminCouponActions.Activate, ActionRequest(disabled)));

        Assert.Equal(CouponCalculationErrorCodes.CouponStateConflict, exception.Code);
    }

    [AdminCouponSqlFact]
    public async Task AnActionWithAStaleRowVersionIsRejected()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var created = await CreateService(context).CreateAsync(CreateRequest(UniqueCode()));

        await using var first = AdminCouponSqlFixture.CreateContext();
        await CreateService(first).ExecuteActionAsync(
            created.PublicId, AdminCouponActions.Activate, ActionRequest(created));

        await using var second = AdminCouponSqlFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => CreateService(second).ExecuteActionAsync(
                created.PublicId, AdminCouponActions.Pause, ActionRequest(created)));

        Assert.Equal(DomainErrorCodes.ConcurrencyConflict, exception.Code);
    }

    [AdminCouponSqlFact]
    public async Task AnActionOutsideTheWhitelistIsNotFound()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var service = CreateService(context);
        var created = await service.CreateAsync(CreateRequest(UniqueCode()));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => service.ExecuteActionAsync(created.PublicId, "expire", ActionRequest(created)));

        Assert.Equal(404, exception.StatusCode);
    }

    [AdminCouponSqlFact]
    public async Task TheReportedUsageUsesTheSameSeatRuleAsTheQuoteEngine()
    {
        // 後台看到的名額若與規則引擎採用的不同，管理員會依一個不存在的餘額做決策。
        // 已過期的 Reserved 不佔名額，Consumed 佔。
        await using var context = AdminCouponSqlFixture.CreateContext();
        var created = await CreateService(context).CreateAsync(CreateRequest(UniqueCode()));

        await SeedRedemptionAsync(context, created.PublicId, CouponRedemptionStatus.Consumed, null);
        await SeedRedemptionAsync(
            context, created.PublicId, CouponRedemptionStatus.Reserved, NowUtc.AddMinutes(-1));
        await SeedRedemptionAsync(
            context, created.PublicId, CouponRedemptionStatus.Reserved, NowUtc.AddMinutes(30));

        await using var verify = AdminCouponSqlFixture.CreateContext();
        var reloaded = await CreateService(verify).FindByPublicIdAsync(created.PublicId);

        Assert.Equal(2, reloaded!.Usage.TotalRedeemedCount);
        Assert.Equal(100 - 2, reloaded.Usage.RemainingCount);
    }

    [AdminCouponSqlFact]
    public async Task AnUnlimitedCouponReportsNullRemainingNotZero()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var created = await CreateService(context).CreateAsync(
            CreateRequest(UniqueCode()) with { TotalUsageLimit = null });

        Assert.Null(created.Usage.RemainingCount);
        Assert.Null(created.Usage.TotalUsageLimit);
    }

    [AdminCouponSqlFact]
    public async Task TheListFiltersByStatusAndPagesStably()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var service = CreateService(context);
        var marker = $"P{Guid.NewGuid():N}"[..12];

        for (var index = 0; index < 5; index++)
        {
            await service.CreateAsync(
                CreateRequest(UniqueCode()) with { NameZhTw = $"{marker}-{index}" });
        }

        var firstPage = await service.ListAsync(
            new AdminCouponQuery(marker, [CouponStatus.Draft], AdminCouponSortOptions.CodeAsc, 1, 2));
        var secondPage = await service.ListAsync(
            new AdminCouponQuery(marker, [CouponStatus.Draft], AdminCouponSortOptions.CodeAsc, 2, 2));

        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal(3, firstPage.TotalPages);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Empty(firstPage.Items.Select(item => item.PublicId)
            .Intersect(secondPage.Items.Select(item => item.PublicId)));
    }

    [AdminCouponSqlFact]
    public async Task TheListExcludesStatusesThatWereNotAskedFor()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();
        var service = CreateService(context);
        var marker = $"S{Guid.NewGuid():N}"[..12];
        var draft = await service.CreateAsync(
            CreateRequest(UniqueCode()) with { NameZhTw = $"{marker}-draft" });
        var active = await service.CreateAsync(
            CreateRequest(UniqueCode()) with { NameZhTw = $"{marker}-active" });
        await service.ExecuteActionAsync(
            active.PublicId, AdminCouponActions.Activate, ActionRequest(active));

        var result = await service.ListAsync(
            new AdminCouponQuery(marker, [CouponStatus.Active], null, 1, 20));

        Assert.Equal(active.PublicId, Assert.Single(result.Items).PublicId);
        Assert.DoesNotContain(result.Items, item => item.PublicId == draft.PublicId);
    }

    [AdminCouponSqlFact]
    public async Task AnUnknownCouponIsNotFound()
    {
        await using var context = AdminCouponSqlFixture.CreateContext();

        Assert.Null(await CreateService(context).FindByPublicIdAsync(Guid.NewGuid()));
    }

    private static IAdminCouponService CreateService(DoSelectDbContext context) =>
        new EfAdminCouponService(context, new FixedTimeProvider(NowUtc));

    private static string UniqueCode() => $"C{Guid.NewGuid():N}"[..16].ToUpperInvariant();

    private static CreateCouponRequest CreateRequest(string code) =>
        new(
            code,
            "後台測試券",
            CouponDiscountType.FixedAmount,
            DiscountValue: 300m,
            MinimumSpend: 3000m,
            MaximumDiscount: null,
            StartsAtUtc: StartsAtUtc,
            EndsAtUtc: EndsAtUtc,
            TotalUsageLimit: 100,
            PerMemberLimit: 1,
            MemberOnly: false,
            ExcludeSaleItems: false,
            ScopeType: CouponScopeType.All,
            CategoryPublicIds: null,
            ProductPublicIds: null,
            ExcludedProductPublicIds: null);

    private static UpdateCouponRequest UpdateRequest(CouponDto current) =>
        new(
            current.Code,
            current.NameZhTw,
            current.DiscountType,
            current.DiscountValue,
            current.MinimumSpend,
            current.MaximumDiscount,
            current.StartsAtUtc,
            current.EndsAtUtc,
            current.Usage.TotalUsageLimit,
            current.Usage.PerMemberLimit,
            current.MemberOnly,
            current.ExcludeSaleItems,
            current.Scope.ScopeType,
            current.Scope.CategoryPublicIds,
            current.Scope.ProductPublicIds,
            current.Scope.ExcludedProductPublicIds,
            current.RowVersion);

    private static CouponActionRequest ActionRequest(CouponDto current) =>
        new("admin_request", null, current.RowVersion);

    private static async Task<(Guid Category, Guid Product, Guid Other)> SeedCatalogAsync(
        DoSelectDbContext context)
    {
        var brand = new Brand(Guid.NewGuid(), $"B{Guid.NewGuid():N}"[..12], "測試品牌", NowUtc);
        context.Set<Brand>().Add(brand);
        await context.SaveChangesAsync();

        var category = new Category(
            Guid.NewGuid(), $"C{Guid.NewGuid():N}"[..12], $"s{Guid.NewGuid():N}"[..12],
            "測試分類", null, NowUtc);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product(
            Guid.NewGuid(), $"P{Guid.NewGuid():N}"[..12], brand.Id, category.Id, "測試商品", NowUtc);
        var other = new Product(
            Guid.NewGuid(), $"P{Guid.NewGuid():N}"[..12], brand.Id, category.Id, "另一商品", NowUtc);
        context.Products.AddRange(product, other);
        await context.SaveChangesAsync();

        return (category.PublicId, product.PublicId, other.PublicId);
    }

    /// <summary>
    /// 建立一筆訪客 Redemption。<c>CouponRedemptions</c> 對 <c>Orders</c> 有外鍵，
    /// 因此仍需一張最小訂單。
    /// </summary>
    private static async Task SeedRedemptionAsync(
        DoSelectDbContext context,
        Guid couponPublicId,
        CouponRedemptionStatus status,
        DateTime? expiresAtUtc)
    {
        var coupon = await context.Coupons
            .SingleAsync(candidate => candidate.PublicId == couponPublicId);
        var order = await SeedOrderAsync(context);

        var hash = new byte[32];
        Random.Shared.NextBytes(hash);

        var redemption = new CouponRedemption(
            Guid.NewGuid(),
            coupon.Id,
            order.Id,
            memberUserId: null,
            guestUsageKeyHash: hash,
            reservedAtUtc: NowUtc.AddHours(-1),
            expiresAtUtc: expiresAtUtc,
            createdAtUtc: NowUtc.AddHours(-1));

        if (status == CouponRedemptionStatus.Consumed)
        {
            redemption.Consume(NowUtc.AddMinutes(-30));
        }

        context.CouponRedemptions.Add(redemption);
        await context.SaveChangesAsync();
    }

    private static async Task<Order> SeedOrderAsync(DoSelectDbContext context)
    {
        var createdAtUtc = NowUtc.AddHours(-2);
        var profile = new ShippingProviderProfile(
            Guid.NewGuid(),
            $"TEST-{Guid.NewGuid():N}"[..16],
            1,
            "Active",
            null,
            null,
            "{}",
            1,
            createdAtUtc);
        context.Add(profile);
        await context.SaveChangesAsync();

        var order = Order.Create(
            Guid.NewGuid(),
            new OrderCreation(
                $"ORD-{Guid.NewGuid():N}"[..32],
                null,
                $"guest-{Guid.NewGuid():N}@example.test",
                OrderStatus.PendingPayment,
                PaymentStatus.Pending,
                FulfillmentStatus.Pending,
                AssemblyStatus.NotRequired,
                100m,
                0m,
                10m,
                0m,
                110m,
                "Test Recipient",
                "0900000000",
                "guest@example.test",
                "100",
                "Taipei",
                "Zhongzheng",
                "Test address",
                null,
                "HOME",
                profile.Id,
                null,
                null,
                null,
                1,
                1,
                null,
                null,
                $"checkout-{Guid.NewGuid():N}",
                null,
                1000m),
            createdAtUtc);

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTime utcNow) => _utcNow = new DateTimeOffset(utcNow);

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
