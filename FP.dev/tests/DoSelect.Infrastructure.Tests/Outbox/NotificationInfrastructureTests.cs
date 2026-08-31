using DoSelect.Application.Notifications;
using DoSelect.Application.Outbox;
using DoSelect.Domain.Notifications;
using DoSelect.Domain.Outbox;
using DoSelect.Infrastructure.Outbox;
using DoSelect.Infrastructure.Notifications;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Persistence.Migrations;
using DoSelect.Infrastructure.Tests.Idempotency;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using DeliveryState = DoSelect.Domain.Notifications.EmailDeliveryStatus;
using SenderState = DoSelect.Application.Notifications.EmailDeliveryStatus;

namespace DoSelect.Infrastructure.Tests.Outbox;

public sealed class NotificationTemplateTests
{
    [Theory]
    [InlineData("zh-TW")]
    [InlineData("ja-JP")]
    [InlineData("ko-KR")]
    public void PaymentCancelled_HasLocalizedInAppContent(string locale)
    {
        var content = new InAppNotificationContentRenderer().Render(
            new InAppNotificationRequestedV1(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "payment.cancelled",
                "PaymentAttempt",
                Guid.CreateVersion7(),
                locale,
                1));

        Assert.NotNull(content);
        Assert.Equal("payment.cancelled", content.Type);
        Assert.False(string.IsNullOrWhiteSpace(content.Title));
        Assert.False(string.IsNullOrWhiteSpace(content.Body));
    }
}

public sealed class NotificationInfrastructureModelTests
{
    private const string SyntheticConnectionString =
        "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;TrustServerCertificate=True;";

    [Fact]
    public void Model_UsesNotificationPublicIdAsTheEmailIdempotencyKey()
    {
        using var context = new DoSelectDbContext(
            new DbContextOptionsBuilder<DoSelectDbContext>()
                .UseSqlServer(SyntheticConnectionString)
                .Options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var delivery = model.FindEntityType(typeof(EmailDelivery))!;
        var notification = model.FindEntityType(typeof(Notification))!;

        Assert.Contains(delivery.GetIndexes(), index =>
            index.IsUnique &&
            index.GetDatabaseName() == "UX_EmailDeliveries_NotificationPublicId");
        Assert.Contains(delivery.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_EmailDeliveries_State");
        Assert.Contains(notification.GetIndexes(), index =>
            index.GetDatabaseName() ==
            "IX_Notifications_RecipientUserId_ReadAtUtc_CreatedAtUtc");
    }

    [Fact]
    public void Migration_CreatesOnlyNotificationAndEmailDeliveryTables()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        new TestableMigration().BuildUp(builder);

        Assert.Equal(
            ["EmailDeliveries", "Notifications"],
            builder.Operations.OfType<CreateTableOperation>()
                .Select(operation => operation.Name)
                .OrderBy(name => name));
        Assert.Equal(5, builder.Operations.OfType<CreateIndexOperation>().Count());
        Assert.Empty(builder.Operations.OfType<DropTableOperation>());
        Assert.Empty(builder.Operations.OfType<DropColumnOperation>());
        Assert.Empty(builder.Operations.OfType<AlterColumnOperation>());
        Assert.Empty(builder.Operations.OfType<SqlOperation>());
    }

    private sealed class TestableMigration : AddNotificationDeliveryInfrastructure
    {
        public void BuildUp(MigrationBuilder builder) => Up(builder);
    }
}

[Trait("Category", "RequiresSqlServer")]
public sealed class NotificationInfrastructureSqlServerTests
{
    private static readonly DateTime Now =
        new(2026, 8, 27, 2, 0, 0, DateTimeKind.Utc);

    [SqlServerFact]
    public async Task Dispatcher_PreservesAggregateOrderAndQueuesDifferentAggregates()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var writer = new EfOutboxWriter(context, new MutableTimeProvider(Now));
            var aggregateA = Guid.CreateVersion7();
            var aggregateB = Guid.CreateVersion7();
            var firstA = writer.Add(InAppRequest(aggregateA, Now.AddMinutes(-3)));
            var secondA = writer.Add(InAppRequest(aggregateA, Now.AddMinutes(-2)));
            var firstB = writer.Add(InAppRequest(aggregateB, Now.AddMinutes(-1)));
            await context.SaveChangesAsync();

            var jobs = new CapturingBackgroundJobClient();
            var dispatcher = new OutboxDispatcher(
                context,
                jobs,
                Options.Create(new BackgroundJobSettings()),
                new MutableTimeProvider(Now),
                NullLogger<OutboxDispatcher>.Instance);

            var dispatched = await dispatcher.DispatchBatchAsync();
            context.ChangeTracker.Clear();

            Assert.Equal(2, dispatched);
            Assert.Equal(2, jobs.Created.Count);
            Assert.All(jobs.Created, item => Assert.Equal("notifications", item.State.Queue));
            Assert.Equal(
                OutboxMessageStatus.Processing,
                (await context.OutboxMessages.SingleAsync(item => item.PublicId == firstA.PublicId)).Status);
            Assert.Equal(
                OutboxMessageStatus.Pending,
                (await context.OutboxMessages.SingleAsync(item => item.PublicId == secondA.PublicId)).Status);
            Assert.Equal(
                OutboxMessageStatus.Processing,
                (await context.OutboxMessages.SingleAsync(item => item.PublicId == firstB.PublicId)).Status);
        });
    }

    [SqlServerFact]
    public async Task InAppConsumer_IsIdempotentByNotificationPublicId()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var memberPublicId = Guid.CreateVersion7();
            context.Users.Add(ApplicationUser.CreateMember(
                memberPublicId,
                "member@example.test",
                Now));
            var writer = new EfOutboxWriter(context, new MutableTimeProvider(Now));
            var message = writer.Add(InAppRequest(
                Guid.CreateVersion7(),
                Now,
                memberPublicId));
            await context.SaveChangesAsync();
            message.Claim(Now, Now.AddMinutes(1));
            await context.SaveChangesAsync();

            var renderer = new FixedInAppRenderer();
            var consumer = new InAppNotificationOutboxConsumer(
                context,
                renderer,
                new MutableTimeProvider(Now));

            Assert.True((await consumer.ConsumeAsync(message)).Succeeded);
            Assert.True((await consumer.ConsumeAsync(message)).Succeeded);
            Assert.Equal(1, await context.Notifications.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task EmailConsumer_RetriesKnownTransientFailureWithoutDuplicatingDelivery()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var clock = new MutableTimeProvider(Now);
            var writer = new EfOutboxWriter(context, clock);
            var message = writer.Add(EmailRequest(Guid.CreateVersion7(), Now));
            await context.SaveChangesAsync();
            message.Claim(Now, Now.AddMinutes(1));
            await context.SaveChangesAsync();

            var sender = new ScriptedEmailSender(
                new EmailDeliveryResult(
                    SenderState.TransientFailure,
                    ErrorCode: EmailDeliveryErrorCodes.TransportUnavailable),
                new EmailDeliveryResult(SenderState.Sent, "provider-id"));
            var consumer = new EmailNotificationOutboxConsumer(
                context,
                new FixedEmailResolver(),
                sender,
                clock,
                NullLogger<EmailNotificationOutboxConsumer>.Instance);

            var first = await consumer.ConsumeAsync(message);
            Assert.True(first.ShouldRetry);
            Assert.Equal(TimeSpan.FromMinutes(1), first.RetryDelay);

            clock.UtcNow = Now.AddMinutes(1);
            var second = await consumer.ConsumeAsync(message);
            Assert.True(second.Succeeded);
            var third = await consumer.ConsumeAsync(message);
            Assert.True(third.Succeeded);

            var delivery = await context.EmailDeliveries.SingleAsync();
            Assert.Equal(DeliveryState.Sent, delivery.Status);
            Assert.Equal(2, delivery.AttemptCount);
            Assert.Equal(2, sender.SendCount);
            Assert.Equal(1, await context.EmailDeliveries.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task RetentionJob_DeletesOnlyProcessedMessagesOlderThanThirtyDays()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var writer = new EfOutboxWriter(context, new MutableTimeProvider(Now));
            var expired = writer.Add(InAppRequest(Guid.CreateVersion7(), Now.AddDays(-31)));
            var retained = writer.Add(InAppRequest(Guid.CreateVersion7(), Now.AddDays(-29)));
            var failed = writer.Add(InAppRequest(Guid.CreateVersion7(), Now.AddDays(-40)));
            await context.SaveChangesAsync();

            expired.Claim(Now.AddDays(-31), Now.AddDays(-31).AddMinutes(1));
            expired.Complete(Now.AddDays(-31).AddMinutes(1));
            retained.Claim(Now.AddDays(-29), Now.AddDays(-29).AddMinutes(1));
            retained.Complete(Now.AddDays(-29).AddMinutes(1));
            failed.Claim(Now.AddDays(-40), Now.AddDays(-40).AddMinutes(1));
            failed.Fail("permanent_failure");
            await context.SaveChangesAsync();

            var deleted = await new OutboxRetentionJob(
                context,
                new MutableTimeProvider(Now)).RunAsync(CancellationToken.None);

            Assert.Equal(1, deleted);
            Assert.False(await context.OutboxMessages.AnyAsync(item => item.PublicId == expired.PublicId));
            Assert.True(await context.OutboxMessages.AnyAsync(item => item.PublicId == retained.PublicId));
            Assert.True(await context.OutboxMessages.AnyAsync(item => item.PublicId == failed.PublicId));
        });
    }

    private static OutboxWriteRequest InAppRequest(
        Guid aggregatePublicId,
        DateTime occurredAtUtc,
        Guid? memberPublicId = null) =>
        OutboxWriteRequest.Create(
            Guid.CreateVersion7(),
            "Order",
            aggregatePublicId,
            new InAppNotificationRequestedV1(
                Guid.CreateVersion7(),
                memberPublicId ?? Guid.CreateVersion7(),
                "order.created",
                "Order",
                aggregatePublicId,
                "zh-TW",
                1),
            occurredAtUtc,
            occurredAtUtc,
            "correlation-notification-test");

    private static OutboxWriteRequest EmailRequest(Guid aggregatePublicId, DateTime occurredAtUtc) =>
        OutboxWriteRequest.Create(
            Guid.CreateVersion7(),
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
            occurredAtUtc,
            occurredAtUtc,
            "correlation-email-test");

    private static async Task RunInMigratedDatabaseAsync(Func<DoSelectDbContext, Task> test)
    {
        var connectionString = SqlServerTestConnection.Build(
            $"DoSelectNotification_{Guid.NewGuid():N}");
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

    private sealed class MutableTimeProvider(DateTime utcNow) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }

    private sealed class FixedInAppRenderer : IInAppNotificationContentRenderer
    {
        public InAppNotificationContent Render(InAppNotificationRequestedV1 request) =>
            new(request.MessageKey, "訂單已成立", "請查看訂單內容。");
    }

    private sealed class FixedEmailResolver : IEmailNotificationContentResolver
    {
        public Task<EmailNotificationContent?> ResolveAsync(
            EmailNotificationRequestedV1 request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailNotificationContent?>(new(
                null,
                new EmailMessage("customer@example.test", "訂單已成立", "請查看訂單內容。")));
    }

    private sealed class ScriptedEmailSender(params EmailDeliveryResult[] results) : IEmailSender
    {
        private readonly Queue<EmailDeliveryResult> _results = new(results);

        public int SendCount { get; private set; }

        public Task<EmailDeliveryResult> SendAsync(
            EmailMessage message,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class CapturingBackgroundJobClient : IBackgroundJobClient
    {
        public List<(Job Job, EnqueuedState State)> Created { get; } = [];

        public string Create(Job job, IState state)
        {
            Created.Add((job, Assert.IsType<EnqueuedState>(state)));
            return Created.Count.ToString();
        }

        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }
}
