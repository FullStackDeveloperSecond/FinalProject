using DoSelect.Domain.Orders;
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

public sealed class OrderShippingMethodBaseFeeSnapshotMigrationTests
{
    private const string SyntheticConnectionString =
        "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;TrustServerCertificate=True;";

    [Fact]
    public void Model_MapsNullableNonNegativeShippingMethodBaseFeeSnapshot()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var order = model.FindEntityType(typeof(Order))!;
        var property = order.FindProperty(nameof(Order.ShippingMethodBaseFeeSnapshot))!;

        Assert.Equal("decimal(18,2)", property.GetColumnType());
        Assert.True(property.IsNullable);
        Assert.Contains(order.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_Orders_ShippingMethodBaseFeeSnapshot" &&
            constraint.Sql ==
                "[ShippingMethodBaseFeeSnapshot] IS NULL OR [ShippingMethodBaseFeeSnapshot] >= 0");
    }

    [Fact]
    public void Up_AddsOnlyNullableSnapshotAndConstraintWithoutBackfill()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        new TestableMigration().BuildUp(builder);

        var column = Assert.Single(builder.Operations.OfType<AddColumnOperation>());
        Assert.Equal("Orders", column.Table);
        Assert.Equal("ShippingMethodBaseFeeSnapshot", column.Name);
        Assert.Equal("decimal(18,2)", column.ColumnType);
        Assert.True(column.IsNullable);
        Assert.Null(column.DefaultValue);
        Assert.Null(column.DefaultValueSql);

        var constraint = Assert.Single(builder.Operations.OfType<AddCheckConstraintOperation>());
        Assert.Equal("Orders", constraint.Table);
        Assert.Equal("CK_Orders_ShippingMethodBaseFeeSnapshot", constraint.Name);

        Assert.Empty(builder.Operations.OfType<SqlOperation>());
        Assert.Empty(builder.Operations.OfType<UpdateDataOperation>());
        Assert.Empty(builder.Operations.OfType<DeleteDataOperation>());
        Assert.Empty(builder.Operations.OfType<AlterColumnOperation>());
    }

    private static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(SyntheticConnectionString)
            .Options);

    private sealed class TestableMigration : AddOrderShippingMethodBaseFeeSnapshot
    {
        public void BuildUp(MigrationBuilder builder) => Up(builder);
    }
}

[Trait("Category", "RequiresSqlServer")]
public sealed class OrderShippingMethodBaseFeeSnapshotMigrationSqlServerTests
{
    private const string ConnectionStringEnvironmentVariable =
        "DOSELECT_SQLSERVER_TEST_CONNECTION";
    private const string LocalConnectionString =
        "Server=.\\SQL2025;Database=DoSelect;Trusted_Connection=True;Encrypt=False;";

    [SqlServerFact]
    public async Task EmptyDatabase_MigratesWithNullableShippingMethodBaseFeeSnapshot()
    {
        var databaseName = $"DoSelectShippingBaseFeeSnapshot_{Guid.NewGuid():N}";
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
            Assert.Equal(1, await ScalarAsync(context,
                """
                SELECT COUNT(*)
                FROM sys.columns AS c
                INNER JOIN sys.tables AS t ON t.object_id = c.object_id
                WHERE t.name = 'Orders'
                  AND c.name = 'ShippingMethodBaseFeeSnapshot'
                  AND c.is_nullable = 1;
                """));
            Assert.Equal(1, await ScalarAsync(context,
                """
                SELECT COUNT(*)
                FROM sys.check_constraints
                WHERE name = 'CK_Orders_ShippingMethodBaseFeeSnapshot';
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
