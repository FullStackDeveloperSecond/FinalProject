using DoSelect.Application.Common;
using DoSelect.Application.Auditing;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Payments;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Outbox;
using DoSelect.Infrastructure.Payments;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Tests.Idempotency;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests.Payments;

/// <summary>
/// 模擬付款完成的寫入路徑，對真實 SQL Server 驗證。
/// </summary>
/// <remarks>
/// 這裡證明的是 <b>Application 層的假物件測試證明不了的事</b>：付款嘗試與訂單
/// 真的在同一個交易裡改變、重播不會付第二次款、任何一步失敗整批回滾。
/// </remarks>
[Trait("Category", "RequiresSqlServer")]
public sealed class SimulatedPaymentWriterSqlServerTests
{
    private const string ConnectionStringEnvironmentVariable =
        "DOSELECT_SQLSERVER_TEST_CONNECTION";
    private const string LocalConnectionString =
        "Server=.\\SQL2025;Database=DoSelect;Trusted_Connection=True;Encrypt=False;";
    private const string TestPepper = "simulated-payment-tests-actor-scope-pepper";

    private static readonly DateTimeOffset Now = new(2026, 8, 30, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTime NowUtc = Now.UtcDateTime;

    [SqlServerFact]
    public async Task SucceedingPaysTheAttemptAndTheOrderInOneTransaction()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            var writer = CreateWriter(context);

            var result = await writer.CompleteAsync(Command(seeded, "sim-pay-success"));

            Assert.False(result.IsReplay);
            Assert.Equal(200, result.StatusCode);
            Assert.Equal(PaymentAttemptStatus.Paid, result.Body.Status);

            // 從資料庫重讀，不是看記憶體裡的實體 —— 要證明的是它真的落地了。
            var (attempt, order) = await ReloadAsync(context, seeded);
            Assert.Equal(PaymentAttemptStatus.Paid, attempt.Status);
            Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
            Assert.Equal(OrderStatus.Confirmed, order.OrderStatus);
            Assert.Equal(NowUtc, attempt.PaidAtUtc);
            Assert.Equal(NowUtc, order.PaidAtUtc);

            var paymentEvent = await context.PaymentEvents.AsNoTracking().SingleAsync();
            Assert.Equal(PaymentEventProcessingStatus.Processed, paymentEvent.ProcessingStatus);
            Assert.Equal("payment.succeeded", paymentEvent.EventType);
            Assert.DoesNotContain("sim-pay-success", paymentEvent.PayloadSummaryJson);
            Assert.Equal(2, await context.OrderStatusHistories.CountAsync());
            Assert.Single(await context.AuditLogs.AsNoTracking().ToArrayAsync());
            Assert.Equal(2, await context.OutboxMessages.CountAsync());
        });
    }

    [SqlServerTheory]
    [InlineData(PaymentMethod.CreditCard)]
    [InlineData(PaymentMethod.ATM)]
    [InlineData(PaymentMethod.ConvenienceCode)]
    [InlineData(PaymentMethod.CashOnDelivery)]
    [InlineData(PaymentMethod.LinePay)]
    [InlineData(PaymentMethod.ApplePay)]
    [InlineData(PaymentMethod.GooglePay)]
    public async Task EveryCheckoutPaymentMethodCanCompleteFromItsRealisticOrderStatus(
        PaymentMethod method)
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context, method: method);

            await CreateWriter(context).CompleteAsync(Command(seeded, $"method-{method}"));

            var (attempt, order) = await ReloadAsync(context, seeded);
            Assert.Equal(PaymentAttemptStatus.Paid, attempt.Status);
            Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
            Assert.Equal(OrderStatus.Confirmed, order.OrderStatus);
        });
    }

    [SqlServerFact]
    public async Task TheAmountChainHoldsAcrossTheOrderAndTheAttempt()
    {
        // alex Issue #65 C1 要釘住的：Order.GrandTotal = PaymentAttempt.Amount = Order.PaidAmount。
        // 在這之前 Order.PaidAmount 沒有任何production 程式碼寫過，永遠是 0。
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            var writer = CreateWriter(context);

            await writer.CompleteAsync(Command(seeded, "sim-pay-amount"));

            var (attempt, order) = await ReloadAsync(context, seeded);
            Assert.Equal(order.GrandTotal, attempt.Amount);
            Assert.Equal(attempt.Amount, order.PaidAmount);
            Assert.NotEqual(0m, order.PaidAmount);
        });
    }

    [SqlServerFact]
    public async Task ReplayingTheSameSimulationKeyDoesNotPayTwice()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            var writer = CreateWriter(context);
            var command = Command(seeded, "sim-pay-replay");

            var first = await writer.CompleteAsync(command);
            var second = await writer.CompleteAsync(command);

            Assert.False(first.IsReplay);
            Assert.True(second.IsReplay);
            Assert.Equal(first.Body.PublicId, second.Body.PublicId);

            // 第二次沒有再走一次狀態機，也沒有把金額加上去。
            var (_, order) = await ReloadAsync(context, seeded);
            Assert.Equal(order.GrandTotal, order.PaidAmount);
            Assert.Single(await context.PaymentEvents.AsNoTracking().ToArrayAsync());
            Assert.Single(await context.AuditLogs.AsNoTracking().ToArrayAsync());
            Assert.Equal(2, await context.OutboxMessages.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task TheSameKeyWithADifferentOutcomeIsAConflictNotAReplay()
    {
        // Request Hash 涵蓋 outcome。少了它，換一個結果會拿回上一次的回應，
        // 呼叫端會以為自己把付款標成失敗了，其實它還是已付款。
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            var writer = CreateWriter(context);

            await writer.CompleteAsync(Command(seeded, "sim-pay-conflict"));

            var conflict = await Assert.ThrowsAsync<IdempotencyConflictException>(
                () => writer.CompleteAsync(Command(
                    seeded, "sim-pay-conflict", SimulatedPaymentOutcome.Failed)));

            Assert.Equal(IdempotencyErrorCodes.PayloadConflict, conflict.ErrorCode);
        });
    }

    [SqlServerFact]
    public async Task FailingLeavesTheOrderUnpaidAndRecordsAFailureCode()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            var writer = CreateWriter(context);

            var result = await writer.CompleteAsync(
                Command(seeded, "sim-pay-failed", SimulatedPaymentOutcome.Failed));

            Assert.Equal(PaymentAttemptStatus.Failed, result.Body.Status);

            var (attempt, order) = await ReloadAsync(context, seeded);
            Assert.Equal(
                SimulatedPaymentWriteConstants.SimulatedFailureCode,
                attempt.FailureCode);
            Assert.Equal(PaymentStatus.Failed, order.PaymentStatus);
            Assert.Equal(0m, order.PaidAmount);
            Assert.Null(order.PaidAtUtc);
        });
    }

    [SqlServerFact]
    public async Task ExpiringMarksBothTheAttemptAndTheOrderExpired()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            var writer = CreateWriter(context);

            await writer.CompleteAsync(
                Command(seeded, "sim-pay-expired", SimulatedPaymentOutcome.Expired));

            var (attempt, order) = await ReloadAsync(context, seeded);
            Assert.Equal(PaymentAttemptStatus.Expired, attempt.Status);
            Assert.Equal(PaymentStatus.Expired, order.PaymentStatus);
            Assert.Equal(0m, order.PaidAmount);
        });
    }

    [SqlServerFact]
    public async Task AnExpiredInstructionCannotBePaidAndChangesNothing()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            // 付款指示的期限已經過了。
            var seeded = await SeedAsync(context, instructionExpiresAtUtc: NowUtc.AddMinutes(-1));
            var writer = CreateWriter(context);

            var problem = await Assert.ThrowsAsync<DomainProblemException>(
                () => writer.CompleteAsync(Command(seeded, "sim-pay-stale")));

            Assert.Equal(PaymentErrorCodes.PaymentAttemptExpired, problem.Code);

            // rollback：付款嘗試與訂單都必須維持原狀。
            var (attempt, order) = await ReloadAsync(context, seeded);
            Assert.Equal(PaymentAttemptStatus.AwaitingPayment, attempt.Status);
            Assert.Equal(PaymentStatus.AwaitingPayment, order.PaymentStatus);
            Assert.Equal(0m, order.PaidAmount);
            Assert.Empty(await context.PaymentEvents.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.OrderStatusHistories.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.AuditLogs.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.OutboxMessages.AsNoTracking().ToArrayAsync());
        });
    }

    [SqlServerFact]
    public async Task GuestCompletionUsesTheGuestScopeAndCreatesOnlyEmailNotification()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context, guest: true);
            var tokenPublicId = Guid.CreateVersion7();
            var command = Command(seeded, "guest-sim-pay") with
            {
                Actor = new SimulatedPaymentActor.Guest(tokenPublicId, seeded.OrderPublicId),
            };

            var result = await CreateWriter(context).CompleteAsync(command);

            Assert.Equal(PaymentAttemptStatus.Paid, result.Body.Status);
            Assert.Single(await context.OutboxMessages.AsNoTracking().ToArrayAsync());
            var audit = await context.AuditLogs.AsNoTracking().SingleAsync();
            Assert.Equal(DoSelect.Domain.Auditing.AuditActorType.Guest, audit.ActorType);
            Assert.Equal(tokenPublicId, audit.ActorPublicId);
        });
    }

    [SqlServerFact]
    public async Task ACompletedAttemptCannotBeCompletedAgainUnderANewKey()
    {
        // 換一把冪等鍵就繞過去的話，一張訂單可以被付款兩次。
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            var writer = CreateWriter(context);

            await writer.CompleteAsync(Command(seeded, "sim-pay-first-key"));

            var problem = await Assert.ThrowsAsync<DomainProblemException>(
                () => writer.CompleteAsync(Command(seeded, "sim-pay-second-key")));

            Assert.Equal(PaymentErrorCodes.PaymentStateConflict, problem.Code);
        });
    }

    [SqlServerFact]
    public async Task AnotherMembersPaymentIsNotFoundRatherThanForbidden()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            var intruder = await AddMemberAsync(context);
            var writer = CreateWriter(context);

            var problem = await Assert.ThrowsAsync<DomainProblemException>(
                () => writer.CompleteAsync(
                    Command(seeded, "sim-pay-intruder") with
                    {
                        Actor = new SimulatedPaymentActor.Member(intruder),
                    }));

            // 404 而不是 403：分開回答等於告訴外人這個 id 存在。
            Assert.Equal(404, problem.StatusCode);

            var (attempt, order) = await ReloadAsync(context, seeded);
            Assert.Equal(PaymentAttemptStatus.AwaitingPayment, attempt.Status);
            Assert.Equal(0m, order.PaidAmount);
        });
    }

    [SqlServerFact]
    public async Task AnUnknownAttemptIsNotFound()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            var writer = CreateWriter(context);

            var problem = await Assert.ThrowsAsync<DomainProblemException>(
                () => writer.CompleteAsync(
                    Command(seeded, "sim-pay-missing") with
                    {
                        PaymentAttemptPublicId = Guid.NewGuid(),
                    }));

            Assert.Equal(404, problem.StatusCode);
        });
    }

    [SqlServerFact]
    public async Task ACancelledOrderRollsBackWithoutPayingAnything()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context, cancelled: true);
            var writer = CreateWriter(context);

            var problem = await Assert.ThrowsAsync<DomainProblemException>(
                () => writer.CompleteAsync(Command(seeded, "sim-pay-cancelled")));

            Assert.Equal(PaymentErrorCodes.PaymentStateConflict, problem.Code);

            var (attempt, order) = await ReloadAsync(context, seeded);
            Assert.Equal(PaymentAttemptStatus.AwaitingPayment, attempt.Status);
            Assert.Equal(0m, order.PaidAmount);
        });
    }

    [SqlServerFact]
    public async Task ARejectedAttemptLeavesNoIdempotencyRecordBehind()
    {
        // 失敗的請求如果留下完成的冪等紀錄，同一把鍵再送就會拿回那個失敗，
        // 使用者修好問題後永遠重試不了。
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context, cancelled: true);
            var writer = CreateWriter(context);

            await Assert.ThrowsAsync<DomainProblemException>(
                () => writer.CompleteAsync(Command(seeded, "sim-pay-retryable")));

            Assert.Empty(await context.IdempotencyRecords.AsNoTracking().ToArrayAsync());

            // 對照組：成功的請求確實會留下紀錄。少了這一段，上面那條在
            // 「這支端點根本不寫冪等紀錄」的實作下也會過 —— 空的斷言最容易騙人。
            var ok = await SeedAsync(context);
            await CreateWriter(context).CompleteAsync(Command(ok, "sim-pay-recorded"));
            Assert.Single(await context.IdempotencyRecords.AsNoTracking().ToArrayAsync());
        });
    }

    [SqlServerFact]
    public async Task AnAuditFailureRollsBackEveryPaymentSideEffect()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            var writer = CreateWriter(context, new ThrowingAuditWriter());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                writer.CompleteAsync(Command(seeded, "sim-pay-audit-failure")));

            var (attempt, order) = await ReloadAsync(context, seeded);
            Assert.Equal(PaymentAttemptStatus.AwaitingPayment, attempt.Status);
            Assert.Equal(PaymentStatus.AwaitingPayment, order.PaymentStatus);
            Assert.Equal(OrderStatus.PendingPayment, order.OrderStatus);
            Assert.Empty(await context.PaymentEvents.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.OrderStatusHistories.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.AuditLogs.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.OutboxMessages.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.IdempotencyRecords.AsNoTracking().ToArrayAsync());
        });
    }

    private static async Task<(PaymentAttempt Attempt, Order Order)> ReloadAsync(
        DoSelectDbContext context,
        SeededPayment seeded)
    {
        context.ChangeTracker.Clear();
        var attempt = await context.PaymentAttempts.AsNoTracking()
            .SingleAsync(candidate => candidate.PublicId == seeded.AttemptPublicId);
        var order = await context.Orders.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == attempt.OrderId);
        return (attempt, order);
    }

    private static CompleteSimulatedPaymentCommand Command(
        SeededPayment seeded,
        string simulationKey,
        SimulatedPaymentOutcome outcome = SimulatedPaymentOutcome.Succeeded) =>
        new(
            seeded.AttemptPublicId,
            outcome,
            simulationKey,
            new SimulatedPaymentActor.Member(seeded.MemberUserId!),
            "correlation-1",
            "0123456789abcdef0123456789abcdef",
            System.Net.IPAddress.Loopback);

    private static SimulatedPaymentWriter CreateWriter(
        DoSelectDbContext context,
        IAuditWriter? auditWriter = null)
    {
        var timeProvider = new FixedTimeProvider(Now);
        return new SimulatedPaymentWriter(
            context,
            new CompleteSimulatedPaymentService(),
            new EfIdempotencyExecutor(
                context,
                Options.Create(new IdempotencyOptions { ActorScopePepper = TestPepper }),
                timeProvider),
            auditWriter ?? new EfAuditWriter(context, timeProvider),
            new EfOutboxWriter(context, timeProvider),
            timeProvider);
    }

    private sealed class ThrowingAuditWriter : IAuditWriter
    {
        public AuditLog Add(AuditWriteRequest request) =>
            throw new InvalidOperationException("Synthetic audit failure.");
    }

    private sealed record SeededPayment(
        Guid AttemptPublicId,
        string? MemberUserId,
        Guid OrderPublicId);

    private static async Task<string> AddMemberAsync(DoSelectDbContext context)
    {
        var member = ApplicationUser.CreateMember(
            Guid.NewGuid(),
            $"member-{Guid.NewGuid():N}@example.test",
            NowUtc);
        context.Users.Add(member);
        await context.SaveChangesAsync();
        return member.Id;
    }

    private static async Task<SeededPayment> SeedAsync(
        DoSelectDbContext context,
        DateTime? instructionExpiresAtUtc = null,
        bool cancelled = false,
        bool guest = false,
        PaymentMethod method = PaymentMethod.CreditCard)
    {
        var memberUserId = guest ? null : await AddMemberAsync(context);

        var profile = new ShippingProviderProfile(
            Guid.NewGuid(),
            $"INV{Guid.NewGuid():N}"[..16],
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
            Guid.NewGuid(), profile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m, null, null, NowUtc);
        context.PackageLimitVersions.Add(packageLimit);
        await context.SaveChangesAsync();

        var order = Order.Create(
            Guid.NewGuid(),
            new OrderCreation(
                $"INV-{Guid.NewGuid():N}"[..32],
                memberUserId,
                guest ? $"guest-{Guid.NewGuid():N}@example.test" : null,
                PaymentMethodPolicy.KindOf(method) == PaymentSettlementKind.CashOnDelivery
                    ? OrderStatus.Confirmed
                    : OrderStatus.PendingPayment,
                // 結帳留下的訂單是「等待付款」，這支端點就是要把它推到已付款。
                PaymentStatus.AwaitingPayment,
                FulfillmentStatus.Preparing,
                AssemblyStatus.NotRequired,
                1000m,
                0m,
                0m,
                0m,
                1000m,
                "[[SYNTHETIC_NAME]]",
                "0912345678",
                "recipient@example.test",
                "100",
                "Taipei",
                "Zhongzheng",
                "[[SYNTHETIC_ADDRESS]]",
                null,
                "HOME",
                profile.Id,
                null,
                null,
                null,
                1,
                1,
                null,
                null,
                $"inv-{Guid.NewGuid():N}",
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

        if (cancelled)
        {
            order.ChangeOrderStatus(OrderStatus.Cancelled, NowUtc);
            await context.SaveChangesAsync();
        }

        var attempt = new PaymentAttempt(
            Guid.CreateVersion7(),
            order.Id,
            method,
            order.GrandTotal,
            "SIM",
            $"checkout-{Guid.NewGuid():N}:initial-payment",
            PaymentMethodPolicy.KindOf(method) == PaymentSettlementKind.CashOnDelivery
                ? null
                : instructionExpiresAtUtc ?? NowUtc.AddHours(1),
            NowUtc);
        // 結帳建立付款嘗試之後立刻發出付款指示，狀態因此是 AwaitingPayment。
        attempt.SetPaymentInstruction("SIM-" + attempt.PublicId.ToString("N"), NowUtc);
        context.PaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        return new SeededPayment(attempt.PublicId, memberUserId, order.PublicId);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static async Task RunInMigratedDatabaseAsync(Func<DoSelectDbContext, Task> test)
    {
        var connectionString = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ??
            LocalConnectionString)
        {
            InitialCatalog = $"DoSelectSimPayment_{Guid.NewGuid():N}",
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
}
