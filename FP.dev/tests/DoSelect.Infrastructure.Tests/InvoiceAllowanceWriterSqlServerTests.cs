using System.Net;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Invoicing;
using DoSelect.Application.Refunds;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Idempotency;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Invoicing;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Refunds;
using DoSelect.Infrastructure.Tests.Idempotency;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests;

[Trait("Category", "RequiresSqlServer")]
public sealed class InvoiceAllowanceWriterSqlServerTests
{
    private const string ConnectionStringEnvironmentVariable =
        "DOSELECT_SQLSERVER_TEST_CONNECTION";
    private const string LocalConnectionString =
        "Server=.\\SQL2025;Database=DoSelect;Trusted_Connection=True;Encrypt=False;";
    private const string TestPepper =
        "invoice-allowance-tests-actor-scope-pepper";
    private static readonly DateTimeOffset Now =
        new(2026, 8, 26, 4, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task CreateAndReplayPersistOneAtomicAllowanceWithAudit()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            var writer = CreateWriter(context);
            var command = Command(seeded, "allowance-success");

            var created = await writer.CreateAsync(command);
            var replayed = await writer.CreateAsync(command);

            Assert.False(created.IsReplay);
            Assert.True(replayed.IsReplay);
            Assert.Equal(201, created.StatusCode);
            Assert.Equal(created.Body.PublicId, replayed.Body.PublicId);
            Assert.Equal(SimulatedInvoice.RequiredDemoMarker, created.Body.DemoMarker);
            Assert.Equal(3, created.Body.Items.Count);
            Assert.Equal([1, 1, 2], created.Body.Items.Select(item => item.Quantity).Order());
            Assert.Equal(
                [InvoiceLineKind.Merchandise, InvoiceLineKind.Shipping, InvoiceLineKind.AssemblyFee],
                created.Body.Items.Select(item => item.Kind).Order());

            context.ChangeTracker.Clear();
            var allowance = await context.SimulatedInvoiceAllowances.SingleAsync();
            Assert.Equal(created.Body.PublicId, allowance.PublicId);
            Assert.Equal(3, await context.SimulatedInvoiceAllowanceItems.CountAsync());
            Assert.Equal(
                SimulatedInvoiceStatus.FullyAllowed,
                (await context.SimulatedInvoices.SingleAsync()).Status);
            var audit = await context.AuditLogs.SingleAsync();
            Assert.Equal(AuditActions.InvoiceAllowanceCreate, audit.Action);
            Assert.Equal(allowance.PublicId, audit.ResourcePublicId);
            Assert.Equal("203.0.113.0/24", audit.MaskedIpAddress);
            Assert.Equal(
                IdempotencyStatus.Succeeded,
                (await context.IdempotencyRecords.SingleAsync()).Status);
            var adminPublicId = await context.Users
                .Where(user => user.Id == seeded.AdminUserId)
                .Select(user => user.PublicId)
                .SingleAsync();
            Assert.Equal(
                IdempotencyActorScope.ForAdmin(adminPublicId).ComputeHash(TestPepper),
                (await context.IdempotencyRecords.SingleAsync()).ActorScopeHash);
        });
    }

    [SqlServerFact]
    public async Task RefundInvoiceReferenceReaderReturnsTheFormalPublicId()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            IRefundInvoiceReferenceReader reader = new RefundInvoiceReferenceReader(context);

            var references = await reader.FindManyAsync(
                [seeded.RefundId, seeded.RefundId, 999_999L]);

            Assert.Equal(seeded.RefundPublicId, references[seeded.RefundId]);
            Assert.Single(references);
        });
    }

    [SqlServerFact]
    public async Task SameKeyWithDifferentPayloadIsRejectedWithoutDuplicateWrites()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            var writer = CreateWriter(context);
            var command = Command(seeded, "allowance-payload-conflict");
            await writer.CreateAsync(command);

            var conflict = await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
                writer.CreateAsync(command with { InvoiceRowVersion = new byte[8] }));

            Assert.Equal(IdempotencyErrorCodes.PayloadConflict, conflict.ErrorCode);
            context.ChangeTracker.Clear();
            Assert.Equal(1, await context.SimulatedInvoiceAllowances.CountAsync());
            Assert.Equal(1, await context.AuditLogs.CountAsync());
            Assert.Equal(1, await context.IdempotencyRecords.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task StaleInvoiceRowVersionRollsBackHeaderAuditAndIdempotency()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            var staleRowVersion = seeded.InvoiceRowVersion.ToArray();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [SimulatedInvoices] SET [UpdatedAtUtc] = DATEADD(millisecond, 1, [UpdatedAtUtc]) WHERE [PublicId] = {seeded.InvoicePublicId}");
            context.ChangeTracker.Clear();

            var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
                CreateWriter(context).CreateAsync(
                    Command(seeded with { InvoiceRowVersion = staleRowVersion }, "allowance-stale")));

            Assert.Equal(DomainErrorCodes.ConcurrencyConflict, exception.Code);
            context.ChangeTracker.Clear();
            Assert.Empty(await context.SimulatedInvoiceAllowances.ToArrayAsync());
            Assert.Empty(await context.AuditLogs.ToArrayAsync());
            Assert.Empty(await context.IdempotencyRecords.ToArrayAsync());
            Assert.Equal(
                SimulatedInvoiceStatus.Issued,
                (await context.SimulatedInvoices.SingleAsync()).Status);
        });
    }

    [SqlServerFact]
    public async Task RefundForAnotherInvoiceIsRejectedWithoutDatabaseSideEffects()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            var unrelatedInvoice = await SeedUnrelatedInvoiceAsync(context);
            var writer = CreateWriter(context);

            var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
                writer.CreateAsync(Command(
                    seeded with
                    {
                        InvoicePublicId = unrelatedInvoice.PublicId,
                        InvoiceRowVersion = unrelatedInvoice.RowVersion,
                    },
                    "allowance-cross-invoice")));

            Assert.Equal(InvoiceErrorCodes.ResourceNotFound, exception.Code);

            context.ChangeTracker.Clear();
            Assert.Empty(await context.SimulatedInvoiceAllowances.ToArrayAsync());
            Assert.Empty(await context.SimulatedInvoiceAllowanceItems.ToArrayAsync());
            Assert.Empty(await context.AuditLogs.ToArrayAsync());
            Assert.Empty(await context.IdempotencyRecords.ToArrayAsync());

            var originalInvoice = await context.SimulatedInvoices.AsNoTracking()
                .SingleAsync(invoice => invoice.PublicId == seeded.InvoicePublicId);
            var unrelatedInvoiceAfter = await context.SimulatedInvoices.AsNoTracking()
                .SingleAsync(invoice => invoice.PublicId == unrelatedInvoice.PublicId);
            Assert.Equal(SimulatedInvoiceStatus.Issued, originalInvoice.Status);
            Assert.Equal(seeded.InvoiceRowVersion, originalInvoice.RowVersion);
            Assert.Equal(SimulatedInvoiceStatus.Issued, unrelatedInvoiceAfter.Status);
            Assert.Equal(unrelatedInvoice.RowVersion, unrelatedInvoiceAfter.RowVersion);
        });
    }
    [SqlServerFact]
    public async Task AuditFailureRollsBackAllowanceInvoiceTransitionAndIdempotency()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            var writer = CreateWriter(context, new ThrowingAuditWriter());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                writer.CreateAsync(Command(seeded, "allowance-audit-failure")));

            context.ChangeTracker.Clear();
            Assert.Empty(await context.SimulatedInvoiceAllowances.ToArrayAsync());
            Assert.Empty(await context.SimulatedInvoiceAllowanceItems.ToArrayAsync());
            Assert.Empty(await context.AuditLogs.ToArrayAsync());
            Assert.Empty(await context.IdempotencyRecords.ToArrayAsync());
            Assert.Equal(
                SimulatedInvoiceStatus.Issued,
                (await context.SimulatedInvoices.SingleAsync()).Status);
        });
    }

    [SqlServerFact]
    public async Task LegacyOtherAdjustmentIsRejectedInsteadOfSilentlyFiltered()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var seeded = await SeedAsync(context);
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE [RefundAllocations] DROP CONSTRAINT [CK_RefundAllocations_TypeAndShape]");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [RefundAllocations] SET [AllocationType] = 'OtherAdjustment' WHERE [RefundId] = {seeded.RefundId} AND [AllocationType] = 'OriginalShipping'");
            context.ChangeTracker.Clear();

            await Assert.ThrowsAsync<InvoiceAllowanceSourceException>(() =>
                new InvoiceAllowanceReader(context).FindByRefundAsync(seeded.RefundPublicId));
        });
    }

    private static InvoiceAllowanceWriter CreateWriter(
        DoSelectDbContext context,
        IAuditWriter? auditWriter = null)
    {
        var timeProvider = new FixedTimeProvider(Now);
        var reader = new InvoiceAllowanceReader(context);
        return new InvoiceAllowanceWriter(
            context,
            new IssueInvoiceAllowanceService(reader, timeProvider),
            new EfIdempotencyExecutor(
                context,
                Options.Create(new IdempotencyOptions { ActorScopePepper = TestPepper }),
                timeProvider),
            auditWriter ?? new EfAuditWriter(context, timeProvider));
    }

    private static CreateInvoiceAllowanceCommand Command(SeededAllowance seeded, string key) =>
        new(
            seeded.InvoicePublicId,
            seeded.RefundPublicId,
            seeded.InvoiceRowVersion,
            key,
            seeded.AdminUserId,
            "invoice-writer-test",
            "0123456789abcdef0123456789abcdef",
            IPAddress.Parse("203.0.113.42"));

    private static async Task<SeededAllowance> SeedAsync(DoSelectDbContext context)
    {
        var createdAtUtc = Now.UtcDateTime.AddDays(-1);
        var profile = new ShippingProviderProfile(
            Guid.NewGuid(),
            $"TEST-{Guid.NewGuid():N}",
            1,
            "Active",
            null,
            null,
            "{}",
            1,
            createdAtUtc);
        var admin = ApplicationUser.CreateAdmin(
            Guid.NewGuid(),
            $"invoice-{Guid.NewGuid():N}@example.test",
            createdAtUtc);
        var role = new IdentityRole(AuditRoleNames.FinanceManager);
        context.AddRange(profile, admin, role);
        await context.SaveChangesAsync();
        var packageLimit = new PackageLimitVersion(
            Guid.NewGuid(), profile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m,
            null, null, createdAtUtc);
        context.PackageLimitVersions.Add(packageLimit);
        await context.SaveChangesAsync();
        context.UserRoles.Add(new IdentityUserRole<string>
        {
            UserId = admin.Id,
            RoleId = role.Id,
        });

        var order = Order.Create(
            Guid.NewGuid(),
            new OrderCreation(
                $"ORD-{Guid.NewGuid():N}"[..32],
                null,
                $"guest-{Guid.NewGuid():N}@example.test",
                OrderStatus.Completed,
                PaymentStatus.Paid,
                FulfillmentStatus.Delivered,
                AssemblyStatus.NotRequired,
                100m,
                0m,
                10m,
                5m,
                115m,
                "Test Recipient",
                "0900000000",
                "guest@example.test",
                "100",
                "Taipei",
                "Zhongzheng",
                "Test address",
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
                 $"checkout-{Guid.NewGuid():N}",
                 null,
                 1,
                 1,
                 new OrderInvoicePreference(
                     SimulatedInvoiceBuyerType.Individual,
                     "guest@example.test",
                     null,
                     null,
                      null,
                      null),
                  1000m,
                  null,
                  new OrderPackageSnapshot(
                      packageLimit.Id, 1m, 40m, 30m, 20m, 90m, 100m)),
            createdAtUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var orderItem = new OrderItem(
            Guid.NewGuid(),
            order.Id,
            null,
            "SKU-TEST",
            "Test product",
            "Default",
            2,
            50m,
            50m,
            50m,
            30m,
            100m,
            0m,
            100m,
            null,
            2,
            createdAtUtc,
            isCouponEligible: true,
            new OrderItemSpecificationSnapshot("Test specification", "{}", 1));
        var payment = new PaymentAttempt(
            Guid.NewGuid(),
            order.Id,
            PaymentMethod.CreditCard,
            115m,
            "TEST",
            $"payment-{Guid.NewGuid():N}",
            null,
            createdAtUtc);
        var invoice = new SimulatedInvoice(
            Guid.NewGuid(),
            new SimulatedInvoiceCreation(
                order.Id,
                $"DEMO-{Guid.NewGuid():N}"[..32],
                SimulatedInvoiceBuyerType.Individual,
                "guest@example.test",
                null,
                null,
                null,
                null,
                109.52m,
                5.48m,
                115m),
            createdAtUtc);
        invoice.Issue(createdAtUtc.AddHours(1));
        context.AddRange(orderItem, payment, invoice);
        await context.SaveChangesAsync();

        context.SimulatedInvoiceItems.AddRange(
            new SimulatedInvoiceItem(
                Guid.NewGuid(), invoice.Id, orderItem.Id, "Test product", "SKU-TEST",
                2, 50m, 0m, 95.24m, 4.76m, 100m, createdAtUtc),
            new SimulatedInvoiceItem(
                Guid.NewGuid(), invoice.Id, null, "Shipping", InvoiceLineSkuCodes.Shipping,
                1, 10m, 0m, 9.52m, .48m, 10m, createdAtUtc),
            new SimulatedInvoiceItem(
                Guid.NewGuid(), invoice.Id, null, "Assembly", InvoiceLineSkuCodes.AssemblyFee,
                1, 5m, 0m, 4.76m, .24m, 5m, createdAtUtc));

        var refund = new Refund(
            Guid.NewGuid(),
            order.Id,
            null,
            payment.Id,
            $"RF-{Guid.NewGuid():N}"[..32],
            115m,
            "full_return",
            null,
            $"refund-{Guid.NewGuid():N}",
            createdAtUtc);
        refund.Approve(115m, admin.Id, createdAtUtc.AddHours(2));
        refund.BeginProcessing(admin.Id, createdAtUtc.AddHours(3));
        refund.Complete(115m, createdAtUtc.AddHours(4));
        context.Refunds.Add(refund);
        await context.SaveChangesAsync();

        context.RefundAllocations.AddRange(
            new RefundAllocation(
                Guid.NewGuid(), refund.Id, orderItem.Id, RefundAllocationType.ItemRefund,
                100m, 0m, createdAtUtc, quantity: 2),
            new RefundAllocation(
                Guid.NewGuid(), refund.Id, null, RefundAllocationType.OriginalShipping,
                10m, 0m, createdAtUtc),
            new RefundAllocation(
                Guid.NewGuid(), refund.Id, null, RefundAllocationType.AssemblyFee,
                5m, 0m, createdAtUtc));
        await context.SaveChangesAsync();

        return new SeededAllowance(
            invoice.PublicId,
            invoice.RowVersion.ToArray(),
            refund.PublicId,
            refund.Id,
            admin.Id);
    }

    private static async Task<SeededInvoice> SeedUnrelatedInvoiceAsync(DoSelectDbContext context)
    {
        var createdAtUtc = Now.UtcDateTime.AddDays(-1);
        var profileId = await context.ShippingProviderProfiles
            .Select(profile => profile.Id)
            .SingleAsync();
        var packageLimitId = await context.PackageLimitVersions
            .Select(limit => limit.Id)
            .SingleAsync();
        var order = Order.Create(
            Guid.NewGuid(),
            new OrderCreation(
                $"ORD-{Guid.NewGuid():N}"[..32],
                null,
                $"guest-{Guid.NewGuid():N}@example.test",
                OrderStatus.Completed,
                PaymentStatus.Paid,
                FulfillmentStatus.Delivered,
                AssemblyStatus.NotRequired,
                100m,
                0m,
                10m,
                5m,
                115m,
                "Unrelated Recipient",
                "0900000000",
                "unrelated@example.test",
                "100",
                "Taipei",
                "Zhongzheng",
                "Unrelated address",
                null,
                "HOME",
                profileId,
                null,
                null,
                null,
                1,
                1,
                null,
                 null,
                 $"checkout-{Guid.NewGuid():N}",
                 null,
                 1,
                 1,
                 new OrderInvoicePreference(
                     SimulatedInvoiceBuyerType.Individual,
                     "unrelated@example.test",
                     null,
                     null,
                      null,
                      null),
                  1000m,
                  null,
                  new OrderPackageSnapshot(
                      packageLimitId, 1m, 40m, 30m, 20m, 90m, 100m)),
            createdAtUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var invoice = new SimulatedInvoice(
            Guid.NewGuid(),
            new SimulatedInvoiceCreation(
                order.Id,
                $"DEMO-{Guid.NewGuid():N}"[..32],
                SimulatedInvoiceBuyerType.Individual,
                "unrelated@example.test",
                null,
                null,
                null,
                null,
                109.52m,
                5.48m,
                115m),
            createdAtUtc);
        invoice.Issue(createdAtUtc.AddHours(1));
        context.SimulatedInvoices.Add(invoice);
        await context.SaveChangesAsync();

        return new SeededInvoice(invoice.PublicId, invoice.RowVersion.ToArray());
    }
    private static async Task RunInMigratedDatabaseAsync(
        Func<DoSelectDbContext, Task> test)
    {
        var connectionString = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ??
            LocalConnectionString)
        {
            InitialCatalog = $"DoSelectAllowanceWriter_{Guid.NewGuid():N}",
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

    private sealed record SeededInvoice(Guid PublicId, byte[] RowVersion);
    private sealed record SeededAllowance(
        Guid InvoicePublicId,
        byte[] InvoiceRowVersion,
        Guid RefundPublicId,
        long RefundId,
        string AdminUserId);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ThrowingAuditWriter : IAuditWriter
    {
        public AuditLog Add(AuditWriteRequest request) =>
            throw new InvalidOperationException("Injected audit failure.");
    }
}
