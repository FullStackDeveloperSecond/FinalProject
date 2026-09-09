using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Tests.Idempotency;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DoSelect.Infrastructure.Tests;

[Trait("Category", "RequiresSqlServer")]
public sealed class CouponQuantityMembershipMigrationTests
{
    [SqlServerFact]
    public async Task Upgrade_PreservesLegacyCouponAndEnforcesNewRules()
    {
        var databaseName = $"DoSelectCouponRules_{Guid.NewGuid():N}";
        var connection = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("DOSELECT_SQLSERVER_TEST_CONNECTION") ??
            "Server=.\\SQL2025;Database=DoSelect;Trusted_Connection=True;Encrypt=False;")
        { InitialCatalog = databaseName };
        await using var context = new DoSelectDbContext(new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(connection.ConnectionString).Options);
        try
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20260909074216_AddBuildOwnedParts");
            var now = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
            var publicId = Guid.CreateVersion7();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Coupons
                    (PublicId, Code, NameZhTw, DiscountType, DiscountValue, MinimumSpend,
                     MaximumDiscount, StartsAtUtc, EndsAtUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ({publicId}, N'LEGACY', N'保留既有優惠', 'Percentage', .10, 20000,
                    2000, {now.AddDays(-1)}, {now.AddDays(1)}, {now}, {now});
                """);
            // Exercise the actual sqlcmd deployment packet, not only EF's separately
            // executed migration operations. GO keeps one connection/transaction.
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName,
                       "database-deploy", "coupon-quantity-membership", "02-forward.sql")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var sql = await File.ReadAllTextAsync(Path.Combine(directory.FullName,
                "database-deploy", "coupon-quantity-membership", "02-forward.sql"));
            await context.Database.OpenConnectionAsync();
            foreach (var batch in System.Text.RegularExpressions.Regex.Split(sql, @"^GO\s*$",
                         System.Text.RegularExpressions.RegexOptions.Multiline))
            {
                if (!string.IsNullOrWhiteSpace(batch))
                    await context.Database.ExecuteSqlRawAsync(batch);
            }
            var coupon = await context.Coupons.AsNoTracking().SingleAsync();
            Assert.Equal(publicId, coupon.PublicId);
            Assert.Equal(.10m, coupon.DiscountValue);
            Assert.Equal(2000m, coupon.MaximumDiscount);
            Assert.Null(coupon.MultiItemDiscountValue);
            Assert.Null(coupon.MemberValidityMonths);
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE Coupons SET MultiItemDiscountValue = .15 WHERE Code = 'LEGACY'");
            await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
                "UPDATE Coupons SET MultiItemDiscountValue = .05 WHERE Code = 'LEGACY'"));
            await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
                "UPDATE Coupons SET MemberValidityMonths = 12 WHERE Code = 'LEGACY'"));
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE Coupons SET MemberOnly = 1, MemberValidityMonths = 12 WHERE Code = 'LEGACY'");
            await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
                "UPDATE Coupons SET MemberValidityMonths = 13 WHERE Code = 'LEGACY'"));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
            // This exact randomly named database was created only by this test.
            await context.Database.EnsureDeletedAsync();
        }
    }
}
