using DoSelect.Domain.Orders;
using DoSelect.Domain.Promotions;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests;

/// <summary>
/// 優惠券規則讀取的 SQL Server Provider-backed 測試環境（DEC-P274／DEC-P307）。
/// </summary>
/// <remarks>
/// 環境變數只決定**伺服器**，資料庫名稱一律強制為這組測試專屬的名稱，
/// 避免與其他 SQL Server 測試互相 <c>EnsureDeleted</c>。
/// </remarks>
public sealed class CouponRuleReaderSqlFixture : IAsyncLifetime
{
    public const string ConnectionStringEnvironmentVariable =
        "DOSELECT_SQLSERVER_TEST_CONNECTION";

    private const string DatabaseName = "DoSelectCouponRuleReaderTests";

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

public sealed class CouponRuleReaderSqlFactAttribute : FactAttribute
{
    public CouponRuleReaderSqlFactAttribute()
    {
        if (!CouponRuleReaderSqlFixture.IsEnabled)
        {
            Skip = "Set " + CouponRuleReaderSqlFixture.ConnectionStringEnvironmentVariable +
                   " to run SQL Server integration tests.";
        }
    }
}

[CollectionDefinition(nameof(CouponRuleReaderSqlCollection))]
public sealed class CouponRuleReaderSqlCollection
    : ICollectionFixture<CouponRuleReaderSqlFixture>;

/// <summary>
/// 實際對 SQL Server 執行查詢的優惠券規則讀取測試。
/// </summary>
/// <remarks>
/// 這些測試證明的是**SQL 轉譯與實際計數**，不是記憶體中的 expression 行為：
/// 過期判斷、32-byte guest hash 的 binary 比對、以及範圍表的實際讀取。
/// </remarks>
[Collection(nameof(CouponRuleReaderSqlCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class CouponRuleReaderSqlServerTests
{
    private static readonly DateTime EvaluatedAtUtc = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    [CouponRuleReaderSqlFact]
    public async Task AnExpiredReservationIsNotCountedBySqlServer()
    {
        // 這是 P1 的實證：過濾在資料庫端執行，不是取回記憶體後再篩。
        await using var context = CouponRuleReaderSqlFixture.CreateContext();
        var coupon = await SeedCouponAsync(context);

        await SeedGuestRedemptionAsync(
            context, coupon, GuestHash(1), CouponRedemptionStatus.Reserved,
            expiresAtUtc: EvaluatedAtUtc.AddMinutes(-1));

        var usage = await new CouponRuleReader(context)
            .GetUsageAsync(coupon.Id, null, GuestHash(1), EvaluatedAtUtc);

        Assert.Equal(0, usage.TotalRedeemedCount);
        Assert.Equal(0, usage.MemberRedeemedCount);
    }

    [CouponRuleReaderSqlFact]
    public async Task AnUnexpiredReservationIsCountedBySqlServer()
    {
        await using var context = CouponRuleReaderSqlFixture.CreateContext();
        var coupon = await SeedCouponAsync(context);

        await SeedGuestRedemptionAsync(
            context, coupon, GuestHash(2), CouponRedemptionStatus.Reserved,
            expiresAtUtc: EvaluatedAtUtc.AddMinutes(1));

        var usage = await new CouponRuleReader(context)
            .GetUsageAsync(coupon.Id, null, GuestHash(2), EvaluatedAtUtc);

        Assert.Equal(1, usage.TotalRedeemedCount);
        Assert.Equal(1, usage.MemberRedeemedCount);
    }

    [CouponRuleReaderSqlFact]
    public async Task AReservationWithoutAnExpiryIsCountedBySqlServer()
    {
        await using var context = CouponRuleReaderSqlFixture.CreateContext();
        var coupon = await SeedCouponAsync(context);

        await SeedGuestRedemptionAsync(
            context, coupon, GuestHash(3), CouponRedemptionStatus.Reserved, expiresAtUtc: null);

        var usage = await new CouponRuleReader(context)
            .GetUsageAsync(coupon.Id, null, GuestHash(3), EvaluatedAtUtc);

        Assert.Equal(1, usage.TotalRedeemedCount);
    }

    [CouponRuleReaderSqlFact]
    public async Task ConsumedCountsAndReleasedOrExpiredDoNot()
    {
        await using var context = CouponRuleReaderSqlFixture.CreateContext();
        var coupon = await SeedCouponAsync(context);

        // Consumed 即使原保留視窗已過，仍占名額。
        await SeedGuestRedemptionAsync(
            context, coupon, GuestHash(4), CouponRedemptionStatus.Consumed,
            expiresAtUtc: EvaluatedAtUtc.AddMinutes(-5));
        await SeedGuestRedemptionAsync(
            context, coupon, GuestHash(5), CouponRedemptionStatus.Released, expiresAtUtc: null);
        await SeedGuestRedemptionAsync(
            context, coupon, GuestHash(6), CouponRedemptionStatus.Expired, expiresAtUtc: null);

        var usage = await new CouponRuleReader(context)
            .GetUsageAsync(coupon.Id, null, null, EvaluatedAtUtc);

        Assert.Equal(1, usage.TotalRedeemedCount);
    }

    [CouponRuleReaderSqlFact]
    public async Task TheGuestHashIsComparedAsBinaryBySqlServer()
    {
        // 32-byte binary 比對必須由 SQL Server 執行且精確。
        // 只差一個位元組的兩把金鑰不得互相計數。
        await using var context = CouponRuleReaderSqlFixture.CreateContext();
        var coupon = await SeedCouponAsync(context);

        var mine = GuestHash(7);
        var theirs = GuestHash(7);
        theirs[31] = 0xFF;

        await SeedGuestRedemptionAsync(
            context, coupon, mine, CouponRedemptionStatus.Consumed, expiresAtUtc: null);
        await SeedGuestRedemptionAsync(
            context, coupon, theirs, CouponRedemptionStatus.Consumed, expiresAtUtc: null);

        var reader = new CouponRuleReader(context);
        var usage = await reader.GetUsageAsync(coupon.Id, null, mine, EvaluatedAtUtc);

        Assert.Equal(2, usage.TotalRedeemedCount);
        Assert.Equal(1, usage.MemberRedeemedCount);
    }

    [CouponRuleReaderSqlFact]
    public async Task TheTotalAndTheOwnerCountUseTheSameExpiryCutoff()
    {
        await using var context = CouponRuleReaderSqlFixture.CreateContext();
        var coupon = await SeedCouponAsync(context);
        var mine = GuestHash(8);

        await SeedGuestRedemptionAsync(
            context, coupon, mine, CouponRedemptionStatus.Reserved,
            expiresAtUtc: EvaluatedAtUtc.AddMinutes(-1));
        await SeedGuestRedemptionAsync(
            context, coupon, mine, CouponRedemptionStatus.Consumed, expiresAtUtc: null);

        var usage = await new CouponRuleReader(context)
            .GetUsageAsync(coupon.Id, null, mine, EvaluatedAtUtc);

        // 過期的那筆兩邊都不算：總量與每人限額不得對同一筆有不同判定。
        Assert.Equal(1, usage.TotalRedeemedCount);
        Assert.Equal(1, usage.MemberRedeemedCount);
    }

    [CouponRuleReaderSqlFact]
    public async Task FindByCodeAsyncReadsTheScopeTablesFromSqlServer()
    {
        await using var context = CouponRuleReaderSqlFixture.CreateContext();
        var coupon = await SeedCouponAsync(context, CouponScopeType.Restricted);

        var snapshot = await new CouponRuleReader(context).FindByCodeAsync(coupon.Code);

        Assert.NotNull(snapshot);
        Assert.Equal(coupon.Id, snapshot!.CouponId);
        Assert.Equal(CouponScopeType.Restricted, snapshot.Scope.ScopeType);
    }

    [CouponRuleReaderSqlFact]
    public async Task AnUnknownCodeResolvesToNullAgainstTheRealDatabase()
    {
        await using var context = CouponRuleReaderSqlFixture.CreateContext();

        Assert.Null(await new CouponRuleReader(context).FindByCodeAsync("NO-SUCH-CODE"));
    }

    private static byte[] GuestHash(byte seed)
    {
        var hash = new byte[32];
        Array.Fill(hash, seed);
        return hash;
    }

    private static async Task<Coupon> SeedCouponAsync(
        DoSelectDbContext context,
        CouponScopeType scopeType = CouponScopeType.All)
    {
        var createdAtUtc = EvaluatedAtUtc.AddDays(-7);
        var coupon = new Coupon(
            Guid.NewGuid(),
            new CouponCreation(
                $"C{Guid.NewGuid():N}"[..16],
                "SQL 測試券",
                CouponDiscountType.FixedAmount,
                DiscountValue: 100m,
                MinimumSpend: 0m,
                MaximumDiscount: null,
                StartsAtUtc: createdAtUtc,
                EndsAtUtc: EvaluatedAtUtc.AddDays(7),
                TotalUsageLimit: 1000,
                PerMemberLimit: 1000,
                MemberOnly: false,
                ExcludeSaleItems: false,
                scopeType),
            createdAtUtc);

        context.Coupons.Add(coupon);
        await context.SaveChangesAsync();
        return coupon;
    }

    /// <summary>
    /// 建立一筆訪客 Redemption。訪客不需要 ApplicationUser，
    /// 但 <c>CouponRedemptions</c> 對 <c>Orders</c> 有外鍵，因此仍需一張最小訂單。
    /// </summary>
    private static async Task SeedGuestRedemptionAsync(
        DoSelectDbContext context,
        Coupon coupon,
        byte[] guestUsageKeyHash,
        CouponRedemptionStatus status,
        DateTime? expiresAtUtc)
    {
        var createdAtUtc = EvaluatedAtUtc.AddDays(-1);
        var order = await SeedOrderAsync(context, createdAtUtc);

        var redemption = new CouponRedemption(
            Guid.NewGuid(),
            coupon.Id,
            order.Id,
            memberUserId: null,
            guestUsageKeyHash,
            reservedAtUtc: createdAtUtc,
            expiresAtUtc,
            createdAtUtc);

        switch (status)
        {
            case CouponRedemptionStatus.Consumed:
                redemption.Consume(createdAtUtc.AddMinutes(1));
                break;
            case CouponRedemptionStatus.Released:
                redemption.Release(createdAtUtc.AddMinutes(1));
                break;
            case CouponRedemptionStatus.Expired:
                redemption.Expire(createdAtUtc.AddMinutes(1));
                break;
        }

        context.CouponRedemptions.Add(redemption);
        await context.SaveChangesAsync();
    }

    private static async Task<Order> SeedOrderAsync(
        DoSelectDbContext context,
        DateTime createdAtUtc)
    {
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
}
