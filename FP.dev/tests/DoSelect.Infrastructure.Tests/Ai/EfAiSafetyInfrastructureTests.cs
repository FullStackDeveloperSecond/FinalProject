using DoSelect.Application.Ai;
using DoSelect.Domain.Ai;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Ai;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Tests.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Ai;

public sealed class EfAiSafetyInfrastructureTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task AdmissionGate_GrantedConsent_ReservesOnceAndReplaysIdempotently()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var member = await SeedMemberWithConsentAsync(context);
            var requestPublicId = Guid.NewGuid();
            var gate = new EfAiSupportAdmissionGate(context, new FixedTimeProvider(Now));

            var before = await gate.ReadAsync(Guid.Parse(member.Id), CancellationToken.None);
            var first = await gate.TryReserveAsync(
                Guid.Parse(member.Id),
                requestPublicId,
                CancellationToken.None);
            var replay = await gate.TryReserveAsync(
                Guid.Parse(member.Id),
                requestPublicId,
                CancellationToken.None);

            Assert.Equal(AiConsentState.Granted, before.ConsentState);
            Assert.Equal(EfAiSupportAdmissionGate.DailySupportLimit, before.RemainingDailyMessages);
            Assert.True(first.IsReserved);
            Assert.Equal(19, first.State.RemainingDailyMessages);
            Assert.True(replay.IsReserved);
            Assert.Equal(19, replay.State.RemainingDailyMessages);
            Assert.Equal(1, await context.AiUsageLedger.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task MigrationChain_CreatesAiSafetyTablesAndLeavesNoPendingModelChange()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);

            Assert.Contains(
                await context.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith(
                    "_AddAiSafetyConsentAndUsage",
                    StringComparison.Ordinal));
            Assert.Equal(0, await context.AiConsentRecords.CountAsync());
            Assert.Equal(0, await context.AiUsageLedger.CountAsync());
        }, useMigrations: true);
    }

    [SqlServerFact]
    public async Task AdmissionGate_ConcurrentLastQuota_AllowsExactlyOneReservation()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            string memberUserId;
            await using (var seed = CreateContext(connectionString))
            {
                var member = await SeedMemberWithConsentAsync(seed);
                memberUserId = member.Id;
                for (var index = 0;
                     index < EfAiSupportAdmissionGate.DailySupportLimit - 1;
                     index++)
                {
                    seed.AiUsageLedger.Add(AiUsageLedgerEntry.ReserveSupport(
                        memberUserId,
                        Guid.NewGuid(),
                        Now.UtcDateTime.AddMinutes(index)));
                }

                await seed.SaveChangesAsync();
            }

            async Task<AiSupportReservationResult> ReserveAsync()
            {
                await using var context = CreateContext(connectionString);
                var gate = new EfAiSupportAdmissionGate(context, new FixedTimeProvider(Now));
                return await gate.TryReserveAsync(
                    Guid.Parse(memberUserId),
                    Guid.NewGuid(),
                    CancellationToken.None);
            }

            var results = await Task.WhenAll(ReserveAsync(), ReserveAsync());

            Assert.Equal(1, results.Count(result => result.IsReserved));
            await using var verify = CreateContext(connectionString);
            Assert.Equal(
                EfAiSupportAdmissionGate.DailySupportLimit,
                await verify.AiUsageLedger.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task AdmissionGate_LatestWithdrawal_DeniesWithoutWritingUsage()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var member = await SeedMemberWithConsentAsync(context);
            context.AiConsentRecords.Add(AiConsentRecord.Withdraw(
                member.Id,
                policyVersion: 1,
                AiConsentPurpose.Support,
                SupportedLocale.ZhTw,
                source: "MemberWeb",
                Now.UtcDateTime,
                Now.UtcDateTime.AddMinutes(1)));
            await context.SaveChangesAsync();
            var gate = new EfAiSupportAdmissionGate(
                context,
                new FixedTimeProvider(Now.AddMinutes(2)));

            var state = await gate.ReadAsync(Guid.Parse(member.Id), CancellationToken.None);
            var reservation = await gate.TryReserveAsync(
                Guid.Parse(member.Id),
                Guid.NewGuid(),
                CancellationToken.None);

            Assert.Equal(AiConsentState.Denied, state.ConsentState);
            Assert.False(reservation.IsReserved);
            Assert.Empty(await context.AiUsageLedger.ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task AdmissionGate_MismatchedConsentPolicyVersion_DeniesWithoutWritingUsage()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var member = ApplicationUser.CreateMember(
                Guid.NewGuid(),
                $"member-{Guid.NewGuid():N}@example.test",
                Now.UtcDateTime);
            context.Users.Add(member);
            await context.SaveChangesAsync();
            context.AiConsentRecords.Add(AiConsentRecord.Grant(
                member.Id,
                policyVersion: 2,
                AiConsentPurpose.Support,
                SupportedLocale.ZhTw,
                source: "MemberWeb",
                Now.UtcDateTime));
            await context.SaveChangesAsync();
            var gate = new EfAiSupportAdmissionGate(context, new FixedTimeProvider(Now));

            var state = await gate.ReadAsync(Guid.Parse(member.Id), CancellationToken.None);
            var reservation = await gate.TryReserveAsync(
                Guid.Parse(member.Id),
                Guid.NewGuid(),
                CancellationToken.None);

            Assert.Equal(AiConsentState.Missing, state.ConsentState);
            Assert.False(reservation.IsReserved);
            Assert.Empty(await context.AiUsageLedger.ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task AdmissionGate_DailyQuota_ResetsAtTaipeiMidnight()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var member = await SeedMemberWithConsentAsync(context);
            for (var index = 0; index < EfAiSupportAdmissionGate.DailySupportLimit; index++)
            {
                context.AiUsageLedger.Add(AiUsageLedgerEntry.ReserveSupport(
                    member.Id,
                    Guid.NewGuid(),
                    Now.UtcDateTime.AddMinutes(index)));
            }

            await context.SaveChangesAsync();

            var beforeMidnight = new EfAiSupportAdmissionGate(
                context,
                new FixedTimeProvider(new DateTimeOffset(2026, 8, 28, 15, 59, 0, TimeSpan.Zero)));
            var afterMidnight = new EfAiSupportAdmissionGate(
                context,
                new FixedTimeProvider(new DateTimeOffset(2026, 8, 28, 16, 0, 0, TimeSpan.Zero)));

            var before = await beforeMidnight.ReadAsync(
                Guid.Parse(member.Id),
                CancellationToken.None);
            var after = await afterMidnight.ReadAsync(
                Guid.Parse(member.Id),
                CancellationToken.None);

            Assert.Equal(0, before.RemainingDailyMessages);
            Assert.Equal(
                new DateTimeOffset(2026, 8, 28, 16, 0, 0, TimeSpan.Zero),
                before.ResetAtUtc);
            Assert.Equal(EfAiSupportAdmissionGate.DailySupportLimit, after.RemainingDailyMessages);
            Assert.Equal(
                new DateTimeOffset(2026, 8, 29, 16, 0, 0, TimeSpan.Zero),
                after.ResetAtUtc);
        });
    }

    [SqlServerFact]
    public async Task ContextReader_ReturnsOnlyOwnerScopedDeidentifiedOrderData()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var owner = await SeedMemberWithConsentAsync(context);
            var other = ApplicationUser.CreateMember(
                Guid.NewGuid(),
                $"other-{Guid.NewGuid():N}@example.test",
                Now.UtcDateTime);
            context.Users.Add(other);
            await context.SaveChangesAsync();
            var order = await SeedMemberOrderAsync(context, owner.Id);
            var reader = new EfAiSupportContextReader(context);

            var ownerResult = await reader.ReadAsync(
                Guid.Parse(owner.Id),
                [order.PublicId],
                CancellationToken.None);
            var otherResult = await reader.ReadAsync(
                Guid.Parse(other.Id),
                [order.PublicId],
                CancellationToken.None);

            Assert.Equal(AiSupportContextStatus.Allowed, ownerResult.Status);
            var payload = Assert.Single(ownerResult.DataItems);
            Assert.Equal("order", payload.SourceType);
            Assert.Equal(order.PublicId.ToString("D"), payload.SourceId);
            Assert.Equal(order.OrderNumber, payload.Title);
            Assert.Contains("Creator GPU", payload.Content, StringComparison.Ordinal);
            Assert.Contains(order.OrderNumber, payload.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("[[SYNTHETIC_NAME]]", payload.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-owner@example.test", payload.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("0912345678", payload.Content, StringComparison.Ordinal);
            Assert.Equal(AiSupportContextStatus.ResourceNotFound, otherResult.Status);
            Assert.Empty(otherResult.DataItems);
        });
    }

    private static async Task<ApplicationUser> SeedMemberWithConsentAsync(
        DoSelectDbContext context)
    {
        var member = ApplicationUser.CreateMember(
            Guid.NewGuid(),
            $"member-{Guid.NewGuid():N}@example.test",
            Now.UtcDateTime);
        context.Users.Add(member);
        await context.SaveChangesAsync();
        context.AiConsentRecords.Add(AiConsentRecord.Grant(
            member.Id,
            policyVersion: AiConsentPolicy.CurrentVersion,
            AiConsentPurpose.Support,
            SupportedLocale.ZhTw,
            source: "MemberWeb",
            Now.UtcDateTime));
        await context.SaveChangesAsync();
        return member;
    }

    private static async Task<Order> SeedMemberOrderAsync(
        DoSelectDbContext context,
        string memberUserId)
    {
        var profile = new ShippingProviderProfile(
            Guid.NewGuid(),
            $"AI-{Guid.NewGuid():N}"[..16],
            1,
            "Active",
            null,
            null,
            "{}",
            1,
            Now.UtcDateTime);
        context.ShippingProviderProfiles.Add(profile);
        await context.SaveChangesAsync();
        var packageLimit = new PackageLimitVersion(
            Guid.NewGuid(),
            profile.Id,
            1,
            30m,
            150m,
            100m,
            100m,
            250m,
            50_000m,
            null,
            null,
            Now.UtcDateTime);
        context.PackageLimitVersions.Add(packageLimit);
        await context.SaveChangesAsync();

        var order = Order.Create(
            Guid.NewGuid(),
            new OrderCreation(
                $"AI-{Guid.NewGuid():N}"[..32],
                memberUserId,
                null,
                OrderStatus.Confirmed,
                PaymentStatus.Paid,
                FulfillmentStatus.Preparing,
                AssemblyStatus.NotRequired,
                100m,
                0m,
                10m,
                0m,
                110m,
                "[[SYNTHETIC_NAME]]",
                "0912345678",
                "synthetic-owner@example.test",
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
                $"ai-{Guid.NewGuid():N}",
                null,
                1,
                1,
                new OrderInvoicePreference(
                    SimulatedInvoiceBuyerType.Individual,
                    "synthetic-owner@example.test",
                    null,
                    null,
                    null,
                    null),
                1_000m,
                null,
                new OrderPackageSnapshot(
                    packageLimit.Id,
                    1m,
                    40m,
                    30m,
                    20m,
                    90m,
                    100m)),
            Now.UtcDateTime);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.OrderItems.Add(new OrderItem(
            Guid.NewGuid(),
            order.Id,
            skuId: null,
            skuCodeSnapshot: "GPU-CREATOR",
            productNameSnapshot: "Creator GPU",
            skuNameSnapshot: "24GB",
            quantity: 1,
            listUnitPrice: 100m,
            saleUnitPrice: 100m,
            finalUnitPrice: 100m,
            unitCostSnapshot: 80m,
            lineSubtotal: 100m,
            discountAllocation: 0m,
            lineTotal: 100m,
            assemblyGroupKey: null,
            returnableQuantity: 1,
            Now.UtcDateTime,
            isCouponEligible: true,
            new OrderItemSpecificationSnapshot("24GB", "{\"vram\":\"24GB\"}", 1)));
        await context.SaveChangesAsync();
        return order;
    }

    private static async Task WithDatabaseAsync(
        Func<string, Task> test,
        bool useMigrations = false)
    {
        var connectionString = SqlServerTestConnection.Build(
            $"DoSelectAiSafety_{Guid.NewGuid():N}") + ";Encrypt=False;";
        await using var setup = CreateContext(connectionString);
        try
        {
            if (useMigrations)
            {
                await setup.Database.MigrateAsync();
            }
            else
            {
                await setup.Database.EnsureCreatedAsync();
            }
            await test(connectionString);
        }
        finally
        {
            await setup.Database.CloseConnectionAsync();
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static DoSelectDbContext CreateContext(string connectionString) => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(connectionString)
            .Options);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
