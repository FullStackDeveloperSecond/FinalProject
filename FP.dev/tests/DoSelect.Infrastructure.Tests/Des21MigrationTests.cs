using DoSelect.Domain.Orders;
using DoSelect.Domain.Promotions;
using DoSelect.Domain.Refunds;
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

public sealed class Des21MigrationTests
{
    private const string SyntheticConnectionString =
        "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;TrustServerCertificate=True;";

    [Fact]
    public void Model_MapsTrustedRefundSnapshotsWithTheRequiredShape()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var order = model.FindEntityType(typeof(Order))!;
        var item = model.FindEntityType(typeof(OrderItem))!;
        var coupon = model.FindEntityType(typeof(OrderCoupon))!;
        var allocation = model.FindEntityType(typeof(RefundAllocation))!;

        Assert.True(item.FindProperty(nameof(OrderItem.IsCouponEligible))!.IsNullable is false);
        Assert.Equal(ValueGenerated.Never,
            item.FindProperty(nameof(OrderItem.IsCouponEligible))!.ValueGenerated);
        Assert.Null(item.FindProperty(nameof(OrderItem.IsCouponEligible))!.GetDefaultValueSql());
        Assert.Equal("decimal(18,2)",
            order.FindProperty(nameof(Order.ShippingFreeThresholdSnapshot))!.GetColumnType());
        Assert.True(order.FindProperty(nameof(Order.ShippingFreeThresholdSnapshot))!.IsNullable);
        Assert.Equal("decimal(18,2)",
            coupon.FindProperty(nameof(OrderCoupon.MinimumSpendAmount))!.GetColumnType());
        Assert.True(coupon.FindProperty(nameof(OrderCoupon.MinimumSpendAmount))!.IsNullable);
        Assert.True(allocation.FindProperty(nameof(RefundAllocation.Quantity))!.IsNullable);
        Assert.Contains(allocation.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_RefundAllocations_TypeAndShape" &&
            constraint.Sql!.Contains("[Quantity] > 0", StringComparison.Ordinal));
    }

    [Fact]
    public void Up_AddsOnlyTheApprovedColumnsAndFailsBeforeGuessingHistoricalQuantity()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        new TestableMigration().BuildUp(builder);
        var operations = builder.Operations;

        Assert.Equal(
            ["Quantity", "ShippingFreeThresholdSnapshot", "IsCouponEligible", "MinimumSpendAmount"],
            operations.OfType<AddColumnOperation>().Select(operation => operation.Name));
        Assert.Empty(operations.OfType<DropColumnOperation>());
        Assert.Empty(operations.OfType<AlterColumnOperation>());
        Assert.Empty(operations.OfType<DeleteDataOperation>());
        Assert.Empty(operations.OfType<UpdateDataOperation>());
        Assert.Null(operations.OfType<AddColumnOperation>().Single(operation =>
            operation.Name == "IsCouponEligible").DefaultValue);
        var preflights = operations.OfType<SqlOperation>().Select(operation => operation.Sql)
            .ToArray();
        Assert.Equal(2, preflights.Length);
        Assert.Contains("THROW 51020", preflights[0], StringComparison.Ordinal);
        Assert.Contains("historical coupon eligibility must not be guessed", preflights[0],
            StringComparison.Ordinal);
        Assert.Contains("THROW 51021", preflights[1], StringComparison.Ordinal);
        Assert.Contains("cannot infer trusted RefundAllocations.Quantity", preflights[1],
            StringComparison.Ordinal);
    }

    private static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(SyntheticConnectionString)
            .Options);

    private sealed class TestableMigration : AddDes21RefundSnapshots
    {
        public void BuildUp(MigrationBuilder builder) => Up(builder);
    }
}

[Trait("Category", "RequiresSqlServer")]
public sealed class Des21MigrationSqlServerTests
{
    private const string ConnectionStringEnvironmentVariable =
        "DOSELECT_SQLSERVER_TEST_CONNECTION";
    private const string LocalConnectionString =
        "Server=.\\SQL2025;Database=DoSelect;Trusted_Connection=True;Encrypt=False;";

    [SqlServerFact]
    public async Task EmptyDatabase_MigratesToTheCurrentModelWithDes21Constraints()
    {
        var databaseName = $"DoSelectDes21_{Guid.NewGuid():N}";
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
            await context.Database.MigrateAsync();

            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.Equal(4, await ScalarAsync(context,
                """
                SELECT COUNT(*)
                FROM sys.columns AS c
                INNER JOIN sys.tables AS t ON t.object_id = c.object_id
                WHERE (t.name = 'RefundAllocations' AND c.name = 'Quantity')
                   OR (t.name = 'Orders' AND c.name = 'ShippingFreeThresholdSnapshot')
                   OR (t.name = 'OrderItems' AND c.name = 'IsCouponEligible')
                   OR (t.name = 'OrderCoupons' AND c.name = 'MinimumSpendAmount');
                """));
            Assert.Equal(3, await ScalarAsync(context,
                """
                SELECT COUNT(*)
                FROM sys.check_constraints
                WHERE name IN
                    ('CK_RefundAllocations_TypeAndShape',
                     'CK_Orders_ShippingFreeThresholdSnapshot',
                     'CK_OrderCoupons_Amounts');
                """));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<int> ScalarAsync(DoSelectDbContext context, string sql)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
