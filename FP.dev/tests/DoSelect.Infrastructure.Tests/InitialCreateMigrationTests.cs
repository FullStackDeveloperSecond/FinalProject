using DoSelect.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace DoSelect.Infrastructure.Tests;

public sealed class InitialCreateMigrationTests
{
    private static readonly string[] WorkbenchColumns =
    [
        "CasePublicId",
        "CaseType",
        "CaseNumber",
        "Title",
        "Status",
        "Priority",
        "RequesterDisplay",
        "AssigneePublicId",
        "CreatedAtUtc",
        "LastActivityAtUtc",
        "SlaDueAtUtc",
        "IsOverdue",
    ];

    [Fact]
    public void Up_CreatesExpectedTablesWithoutDestructiveOperations()
    {
        var operations = BuildUpOperations();

        Assert.Equal(93, operations.OfType<CreateTableOperation>().Count());
        Assert.Equal(315, operations.OfType<CreateIndexOperation>().Count());
        Assert.Empty(operations.OfType<DropTableOperation>());
        Assert.Empty(operations.OfType<DropColumnOperation>());
        Assert.Empty(operations.OfType<AlterColumnOperation>());
        Assert.Empty(operations.OfType<RenameTableOperation>());
        Assert.Empty(operations.OfType<RenameColumnOperation>());
        Assert.Empty(operations.OfType<DeleteDataOperation>());
        Assert.Empty(operations.OfType<UpdateDataOperation>());
    }

    [Fact]
    public void Up_CreatesCaseWorkbenchWithFixedTwelveColumnContract()
    {
        var sql = Assert.Single(BuildUpOperations().OfType<SqlOperation>()).Sql;

        Assert.Contains("EXEC(N'CREATE VIEW [dbo].[vw_CaseWorkbench]", sql, StringComparison.Ordinal);
        Assert.Contains("CAST(''Support'' AS varchar(16))", sql, StringComparison.Ordinal);
        Assert.Contains("CAST(''Return'' AS varchar(16))", sql, StringComparison.Ordinal);
        Assert.Contains("CAST(''Report'' AS varchar(16))", sql, StringComparison.Ordinal);
        Assert.Contains("CAST(N''會員'' AS nvarchar(200))", sql, StringComparison.Ordinal);
        Assert.Contains("THEN N''訪客''", sql, StringComparison.Ordinal);

        foreach (var column in WorkbenchColumns)
        {
            var expectedAliasCount = column == "SlaDueAtUtc" ? 4 : 3;
            Assert.Equal(expectedAliasCount, Count(sql, $"AS [{column}]"));
        }
    }

    [Fact]
    public void Down_DropsViewBeforeDroppingItsSourceTables()
    {
        var operations = BuildDownOperations();
        var first = Assert.IsType<SqlOperation>(operations[0]);

        Assert.Equal("DROP VIEW IF EXISTS [dbo].[vw_CaseWorkbench];", first.Sql);
        Assert.Equal(93, operations.OfType<DropTableOperation>().Count());
    }

    private static IReadOnlyList<MigrationOperation> BuildUpOperations()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        new TestableInitialCreate().BuildUp(builder);
        return builder.Operations;
    }

    private static IReadOnlyList<MigrationOperation> BuildDownOperations()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        new TestableInitialCreate().BuildDown(builder);
        return builder.Operations;
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class TestableInitialCreate : InitialCreate
    {
        public void BuildUp(MigrationBuilder builder) => Up(builder);

        public void BuildDown(MigrationBuilder builder) => Down(builder);
    }
}
