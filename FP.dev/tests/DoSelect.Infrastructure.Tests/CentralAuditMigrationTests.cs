using System.Net;
using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Idempotency;
using DoSelect.Infrastructure.Auditing;
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

public sealed class CentralAuditMigrationTests
{
    private const string SyntheticConnectionString =
        "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;TrustServerCertificate=True;";

    [Fact]
    public void Model_MapsAppendOnlyAuditTableWithJsonAndRetentionConstraints()
    {
        using var context = new DoSelectDbContext(
            new DbContextOptionsBuilder<DoSelectDbContext>()
                .UseSqlServer(SyntheticConnectionString)
                .Options);
        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(AuditLog))!;

        Assert.Equal("AuditLogs", entity.GetTableName());
        Assert.Null(entity.FindProperty("UpdatedAtUtc"));
        Assert.Null(entity.FindProperty("RowVersion"));
        Assert.Equal("datetime2(3)",
            entity.FindProperty(nameof(AuditLog.RetentionUntilUtc))!.GetColumnType());
        Assert.Contains(entity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_AuditLogs_Json" &&
            constraint.Sql!.Contains("ISJSON", StringComparison.Ordinal));
        Assert.Contains(entity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_AuditLogs_Actor" &&
            constraint.Sql!.Contains("ActorRolesJson", StringComparison.Ordinal));
        Assert.Contains(entity.GetIndexes(), index =>
            index.GetDatabaseName() == "IX_AuditLogs_Retention");
    }

    [Fact]
    public void Up_CreatesOnlyTheCentralAuditTableWithoutDestructiveOperations()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        new TestableMigration().BuildUp(builder);
        var operations = builder.Operations;
        var table = Assert.Single(operations.OfType<CreateTableOperation>());

        Assert.Equal("AuditLogs", table.Name);
        Assert.Equal(6, table.CheckConstraints.Count);
        Assert.Equal(6, operations.OfType<CreateIndexOperation>().Count());
        Assert.Empty(operations.OfType<DropTableOperation>());
        Assert.Empty(operations.OfType<DropColumnOperation>());
        Assert.Empty(operations.OfType<AlterColumnOperation>());
        Assert.Empty(operations.OfType<SqlOperation>());
    }

    private sealed class TestableMigration : AddCentralAuditLogs
    {
        public void BuildUp(MigrationBuilder builder) => Up(builder);
    }
}

[Trait("Category", "RequiresSqlServer")]
public sealed class CentralAuditSqlServerTests
{
    private const string ConnectionStringEnvironmentVariable =
        "DOSELECT_SQLSERVER_TEST_CONNECTION";
    private const string LocalConnectionString =
        "Server=.\\SQL2025;Database=DoSelect;Trusted_Connection=True;Encrypt=False;";
    private static readonly DateTimeOffset Now =
        new(2026, 8, 26, 4, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task CoreAndAuditCommitTogetherAndPersistOnlySanitizedData()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var writer = new EfAuditWriter(context, new FixedTimeProvider(Now));
            await using var transaction = await context.Database.BeginTransactionAsync();
            var idempotency = Idempotency("audit-success");
            context.IdempotencyRecords.Add(idempotency);
            var audit = writer.Add(Request(Guid.NewGuid()));

            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            context.ChangeTracker.Clear();

            Assert.NotNull(await context.IdempotencyRecords.SingleOrDefaultAsync(record =>
                record.Operation == "audit-success"));
            var storedAudit = await context.AuditLogs.SingleAsync(candidate =>
                candidate.PublicId == audit.PublicId);
            Assert.Equal("refund.approved", storedAudit.Reason);
            using var changedFields = JsonDocument.Parse(storedAudit.ChangedFieldsJson);
            Assert.Equal(2, changedFields.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(
                "人工覆核退款資料後執行。",
                changedFields.RootElement.GetProperty("note").GetString());
            Assert.DoesNotContain("@", storedAudit.ChangedFieldsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("token", storedAudit.ChangedFieldsJson,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    [SqlServerFact]
    public async Task AuditInsertFailureRollsBackCoreDataInTheOwnedTransaction()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var duplicatePublicId = Guid.NewGuid();
            var writer = new EfAuditWriter(context, new FixedTimeProvider(Now));
            writer.Add(Request(duplicatePublicId));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                context.IdempotencyRecords.Add(Idempotency("audit-rollback"));
                writer.Add(Request(duplicatePublicId));
                await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
                await transaction.RollbackAsync();
            }

            context.ChangeTracker.Clear();
            Assert.Null(await context.IdempotencyRecords.SingleOrDefaultAsync(record =>
                record.Operation == "audit-rollback"));
            Assert.Equal(1, await context.AuditLogs.CountAsync(candidate =>
                candidate.PublicId == duplicatePublicId));
        });
    }

    private static AuditWriteRequest Request(Guid auditPublicId) =>
        AuditWriteRequest.Create(
            auditPublicId,
            AuditActor.Create(
                AuditActorType.Admin,
                Guid.NewGuid(),
                [AuditRoleNames.FinanceManager]),
            AuditActions.RefundExecute,
            AuditResourceTypes.Refund,
            Guid.NewGuid(),
            AuditResult.Success,
            errorCode: null,
            [
                AuditFieldChange.Code("status", "Approved", "Succeeded"),
                AuditFieldChange.Changed("succeededAmount"),
            ],
            "refund.approved",
            "correlation-audit-test",
            "0123456789abcdef0123456789abcdef",
            jobPublicId: null,
            IPAddress.Parse("203.0.113.42"),
            "人工覆核退款資料後執行。");

    private static IdempotencyRecord Idempotency(string operation) =>
        new(
            new byte[32],
            operation,
            Guid.NewGuid().ToString("N"),
            new byte[32],
            Now.UtcDateTime.AddHours(24),
            Now.UtcDateTime);

    private static async Task RunInMigratedDatabaseAsync(
        Func<DoSelectDbContext, Task> test)
    {
        var connectionString = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ??
            LocalConnectionString)
        {
            InitialCatalog = $"DoSelectAudit_{Guid.NewGuid():N}",
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var context = new DoSelectDbContext(options);
        try
        {
            await context.Database.MigrateAsync();
            await test(context);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
            await context.Database.EnsureDeletedAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
