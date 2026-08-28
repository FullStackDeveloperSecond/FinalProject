using DoSelect.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace DoSelect.Infrastructure.Tests.Catalog;

public sealed class ProductImageVariantHashMigrationTests
{
    [Fact]
    public void Up_AddsOnlyThreeNullableBinaryHashes()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        new TestableMigration().BuildUp(builder);

        var columns = builder.Operations.OfType<AddColumnOperation>().ToArray();
        Assert.Equal(3, columns.Length);
        Assert.Equal(
            ["LargeSha256", "MediumSha256", "SmallSha256"],
            columns.Select(column => column.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.All(columns, column =>
        {
            Assert.Equal("ProductImages", column.Table);
            Assert.Equal("binary(32)", column.ColumnType);
            Assert.True(column.IsNullable);
        });
        Assert.Empty(builder.Operations.OfType<DropColumnOperation>());
        Assert.Empty(builder.Operations.OfType<AlterColumnOperation>());
        Assert.Empty(builder.Operations.OfType<SqlOperation>());
    }

    private sealed class TestableMigration : AddProductImageVariantHashes
    {
        public void BuildUp(MigrationBuilder builder) => Up(builder);
    }
}
