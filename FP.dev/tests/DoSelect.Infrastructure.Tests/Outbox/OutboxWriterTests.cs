using System.Text.Json;
using DoSelect.Application.Outbox;
using DoSelect.Domain.Outbox;
using DoSelect.Infrastructure.Outbox;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Tests.Outbox;

public sealed class OutboxWriterTests
{
    private const string SyntheticConnectionString =
        "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;TrustServerCertificate=True;";
    private static readonly DateTime Now =
        new(2026, 8, 26, 8, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Add_TracksOnlyTheApprovedPayloadWithoutSaving()
    {
        using var context = CreateContext();
        var writer = new EfOutboxWriter(context, new FixedTimeProvider(Now));
        var orderPublicId = Guid.CreateVersion7();
        var messagePublicId = Guid.CreateVersion7();
        var request = OutboxWriteRequest.Create(
            messagePublicId,
            "Order",
            orderPublicId,
            new EmailNotificationRequestedV1(
                Guid.CreateVersion7(),
                "order.created",
                "order.customer",
                "Order",
                orderPublicId,
                "zh-TW",
                1),
            Now,
            Now,
            "correlation-order-created");

        var message = writer.Add(request);

        Assert.Equal(EntityState.Added, context.Entry(message).State);
        Assert.Equal(messagePublicId, message.PublicId);
        Assert.Equal(OutboxMessageStatus.Pending, message.Status);
        Assert.Equal(0, message.AttemptCount);
        Assert.Equal(Now, message.CreatedAtUtc);
        using var document = JsonDocument.Parse(message.PayloadJson);
        Assert.Equal("order.created", document.RootElement.GetProperty("templateKey").GetString());
        Assert.False(document.RootElement.TryGetProperty("email", out _));
        Assert.DoesNotContain("@", message.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Model_UsesTheApprovedAContractAndDispatcherIndexes()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(OutboxMessage))!;

        Assert.Equal("OutboxMessages", entity.GetTableName());
        Assert.NotNull(entity.FindProperty(nameof(OutboxMessage.PublicId)));
        Assert.NotNull(entity.FindProperty(nameof(OutboxMessage.Type)));
        Assert.NotNull(entity.FindProperty(nameof(OutboxMessage.PayloadVersion)));
        Assert.NotNull(entity.FindProperty(nameof(OutboxMessage.AggregateType)));
        Assert.NotNull(entity.FindProperty(nameof(OutboxMessage.AggregatePublicId)));
        Assert.Null(entity.FindProperty("EventId"));
        Assert.Null(entity.FindProperty("EventType"));
        Assert.Null(entity.FindProperty("SchemaVersion"));
        Assert.True(entity.FindProperty(nameof(OutboxMessage.RowVersion))!.IsConcurrencyToken);
        Assert.Contains(entity.GetIndexes(), index =>
            index.GetDatabaseName() == "IX_OutboxMessages_Status_AvailableAtUtc");
        Assert.Contains(entity.GetIndexes(), index =>
            index.GetDatabaseName() == "IX_OutboxMessages_Aggregate_OccurredAtUtc");
        Assert.Contains(entity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_OutboxMessages_PayloadJson" &&
            constraint.Sql!.Contains("ISJSON", StringComparison.Ordinal));
    }

    [Fact]
    public void Registration_UsesOneScopedWriter()
    {
        var services = new ServiceCollection();

        services.AddDoSelectOutbox();

        var descriptor = Assert.Single(services, candidate =>
            candidate.ServiceType == typeof(IOutboxWriter));
        Assert.Equal(typeof(EfOutboxWriter), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    private static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(SyntheticConnectionString)
            .Options);

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
