using System.Data;
using System.Data.Common;
using System.Net;
using DoSelect.Application.Auditing;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Refunds;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Refunds;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests;

/// <summary>
/// 退款執行的 SQL Server Provider-backed 測試環境。
/// </summary>
/// <remarks>
/// 環境變數只決定**伺服器**，資料庫名稱強制為這組測試專屬的名稱，
/// 避免與其他 SQL Server 測試互相 <c>EnsureDeleted</c>。
/// </remarks>
public sealed class RefundExecutorSqlFixture : IAsyncLifetime
{
    public const string ConnectionStringEnvironmentVariable =
        "DOSELECT_SQLSERVER_TEST_CONNECTION";

    private const string DatabaseName = "DoSelectRefundExecutorTests";

    private const string LocalServer = "Server=.\\SQL2025;";

    public static bool IsEnabled =>
        !string.IsNullOrWhiteSpace(GetConfiguredConnectionString()) ||
        (OperatingSystem.IsWindows() &&
         !string.Equals(
             Environment.GetEnvironmentVariable("CI"),
             "true",
             StringComparison.OrdinalIgnoreCase));

    /// <summary>整組測試共用的財務管理員 Identity Id。</summary>
    public static string AdminUserId { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        // 執行路徑會在同一交易內把 Identity Id 換成管理員 PublicId 與角色快照，
        // 並重新確認仍具 Refund.Execute 的角色。
        var admin = ApplicationUser.CreateAdmin(
            Guid.NewGuid(),
            $"refund-admin-{Guid.NewGuid():N}@example.test",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        var role = new IdentityRole(AuditRoleNames.FinanceManager);
        context.AddRange(admin, role);
        await context.SaveChangesAsync();

        context.UserRoles.Add(new IdentityUserRole<string>
        {
            UserId = admin.Id,
            RoleId = role.Id,
        });
        await context.SaveChangesAsync();

        AdminUserId = admin.Id;
    }

    public async Task DisposeAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public static DoSelectDbContext CreateContext(params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(BuildConnectionString())
            .AddInterceptors(interceptors)
            .Options);

    private static string BuildConnectionString()
    {
        var configured = GetConfiguredConnectionString();
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
            string.IsNullOrWhiteSpace(configured) ? LocalServer : configured)
        {
            InitialCatalog = DatabaseName,
            TrustServerCertificate = true,
        };

        if (string.IsNullOrWhiteSpace(configured))
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    private static string? GetConfiguredConnectionString() =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
}

public sealed class RefundExecutorSqlFactAttribute : FactAttribute
{
    public RefundExecutorSqlFactAttribute()
    {
        if (!RefundExecutorSqlFixture.IsEnabled)
        {
            Skip = "Set " + RefundExecutorSqlFixture.ConnectionStringEnvironmentVariable +
                   " to run SQL Server integration tests.";
        }
    }
}

[CollectionDefinition(nameof(RefundExecutorSqlCollection))]
public sealed class RefundExecutorSqlCollection : ICollectionFixture<RefundExecutorSqlFixture>;

/// <summary>
/// 退款執行對真實 SQL Server 的驗證。
/// </summary>
/// <remarks>
/// 這裡要證明的是只有資料庫才看得到的行為：rowversion 條件更新、共用冪等的
/// 回放與 Payload 衝突、Audit 與退款狀態同交易回滾，以及可信快照缺漏時
/// 什麼都不寫。
/// </remarks>
[Collection(nameof(RefundExecutorSqlCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class RefundExecutorSqlServerTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    [RefundExecutorSqlFact]
    public async Task AnApprovedRefundWithACompleteSnapshotSettlesAndWritesAllocations()
    {
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);

        var result = await CreateExecutor(context).ExecuteAsync(Request(refund));

        Assert.True(result.IsSuccess);
        Assert.Equal(500m, result.SettledAmount);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.Succeeded, stored.Status);
        Assert.True(await verify.RefundAllocations.AnyAsync(a => a.RefundId == stored.Id));
    }

    [RefundExecutorSqlFact]
    public async Task TheAllocationsAndTheRefundStatusAreWrittenInTheSameTransaction()
    {
        // 沒有分攤的成功退款會讓對帳、發票折讓與稽核的 allocationCount 全部失真。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);

        await CreateExecutor(context).ExecuteAsync(Request(refund));

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        var allocations = await verify.RefundAllocations
            .Where(a => a.RefundId == stored.Id)
            .ToArrayAsync();

        Assert.NotEmpty(allocations);
        Assert.All(allocations, allocation => Assert.True(allocation.Amount > 0m));
        Assert.All(allocations, allocation =>
            Assert.NotEqual(RefundAllocationType.OtherAdjustment, allocation.AllocationType));
    }

    [RefundExecutorSqlFact]
    public async Task AStaleRowVersionWritesNothing()
    {
        // 管理員拿舊畫面的版本執行：伺服器仍可能依目前資料完成退款，
        // 必須在寫入任何東西之前擋下。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);

        await using var execute = RefundExecutorSqlFixture.CreateContext();
        var result = await CreateExecutor(execute).ExecuteAsync(
            Request(refund) with { RefundRowVersion = [9, 9, 9, 9, 9, 9, 9, 9] });

        Assert.Equal(RefundErrorCodes.ConcurrencyConflict, result.ErrorCode);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.Approved, stored.Status);
        Assert.False(await verify.RefundAllocations.AnyAsync(a => a.RefundId == stored.Id));
        Assert.False(await verify.Set<AuditLog>()
            .AnyAsync(log => log.ResourcePublicId == refund.PublicId));
    }

    [RefundExecutorSqlFact]
    public async Task TheSameKeyAndPayloadReplaysWithoutASecondEffect()
    {
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);
        var request = Request(refund);

        await using var first = RefundExecutorSqlFixture.CreateContext();
        var initial = await CreateExecutor(first).ExecuteAsync(request);

        await using var second = RefundExecutorSqlFixture.CreateContext();
        var replay = await CreateExecutor(second).ExecuteAsync(request);

        Assert.True(initial.IsSuccess);
        Assert.True(replay.IsSuccess);
        Assert.Equal(initial.SettledAmount, replay.SettledAmount);
        Assert.Equal(500m, replay.SettledAmount);

        // 回放不得產生第二組分攤或第二筆稽核。
        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        var allocationCount = await verify.RefundAllocations
            .CountAsync(a => a.RefundId == stored.Id);
        var auditCount = await verify.Set<AuditLog>()
            .CountAsync(log => log.ResourcePublicId == refund.PublicId);

        Assert.Equal(1, auditCount);
        Assert.True(allocationCount > 0);
    }

    [RefundExecutorSqlFact]
    public async Task TheSameKeyWithADifferentReasonIsAPayloadConflict()
    {
        // RequestHash 涵蓋 ReasonCode；換了理由就不是同一個命令。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);

        await using var first = RefundExecutorSqlFixture.CreateContext();
        await CreateExecutor(first).ExecuteAsync(Request(refund));

        // Executor 刻意不攔 IdempotencyConflictException —— GlobalExceptionHandler 會把它
        // 轉成 409 並帶上 Retry-After。攔下來只留 ErrorCode 會把 RetryAfterSeconds 丟掉。
        await using var second = RefundExecutorSqlFixture.CreateContext();
        var conflict = await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => CreateExecutor(second).ExecuteAsync(
                Request(refund) with { ReasonCode = "goodwill" }));

        Assert.Equal(IdempotencyErrorCodes.PayloadConflict, conflict.ErrorCode);
    }

    [RefundExecutorSqlFact]
    public async Task ADifferentKeyOnASucceededRefundIsAStateConflict()
    {
        // 換一把新金鑰再送一次已完成的退款不是重播，不得產生第二次副作用。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);

        await using var first = RefundExecutorSqlFixture.CreateContext();
        await CreateExecutor(first).ExecuteAsync(Request(refund));

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);

        await using var second = RefundExecutorSqlFixture.CreateContext();
        var result = await CreateExecutor(second).ExecuteAsync(
            Request(refund) with
            {
                IdempotencyKey = $"refund-execute-{Guid.NewGuid():N}",
                RefundRowVersion = stored.RowVersion,
            });

        Assert.Equal(RefundErrorCodes.RefundStateConflict, result.ErrorCode);
    }

    [RefundExecutorSqlFact]
    public async Task AnUnsafeNoteRollsBackEverything()
    {
        // 中央 Audit 拒收含 Email 的自由文字。稽核建構失敗必須讓退款狀態、
        // 分攤與冪等紀錄全部回滾（DEC-P289）。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);

        await using var execute = RefundExecutorSqlFixture.CreateContext();
        await Assert.ThrowsAnyAsync<Exception>(
            () => CreateExecutor(execute).ExecuteAsync(
                Request(refund) with { Note = "contact me@example.com" }));

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        var key = Request(refund).IdempotencyKey;

        Assert.Equal(RefundStatus.Approved, stored.Status);
        Assert.False(await verify.RefundAllocations.AnyAsync(a => a.RefundId == stored.Id));
        Assert.False(await verify.Set<AuditLog>()
            .AnyAsync(log => log.ResourcePublicId == refund.PublicId));

        // 整組測試共用同一個資料庫，因此必須比對**這一把**金鑰，
        // 不能問「有沒有任何冪等紀錄」—— 那會抓到其他測試留下的。
        Assert.False(await verify.IdempotencyRecords.AnyAsync(record => record.Key == key));
    }

    [RefundExecutorSqlFact]
    public async Task ARejectedExecutionLeavesNoIdempotencyRecord()
    {
        // 拒絕不能被記成完成結果：呼叫端修正原因後用同一把金鑰重送，
        // 必須真的重試，而不是拿回原本那個拒絕的回放。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context, withTrustedInputs: false);

        await using var execute = RefundExecutorSqlFixture.CreateContext();
        var result = await CreateExecutor(execute).ExecuteAsync(Request(refund));

        Assert.Equal(RefundErrorCodes.RefundSnapshotUnavailable, result.ErrorCode);

        // 比對這一把金鑰，不是「有沒有任何冪等紀錄」—— 整組測試共用資料庫。
        var key = Request(refund).IdempotencyKey;
        await using var verify = RefundExecutorSqlFixture.CreateContext();
        Assert.False(await verify.IdempotencyRecords.AnyAsync(record => record.Key == key));
    }

    [RefundExecutorSqlFact]
    public async Task AnIncompleteTrustedSnapshotWritesNothing()
    {
        // E1：兩欄未記錄時什麼都不寫 —— 不建立分攤、不改狀態、不寫稽核。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context, withTrustedInputs: false);

        await using var execute = RefundExecutorSqlFixture.CreateContext();
        var result = await CreateExecutor(execute).ExecuteAsync(Request(refund));

        Assert.Equal(RefundErrorCodes.RefundSnapshotUnavailable, result.ErrorCode);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.Approved, stored.Status);
        Assert.False(await verify.RefundAllocations.AnyAsync(a => a.RefundId == stored.Id));
        Assert.False(await verify.Set<AuditLog>()
            .AnyAsync(log => log.ResourcePublicId == refund.PublicId));
    }

    [RefundExecutorSqlFact]
    public async Task AnUnmappableReturnReasonIsRefused()
    {
        // LateNonDefectiveGoodwill 尚無 Returns 的輸入路徑，Reader 不得猜測 ——
        // 猜錯會直接改變退貨運費由誰負擔。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context, reasonCode: "LateNonDefectiveGoodwill");

        await using var execute = RefundExecutorSqlFixture.CreateContext();
        var result = await CreateExecutor(execute).ExecuteAsync(Request(refund));

        Assert.Equal(RefundErrorCodes.RefundSnapshotUnavailable, result.ErrorCode);
    }

    [RefundExecutorSqlFact]
    public async Task AFailedRefundIsRetriedWithoutDuplicatingSideEffects()
    {
        // Failed → Processing → Succeeded。重試後仍只有一組分攤與一筆稽核。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);

        var tracked = await context.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        tracked.BeginProcessing(RefundExecutorSqlFixture.AdminUserId, NowUtc.AddMinutes(-10));
        tracked.Transition(RefundStatus.Failed, NowUtc.AddMinutes(-9));
        await context.SaveChangesAsync();

        await using var execute = RefundExecutorSqlFixture.CreateContext();
        var reloaded = await execute.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.Failed, reloaded.Status);

        var result = await CreateExecutor(execute).ExecuteAsync(
            Request(refund) with { RefundRowVersion = reloaded.RowVersion });

        Assert.True(result.IsSuccess);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.Succeeded, stored.Status);
        Assert.Equal(
            1,
            await verify.Set<AuditLog>().CountAsync(log => log.ResourcePublicId == refund.PublicId));
    }

    [RefundExecutorSqlFact]
    public async Task ACustomCorrelationIdDoesNotBreakTheAudit()
    {
        // CorrelationId 與 W3C TraceId 是兩種格式；混用會讓稽核建構失敗而回 500。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);

        await using var execute = RefundExecutorSqlFixture.CreateContext();
        var result = await CreateExecutor(execute).ExecuteAsync(
            Request(refund) with { CorrelationId = "refund-request-1" });

        Assert.True(result.IsSuccess);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var audit = await verify.Set<AuditLog>()
            .SingleAsync(log => log.ResourcePublicId == refund.PublicId);
        Assert.Equal("refund-request-1", audit.CorrelationId);
    }

    [RefundExecutorSqlFact]
    public async Task TheAuditNeverCarriesTheInternalIdentityId()
    {
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);

        await using var execute = RefundExecutorSqlFixture.CreateContext();
        await CreateExecutor(execute).ExecuteAsync(Request(refund));

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var audit = await verify.Set<AuditLog>()
            .SingleAsync(log => log.ResourcePublicId == refund.PublicId);
        var expectedPublicId = await verify.Users
            .Where(user => user.Id == RefundExecutorSqlFixture.AdminUserId)
            .Select(user => user.PublicId)
            .SingleAsync();

        Assert.Equal(expectedPublicId, audit.ActorPublicId);
        Assert.DoesNotContain(
            RefundExecutorSqlFixture.AdminUserId, audit.ActorRolesJson, StringComparison.Ordinal);
    }

    [RefundExecutorSqlFact]
    public async Task TheSignedAllocationTotalEqualsTheSucceededAmount()
    {
        // 財務等式而不是「非空」：分攤的有號合計必須精確等於 SucceededAmount。
        // 不相等就是一筆自我矛盾的紀錄，而且分攤寫入後不可變。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);

        await CreateExecutor(context).ExecuteAsync(Request(refund));

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        var allocations = await verify.RefundAllocations
            .Where(a => a.RefundId == stored.Id)
            .ToArrayAsync();

        var signedTotal = allocations.Sum(allocation =>
            RefundPolicy.DirectionOf(allocation.AllocationType) == RefundAllocationDirection.Credit
                ? allocation.Amount
                : -allocation.Amount);

        Assert.Equal(500m, stored.ApprovedAmount);
        Assert.Equal(500m, stored.SucceededAmount);
        Assert.Equal(500m, signedTotal);
    }

    [RefundExecutorSqlFact]
    public async Task AnApprovedAmountThatDisagreesWithTheCalculationWritesNothing()
    {
        // 可信快照算出 500，但退款只核准 400。先前這正是本檔案的預設資料，
        // 而斷言只看「allocations 非空」，所以測不出來。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context, approvedAmount: 400m);

        await using var execute = RefundExecutorSqlFixture.CreateContext();
        var result = await CreateExecutor(execute).ExecuteAsync(Request(refund));

        Assert.Equal(RefundErrorCodes.RefundCalculationMismatch, result.ErrorCode);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.Approved, stored.Status);
        Assert.Null(stored.SucceededAmount);
        Assert.False(await verify.RefundAllocations.AnyAsync(a => a.RefundId == stored.Id));
        Assert.False(await verify.Set<AuditLog>()
            .AnyAsync(log => log.ResourcePublicId == refund.PublicId));
    }

    [RefundExecutorSqlFact]
    public async Task ALegalNoteIsStoredAlongsideTheReasonCodeAndTheRequestIp()
    {
        // reason 只收 safe-code，note 走獨立欄位。先前兩者被串成
        // `reasonCode: note` 塞進 reason，任何含空白的 note 都會變成 500。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);

        await using var execute = RefundExecutorSqlFixture.CreateContext();
        var result = await CreateExecutor(execute).ExecuteAsync(
            Request(refund) with
            {
                Note = "Customer confirmed the damaged item by phone",
                RemoteIpAddress = IPAddress.Parse("203.0.113.7"),
            });

        Assert.True(result.IsSuccess);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var audit = await verify.Set<AuditLog>()
            .SingleAsync(log => log.ResourcePublicId == refund.PublicId);

        Assert.Equal("customer_request", audit.Reason);
        Assert.Contains("Customer confirmed", audit.ChangedFieldsJson, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(audit.MaskedIpAddress));
    }

    [RefundExecutorSqlFact]
    public async Task AFreeShippingOrderUsesTheHistoricalBaseFeeSnapshot()
    {
        // 訂單當初免運且有門檻快照：追回的必須是**下單當時**的基本費快照，
        // 不是現行 ShippingMethod.BaseFee。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(
            context, approvedAmount: 440m, freeShipping: true, withBaseFeeSnapshot: true);

        await using var execute = RefundExecutorSqlFixture.CreateContext();
        var result = await CreateExecutor(execute).ExecuteAsync(Request(refund));

        Assert.True(result.IsSuccess);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        var clawback = await verify.RefundAllocations
            .Where(a => a.RefundId == stored.Id &&
                        a.AllocationType == RefundAllocationType.ShippingClawback)
            .SingleAsync();

        Assert.Equal(60m, clawback.Amount);
    }

    [RefundExecutorSqlFact]
    public async Task AFreeShippingOrderWithoutTheSnapshotIsRefusedWithZeroWrites()
    {
        // 舊訂單沒有基本費快照。不得回查現行 ShippingMethod，也不得用 0 猜測。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(
            context, approvedAmount: 440m, freeShipping: true, withBaseFeeSnapshot: false);

        await using var execute = RefundExecutorSqlFixture.CreateContext();
        var result = await CreateExecutor(execute).ExecuteAsync(Request(refund));

        Assert.Equal(RefundErrorCodes.RefundSnapshotUnavailable, result.ErrorCode);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.Approved, stored.Status);
        Assert.False(await verify.RefundAllocations.AnyAsync(a => a.RefundId == stored.Id));
        Assert.False(await verify.Set<AuditLog>()
            .AnyAsync(log => log.ResourcePublicId == refund.PublicId));
    }

    [RefundExecutorSqlFact]
    public async Task AnOldFreeShippingOrderCanStillBeFullyRefundedWithoutTheSnapshot()
    {
        // 完整退貨走 OriginalShipping 退還原運費那條，根本不會執行免運追回，
        // 因此不需要基準運費快照。先前少了這個判斷，讓所有舊免運訂單連完整退貨
        // 都被拒絕 —— 而那些退款其實完全算得出來。
        //
        // 免運訂單完整退貨：商品 1000（2 件全退）+ 退還運費 0 = 1000。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(
            context,
            approvedAmount: 1000m,
            freeShipping: true,
            withBaseFeeSnapshot: false,
            returnedQuantity: 2);

        await using var execute = RefundExecutorSqlFixture.CreateContext();
        var result = await CreateExecutor(execute).ExecuteAsync(Request(refund));

        Assert.True(
            result.IsSuccess,
            $"Expected a full return to succeed without the snapshot, got {result.ErrorCode}.");

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.Succeeded, stored.Status);
        Assert.Equal(1000m, stored.SucceededAmount);

        // 完整退貨不得產生免運追回。
        Assert.False(await verify.RefundAllocations.AnyAsync(a =>
            a.RefundId == stored.Id &&
            a.AllocationType == RefundAllocationType.ShippingClawback));
    }

    [RefundExecutorSqlFact]
    public async Task TwoConcurrentRefundsOnTheSameOrderCannotExceedThePaidAmount()
    {
        // 這是 Serializable 與死結重試存在的**唯一理由**。先前所有 SQL 測試都是
        // sequential，這條保證從來沒有被實證過。
        //
        // 同一張訂單、兩筆不同 Refund、各自核准 500，但訂單只付了 700 ——
        // 兩筆都成功就是超額退款。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var (first, second) = await SeedTwoRefundsOnOneOrderAsync(context);

        // Task.WhenAll 本身**不保證重疊**。先前的 barrier 放在 ExecuteAsync **之前**，
        // 放行時兩筆 SQL 交易都還沒建立，排程器仍可能讓第一筆整個跑完才開始第二筆 ——
        // 第二筆只是循序讀到餘額不足而失敗，測試照樣綠，Serializable 與死結重試
        // 存在的理由完全沒被驗證。
        //
        // 競爭是**自然發生**的，不需要人工製造：共用 Executor 在 Serializable 交易內
        // 寫冪等紀錄，兩筆同時進來時其中一筆會被對方的鍵範圍鎖擋住，直到對方提交。
        // 這裡的 Barrier 只在交易之外讓兩個執行緒同時起跑，本身不構成併發證據 ——
        // 證據是下面對「交易重跑次數」與「交易內被擋住多久」的量測。
        var firstProbe = new ConcurrencyProbeInterceptor();
        var secondProbe = new ConcurrencyProbeInterceptor();

        await using var firstContext = RefundExecutorSqlFixture.CreateContext(firstProbe);
        await using var secondContext = RefundExecutorSqlFixture.CreateContext(secondProbe);

        using var start = new Barrier(2);

        async Task<ExecuteRefundResult> RunAsync(
            DoSelectDbContext context, Refund refund)
        {
            await Task.Yield();
            start.SignalAndWait();
            return await CreateExecutor(context).ExecuteAsync(Request(refund));
        }

        var results = await Task.WhenAll(
            RunAsync(firstContext, first),
            RunAsync(secondContext, second));

        // **這兩條才是併發的證據，缺了它們這條測試證明不了任何事。**
        //
        // 1. 交易重跑：共用 Executor 每跑一次交易本體就寫一次冪等紀錄。循序完成時
        //    兩邊各寫一次、合計 2；只有其中一筆真的被資料庫回滾並整段重跑，合計才會到 3。
        // 2. 交易內等待：被擋住的那一筆會在相鄰兩道命令之間停住，等對方提交才繼續。
        //    循序完成不會出現這種等待。
        //
        // 先前那版只有 Task.WhenAll 加一個交易外的 barrier，兩邊仍可能循序完成、
        // 第二筆只是讀到餘額不足 —— 測試照樣綠，Serializable 與死結重試的存在理由
        // 從來沒有被驗證過。
        var attempts = firstProbe.TransactionAttempts + secondProbe.TransactionAttempts;
        Assert.True(
            attempts >= 3,
            $"兩筆交易本體合計只執行 {attempts} 次" +
            $"（{firstProbe.TransactionAttempts}／{secondProbe.TransactionAttempts}），" +
            "沒有任何一筆被資料庫回滾重跑，代表它們其實是循序完成的。");

        var longestWait = firstProbe.LongestWaitBetweenCommands > secondProbe.LongestWaitBetweenCommands
            ? firstProbe.LongestWaitBetweenCommands
            : secondProbe.LongestWaitBetweenCommands;
        Assert.True(
            longestWait >= TimeSpan.FromMilliseconds(200),
            $"交易內最長等待只有 {longestWait.TotalMilliseconds:F0}ms，" +
            "沒有任何一筆被對方的鎖擋住，兩筆交易並未同時存在。");

        Assert.Equal(1, results.Count(result => result.IsSuccess));

        var loser = results.Single(result => !result.IsSuccess);
        Assert.Contains(
            loser.ErrorCode,
            new[]
            {
                RefundErrorCodes.RefundAmountExceeded,
                RefundErrorCodes.ConcurrencyConflict,
            });

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var settledTotal = await verify.Refunds
            .Where(r => (r.PublicId == first.PublicId || r.PublicId == second.PublicId) &&
                        r.SucceededAmount != null)
            .SumAsync(r => r.SucceededAmount!.Value);

        Assert.Equal(500m, settledTotal);
        Assert.True(settledTotal <= 700m, $"Settled {settledTotal} exceeds the paid amount 700.");

        // 失敗的那一筆不得留下分攤、稽核或冪等完成紀錄。
        var loserPublicId = results[0].IsSuccess ? second.PublicId : first.PublicId;
        var loserRefund = await verify.Refunds.SingleAsync(r => r.PublicId == loserPublicId);

        Assert.Equal(RefundStatus.Approved, loserRefund.Status);
        Assert.False(await verify.RefundAllocations.AnyAsync(a => a.RefundId == loserRefund.Id));
        Assert.False(await verify.Set<AuditLog>()
            .AnyAsync(log => log.ResourcePublicId == loserPublicId));

        // 這一條先前只寫在註解裡、沒有實際斷言。失敗方不得留下冪等完成紀錄 ——
        // 留下來的話，管理員修正原因後用同一把金鑰重送會拿回舊的拒絕。
        var loserKey = Request(results[0].IsSuccess ? second : first).IdempotencyKey;
        Assert.False(await verify.IdempotencyRecords.AnyAsync(record => record.Key == loserKey));
    }

    /// <summary>
    /// 同一商品分兩次部分退款：第二次必須看見第一次已退的數量。
    /// </summary>
    /// <remarks>
    /// 歷史已退數量原本取自 <c>OrderItem.ReturnedQuantity</c>，但 Returns 模組明確不維護
    /// 那個欄位，全專案也沒有任何生產程式碼呼叫 <c>RecordReturnedQuantity</c> —— 它恆為 0。
    /// <para>
    /// 這條測試用「折扣尾差 + 完整退貨」把那個缺陷逼出來。訂單一列 3 件、單價 500、
    /// 訂單級折扣分攤 100（無法被 3 整除，所以尾差是真的）：
    /// </para>
    /// <list type="bullet">
    /// <item>第一次退 2 件：折扣分攤 Round(100×2/3)=66.67，淨額 1000−66.67=933.33，非完整退貨。</item>
    /// <item>第二次退 1 件：已退 2 件 → 最後一批，折扣分攤取剩餘 100−66.67=33.33，
    /// 淨額 500−33.33=466.67；且 2+1=3 進入**完整退貨**路徑，退還原運費 60，
    /// 合計 526.67。</item>
    /// </list>
    /// <para>
    /// 若歷史數量仍為 0，第二次會算成 0+1≠3 的部分退貨：淨額同樣是 466.67，
    /// 但**沒有** OriginalShipping 60，與已核准的 526.67 不符，
    /// 因此會被對帳擋成 <c>refund_calculation_mismatch</c>。淨額相同、只有完整退貨路徑
    /// 不同，所以只斷言金額是抓不到的 —— 必須連分攤組成一起斷言。
    /// </para>
    /// </remarks>
    [RefundExecutorSqlFact]
    public async Task ASecondPartialRefundSeesTheQuantityTheFirstOneAlreadyRefunded()
    {
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var (first, second, itemId) = await SeedTwoSequentialPartialRefundsAsync(context);

        await using (var firstContext = RefundExecutorSqlFixture.CreateContext())
        {
            var firstResult = await CreateExecutor(firstContext).ExecuteAsync(Request(first));
            Assert.True(firstResult.IsSuccess);
            Assert.Equal(933.33m, firstResult.SettledAmount);
        }

        await using (var secondContext = RefundExecutorSqlFixture.CreateContext())
        {
            var secondResult = await CreateExecutor(secondContext).ExecuteAsync(Request(second));
            Assert.True(secondResult.IsSuccess);
            Assert.Equal(526.67m, secondResult.SettledAmount);
        }

        await using var verify = RefundExecutorSqlFixture.CreateContext();

        var firstItem = await verify.RefundAllocations.SingleAsync(allocation =>
            allocation.RefundId == first.Id &&
            allocation.AllocationType == RefundAllocationType.ItemRefund);
        var secondItem = await verify.RefundAllocations.SingleAsync(allocation =>
            allocation.RefundId == second.Id &&
            allocation.AllocationType == RefundAllocationType.ItemRefund);

        Assert.Equal(itemId, firstItem.OrderItemId);
        Assert.Equal(itemId, secondItem.OrderItemId);
        Assert.Equal(2, firstItem.Quantity);
        Assert.Equal(1, secondItem.Quantity);
        Assert.Equal(933.33m, firstItem.Amount);
        Assert.Equal(466.67m, secondItem.Amount);

        // 尾差：兩次的折扣分攤合計必須精確等於原始分攤 100，不能多也不能少。
        Assert.Equal(66.67m, firstItem.OriginalDiscountAllocation);
        Assert.Equal(33.33m, secondItem.OriginalDiscountAllocation);
        Assert.Equal(
            100m,
            firstItem.OriginalDiscountAllocation + secondItem.OriginalDiscountAllocation);

        // 完整退貨路徑：只有第二次退還原運費。這是這條測試真正的判別點。
        Assert.False(await verify.RefundAllocations.AnyAsync(allocation =>
            allocation.RefundId == first.Id &&
            allocation.AllocationType == RefundAllocationType.OriginalShipping));

        var shipping = await verify.RefundAllocations.SingleAsync(allocation =>
            allocation.RefundId == second.Id &&
            allocation.AllocationType == RefundAllocationType.OriginalShipping);
        Assert.Equal(60m, shipping.Amount);

        // 每一筆的有號分攤合計都必須精確等於該筆已結清金額。
        await AssertSignedAllocationTotalAsync(verify, first.Id, 933.33m);
        await AssertSignedAllocationTotalAsync(verify, second.Id, 526.67m);

        // 兩次合計不得超過訂單實付。
        var settled = await verify.Refunds
            .Where(refund => refund.Status == RefundStatus.Succeeded &&
                (refund.Id == first.Id || refund.Id == second.Id))
            .SumAsync(refund => refund.SucceededAmount!.Value);
        Assert.Equal(1460m, settled);
    }

    private static async Task AssertSignedAllocationTotalAsync(
        DoSelectDbContext context,
        long refundId,
        decimal expected)
    {
        var allocations = await context.RefundAllocations
            .Where(allocation => allocation.RefundId == refundId)
            .ToArrayAsync();

        var signedTotal = allocations.Sum(allocation =>
            RefundPolicy.DirectionOf(allocation.AllocationType) == RefundAllocationDirection.Credit
                ? allocation.Amount
                : -allocation.Amount);

        Assert.Equal(expected, signedTotal);

        var refund = await context.Refunds.SingleAsync(candidate => candidate.Id == refundId);
        Assert.Equal(expected, refund.SucceededAmount);
    }

    /// <summary>
    /// 觀察一筆退款交易實際被執行了幾次，以及它在交易內被資料庫擋住多久。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>TransactionAttempts</c> 數的是 <c>INSERT INTO [IdempotencyRecords]</c>：共用
    /// Executor 每執行一次交易本體就寫一次冪等紀錄，整筆交易被回滾重跑時會再寫一次。
    /// 兩邊合計超過 2，就代表有一筆真的被資料庫回滾並重跑 —— 循序完成永遠做不到。
    /// </para>
    /// <para>
    /// <c>LongestWaitBetweenCommands</c> 是相鄰兩道命令之間最長的間隔。被對方的
    /// Serializable 範圍鎖擋住時，那段等待會完整落在這個間隔裡，是「兩筆交易同時
    /// 存在」的直接量測。
    /// </para>
    /// </remarks>
    private sealed class ConcurrencyProbeInterceptor : DbCommandInterceptor
    {
        /// <summary>共用 Executor 每執行一次交易本體，就會寫一次冪等紀錄。</summary>
        private const string TransactionBodyMarker = "INSERT INTO [IdempotencyRecords]";

        private readonly System.Diagnostics.Stopwatch _clock =
            System.Diagnostics.Stopwatch.StartNew();

        private int _transactionAttempts;
        private long _previousCommandTicks;
        private long _longestGapTicks;

        public int TransactionAttempts => Volatile.Read(ref _transactionAttempts);

        public TimeSpan LongestWaitBetweenCommands =>
            TimeSpan.FromTicks(Volatile.Read(ref _longestGapTicks));

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Observe(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Observe(command);
            return ValueTask.FromResult(result);
        }

        private void Observe(DbCommand command)
        {
            var now = _clock.Elapsed.Ticks;
            var previous = Interlocked.Exchange(ref _previousCommandTicks, now);
            if (previous > 0 && now - previous > Volatile.Read(ref _longestGapTicks))
            {
                Volatile.Write(ref _longestGapTicks, now - previous);
            }

            if (command.CommandText.Contains(TransactionBodyMarker, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _transactionAttempts);
            }
        }
    }

    private static IRefundExecutor CreateExecutor(DoSelectDbContext context)
    {
        var timeProvider = new FixedTimeProvider(NowUtc);
        return new RefundExecutor(
            context,
            new EfAuditWriter(context, timeProvider),
            new EfIdempotencyExecutor(
                context,
                Options.Create(new IdempotencyOptions
                {
                    ActorScopePepper = new string('p', 48),
                }),
                timeProvider),
            timeProvider);
    }

    private static ExecuteRefundRequest Request(Refund refund) =>
        new(
            refund.PublicId,
            refund.RowVersion,
            $"refund-execute-{refund.PublicId:N}",
            RefundExecutorSqlFixture.AdminUserId,
            "customer_request",
            Note: null,
            CorrelationId: "refund-test-correlation",
            TraceId: new string('a', 32),
            RemoteIpAddress: null);

    /// <summary>
    /// 同一張訂單上的兩筆退款，各自核准 500，但訂單只收款 700。
    /// </summary>
    /// <remarks>
    /// 兩筆都成功就是超額退款，因此只能有一筆通過 —— 這正是可退款餘額的範圍查詢
    /// 需要 Serializable 保護的情境。兩筆各自有獨立的 ReturnRequest 與 ReturnItem，
    /// 讓後端算出的淨額都是 500。
    /// </remarks>
    private static async Task<(Refund First, Refund Second)> SeedTwoRefundsOnOneOrderAsync(
        DoSelectDbContext context)
    {
        var first = await SeedRefundAsync(context, paidAmount: 700m, returnableQuantity: 2);
        var order = await context.Orders.SingleAsync(o => o.Id == first.OrderId);
        var second = await SeedSecondRefundAsync(context, order, first.PaymentAttemptId);
        return (first, second);
    }

    private static async Task<Refund> SeedSecondRefundAsync(
        DoSelectDbContext context,
        Order order,
        long paymentAttemptId)
    {
        var createdAtUtc = NowUtc.AddDays(-3);
        var item = await context.OrderItems.FirstAsync(i => i.OrderId == order.Id);

        var returnRequest = new ReturnRequest(
            Guid.NewGuid(), $"RT-{Guid.NewGuid():N}"[..20], order.Id, null,
            "Defective", "Second damaged unit", 1, createdAtUtc);
        returnRequest.Transition(ReturnRequestStatus.UnderReview, createdAtUtc);
        returnRequest.CaptureRefundTrustedInputs(
            AssemblyFeeDisposition.NotApplicable, returnShippingCost: 0m, createdAtUtc);
        context.ReturnRequests.Add(returnRequest);
        await context.SaveChangesAsync();

        context.ReturnItems.Add(new ReturnItem(
            Guid.NewGuid(), returnRequest.Id, item.Id, quantity: 1,
            requestedRefund: 500m, inspectionStatus: "Pending", createdAtUtc));

        var refund = new Refund(
            Guid.NewGuid(), order.Id, returnRequest.Id, paymentAttemptId,
            $"RF-{Guid.NewGuid():N}"[..20], requestedAmount: 500m,
            reasonCode: "customer_request", requestedBy: RefundExecutorSqlFixture.AdminUserId,
            idempotencyKey: $"create-{Guid.NewGuid():N}", createdAtUtc);
        refund.Approve(500m, RefundExecutorSqlFixture.AdminUserId, createdAtUtc.AddHours(1));
        context.Refunds.Add(refund);
        await context.SaveChangesAsync();

        return refund;
    }

    /// <summary>
    /// 一張訂單、一列 3 件商品，兩筆各自核准好的部分退款（先 2 件、再 1 件）。
    /// </summary>
    /// <remarks>
    /// 折扣分攤刻意取 100（除以 3 除不盡），讓最後一批的折扣尾差是真的；
    /// 實付 1460 剛好等於兩筆核准金額之和，任何一筆算錯都會撞上餘額或對帳。
    /// </remarks>
    private static async Task<(Refund First, Refund Second, long OrderItemId)>
        SeedTwoSequentialPartialRefundsAsync(DoSelectDbContext context)
    {
        var createdAtUtc = NowUtc.AddDays(-3);

        var profile = new ShippingProviderProfile(
            Guid.NewGuid(), $"TEST-{Guid.NewGuid():N}"[..16], 1, "Active",
            null, null, "{}", 1, createdAtUtc);
        context.Add(profile);
        await context.SaveChangesAsync();

        var packageLimit = new PackageLimitVersion(
            Guid.NewGuid(), profile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m,
            null, null, createdAtUtc);
        context.PackageLimitVersions.Add(packageLimit);
        await context.SaveChangesAsync();

        var order = Order.Create(
            Guid.NewGuid(),
            new OrderCreation(
                $"ORD-{Guid.NewGuid():N}"[..32], null,
                $"guest-{Guid.NewGuid():N}@example.test",
                OrderStatus.Completed, PaymentStatus.Paid, FulfillmentStatus.Delivered,
                AssemblyStatus.NotRequired,
                1500m, 100m, 60m, 0m, 1460m,
                "Test Recipient", "0900000000", "guest@example.test",
                "100", "Taipei", "Zhongzheng", "Test address", null,
                "HOME", profile.Id, null, null, null, 1, 1, null, null,
                $"checkout-{Guid.NewGuid():N}", null, 1, 1,
                new OrderInvoicePreference(
                    SimulatedInvoiceBuyerType.Individual,
                    "guest@example.test", null, null, null, null),
                1460m,
                null,
                new OrderPackageSnapshot(packageLimit.Id, 1m, 40m, 30m, 20m, 90m, 100m),
                60m),
            createdAtUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var item = new OrderItem(
            Guid.NewGuid(), order.Id, null, "SKU-1", "Product", "Sku",
            quantity: 3, listUnitPrice: 500m, saleUnitPrice: 500m, finalUnitPrice: 500m,
            unitCostSnapshot: 300m, lineSubtotal: 1500m, discountAllocation: 100m,
            lineTotal: 1400m, assemblyGroupKey: null, returnableQuantity: 3,
            createdAtUtc: createdAtUtc, isCouponEligible: false,
            specificationSnapshot: new OrderItemSpecificationSnapshot("{}", "{}", 1));
        context.OrderItems.Add(item);

        var attempt = new PaymentAttempt(
            Guid.NewGuid(), order.Id, PaymentMethod.CreditCard, 1460m, null,
            $"pay-{Guid.NewGuid():N}", null, createdAtUtc);
        attempt.Transition(PaymentAttemptStatus.AwaitingPayment, createdAtUtc);
        attempt.Transition(PaymentAttemptStatus.Processing, createdAtUtc);
        attempt.Transition(PaymentAttemptStatus.Paid, createdAtUtc);
        context.Add(attempt);
        await context.SaveChangesAsync();

        var first = await SeedPartialRefundAsync(
            context, order, item, attempt.Id, quantity: 2, approvedAmount: 933.33m, createdAtUtc);
        var second = await SeedPartialRefundAsync(
            context, order, item, attempt.Id, quantity: 1, approvedAmount: 526.67m, createdAtUtc);

        return (first, second, item.Id);
    }

    private static async Task<Refund> SeedPartialRefundAsync(
        DoSelectDbContext context,
        Order order,
        OrderItem item,
        long paymentAttemptId,
        int quantity,
        decimal approvedAmount,
        DateTime createdAtUtc)
    {
        var returnRequest = new ReturnRequest(
            Guid.NewGuid(), $"RT-{Guid.NewGuid():N}"[..20], order.Id, null,
            "Defective", "Damaged on arrival", quantity, createdAtUtc);
        returnRequest.Transition(ReturnRequestStatus.UnderReview, createdAtUtc);
        returnRequest.CaptureRefundTrustedInputs(
            AssemblyFeeDisposition.NotApplicable, returnShippingCost: 0m, createdAtUtc);
        context.ReturnRequests.Add(returnRequest);
        await context.SaveChangesAsync();

        context.ReturnItems.Add(new ReturnItem(
            Guid.NewGuid(), returnRequest.Id, item.Id, quantity: quantity,
            requestedRefund: 500m * quantity, inspectionStatus: "Pending", createdAtUtc));

        var refund = new Refund(
            Guid.NewGuid(), order.Id, returnRequest.Id, paymentAttemptId,
            // 申請金額不得小於核准金額。完整退貨那一筆的核准金額含退還原運費，
            // 比商品金額高，因此申請金額直接取核准金額。
            $"RF-{Guid.NewGuid():N}"[..20], requestedAmount: approvedAmount,
            reasonCode: "customer_request", requestedBy: RefundExecutorSqlFixture.AdminUserId,
            idempotencyKey: $"create-{Guid.NewGuid():N}", createdAtUtc);
        refund.Approve(approvedAmount, RefundExecutorSqlFixture.AdminUserId, createdAtUtc.AddHours(1));
        context.Refunds.Add(refund);
        await context.SaveChangesAsync();

        return refund;
    }

    /// <summary>
    /// 建立一筆可執行的已核准退款，連同它需要的完整上游資料。
    /// </summary>
    private static async Task<Refund> SeedRefundAsync(
        DoSelectDbContext context,
        bool withTrustedInputs = true,
        string reasonCode = "Defective",
        decimal approvedAmount = 500m,
        bool freeShipping = false,
        bool withBaseFeeSnapshot = true,
        decimal paidAmount = 1060m,
        int returnableQuantity = 2,
        int returnedQuantity = 1)
    {
        var createdAtUtc = NowUtc.AddDays(-3);

        var profile = new ShippingProviderProfile(
            Guid.NewGuid(), $"TEST-{Guid.NewGuid():N}"[..16], 1, "Active",
            null, null, "{}", 1, createdAtUtc);
        context.Add(profile);
        await context.SaveChangesAsync();

        var packageLimit = new PackageLimitVersion(
            Guid.NewGuid(), profile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m,
            null, null, createdAtUtc);
        context.PackageLimitVersions.Add(packageLimit);
        await context.SaveChangesAsync();

        // 訂單刻意留下 60 元實付運費：免運追回需要的基準運費沒有訂單快照，
        // 免運訂單會被 RefundTrustedInputsReader 依 DEC-P287 拒絕。
        var order = Order.Create(
            Guid.NewGuid(),
            new OrderCreation(
                $"ORD-{Guid.NewGuid():N}"[..32], null,
                $"guest-{Guid.NewGuid():N}@example.test",
                OrderStatus.Completed, PaymentStatus.Paid, FulfillmentStatus.Delivered,
                AssemblyStatus.NotRequired,
                1000m, 0m, freeShipping ? 0m : 60m, 0m, freeShipping ? 1000m : 1060m,
                "Test Recipient", "0900000000", "guest@example.test",
                "100", "Taipei", "Zhongzheng", "Test address", null,
                "HOME", profile.Id, null, null, null, 1, 1, null, null,
                $"checkout-{Guid.NewGuid():N}", null, 1, 1,
                new OrderInvoicePreference(
                    SimulatedInvoiceBuyerType.Individual,
                    "guest@example.test", null, null, null, null),
                freeShipping ? 1000m : 1060m,
                null,
                new OrderPackageSnapshot(packageLimit.Id, 1m, 40m, 30m, 20m, 90m, 100m),
                // 免運規則套用前的配送方式基本費。舊訂單為 Null 且不回填。
                withBaseFeeSnapshot ? 60m : null),
            createdAtUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var item = new OrderItem(
            Guid.NewGuid(), order.Id, null, "SKU-1", "Product", "Sku",
            quantity: 2, listUnitPrice: 500m, saleUnitPrice: 500m, finalUnitPrice: 500m,
            unitCostSnapshot: 300m, lineSubtotal: 1000m, discountAllocation: 0m,
            lineTotal: 1000m, assemblyGroupKey: null, returnableQuantity: returnableQuantity,
            createdAtUtc: createdAtUtc, isCouponEligible: false,
            specificationSnapshot: new OrderItemSpecificationSnapshot("{}", "{}", 1));
        context.OrderItems.Add(item);

        // 可退款餘額 = 已成功收款 - 其他退款已成功累計。付款必須真的走到 Paid，
        // 否則餘額為 0，每一條測試都會先撞上 refund_amount_exceeded。
        var attempt = new PaymentAttempt(
            Guid.NewGuid(), order.Id, PaymentMethod.CreditCard, paidAmount, null,
            $"pay-{Guid.NewGuid():N}", null, createdAtUtc);
        attempt.Transition(PaymentAttemptStatus.AwaitingPayment, createdAtUtc);
        attempt.Transition(PaymentAttemptStatus.Processing, createdAtUtc);
        attempt.Transition(PaymentAttemptStatus.Paid, createdAtUtc);
        context.Add(attempt);
        await context.SaveChangesAsync();

        var returnRequest = new ReturnRequest(
            Guid.NewGuid(), $"RT-{Guid.NewGuid():N}"[..20], order.Id, null,
            reasonCode, "Damaged on arrival", 1, createdAtUtc);
        returnRequest.Transition(ReturnRequestStatus.UnderReview, createdAtUtc);
        if (withTrustedInputs)
        {
            returnRequest.CaptureRefundTrustedInputs(
                AssemblyFeeDisposition.NotApplicable, returnShippingCost: 0m, createdAtUtc);
        }

        context.ReturnRequests.Add(returnRequest);
        await context.SaveChangesAsync();

        context.ReturnItems.Add(new ReturnItem(
            Guid.NewGuid(), returnRequest.Id, item.Id, quantity: returnedQuantity,
            requestedRefund: 500m * returnedQuantity, inspectionStatus: "Pending", createdAtUtc));

        var refund = new Refund(
            Guid.NewGuid(), order.Id, returnRequest.Id, attempt.Id,
            $"RF-{Guid.NewGuid():N}"[..20], requestedAmount: Math.Max(approvedAmount, 500m),
            reasonCode: "customer_request", requestedBy: RefundExecutorSqlFixture.AdminUserId,
            idempotencyKey: $"create-{Guid.NewGuid():N}", createdAtUtc);
        refund.Approve(approvedAmount, RefundExecutorSqlFixture.AdminUserId, createdAtUtc.AddHours(1));
        context.Refunds.Add(refund);
        await context.SaveChangesAsync();

        return refund;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTime utcNow) => _utcNow = new DateTimeOffset(utcNow);

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
