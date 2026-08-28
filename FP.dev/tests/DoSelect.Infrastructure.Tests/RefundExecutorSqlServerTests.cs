using System.Data;
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

    public static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(BuildConnectionString())
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

        await using var firstContext = RefundExecutorSqlFixture.CreateContext();
        await using var secondContext = RefundExecutorSqlFixture.CreateContext();

        // Task.WhenAll 本身**不保證重疊**：兩邊可能循序完成，第二筆只是看到餘額
        // 已經不足而失敗，測試照樣通過卻沒有測到競爭。barrier 讓兩個執行緒都抵達
        // 同一點之後才進入 Executor，兩個交易因此真的同時存在。
        using var barrier = new Barrier(2);

        async Task<ExecuteRefundResult> RunAsync(
            DoSelectDbContext context, Refund refund)
        {
            await Task.Yield();
            barrier.SignalAndWait();
            return await CreateExecutor(context).ExecuteAsync(Request(refund));
        }

        var results = await Task.WhenAll(
            RunAsync(firstContext, first),
            RunAsync(secondContext, second));

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
