using DoSelect.Application.Outbox;
using DoSelect.Domain.Idempotency;
using DoSelect.Domain.Outbox;
using DoSelect.Infrastructure.Outbox;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Migrations;
using DoSelect.Infrastructure.Tests.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace DoSelect.Infrastructure.Tests.Outbox;

public sealed class OutboxMigrationTests
{
    [Fact]
    public void Up_CreatesOnlyTheTransactionalOutboxTable()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        new TestableMigration().BuildUp(builder);

        var table = Assert.Single(builder.Operations.OfType<CreateTableOperation>());
        Assert.Equal("OutboxMessages", table.Name);
        Assert.Equal(5, table.CheckConstraints.Count);
        Assert.Equal(3, builder.Operations.OfType<CreateIndexOperation>().Count());

        var payload = Assert.Single(table.Columns, column => column.Name == "PayloadJson");
        Assert.Equal("varchar(8000)", payload.ColumnType);
        Assert.Equal(8000, payload.MaxLength);
        Assert.False(payload.IsUnicode);

        Assert.Empty(builder.Operations.OfType<DropTableOperation>());
        Assert.Empty(builder.Operations.OfType<DropColumnOperation>());
        Assert.Empty(builder.Operations.OfType<AlterColumnOperation>());
        Assert.Empty(builder.Operations.OfType<SqlOperation>());
    }

    private sealed class TestableMigration : AddTransactionalOutbox
    {
        public void BuildUp(MigrationBuilder builder) => Up(builder);
    }
}

[Trait("Category", "RequiresSqlServer")]
public sealed class OutboxSqlServerTests
{
    private static readonly DateTime Now =
        new(2026, 8, 26, 8, 30, 0, DateTimeKind.Utc);

    [SqlServerFact]
    public async Task BusinessRecordAndOutboxMessageCommitTogether()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var writer = new EfOutboxWriter(context, new FixedTimeProvider(Now));
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.IdempotencyRecords.Add(Idempotency("outbox-success"));
            var message = writer.Add(Request(Guid.CreateVersion7()));

            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            context.ChangeTracker.Clear();

            Assert.NotNull(await context.IdempotencyRecords.SingleOrDefaultAsync(record =>
                record.Operation == "outbox-success"));
            var stored = await context.OutboxMessages.SingleAsync(candidate =>
                candidate.PublicId == message.PublicId);
            Assert.Equal(OutboxMessageStatus.Pending, stored.Status);
            Assert.Equal(OutboxEventTypes.EmailNotificationRequestedV1, stored.Type);
        });
    }

    [SqlServerFact]
    public async Task OutboxInsertFailureRollsBackBusinessRecord()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var duplicatePublicId = Guid.CreateVersion7();
            var writer = new EfOutboxWriter(context, new FixedTimeProvider(Now));
            writer.Add(Request(duplicatePublicId));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                context.IdempotencyRecords.Add(Idempotency("outbox-rollback"));
                writer.Add(Request(duplicatePublicId));
                await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
                await transaction.RollbackAsync();
            }

            context.ChangeTracker.Clear();
            Assert.Null(await context.IdempotencyRecords.SingleOrDefaultAsync(record =>
                record.Operation == "outbox-rollback"));
            Assert.Equal(1, await context.OutboxMessages.CountAsync(candidate =>
                candidate.PublicId == duplicatePublicId));
        });
    }

    private static OutboxWriteRequest Request(Guid publicId)
    {
        var aggregatePublicId = Guid.CreateVersion7();
        return OutboxWriteRequest.Create(
            publicId,
            "Order",
            aggregatePublicId,
            new EmailNotificationRequestedV1(
                Guid.CreateVersion7(),
                "order.created",
                "order.customer",
                "Order",
                aggregatePublicId,
                "zh-TW",
                1),
            Now,
            Now,
            "correlation-outbox-test");
    }

    private static IdempotencyRecord Idempotency(string operation) =>
        new(
            new byte[32],
            operation,
            Guid.NewGuid().ToString("N"),
            new byte[32],
            Now.AddHours(24),
            Now);

    private static async Task RunInMigratedDatabaseAsync(
        Func<DoSelectDbContext, Task> test)
    {
        var connectionString = SqlServerTestConnection.Build(
            $"DoSelectOutbox_{Guid.NewGuid():N}");
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

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
