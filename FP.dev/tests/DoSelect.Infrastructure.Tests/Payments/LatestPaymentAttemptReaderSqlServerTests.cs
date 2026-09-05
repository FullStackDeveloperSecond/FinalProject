using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Payments;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Tests.Idempotency;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace DoSelect.Infrastructure.Tests.Payments;

/// <summary>
/// 「最新一筆付款嘗試」的排序，對真實 SQL Server 驗證。
/// </summary>
/// <remarks>
/// 排序是這支端點唯一會出錯而且不容易被發現的地方 —— Application 層的假 Reader
/// 直接回傳指定的那一筆，證明不了 SQL 真的挑對了列。
/// </remarks>
[Trait("Category", "RequiresSqlServer")]
public sealed class LatestPaymentAttemptReaderSqlServerTests
{
    private const string ConnectionStringEnvironmentVariable =
        "DOSELECT_SQLSERVER_TEST_CONNECTION";
    private const string LocalConnectionString =
        "Server=.\\SQL2025;Database=DoSelect;Trusted_Connection=True;Encrypt=False;";

    private static readonly DateTime NowUtc = new(2026, 9, 1, 4, 0, 0, DateTimeKind.Utc);

    [SqlServerFact]
    public async Task TheNewestAttemptWins()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var orderId = await SeedOrderAsync(context);
            await AddAttemptAsync(context, orderId, NowUtc.AddMinutes(-10));
            var newest = await AddAttemptAsync(context, orderId, NowUtc);
            var reader = new LatestPaymentAttemptReader(context);

            var attempt = await reader.FindLatestAsync(orderId);

            Assert.Equal(newest, attempt!.PublicId);
        });
    }

    [SqlServerFact]
    public async Task TheOrderingIsTotalSoTheTopRowIsDeterministic()
    {
        // alex Issue #86 A1 要求穩定的次排序。
        //
        // 這裡斷言的是「送出去的 SQL 有完整排序鍵」，不是「多讀幾次都一樣」——
        // 後者測不出東西：拿掉 ThenByDescending 之後 SQL Server 在這種小表上
        // 仍然每次回同一列（我實際跑過，那版測試在有無修正下都是綠的）。
        // 排序不完整的傷害是「不保證」，不是「一定會錯」，所以要驗查詢本身。
        await RunInMigratedDatabaseAsync(async context =>
        {
            var orderId = await SeedOrderAsync(context);
            await AddAttemptAsync(context, orderId, NowUtc);
            await AddAttemptAsync(context, orderId, NowUtc);

            var sql = context.PaymentAttempts.AsNoTracking()
                .Where(attempt => attempt.OrderId == orderId)
                .OrderByDescending(attempt => attempt.CreatedAtUtc)
                .ThenByDescending(attempt => attempt.Id)
                .ToQueryString();

            // 產生的 ORDER BY 必須同時含建立時間與識別鍵。
            var orderBy = sql[sql.LastIndexOf("ORDER BY", StringComparison.Ordinal)..];
            Assert.Contains("CreatedAtUtc", orderBy, StringComparison.Ordinal);
            Assert.Contains("Id", orderBy, StringComparison.Ordinal);
        });
    }

    [SqlServerFact]
    public async Task TheReaderExecutesATotallyOrderedQuery()
    {
        // 上一條驗的是「這個排序寫法會產生完整的 ORDER BY」，但它自己組查詢，
        // 所以證明不了 production 的 Reader 用的是同一個排序。這條攔截 Reader
        // 真正送出去的 SQL —— 拿掉 ThenByDescending 就會紅。
        await RunInMigratedDatabaseAsync(async context =>
        {
            var orderId = await SeedOrderAsync(context);
            await AddAttemptAsync(context, orderId, NowUtc);

            var captured = new CapturedSql();
            await using var observed = new DoSelectDbContext(
                new DbContextOptionsBuilder<DoSelectDbContext>()
                    .UseSqlServer(context.Database.GetConnectionString())
                    .AddInterceptors(captured)
                    .Options);

            await new LatestPaymentAttemptReader(observed).FindLatestAsync(orderId);

            var select = Assert.Single(captured.Statements, statement => statement.Contains("PaymentAttempts", StringComparison.Ordinal));
            var orderBy = select[select.LastIndexOf("ORDER BY", StringComparison.Ordinal)..];
            Assert.Contains("CreatedAtUtc", orderBy, StringComparison.Ordinal);
            Assert.Contains("Id", orderBy, StringComparison.Ordinal);
        });
    }

    /// <summary>抄下實際送到資料庫的 SQL。</summary>
    private sealed class CapturedSql : DbCommandInterceptor
    {
        public List<string> Statements { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Statements.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [SqlServerTheory]
    [InlineData(PaymentAttemptStatus.Failed)]
    [InlineData(PaymentAttemptStatus.Expired)]
    [InlineData(PaymentAttemptStatus.Cancelled)]
    [InlineData(PaymentAttemptStatus.Paid)]
    public async Task ATerminalAttemptIsNotSkipped(PaymentAttemptStatus status)
    {
        // Issue #86 A1：不篩狀態。濾掉終態的話，付款失敗後重新整理就看不到失敗原因。
        await RunInMigratedDatabaseAsync(async context =>
        {
            var orderId = await SeedOrderAsync(context);
            await AddAttemptAsync(context, orderId, NowUtc.AddMinutes(-10));
            var terminal = await AddAttemptAsync(context, orderId, NowUtc, status);
            var reader = new LatestPaymentAttemptReader(context);

            var attempt = await reader.FindLatestAsync(orderId);

            Assert.Equal(terminal, attempt!.PublicId);
            Assert.Equal(status, attempt.Status);
        });
    }

    [SqlServerFact]
    public async Task AnotherOrdersAttemptsAreNeverReturned()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var mine = await SeedOrderAsync(context);
            var other = await SeedOrderAsync(context);
            await AddAttemptAsync(context, other, NowUtc);
            var reader = new LatestPaymentAttemptReader(context);

            Assert.Null(await reader.FindLatestAsync(mine));
        });
    }

    [SqlServerFact]
    public async Task AnOrderWithNoAttemptReturnsNull()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var orderId = await SeedOrderAsync(context);
            var reader = new LatestPaymentAttemptReader(context);

            Assert.Null(await reader.FindLatestAsync(orderId));
            Assert.Null(await reader.FindLatestAsync(0L));
        });
    }

    [SqlServerFact]
    public async Task TheOrderLookupCarriesTheOwnerAndTheInternalKey()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var memberUserId = await AddMemberAsync(context);
            var orderId = await SeedOrderAsync(context, memberUserId);
            var publicId = await context.Orders.AsNoTracking()
                .Where(order => order.Id == orderId)
                .Select(order => order.PublicId)
                .SingleAsync();
            var reader = new LatestPaymentAttemptReader(context);

            var reference = await reader.FindOrderAsync(publicId);

            Assert.Equal(orderId, reference!.OrderId);
            Assert.Equal(memberUserId, reference.MemberUserId);
        });
    }

    [SqlServerFact]
    public async Task AGuestOrderHasNoMemberOwner()
    {
        // 訪客訂單的 MemberUserId 是 null，擁有者比對因此永遠不會把它當成某個會員的訂單。
        await RunInMigratedDatabaseAsync(async context =>
        {
            var orderId = await SeedOrderAsync(context);
            var publicId = await context.Orders.AsNoTracking()
                .Where(order => order.Id == orderId)
                .Select(order => order.PublicId)
                .SingleAsync();
            var reader = new LatestPaymentAttemptReader(context);

            Assert.Null((await reader.FindOrderAsync(publicId))!.MemberUserId);
        });
    }

    [SqlServerFact]
    public async Task AnUnknownOrderReturnsNull()
    {
        await RunInMigratedDatabaseAsync(async context =>
        {
            var reader = new LatestPaymentAttemptReader(context);

            Assert.Null(await reader.FindOrderAsync(Guid.NewGuid()));
            Assert.Null(await reader.FindOrderAsync(Guid.Empty));
        });
    }

    private static async Task<Guid> AddAttemptAsync(
        DoSelectDbContext context,
        long orderId,
        DateTime createdAtUtc,
        PaymentAttemptStatus status = PaymentAttemptStatus.AwaitingPayment)
    {
        var attempt = new PaymentAttempt(
            Guid.CreateVersion7(),
            orderId,
            PaymentMethod.CreditCard,
            1000m,
            "SIM",
            $"key-{Guid.NewGuid():N}",
            createdAtUtc.AddHours(1),
            createdAtUtc);
        attempt.SetPaymentInstruction($"SIM-{Guid.NewGuid():N}"[..20], createdAtUtc);

        switch (status)
        {
            case PaymentAttemptStatus.AwaitingPayment:
                break;
            case PaymentAttemptStatus.Expired:
            case PaymentAttemptStatus.Cancelled:
                attempt.Transition(status, createdAtUtc);
                break;
            case PaymentAttemptStatus.Paid:
                attempt.Transition(PaymentAttemptStatus.Processing, createdAtUtc);
                attempt.Transition(PaymentAttemptStatus.Paid, createdAtUtc);
                break;
            case PaymentAttemptStatus.Failed:
                attempt.Transition(PaymentAttemptStatus.Processing, createdAtUtc);
                attempt.Transition(PaymentAttemptStatus.Failed, createdAtUtc, "simulated_failure");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        context.PaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();
        return attempt.PublicId;
    }

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

    private static async Task<long> SeedOrderAsync(
        DoSelectDbContext context,
        string? memberUserId = null)
    {
        var profile = new ShippingProviderProfile(
            Guid.NewGuid(), $"INV{Guid.NewGuid():N}"[..16], 1, "Active",
            null, null, "{}", 1, NowUtc);
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
                memberUserId is null ? $"guest-{Guid.NewGuid():N}@example.test" : null,
                OrderStatus.PendingPayment,
                PaymentStatus.AwaitingPayment,
                FulfillmentStatus.Preparing,
                AssemblyStatus.NotRequired,
                1000m, 0m, 0m, 0m, 1000m,
                "[[SYNTHETIC_NAME]]", "0912345678", "recipient@example.test",
                "100", "Taipei", "Zhongzheng", "[[SYNTHETIC_ADDRESS]]", null, "HOME",
                profile.Id, null, null, null, 1, 1, null, null,
                $"inv-{Guid.NewGuid():N}", null, 1, 1,
                new OrderInvoicePreference(
                    DoSelect.Domain.Invoicing.SimulatedInvoiceBuyerType.Individual,
                    "buyer@example.test", null, null, null, null),
                1_000m, null,
                new OrderPackageSnapshot(packageLimit.Id, 1m, 40m, 30m, 20m, 90m, 100m)),
            NowUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private static async Task RunInMigratedDatabaseAsync(Func<DoSelectDbContext, Task> test)
    {
        var connectionString = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ??
            LocalConnectionString)
        {
            InitialCatalog = $"DoSelectLatestAttempt_{Guid.NewGuid():N}",
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
