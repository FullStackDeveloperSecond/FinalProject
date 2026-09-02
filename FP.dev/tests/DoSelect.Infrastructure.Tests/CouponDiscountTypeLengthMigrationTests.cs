using DoSelect.Domain.Promotions;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Migrations;
using DoSelect.Infrastructure.Tests.Idempotency;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace DoSelect.Infrastructure.Tests;

public sealed class CouponDiscountTypeLengthMigrationTests
{
    private const string SyntheticConnectionString =
        "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;TrustServerCertificate=True;";

    [Fact]
    public void Model_MapsBothCouponDiscountTypeColumnsToVarchar24()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        AssertDiscountType(model.FindEntityType(typeof(Coupon))!);
        AssertDiscountType(model.FindEntityType(typeof(OrderCoupon))!);
    }

    [Fact]
    public void Up_WidensOnlyTheTwoDiscountTypeColumns()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        new TestableMigration().BuildUp(builder);

        var operations = builder.Operations.OfType<AlterColumnOperation>().ToArray();
        Assert.Equal(2, operations.Length);
        Assert.Equal(["Coupons", "OrderCoupons"], operations.Select(operation => operation.Table).Order());
        Assert.All(operations, operation =>
        {
            Assert.Equal("DiscountType", operation.Name);
            Assert.Equal("varchar(24)", operation.ColumnType);
            Assert.Equal(24, operation.MaxLength);
            Assert.Equal("varchar(16)", operation.OldColumn.ColumnType);
            Assert.Equal(16, operation.OldColumn.MaxLength);
            Assert.False(operation.IsNullable);
        });
        Assert.DoesNotContain(builder.Operations, operation => operation is not AlterColumnOperation);
    }

    private static void AssertDiscountType(IReadOnlyEntityType entityType)
    {
        var property = entityType.FindProperty("DiscountType")!;
        Assert.Equal(24, property.GetMaxLength());
        Assert.Equal("varchar(24)", property.GetColumnType());
        Assert.False(property.IsNullable);
    }

    private static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(SyntheticConnectionString)
            .Options);

    private sealed class TestableMigration : WidenCouponDiscountTypeColumns
    {
        public void BuildUp(MigrationBuilder builder) => Up(builder);
    }
}

[Trait("Category", "RequiresSqlServer")]
public sealed class CouponDiscountTypeLengthMigrationSqlServerTests
{
    private const string PreviousMigration = "20260829040926_AddInventoryMovementUnitCostSnapshot";
    private const string ConnectionStringEnvironmentVariable = "DOSELECT_SQLSERVER_TEST_CONNECTION";
    private const string LocalConnectionString =
        "Server=.\\SQL2025;Database=DoSelect;Trusted_Connection=True;Encrypt=False;";

    [SqlServerFact]
    public async Task ExistingCouponSurvivesWidening_AndAssemblyFreeShippingCanBeStored()
    {
        var databaseName = $"DoSelectCouponDiscountType_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ??
            LocalConnectionString)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var context = new DoSelectDbContext(options);
        try
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            var now = DateTime.UtcNow;
            var existing = CreateCoupon("BEFOREWIDEN", CouponDiscountType.FixedAmount, 100m, now);
            context.Coupons.Add(existing);
            await context.SaveChangesAsync();

            await migrator.MigrateAsync();
            context.ChangeTracker.Clear();

            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.Equal(2, await ScalarAsync(context,
                """
                SELECT COUNT(*)
                FROM sys.columns AS c
                INNER JOIN sys.tables AS t ON t.object_id = c.object_id
                WHERE t.name IN ('Coupons', 'OrderCoupons')
                  AND c.name = 'DiscountType'
                  AND TYPE_NAME(c.user_type_id) = 'varchar'
                  AND c.max_length = 24;
                """));
            Assert.Equal(CouponDiscountType.FixedAmount,
                (await context.Coupons.SingleAsync(coupon => coupon.Code == "BEFOREWIDEN")).DiscountType);

            var assembly = CreateCoupon(
                "ASSEMBLYFREE",
                CouponDiscountType.AssemblyFreeShipping,
                discountValue: null,
                now);
            context.Coupons.Add(assembly);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            Assert.Equal(CouponDiscountType.AssemblyFreeShipping,
                (await context.Coupons.SingleAsync(coupon => coupon.Code == "ASSEMBLYFREE")).DiscountType);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static Coupon CreateCoupon(
        string code,
        CouponDiscountType discountType,
        decimal? discountValue,
        DateTime now)
    {
        var coupon = new Coupon(
            Guid.CreateVersion7(),
            new CouponCreation(
                code,
                code,
                discountType,
                discountValue,
                0m,
                null,
                now.AddDays(-1),
                now.AddDays(1),
                100,
                100,
                false,
                false,
                CouponScopeType.All),
            now);
        coupon.ActivateNow(CouponUsageState.Unused, now);
        return coupon;
    }

    private static async Task<int> ScalarAsync(DoSelectDbContext context, string sql)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
