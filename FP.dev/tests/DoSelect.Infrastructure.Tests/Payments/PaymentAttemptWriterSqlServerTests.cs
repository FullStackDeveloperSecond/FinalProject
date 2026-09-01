using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Orders;
using DoSelect.Application.Payments;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Payments;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Tests.Idempotency;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests.Payments;

[Trait("Category", "RequiresSqlServer")]
public sealed class PaymentAttemptWriterSqlServerTests
{
    private const string TestPepper = "payment-attempt-writer-tests-pepper-32-bytes";
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTime NowUtc = Now.UtcDateTime;

    [SqlServerFact]
    public async Task CreateAndReplay_AddOneNewAttemptWithoutMovingTheTerminalAttemptBackwards()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seed = await SeedRetryableOrderAsync(context);
            var writer = CreateWriter(context);
            var command = new CreatePaymentAttemptCommand(
                seed.OrderPublicId,
                PaymentMethod.CreditCard,
                seed.OrderRowVersion,
                "payment-retry-key",
                new OrderActor.Member(seed.MemberUserId));

            var first = await writer.CreateAsync(command);
            var replay = await writer.CreateAsync(command);

            Assert.Equal(201, first.StatusCode);
            Assert.False(first.IsReplay);
            Assert.True(replay.IsReplay);
            Assert.Equal(first.Body.PublicId, replay.Body.PublicId);
            Assert.Equal(seed.GrandTotal, first.Body.Amount);
            Assert.Equal(PaymentAttemptStatus.AwaitingPayment, first.Body.Status);
            Assert.Null(first.Body.Instruction);

            context.ChangeTracker.Clear();
            var attempts = await context.PaymentAttempts.AsNoTracking()
                .Where(attempt => attempt.OrderId == seed.OrderId)
                .OrderBy(attempt => attempt.CreatedAtUtc)
                .ThenBy(attempt => attempt.Id)
                .ToListAsync();
            Assert.Equal(2, attempts.Count);
            Assert.Equal(PaymentAttemptStatus.Cancelled, attempts[0].Status);
            Assert.Equal(PaymentAttemptStatus.AwaitingPayment, attempts[1].Status);
            Assert.Equal(seed.GrandTotal, attempts[1].Amount);
        });
    }

    [SqlServerFact]
    public async Task Create_WithStaleOrderVersion_ReturnsConflictAndDoesNotInsert()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seed = await SeedRetryableOrderAsync(context);
            var stale = seed.OrderRowVersion.ToArray();
            stale[^1] ^= 0xff;

            var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
                CreateWriter(context).CreateAsync(new CreatePaymentAttemptCommand(
                    seed.OrderPublicId,
                    PaymentMethod.CreditCard,
                    stale,
                    "payment-stale-key",
                    new OrderActor.Member(seed.MemberUserId))));

            Assert.Equal(PaymentErrorCodes.ConcurrencyConflict, exception.Code);
            context.ChangeTracker.Clear();
            Assert.Single(await context.PaymentAttempts.AsNoTracking()
                .Where(attempt => attempt.OrderId == seed.OrderId)
                .ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task Create_ForAnotherMembersOrder_ReturnsNotFoundAndDoesNotInsert()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seed = await SeedRetryableOrderAsync(context);
            var otherMember = ApplicationUser.CreateMember(
                Guid.CreateVersion7(),
                $"other-payment-{Guid.NewGuid():N}@example.test",
                NowUtc);
            context.Users.Add(otherMember);
            await context.SaveChangesAsync();

            var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
                CreateWriter(context).CreateAsync(new CreatePaymentAttemptCommand(
                    seed.OrderPublicId,
                    PaymentMethod.CreditCard,
                    seed.OrderRowVersion,
                    "payment-not-owner-key",
                    new OrderActor.Member(otherMember.Id))));

            Assert.Equal(PaymentErrorCodes.ResourceNotFound, exception.Code);
            context.ChangeTracker.Clear();
            Assert.Single(await context.PaymentAttempts.AsNoTracking()
                .Where(attempt => attempt.OrderId == seed.OrderId)
                .ToListAsync());
            Assert.Empty(await context.IdempotencyRecords.AsNoTracking().ToListAsync());
        });
    }

    private static PaymentAttemptWriter CreateWriter(DoSelectDbContext context)
    {
        var timeProvider = new FixedTimeProvider(Now);
        var reader = new PaymentAttemptReader(context);
        return new PaymentAttemptWriter(
            context,
            new StartPaymentAttemptService(reader, timeProvider),
            new EfIdempotencyExecutor(
                context,
                Options.Create(new IdempotencyOptions { ActorScopePepper = TestPepper }),
                timeProvider),
            timeProvider);
    }

    private static async Task<SeededOrder> SeedRetryableOrderAsync(DoSelectDbContext context)
    {
        var member = ApplicationUser.CreateMember(
            Guid.CreateVersion7(),
            $"payment-retry-{Guid.NewGuid():N}@example.test",
            NowUtc);
        context.Users.Add(member);
        await context.SaveChangesAsync();

        var profile = new ShippingProviderProfile(
            Guid.CreateVersion7(),
            $"PAY{Guid.NewGuid():N}"[..16],
            1,
            "Active",
            null,
            null,
            "{}",
            1,
            NowUtc);
        context.ShippingProviderProfiles.Add(profile);
        await context.SaveChangesAsync();
        var packageLimit = new PackageLimitVersion(
            Guid.CreateVersion7(), profile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m, null, null, NowUtc);
        context.PackageLimitVersions.Add(packageLimit);
        await context.SaveChangesAsync();

        var order = Order.Create(
            Guid.CreateVersion7(),
            new OrderCreation(
                $"PAY-{Guid.NewGuid():N}"[..32],
                member.Id,
                null,
                OrderStatus.PendingPayment,
                PaymentStatus.AwaitingPayment,
                FulfillmentStatus.Preparing,
                AssemblyStatus.NotRequired,
                1_000m,
                0m,
                0m,
                0m,
                1_000m,
                "Buyer",
                "0912345678",
                "buyer@example.test",
                "100",
                "Taipei",
                "Zhongzheng",
                "No. 1",
                null,
                "HOME",
                profile.Id,
                null,
                null,
                null,
                1,
                1,
                null,
                NowUtc.AddHours(1),
                $"checkout-{Guid.NewGuid():N}",
                null,
                1,
                1,
                new OrderInvoicePreference(
                    DoSelect.Domain.Invoicing.SimulatedInvoiceBuyerType.Individual,
                    "buyer@example.test",
                    null,
                    null,
                    null,
                    null),
                1_000m,
                null,
                new OrderPackageSnapshot(packageLimit.Id, 1m, 40m, 30m, 20m, 90m, 100m)),
            NowUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var terminalAttempt = new PaymentAttempt(
            Guid.CreateVersion7(),
            order.Id,
            PaymentMethod.CreditCard,
            order.GrandTotal,
            "SIMULATED",
            $"initial-{Guid.NewGuid():N}",
            NowUtc.AddMinutes(15),
            NowUtc.AddMinutes(-1));
        terminalAttempt.SetPaymentInstruction(
            "SIM-" + terminalAttempt.PublicId.ToString("N"),
            NowUtc.AddMinutes(-1));
        terminalAttempt.Transition(PaymentAttemptStatus.Cancelled, NowUtc.AddSeconds(-30));
        context.PaymentAttempts.Add(terminalAttempt);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var persisted = await context.Orders.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == order.Id);
        return new SeededOrder(
            persisted.Id,
            persisted.PublicId,
            persisted.RowVersion,
            persisted.GrandTotal,
            member.Id);
    }

    private static async Task RunInMigratedDatabaseAsync(Func<DoSelectDbContext, Task> test)
    {
        var baseConnection = Environment.GetEnvironmentVariable("DOSELECT_SQLSERVER_TEST_CONNECTION") ??
            "Server=.\\SQL2025;Database=DoSelect;Trusted_Connection=True;Encrypt=False;";
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"DoSelectPaymentAttempt_{Guid.NewGuid():N}",
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

    private sealed record SeededOrder(
        long OrderId,
        Guid OrderPublicId,
        byte[] OrderRowVersion,
        decimal GrandTotal,
        string MemberUserId);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
