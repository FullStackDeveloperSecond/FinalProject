using System.Data.Common;
using DoSelect.Application.Invoicing;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Invoicing;
using DoSelect.Infrastructure.Orders;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Tests.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DoSelect.Infrastructure.Tests.Orders;

/// <summary>
/// Issue #65 A1 裁定的三個埠，對真實 SQL Server provider 驗證。
/// </summary>
/// <remarks>
/// 用 provider-backed 而不是 in-memory：這些埠的重點是它們產生的 SQL ——
/// 批次查詢是不是真的一次往返、投影會不會被翻成 N+1、月份前綴比對能不能下推。
/// in-memory provider 對這些一律「通過」，等於沒驗。
/// </remarks>
[Trait("Category", "RequiresSqlServer")]
public sealed class OrderInvoicePortsSqlServerTests
    : IClassFixture<OrderInvoicePortsFixture>
{
    private static readonly DateTime NowUtc = new(2026, 8, 30, 4, 0, 0, DateTimeKind.Utc);

    [SqlServerFact]
    public async Task IssuanceSnapshotCarriesTheOrderStateBuyerAndMerchandiseLines()
    {
        await RunAsync(async context =>
        {
            var seeded = await SeedOrderAsync(context, shippingFee: 0m, assemblyFee: 0m);
            var reader = new OrderInvoiceIssuanceReader(context);

            var snapshot = await reader.FindIssuanceSnapshotAsync(seeded.OrderPublicId);

            Assert.NotNull(snapshot);
            Assert.Equal(seeded.OrderId, snapshot!.OrderId);
            Assert.False(snapshot.OrderIsCancelled);
            Assert.True(snapshot.OrderIsPaid);
            Assert.Equal(1000m, snapshot.OrderPaidAmount);
            Assert.Equal(SimulatedInvoiceBuyerType.Individual, snapshot.BuyerType);
            Assert.Equal("buyer@example.test", snapshot.BuyerEmail);

            var line = Assert.Single(snapshot.Lines);
            Assert.Equal(InvoiceLineKind.Merchandise, line.Line.Kind);
            Assert.Equal(seeded.OrderItemPublicId, line.Line.OrderItemPublicId);

            // 窄內部 Key 例外：商品列一定要帶得回 OrderItemId，
            // 之後才寫得進 SimulatedInvoiceItems.OrderItemId（DEC-P299）。
            Assert.Equal(seeded.OrderItemId, line.OrderItemId);
        });
    }

    [SqlServerFact]
    public async Task ShippingAndAssemblyBecomeNonMerchandiseLinesWithTheReservedSkuCodes()
    {
        await RunAsync(async context =>
        {
            var seeded = await SeedOrderAsync(context, shippingFee: 60m, assemblyFee: 500m);
            var reader = new OrderInvoiceIssuanceReader(context);

            var snapshot = await reader.FindIssuanceSnapshotAsync(seeded.OrderPublicId);

            var shipping = snapshot!.Lines.Single(
                source => source.Line.Kind == InvoiceLineKind.Shipping);
            var assembly = snapshot.Lines.Single(
                source => source.Line.Kind == InvoiceLineKind.AssemblyFee);

            Assert.Equal(InvoiceLineSkuCodes.Shipping, shipping.Line.SkuCodeSnapshot);
            Assert.Equal(InvoiceLineSkuCodes.AssemblyFee, assembly.Line.SkuCodeSnapshot);
            Assert.Equal(60m, shipping.Line.GrossAmount);
            Assert.Equal(500m, assembly.Line.GrossAmount);

            // 非商品列沒有對應的訂單品項。
            Assert.Null(shipping.OrderItemId);
            Assert.Null(assembly.OrderItemId);
        });
    }

    [SqlServerFact]
    public async Task AZeroFeeDoesNotBecomeAnInvoiceLine()
    {
        await RunAsync(async context =>
        {
            var seeded = await SeedOrderAsync(context, shippingFee: 0m, assemblyFee: 500m);
            var reader = new OrderInvoiceIssuanceReader(context);

            var snapshot = await reader.FindIssuanceSnapshotAsync(seeded.OrderPublicId);

            Assert.DoesNotContain(
                snapshot!.Lines,
                source => source.Line.Kind == InvoiceLineKind.Shipping);
        });
    }

    [SqlServerFact]
    public async Task ACancelledOrderIsReportedAsCancelled()
    {
        await RunAsync(async context =>
        {
            var seeded = await SeedOrderAsync(
                context, shippingFee: 0m, assemblyFee: 0m, cancelled: true);
            var reader = new OrderInvoiceIssuanceReader(context);

            var snapshot = await reader.FindIssuanceSnapshotAsync(seeded.OrderPublicId);

            Assert.True(snapshot!.OrderIsCancelled);
        });
    }

    [SqlServerFact]
    public async Task AnUnknownOrderReturnsNullInsteadOfThrowing()
    {
        await RunAsync(async context =>
        {
            var reader = new OrderInvoiceIssuanceReader(context);

            Assert.Null(await reader.FindIssuanceSnapshotAsync(Guid.NewGuid()));
            Assert.Null(await reader.FindIssuanceSnapshotAsync(Guid.Empty));
        });
    }

    [SqlServerFact]
    public async Task TheReferenceReaderSurfacesTheMemberOwner()
    {
        await RunAsync(async context =>
        {
            var seeded = await SeedOrderAsync(
                context, shippingFee: 0m, assemblyFee: 0m, withMember: true);
            var reader = new OrderInvoiceReferenceReader(context);

            var reference = await reader.FindAsync(seeded.OrderPublicId);

            Assert.NotNull(reference!.MemberUserId);
            Assert.Null(reference.GuestEmailNormalized);
            Assert.Equal(seeded.OrderPublicId, reference.OrderPublicId);
        });
    }

    [SqlServerFact]
    public async Task TheReferenceReaderSurfacesTheGuestEmailForAGuestOrder()
    {
        await RunAsync(async context =>
        {
            var seeded = await SeedOrderAsync(
                context,
                shippingFee: 0m,
                assemblyFee: 0m,
                guestEmail: "guest@example.test");
            var reader = new OrderInvoiceReferenceReader(context);

            var reference = await reader.FindAsync(seeded.OrderPublicId);

            Assert.Null(reference!.MemberUserId);
            Assert.Equal("guest@example.test", reference.GuestEmailNormalized);
        });
    }

    [SqlServerFact]
    public async Task TheBatchedLookupTakesOneRoundTripForManyOrders()
    {
        // alex Issue #65 驗收條件：後台摘要必須批次查詢、避免 N+1。
        // 斷言的是實際往返次數，不只是「回傳的內容對」—— 逐筆查也會回傳正確內容。
        var counter = new CommandCounter();

        await RunAsync(
            async context =>
            {
                var first = await SeedOrderAsync(context, 0m, 0m);
                var second = await SeedOrderAsync(context, 0m, 0m);
                var third = await SeedOrderAsync(context, 0m, 0m);
                var reader = new OrderInvoiceReferenceReader(context);

                counter.Reset();
                var references = await reader.FindManyAsync(
                    [first.OrderId, second.OrderId, third.OrderId]);

                Assert.Equal(3, references.Count);
                Assert.Equal(first.OrderPublicId, references[first.OrderId].OrderPublicId);
                Assert.Equal(1, counter.Count);
            },
            counter);
    }

    [SqlServerFact]
    public async Task TheBatchedLookupIgnoresDuplicatesAndUnknownIds()
    {
        await RunAsync(async context =>
        {
            var seeded = await SeedOrderAsync(context, 0m, 0m);
            var reader = new OrderInvoiceReferenceReader(context);

            var references = await reader.FindManyAsync(
                [seeded.OrderId, seeded.OrderId, 999_999L, 0L, -1L]);

            // 找不到的 id 不是錯誤，也不該補一筆空的 —— 呼叫端要能分辨「沒有這張訂單」。
            Assert.Equal(seeded.OrderId, Assert.Single(references).Key);
        });
    }

    [SqlServerFact]
    public async Task TheBatchedLookupDoesNotQueryForAnEmptyRequest()
    {
        var counter = new CommandCounter();

        await RunAsync(
            async context =>
            {
                var reader = new OrderInvoiceReferenceReader(context);

                counter.Reset();
                var references = await reader.FindManyAsync([]);

                Assert.Empty(references);
                Assert.Equal(0, counter.Count);
            },
            counter);
    }

    [SqlServerFact]
    public async Task InvoiceExistenceFlipsOnceAnInvoiceIsStored()
    {
        await RunAsync(async context =>
        {
            var seeded = await SeedOrderAsync(context, 0m, 0m);
            var reader = new InvoiceExistenceReader(context);

            Assert.False(await reader.HasInvoiceAsync(seeded.OrderId));

            context.SimulatedInvoices.Add(Invoice(seeded.OrderId, "DEMO-202612-000001"));
            await context.SaveChangesAsync();

            Assert.True(await reader.HasInvoiceAsync(seeded.OrderId));
        });
    }

    [SqlServerFact]
    public async Task TheSequenceStartsAtOneAndContinuesFromTheHighestUsedNumber()
    {
        await RunAsync(async context =>
        {
            var month = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
            var sequence = new InvoiceNumberSequence(context);

            Assert.Equal(1, await sequence.NextAsync(month));

            var first = await SeedOrderAsync(context, 0m, 0m);
            var second = await SeedOrderAsync(context, 0m, 0m);
            context.SimulatedInvoices.AddRange(
                Invoice(first.OrderId, "DEMO-202601-000001"),
                Invoice(second.OrderId, "DEMO-202601-000007"));
            await context.SaveChangesAsync();

            // 取最大值加一，不是取筆數：作廢的發票仍然佔用號碼，
            // 用筆數會在有作廢紀錄之後開始重複發號。
            Assert.Equal(8, await sequence.NextAsync(month));
        });
    }

    [SqlServerFact]
    public async Task TheSequenceIsScopedToTheMonthInsideTheInvoiceNumber()
    {
        await RunAsync(async context =>
        {
            var seeded = await SeedOrderAsync(context, 0m, 0m);
            context.SimulatedInvoices.Add(Invoice(seeded.OrderId, "DEMO-202602-000042"));
            await context.SaveChangesAsync();
            var sequence = new InvoiceNumberSequence(context);

            // 別的月份的號碼不影響這個月的序號。
            Assert.Equal(
                1,
                await sequence.NextAsync(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.Equal(
                43,
                await sequence.NextAsync(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)));
        });
    }

    [SqlServerFact]
    public async Task TheSequenceRefusesToGuessWhenAStoredNumberIsMalformed()
    {
        // 解析不出來就當成 0 的話，下一次會發出一個已經用掉的號碼，
        // 最後撞上唯一索引 —— 而且錯誤會出現在離原因很遠的地方。
        await RunAsync(async context =>
        {
            var seeded = await SeedOrderAsync(context, 0m, 0m);
            context.SimulatedInvoices.Add(Invoice(seeded.OrderId, "DEMO-202604-XXXXXX"));
            await context.SaveChangesAsync();
            var sequence = new InvoiceNumberSequence(context);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sequence.NextAsync(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)));
        });
    }

    /// <summary>
    /// 尾碼必須恰好六個 ASCII 數字、值 1～999999。
    /// </summary>
    /// <remarks>
    /// 先前用 <c>int.TryParse</c>，這四種都會被當成合法而放行，然後拿它當
    /// 「用過的最大值」繼續發號。原本只有 <c>XXXXXX</c> 一條反向測試，
    /// 所以「不符合格式就直接拒絕」只做到一部分（alex #67 P3）。
    /// </remarks>
    [SqlServerTheory]
    [InlineData("DEMO-202605-00001")]
    [InlineData("DEMO-202605-+00001")]
    [InlineData("DEMO-202605-000000")]
    [InlineData("DEMO-202605-1000000")]
    [InlineData("DEMO-202605- 00001")]
    public async Task TheSequenceRejectsANumberThatIsNotSixDigits(string invoiceNumber)
    {
        await RunAsync(async context =>
        {
            var seeded = await SeedOrderAsync(context, 0m, 0m);
            context.SimulatedInvoices.Add(Invoice(seeded.OrderId, invoiceNumber));
            await context.SaveChangesAsync();
            var sequence = new InvoiceNumberSequence(context);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sequence.NextAsync(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)));
        });
    }

    [SqlServerFact]
    public async Task TheSequenceRefusesToIssueOnceTheMonthIsExhausted()
    {
        // 999999 是合法的已使用號碼，但它存在時這個月已經沒有下一號。
        //
        // 這條原本斷言 NextAsync 回 1_000_000 —— 那是把一個 DemoInvoiceNumber.Format
        // 拒絕的值釘成正式行為：取號會成功，然後在 IssueInvoiceService 格式化時
        // 丟 ArgumentOutOfRangeException（alex #67 P3）。
        await RunAsync(async context =>
        {
            var seeded = await SeedOrderAsync(context, 0m, 0m);
            context.SimulatedInvoices.Add(Invoice(seeded.OrderId, "DEMO-202611-999999"));
            await context.SaveChangesAsync();
            var sequence = new InvoiceNumberSequence(context);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sequence.NextAsync(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)));
        });
    }

    [SqlServerFact]
    public async Task TheSequenceStillIssuesTheLastNumberOfTheMonth()
    {
        // 正向邊界：999998 已用時，999999 仍然發得出來，而且 Format 收得下。
        await RunAsync(async context =>
        {
            var seeded = await SeedOrderAsync(context, 0m, 0m);
            context.SimulatedInvoices.Add(Invoice(seeded.OrderId, "DEMO-202612-999998"));
            await context.SaveChangesAsync();
            var sequence = new InvoiceNumberSequence(context);
            var issuedAtUtc = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

            var next = await sequence.NextAsync(issuedAtUtc);

            Assert.Equal(999_999, next);
            Assert.Equal("DEMO-202612-999999", DemoInvoiceNumber.Format(issuedAtUtc, next));
        });
    }

    [SqlServerFact]
    public async Task TheSequenceRejectsANonUtcTimestamp()
    {
        await RunAsync(async context =>
        {
            var sequence = new InvoiceNumberSequence(context);

            await Assert.ThrowsAsync<ArgumentException>(
                () => sequence.NextAsync(new DateTime(2026, 8, 30, 4, 0, 0, DateTimeKind.Local)));
        });
    }

    private static SimulatedInvoice Invoice(long orderId, string invoiceNumber) =>
        new(
            Guid.NewGuid(),
            new SimulatedInvoiceCreation(
                orderId,
                invoiceNumber,
                SimulatedInvoiceBuyerType.Individual,
                "buyer@example.test",
                null,
                null,
                null,
                null,
                952m,
                48m,
                1000m),
            NowUtc);

    private sealed record SeededOrder(
        long OrderId,
        Guid OrderPublicId,
        long OrderItemId,
        Guid OrderItemPublicId);

    private static async Task<SeededOrder> SeedOrderAsync(
        DoSelectDbContext context,
        decimal shippingFee,
        decimal assemblyFee,
        bool cancelled = false,
        bool withMember = false,
        string? guestEmail = "guest@example.test")
    {
        // Orders.MemberUserId 對 AspNetUsers 有外鍵，會員訂單得先有真的使用者。
        string? memberUserId = null;
        if (withMember)
        {
            var member = ApplicationUser.CreateMember(
                Guid.NewGuid(),
                $"member-{Guid.NewGuid():N}@example.test",
                NowUtc);
            context.Users.Add(member);
            await context.SaveChangesAsync();
            memberUserId = member.Id;
            guestEmail = null;
        }

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
                guestEmail,
                cancelled ? OrderStatus.Cancelled : OrderStatus.Confirmed,
                PaymentStatus.Paid,
                FulfillmentStatus.Preparing,
                AssemblyStatus.NotRequired,
                1000m,
                0m,
                shippingFee,
                assemblyFee,
                1000m + shippingFee + assemblyFee,
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
                    SimulatedInvoiceBuyerType.Individual,
                    "buyer@example.test",
                    null,
                    null,
                    null,
                    null),
                1_000m,
                null,
                new OrderPackageSnapshot(packageLimit.Id, 1m, 40m, 30m, 20m, 90m, 100m)),
            NowUtc);
        // PaidAmount 不在 OrderCreation 裡 —— 它由付款投影寫入。少了這一步，
        // 快照的 OrderPaidAmount 會是 0，而發票金額必須等於實付。
        order.ApplyPaymentProjection(
            PaymentStatus.Paid, 1000m + shippingFee + assemblyFee, NowUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Order.Create 只接受可轉移的初始狀態，取消要靠狀態機。
        if (cancelled && order.OrderStatus != OrderStatus.Cancelled)
        {
            order.ChangeOrderStatus(OrderStatus.Cancelled, NowUtc);
            await context.SaveChangesAsync();
        }

        var item = new OrderItem(
            Guid.NewGuid(),
            order.Id,
            skuId: null,
            skuCodeSnapshot: "SKU-1",
            productNameSnapshot: "測試商品",
            skuNameSnapshot: "標準",
            quantity: 1,
            listUnitPrice: 1000m,
            saleUnitPrice: 1000m,
            finalUnitPrice: 1000m,
            unitCostSnapshot: 800m,
            lineSubtotal: 1000m,
            discountAllocation: 0m,
            lineTotal: 1000m,
            assemblyGroupKey: null,
            returnableQuantity: 1,
            NowUtc,
            isCouponEligible: true,
            new OrderItemSpecificationSnapshot("標準", "{}", 1));
        context.OrderItems.Add(item);
        await context.SaveChangesAsync();

        return new SeededOrder(order.Id, order.PublicId, item.Id, item.PublicId);
    }

    /// <summary>數這個 DbContext 實際往資料庫送了幾個命令。</summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => _count;

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <remarks>
    /// 整個類別共用一個資料庫（<see cref="OrderInvoicePortsFixture"/> 建一次），
    /// 每條測試只開自己的 DbContext。先前每條各建一個資料庫並 Migrate，
    /// 十五條跑了一個多小時，本機 SQL Server 還被塞到逾時 —— 那個成本不是在驗任何東西。
    /// <para>
    /// 因此測試之間必須互不干擾：訂單各自用新的 PublicId，
    /// 而查「整個月」的流水號測試各用不同月份。
    /// </para>
    /// </remarks>
    private static async Task RunAsync(
        Func<DoSelectDbContext, Task> test,
        CommandCounter? counter = null)
    {
        var builder = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(OrderInvoicePortsFixture.ConnectionString);
        if (counter is not null)
        {
            builder.AddInterceptors(counter);
        }

        await using var context = new DoSelectDbContext(builder.Options);
        await test(context);
    }
}

/// <summary>
/// 這組測試共用的資料庫：建一次、用完刪掉。
/// </summary>
public sealed class OrderInvoicePortsFixture : IAsyncLifetime
{
    public static string ConnectionString { get; } =
        SqlServerTestConnection.Build("DoSelectOrderInvoicePorts");

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    private static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(ConnectionString)
            .Options);
}
