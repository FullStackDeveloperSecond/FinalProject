using System.Data;
using System.Data.Common;
using System.Net;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Refunds;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Promotions;
using DoSelect.Domain.Refunds;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Persistence.Returns;
using DoSelect.Infrastructure.Refunds;
using DoSelect.Infrastructure.Orders;
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

    /// <summary>
    /// 第二位財務管理員。並行測試需要**不同的 Actor Scope** ——
    /// 同一位管理員的兩個請求會先被冪等索引的鍵範圍鎖序列化，
    /// 根本到不了退款餘額查詢，那樣測到的不是退款層的保護。
    /// </summary>
    public static string SecondAdminUserId { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        // 執行路徑會在同一交易內重查管理員資格：帳號型別、AccountStatus、
        // AdminProfile.IsActive 與退款角色，四項與登入資格逐項一致。
        // 夾具因此必須把管理員種成**真的能登入**的狀態 —— 先前只建了帳號與角色，
        // 那樣的管理員其實連後台都登不進去。
        var admin = CreateActiveAdmin();
        var role = new IdentityRole(AuditRoleNames.FinanceManager);
        context.AddRange(admin, role);
        await context.SaveChangesAsync();
        context.Add(ActiveProfileFor(admin));
        await context.SaveChangesAsync();

        var secondAdmin = CreateActiveAdmin();
        context.Add(secondAdmin);
        await context.SaveChangesAsync();
        context.Add(ActiveProfileFor(secondAdmin));
        await context.SaveChangesAsync();

        context.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = admin.Id, RoleId = role.Id },
            new IdentityUserRole<string> { UserId = secondAdmin.Id, RoleId = role.Id });
        await context.SaveChangesAsync();

        AdminUserId = admin.Id;
        SecondAdminUserId = secondAdmin.Id;
    }

    /// <summary>建立一位帳號已啟用的管理員。</summary>
    /// <remarks>
    /// <c>CreateAdmin</c> 出來是 <c>PendingEmailVerification</c>；
    /// <c>ConfirmEmail</c> 才會轉成 <c>Active</c>，那是退款資格重查要求的狀態。
    /// </remarks>
    public static ApplicationUser CreateActiveAdmin()
    {
        var createdAtUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var admin = ApplicationUser.CreateAdmin(
            Guid.NewGuid(),
            $"refund-admin-{Guid.NewGuid():N}@example.test",
            createdAtUtc);
        admin.ConfirmEmail(createdAtUtc);
        return admin;
    }

    /// <summary>建立一份啟用中的 <c>AdminProfile</c>。沒有 Profile 一律視為不合格。</summary>
    public static AdminProfile ActiveProfileFor(ApplicationUser admin) =>
        new(
            admin.Id,
            Guid.NewGuid(),
            $"EMP-{Guid.NewGuid():N}"[..12],
            "退款測試管理員",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

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
    public async Task RefundListFiltersAndReturnsTheCompleteDetailWithoutInternalIds()
    {
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);
        await CreateExecutor(context).ExecuteAsync(Request(refund));

        await using var queryContext = RefundExecutorSqlFixture.CreateContext();
        var result = await new RefundReader(queryContext).ListAsync(new AdminRefundQuery(
            [RefundStatus.Succeeded],
            NowUtc.AddDays(-7),
            NowUtc.AddDays(1),
            refund.RefundNumber,
            PageNumber: 1,
            PageSize: 20));

        var item = Assert.Single(result.Items);
        Assert.Equal(refund.PublicId, item.PublicId);
        Assert.Equal(RefundStatus.Succeeded, item.Status);
        Assert.NotEmpty(item.Allocations);
        Assert.NotNull(item.RequestedBy);
        Assert.NotNull(item.ApprovedBy);
        Assert.NotNull(item.ExecutedBy);
        Assert.DoesNotContain(RefundExecutorSqlFixture.AdminUserId, item.ExecutedBy!.MaskedLabel);
        Assert.Equal(DateTimeKind.Utc, item.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, item.SucceededAtUtc!.Value.Kind);
    }

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

        var order = await verify.Orders.SingleAsync(candidate => candidate.Id == stored.OrderId);
        Assert.Equal(OrderRefundStatus.PartiallyRefunded, order.OrderRefundStatus);
        Assert.Equal(500m, order.RefundedAmount);

        var projectionHistory = Assert.Single(await verify.OrderStatusHistories
            .Where(candidate =>
                candidate.OrderId == stored.OrderId &&
                candidate.StateDimension == OrderStateDimension.OrderRefundStatus &&
                candidate.ToStatus == OrderRefundStatus.PartiallyRefunded.ToString())
            .ToListAsync());
        Assert.Equal(OrderRefundStatus.Pending.ToString(), projectionHistory.FromStatus);
        Assert.Equal("refund-succeeded", projectionHistory.ReasonCode);
        Assert.Equal(RefundExecutorSqlFixture.AdminUserId, projectionHistory.ActorUserId);
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
    public async Task ASuccessfulExecutionCompletesTheAssociatedReturnInTheSameTransaction()
    {
        // #98 追蹤（接續 #99 A1）：正常正額退款執行成功後，先前沒有 production 呼叫端
        // 把對應退貨從 AwaitingRefund 推到 Completed，退貨會永遠卡住。這裡證明
        // RefundExecutor 執行成功時，退貨的結案與退款狀態、分攤、稽核同一筆交易落地。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);
        var returnRequestId = refund.ReturnRequestId!.Value;

        await using var verifyBefore = RefundExecutorSqlFixture.CreateContext();
        Assert.Equal(
            ReturnRequestStatus.AwaitingRefund,
            (await verifyBefore.ReturnRequests.SingleAsync(r => r.Id == returnRequestId)).Status);

        var result = await CreateExecutor(context).ExecuteAsync(Request(refund));
        Assert.True(result.IsSuccess);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var returnRequest = await verify.ReturnRequests.SingleAsync(r => r.Id == returnRequestId);
        Assert.Equal(ReturnRequestStatus.Completed, returnRequest.Status);

        var history = Assert.Single(await verify.ReturnStatusHistories
            .Where(h => h.ReturnRequestId == returnRequestId && h.ToStatus == ReturnRequestStatus.Completed)
            .ToListAsync());
        Assert.Equal(ReturnRequestStatus.AwaitingRefund, history.FromStatus);
        Assert.Equal("refund-succeeded", history.ReasonCode);
        Assert.Equal(RefundExecutorSqlFixture.AdminUserId, history.ActorUserId);
    }

    [RefundExecutorSqlFact]
    public async Task AFailedExecutionLeavesTheAssociatedReturnInAwaitingRefund()
    {
        // 執行失敗時（例如版本衝突）退貨不能單獨被推進 Completed——沒有真的收到錢的
        // 退貨若顯示已結案，顧客與稽核都會被誤導。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);
        var returnRequestId = refund.ReturnRequestId!.Value;

        await using var execute = RefundExecutorSqlFixture.CreateContext();
        var result = await CreateExecutor(execute).ExecuteAsync(
            Request(refund) with { RefundRowVersion = [9, 9, 9, 9, 9, 9, 9, 9] });
        Assert.Equal(RefundErrorCodes.ConcurrencyConflict, result.ErrorCode);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var returnRequest = await verify.ReturnRequests.SingleAsync(r => r.Id == returnRequestId);
        Assert.Equal(ReturnRequestStatus.AwaitingRefund, returnRequest.Status);
        Assert.False(await verify.ReturnStatusHistories
            .AnyAsync(h => h.ReturnRequestId == returnRequestId && h.ToStatus == ReturnRequestStatus.Completed));
    }

    [RefundExecutorSqlFact]
    public async Task ARefundWhoseReturnAlreadyLeftAwaitingRefundThrowsAndWritesNothing()
    {
        // 正常流程下不可能發生：一張退貨只會有唯一一筆 Refund，且那筆 Refund 建立的
        // 同一筆交易才把退貨推進 AwaitingRefund，該狀態唯一出口是 Completed。這裡
        // 直接繞過正常流程人為構造「別的原因已經把退貨結案，退款卻還沒執行」，
        // 證明這個不變量真的被守住：寧可整筆退款執行回滾，也不要靜默略過或誤結案。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);
        var returnRequestId = refund.ReturnRequestId!.Value;

        await using (var corrupt = RefundExecutorSqlFixture.CreateContext())
        {
            var returnRequest = await corrupt.ReturnRequests.SingleAsync(r => r.Id == returnRequestId);
            returnRequest.Transition(ReturnRequestStatus.Completed, NowUtc);
            await corrupt.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateExecutor(context).ExecuteAsync(Request(refund)));

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.Approved, stored.Status);
        Assert.False(await verify.RefundAllocations.AnyAsync(a => a.RefundId == stored.Id));
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
    public async Task AReplayAfterTheFinanceRoleIsRevokedIsForbidden()
    {
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);
        var adminUserId = await SeedFinanceManagerAsync(context);
        var request = Request(refund, adminUserId);

        await using (var first = RefundExecutorSqlFixture.CreateContext())
        {
            var initial = await CreateExecutor(first).ExecuteAsync(request);
            Assert.True(initial.IsSuccess);
        }

        await using (var revoking = RefundExecutorSqlFixture.CreateContext())
        {
            var assignments = await revoking.UserRoles
                .Where(assignment => assignment.UserId == adminUserId)
                .ToArrayAsync();
            revoking.UserRoles.RemoveRange(assignments);
            await revoking.SaveChangesAsync();
        }

        await using var replaying = RefundExecutorSqlFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => CreateExecutor(replaying).ExecuteAsync(request));

        Assert.Equal(403, exception.StatusCode);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(candidate => candidate.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.Succeeded, stored.Status);
        Assert.Equal(1, await verify.Set<AuditLog>()
            .CountAsync(log => log.ResourcePublicId == refund.PublicId));
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

        var order = await verify.Orders.SingleAsync(candidate => candidate.Id == stored.OrderId);
        Assert.Equal(OrderRefundStatus.Refunded, order.OrderRefundStatus);
        Assert.Equal(1000m, order.RefundedAmount);

        // 完整退貨不得產生免運追回。
        Assert.False(await verify.RefundAllocations.AnyAsync(a =>
            a.RefundId == stored.Id &&
            a.AllocationType == RefundAllocationType.ShippingClawback));
    }

    /// <summary>
    /// 管理員在交易外的身分解析之後、退款交易之內被撤權，這筆退款必須整個擋下來。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 先前整個 Actor（含角色快照）都在交易之外解析，於是「解析通過」到「交易開啟」
    /// 之間存在一個授權窗口：財務管理員在這段時間被撤銷角色，handler 仍會拿舊快照
    /// 完成退款並寫稽核。Controller 的 Policy 用的是既有登入 Claims，補不掉這個窗口。
    /// </para>
    /// <para>
    /// 這條測試用 interceptor 精準命中那個窗口：攔到交易外那道 <c>AspNetUsers</c>
    /// 查詢之後，用另一條連線把角色撤掉，接著交易才開始。
    /// </para>
    /// <para>
    /// 刻意**另外建一位專用管理員**再撤他的權：整組測試共用
    /// <c>AdminUserId</c>，撤掉它會連累同一個 class 裡其他測試。
    /// </para>
    /// </remarks>
    [RefundExecutorSqlFact]
    public async Task ARoleRevokedAfterTheIdentityLookupStopsTheRefundInsideTheTransaction()
    {
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);
        var doomedAdminId = await SeedFinanceManagerAsync(context);

        var revoker = new RevokeRoleAfterIdentityLookupInterceptor(doomedAdminId);
        await using var executing = RefundExecutorSqlFixture.CreateContext(revoker);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => CreateExecutor(executing).ExecuteAsync(Request(refund, doomedAdminId)));

        Assert.Equal(403, exception.StatusCode);
        Assert.True(revoker.Revoked, "撤權從未發生，這一輪沒有命中那個窗口。");

        // 交易必須整個回滾：狀態、分攤、稽核與冪等完成紀錄都不得留下。
        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);

        Assert.Equal(RefundStatus.Approved, stored.Status);
        Assert.Null(stored.SucceededAmount);
        Assert.False(await verify.RefundAllocations.AnyAsync(a => a.RefundId == stored.Id));
        Assert.False(await verify.Set<AuditLog>()
            .AnyAsync(log => log.ResourcePublicId == refund.PublicId));
        Assert.False(await verify.IdempotencyRecords
            .AnyAsync(record => record.Key == Request(refund, doomedAdminId).IdempotencyKey));
    }

    /// <summary>
    /// 帳號在交易開始的窗口被停權，退款必須整個擋下來。
    /// </summary>
    /// <remarks>
    /// 交易內的資格重查要與管理員登入資格逐項一致。只查角色不夠：帳號被停權但
    /// 角色列還在時，退款照樣會完成，形成「登入時擋得住、執行退款時擋不住」的落差。
    /// </remarks>
    [RefundExecutorSqlFact]
    public async Task AnAccountSuspendedAfterTheIdentityLookupStopsTheRefund()
    {
        await AssertRefundIsRefusedWhenAsync(
            (admin, profile) => admin.Suspend(NowUtc),
            "帳號已停權，退款仍然完成了。");
    }

    /// <summary>
    /// <c>AdminProfile</c> 在交易開始的窗口被停用，退款必須整個擋下來。
    /// </summary>
    [RefundExecutorSqlFact]
    public async Task AnAdminProfileDeactivatedAfterTheIdentityLookupStopsTheRefund()
    {
        await AssertRefundIsRefusedWhenAsync(
            (admin, profile) => profile.SetActive(false, NowUtc),
            "AdminProfile 已停用，退款仍然完成了。");
    }

    /// <summary>
    /// 在交易開始的那一刻改變管理員資格，斷言退款被擋下且零寫入。
    /// </summary>
    /// <remarks>
    /// 攔截點是<b>交易開始</b>：交易外的身分解析已經全部做完，交易還沒開 ——
    /// 正是要驗的窗口。改用獨立的 <c>DbContext</c> 變更，才不會被拉進待測交易。
    /// </remarks>
    private static async Task AssertRefundIsRefusedWhenAsync(
        Action<ApplicationUser, AdminProfile> makeIneligible,
        string failureMessage)
    {
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context);
        var doomedAdminId = await SeedFinanceManagerAsync(context);

        var changer = new ChangeAdminEligibilityAtTransactionStartInterceptor(
            doomedAdminId, makeIneligible);

        await using var executing = RefundExecutorSqlFixture.CreateContext(changer);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => CreateExecutor(executing).ExecuteAsync(Request(refund, doomedAdminId)));

        Assert.Equal(403, exception.StatusCode);
        Assert.True(changer.Applied, "資格從未被改變，這一輪沒有命中那個窗口。");

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);

        Assert.Equal(RefundStatus.Approved, stored.Status);
        Assert.Null(stored.SucceededAmount);
        Assert.False(
            await verify.RefundAllocations.AnyAsync(a => a.RefundId == stored.Id),
            failureMessage);
        Assert.False(await verify.Set<AuditLog>()
            .AnyAsync(log => log.ResourcePublicId == refund.PublicId));
        Assert.False(await verify.IdempotencyRecords
            .AnyAsync(record => record.Key == Request(refund, doomedAdminId).IdempotencyKey));
    }

    /// <summary>
    /// 在退款交易即將開始的那一刻，用另一條連線改掉管理員的資格。
    /// </summary>
    private sealed class ChangeAdminEligibilityAtTransactionStartInterceptor : DbTransactionInterceptor
    {
        private readonly string _adminUserId;
        private readonly Action<ApplicationUser, AdminProfile> _makeIneligible;
        private int _done;

        public ChangeAdminEligibilityAtTransactionStartInterceptor(
            string adminUserId,
            Action<ApplicationUser, AdminProfile> makeIneligible)
        {
            _adminUserId = adminUserId;
            _makeIneligible = makeIneligible;
        }

        public bool Applied => Volatile.Read(ref _done) == 2;

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _done, 1, 0) == 0)
            {
                Apply();
                Volatile.Write(ref _done, 2);
            }

            return ValueTask.FromResult(result);
        }

        private void Apply()
        {
            using var changing = RefundExecutorSqlFixture.CreateContext();
            var admin = changing.Users.Single(user => user.Id == _adminUserId);
            var profile = changing.AdminProfiles.Single(entry => entry.UserId == _adminUserId);
            _makeIneligible(admin, profile);
            changing.SaveChanges();
        }
    }

    /// <summary>建立一位只給單一測試用的財務管理員，回傳 Identity Id。</summary>
    private static async Task<string> SeedFinanceManagerAsync(DoSelectDbContext context)
    {
        var admin = RefundExecutorSqlFixture.CreateActiveAdmin();
        context.Add(admin);
        await context.SaveChangesAsync();
        context.Add(RefundExecutorSqlFixture.ActiveProfileFor(admin));
        await context.SaveChangesAsync();

        var role = await context.Roles.SingleAsync(
            candidate => candidate.Name == AuditRoleNames.FinanceManager);
        context.UserRoles.Add(new IdentityUserRole<string>
        {
            UserId = admin.Id,
            RoleId = role.Id,
        });
        await context.SaveChangesAsync();

        return admin.Id;
    }

    /// <summary>
    /// 在退款交易即將開始的那一刻，用另一條連線撤掉管理員的角色。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 攔截點必須是**交易開始**，才對得準要驗的窗口：交易外的身分解析已經全部做完，
    /// 交易還沒開。
    /// </para>
    /// <para>
    /// 第一版攔的是 <c>FROM [AspNetUsers]</c> 查詢之後 —— 那個位置**驗不出東西**：
    /// 舊實作在交易外解析時，角色查詢就排在使用者查詢後面，撤權照樣會被它讀到，
    /// 所以連沒修的版本都是綠的。
    /// </para>
    /// <para>
    /// 撤權用獨立的 <c>DbContext</c>，才不會被拉進待測的那筆交易裡。
    /// </para>
    /// </remarks>
    private sealed class RevokeRoleAfterIdentityLookupInterceptor : DbTransactionInterceptor
    {
        private readonly string _adminUserId;
        private int _done;

        public RevokeRoleAfterIdentityLookupInterceptor(string adminUserId) =>
            _adminUserId = adminUserId;

        public bool Revoked => Volatile.Read(ref _done) == 2;

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _done, 1, 0) == 0)
            {
                Revoke();
                Volatile.Write(ref _done, 2);
            }

            return ValueTask.FromResult(result);
        }

        private void Revoke()
        {
            using var revoking = RefundExecutorSqlFixture.CreateContext();
            var assignments = revoking.UserRoles
                .Where(assignment => assignment.UserId == _adminUserId)
                .ToArray();
            revoking.UserRoles.RemoveRange(assignments);
            revoking.SaveChanges();
        }
    }

    [RefundExecutorSqlFact]
    public async Task TwoConcurrentRefundsOnTheSameOrderCannotExceedThePaidAmount()
    {
        // 這是 Serializable 範圍鎖存在的**唯一理由**：兩位不同的財務管理員同時對
        // 同一張訂單執行兩筆不同的退款。各自核准 500，訂單只付了 700 ——
        // 兩筆都成功就是超額退款。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var (first, second) = await SeedTwoRefundsOnOneOrderAsync(context);

        // **兩邊必須是不同的管理員。** 同一位管理員的兩個請求會先被冪等紀錄的
        // 鍵範圍鎖序列化（Actor Scope 是冪等鍵的一部分），第二筆根本走不到退款
        // 餘額查詢 —— 那樣測到的是冪等索引，不是 Refunds(OrderId) 的範圍鎖。
        var firstRequest = Request(first);
        var secondRequest = Request(second, RefundExecutorSqlFixture.SecondAdminUserId);
        Assert.NotEqual(firstRequest.ExecutedByAdminUserId, secondRequest.ExecutedByAdminUserId);
        Assert.NotEqual(firstRequest.IdempotencyKey, secondRequest.IdempotencyKey);

        // 第一筆讀完可退款餘額、握住 Refunds(OrderId) 的範圍鎖之後 Set 這個事件，
        // 第二筆在**交易之外**等它，再開始執行。
        //
        // 兩個等待都是單向的：第一筆只等時間、第二筆只等事件而且還沒開交易，
        // 因此不會在持有鎖時互等。純用時間窗口不夠 —— CI 上出現過第二筆沒在
        // 窗口內抵達，兩筆循序完成。
        using var firstIsHoldingLocks = new ManualResetEventSlim(false);
        using var secondIsEnteringTransaction = new ManualResetEventSlim(false);

        var firstProbe = new ConcurrencyProbeInterceptor(
            signalWhenHoldingLocks: firstIsHoldingLocks,
            waitForOtherSide: secondIsEnteringTransaction);
        var secondProbe = new ConcurrencyProbeInterceptor(
            signalBeforeTransactionBody: secondIsEnteringTransaction);

        await using var firstContext = RefundExecutorSqlFixture.CreateContext(firstProbe);
        await using var secondContext = RefundExecutorSqlFixture.CreateContext(secondProbe);

        async Task<ExecuteRefundResult> RunFirstAsync()
        {
            await Task.Yield();
            return await CreateExecutor(firstContext).ExecuteAsync(firstRequest);
        }

        async Task<ExecuteRefundResult> RunSecondAsync()
        {
            await Task.Yield();

            // 等待點在交易之外，不持有任何鎖。
            firstIsHoldingLocks.Wait(TimeSpan.FromSeconds(30));

            return await CreateExecutor(secondContext).ExecuteAsync(secondRequest);
        }

        var results = await Task.WhenAll(RunFirstAsync(), RunSecondAsync());

        Assert.True(
            firstIsHoldingLocks.IsSet,
            "第一筆從未讀完餘額，第二筆等不到它進入交易，這一輪沒有製造出競爭。");
        Assert.True(
            secondIsEnteringTransaction.IsSet,
            "第二筆從未進入交易本體，第一筆等不到它，這一輪沒有製造出競爭。");

        // 併發的證據：第二筆是在第一筆握住交易之後才開始的，而且第一筆一直等到
        // 第二筆宣告「我要進交易了」才多握兩秒。所以第二筆這段等待完全發生在
        // 資料庫裡 —— 兩筆交易同時存在，第二筆被擋住直到第一筆放手。
        // 循序完成不會出現這種等待（對照組實測只有幾十毫秒）。
        //
        // **刻意不斷言是哪一道 SQL 被擋。** 實測阻塞點會隨環境改變：單獨跑時第二筆
        // 停在冪等表的存取；併進完整 Infrastructure 回合時，它很快通過冪等表，
        // 改為卡在 `INSERT INTO [AuditLogs]` 約 2.3 秒。鎖的範圍取決於當下的資料量
        // 與索引狀態，不是這條測試該固定的東西。
        //
        // 因此這裡只主張「兩筆交易確實重疊，而且資料庫擋住了第二筆」，
        // 不主張它證明了 `Refunds(OrderId)` 範圍鎖。失敗訊息會印出實際卡住的那道
        // SQL，方便下次判讀。
        Assert.True(
            secondProbe.LongestBlockedWait >= TimeSpan.FromMilliseconds(400),
            $"第二筆最長只被擋了 {secondProbe.LongestBlockedWait.TotalMilliseconds:F0}ms" +
            $"（卡在：{Excerpt(secondProbe.BlockedCommand)}；" +
            $"冪等表上 {secondProbe.WaitOnIdempotencyTable.TotalMilliseconds:F0}ms），" +
            "兩筆交易並未同時存在，這一輪沒有測到競爭。");

        // 兩邊都真的跑過交易本體。
        Assert.True(firstProbe.TransactionAttempts >= 1, "第一筆沒有執行交易本體。");
        Assert.True(secondProbe.TransactionAttempts >= 1, "第二筆沒有執行交易本體。");

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

        await using (var afterFirst = RefundExecutorSqlFixture.CreateContext())
        {
            var order = await afterFirst.Orders.SingleAsync(candidate => candidate.Id == first.OrderId);
            // 第二筆仍是 Approved，因此彙總維持 Pending；成功累計仍必須同步成 933.33。
            Assert.Equal(OrderRefundStatus.Pending, order.OrderRefundStatus);
            Assert.Equal(933.33m, order.RefundedAmount);
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

        var projectedOrder = await verify.Orders.SingleAsync(candidate => candidate.Id == first.OrderId);
        Assert.Equal(OrderRefundStatus.Refunded, projectedOrder.OrderRefundStatus);
        Assert.Equal(1460m, projectedOrder.RefundedAmount);
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
    /// 量測一筆退款在資料庫裡被擋住多久、擋在哪一道 SQL，並協調兩筆的重疊。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 攔截點在命令送出之前，所以某道命令的阻塞會呈現在它與**下一道**命令之間的
    /// 間隔 —— 因此間隔一律歸給前一道命令，<c>BlockedCommand</c> 記的就是那一道。
    /// </para>
    /// <para>
    /// 重疊靠兩個單向訊號，不用固定睡眠：第一筆讀完餘額後 Set
    /// <c>signalWhenHoldingLocks</c>，然後等 <c>waitForOtherSide</c>；第二筆在交易之外
    /// 等第一個訊號，並在自己第一道進交易的命令送出前 Set 第二個訊號。
    /// 第二筆 Set 完才開始阻塞，第一筆等到訊號就往前走，兩邊不會互等。
    /// </para>
    /// <para>
    /// 固定睡眠不可靠：滿載的完整測試回合裡，第二筆可能在睡眠結束後才被排到，
    /// 兩筆就變成循序完成。
    /// </para>
    /// </remarks>
    private sealed class ConcurrencyProbeInterceptor : DbCommandInterceptor
    {
        /// <summary>共用 Executor 每執行一次交易本體，就會寫一次冪等紀錄。</summary>
        private const string TransactionBodyMarker = "INSERT INTO [IdempotencyRecords]";

        /// <summary>
        /// 交易內第一道會碰到的表。宣告點必須放在這裡，不能放在 INSERT ——
        /// 冪等紀錄的**查詢**就已經可能卡在對方的鎖上，那時還沒送出 INSERT，
        /// 對方會等不到宣告而逾時。
        /// </summary>
        private const string IdempotencyTableMarker = "IdempotencyRecords";

        /// <summary>可退款餘額的第二段：其他已成功退款的累計。</summary>
        private const string BalanceReadMarker = "SUM([r].[SucceededAmount])";

        /// <summary>
        /// 等對方宣告的上限。刻意壓短：這段等待是**握著 Serializable 鎖**在等，
        /// 設太長會在對方沒出現時把鎖抱住很久，連累同一台 SQL Server 上其他測試
        /// （實測 30 秒版本讓完整回合裡好幾條無關測試卡到四分半而失敗）。
        /// </summary>
        private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 收到對方「我要進交易了」之後再多握住鎖的時間。
        /// </summary>
        /// <remarks>
        /// 訊號只保證**順序**（對方確實已經在等鎖），這段固定持有才保證**時長**：
        /// 沒有它的話第一筆收到訊號就立刻放行，對方只被擋幾百毫秒，斷言得靠一個
        /// 和機器速度賽跑的門檻。
        /// </remarks>
        private static readonly TimeSpan HoldAfterTheOtherSideIsWaiting =
            TimeSpan.FromMilliseconds(800);

        private readonly System.Diagnostics.Stopwatch _clock =
            System.Diagnostics.Stopwatch.StartNew();

        private readonly object _gate = new();

        private readonly ManualResetEventSlim? _signalWhenHoldingLocks;
        private readonly ManualResetEventSlim? _waitForOtherSide;
        private readonly ManualResetEventSlim? _signalBeforeTransactionBody;

        private int _transactionAttempts;
        private long _previousCommandTicks;
        private string _previousCommandText = string.Empty;
        private long _longestBlockedWaitTicks;
        private string _blockedCommand = string.Empty;
        private bool _previousTouchedIdempotency;
        private long _idempotencyWaitTicks;
        private bool _balanceRead;
        private int _held;
        private int _announced;

        public ConcurrencyProbeInterceptor(
            ManualResetEventSlim? signalWhenHoldingLocks = null,
            ManualResetEventSlim? waitForOtherSide = null,
            ManualResetEventSlim? signalBeforeTransactionBody = null)
        {
            _signalWhenHoldingLocks = signalWhenHoldingLocks;
            _waitForOtherSide = waitForOtherSide;
            _signalBeforeTransactionBody = signalBeforeTransactionBody;
        }

        public int TransactionAttempts => Volatile.Read(ref _transactionAttempts);

        /// <summary>這一筆最久被資料庫擋住多久。</summary>
        public TimeSpan LongestBlockedWait
        {
            get { lock (_gate) { return TimeSpan.FromTicks(_longestBlockedWaitTicks); } }
        }

        /// <summary>被擋最久的是哪一道 SQL，只用在失敗訊息裡。</summary>
        public string BlockedCommand
        {
            get { lock (_gate) { return _blockedCommand; } }
        }

        /// <summary>
        /// 存取冪等表的那道 SQL 自己被擋住多久 —— 這才是這條路徑的序列化點。
        /// </summary>
        /// <remarks>
        /// 刻意不用「所有命令裡最長的間隔」：滿載的完整測試回合裡，某道無關的命令
        /// 也可能因為機器忙碌而間隔更久，斷言就會指向錯誤的地方。
        /// </remarks>
        public TimeSpan WaitOnIdempotencyTable
        {
            get { lock (_gate) { return TimeSpan.FromTicks(_idempotencyWaitTicks); } }
        }

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
            var enteringTransactionBody =
                command.CommandText.Contains(TransactionBodyMarker, StringComparison.Ordinal);

            // 第二筆：碰冪等表之前先宣告，接著這道命令就會卡在第一筆的鎖上。
            if (command.CommandText.Contains(IdempotencyTableMarker, StringComparison.Ordinal) &&
                _signalBeforeTransactionBody is not null &&
                Interlocked.CompareExchange(ref _announced, 1, 0) == 0)
            {
                _signalBeforeTransactionBody.Set();
            }

            // 第一筆：讀完餘額、握住鎖之後放行對方，並等它宣告自己已經要進交易。
            var heldThisTime = false;
            if (_balanceRead &&
                _signalWhenHoldingLocks is not null &&
                Interlocked.CompareExchange(ref _held, 1, 0) == 0)
            {
                _signalWhenHoldingLocks.Set();
                _waitForOtherSide?.Wait(SignalTimeout);
                Thread.Sleep(HoldAfterTheOtherSideIsWaiting);
                heldThisTime = true;
            }

            var now = _clock.Elapsed.Ticks;
            var previous = Interlocked.Exchange(ref _previousCommandTicks, now);

            // 間隔歸給前一道命令：阻塞發生在它執行的那段時間裡。
            //
            // 自己刻意的等待必須整段丟掉。先前把計時點放在等待之後，以為這樣就
            // 排除了 —— 其實反而把自己的等待算成「被對方擋住」，連循序執行的
            // 對照組都會綠。
            if (!heldThisTime && previous > 0)
            {
                lock (_gate)
                {
                    if (now - previous > _longestBlockedWaitTicks)
                    {
                        _longestBlockedWaitTicks = now - previous;
                        _blockedCommand = _previousCommandText;
                    }

                    if (_previousTouchedIdempotency && now - previous > _idempotencyWaitTicks)
                    {
                        _idempotencyWaitTicks = now - previous;
                    }
                }
            }

            // 保留完整文字：截斷會把表名切掉（EF 的欄位清單很長），
            // 之後想比對「卡在哪張表」就永遠對不上。顯示時才截短。
            _previousCommandText = string.Join(" ", command.CommandText.Split());
            _previousTouchedIdempotency =
                command.CommandText.Contains(IdempotencyTableMarker, StringComparison.Ordinal);

            if (enteringTransactionBody)
            {
                Interlocked.Increment(ref _transactionAttempts);
            }

            if (command.CommandText.Contains(BalanceReadMarker, StringComparison.Ordinal))
            {
                _balanceRead = true;
            }
        }
    }

    /// <summary>訊息用的短版 SQL，長度只影響可讀性，不影響比對。</summary>
    private static string Excerpt(string commandText) =>
        commandText.Length <= 100 ? commandText : commandText[..100] + "…";

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
            new RefundReturnCompletionPort(context),
            new EfRefundOrderProjectionPort(context),
            timeProvider);
    }

    private static ExecuteRefundRequest Request(Refund refund, string? adminUserId = null) =>
        new(
            refund.PublicId,
            refund.RowVersion,
            $"refund-execute-{refund.PublicId:N}",
            adminUserId ?? RefundExecutorSqlFixture.AdminUserId,
            "customer_request",
            Note: null,
            CorrelationId: "refund-test-correlation",
            TraceId: new string('a', 32),
            RemoteIpAddress: null);

    private static IRefundApprover CreateApprover(DoSelectDbContext context)
    {
        var timeProvider = new FixedTimeProvider(NowUtc);
        return new RefundApprover(
            context,
            new EfAuditWriter(context, timeProvider),
            new EfIdempotencyExecutor(
                context,
                Options.Create(new IdempotencyOptions
                {
                    ActorScopePepper = new string('p', 48),
                }),
                timeProvider),
            new RefundReturnCompletionPort(context),
            new EfRefundOrderProjectionPort(context),
            timeProvider);
    }

    private static ApproveRefundRequest ApproveRequest(Refund refund, string? adminUserId = null) =>
        new(
            refund.PublicId,
            refund.RowVersion,
            $"refund-approve-{refund.PublicId:N}",
            adminUserId ?? RefundExecutorSqlFixture.AdminUserId,
            "customer_request",
            Note: null,
            CorrelationId: "refund-test-correlation",
            TraceId: new string('a', 32),
            RemoteIpAddress: null);

    // ── 退款核准（alex 2026-09-04 #98 WP2 裁定） ──────────────────────────────

    [RefundExecutorSqlFact]
    public async Task APendingReviewRefundWithACompleteSnapshotIsApprovedAndWritesAudit()
    {
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context, leaveAsPendingReview: true);

        var result = await CreateApprover(context).ApproveAsync(ApproveRequest(refund));

        Assert.True(result.IsSuccess);
        Assert.Equal(500m, result.ApprovedAmount);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.Approved, stored.Status);
        Assert.Equal(500m, stored.ApprovedAmount);

        var audit = await verify.Set<AuditLog>()
            .SingleAsync(log => log.ResourcePublicId == refund.PublicId);
        Assert.Equal(AuditActions.RefundApprove, audit.Action);
        Assert.Equal(AuditResult.Success, audit.Result);
    }

    [RefundExecutorSqlFact]
    public async Task ApprovingTwiceWithDifferentKeysIsAStateConflictNotADoubleApproval()
    {
        // 第一次核准成功後換一把新金鑰再送——不是重播，是對一筆已核准退款再次
        // 核准，必須是狀態衝突，不能把它變成核准兩次或悄悄改金額。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context, leaveAsPendingReview: true);
        var approver = CreateApprover(context);

        var first = await approver.ApproveAsync(ApproveRequest(refund));
        Assert.True(first.IsSuccess);

        await using var reread = RefundExecutorSqlFixture.CreateContext();
        var afterFirst = await reread.Refunds.AsNoTracking()
            .SingleAsync(r => r.PublicId == refund.PublicId);

        await using var second = RefundExecutorSqlFixture.CreateContext();
        var secondResult = await CreateApprover(second).ApproveAsync(new ApproveRefundRequest(
            refund.PublicId,
            afterFirst.RowVersion,
            $"refund-approve-second-{refund.PublicId:N}",
            RefundExecutorSqlFixture.AdminUserId,
            "customer_request",
            Note: null,
            CorrelationId: "refund-test-correlation",
            TraceId: new string('a', 32),
            RemoteIpAddress: null));

        Assert.Equal(RefundErrorCodes.RefundStateConflict, secondResult.ErrorCode);
    }

    [RefundExecutorSqlFact]
    public async Task TheSameApprovalKeyAndPayloadReplaysWithoutASecondAuditEntry()
    {
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context, leaveAsPendingReview: true);
        var request = ApproveRequest(refund);

        var first = await CreateApprover(context).ApproveAsync(request);
        var second = await CreateApprover(context).ApproveAsync(request);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.ApprovedAmount, second.ApprovedAmount);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var auditCount = await verify.Set<AuditLog>()
            .CountAsync(log => log.ResourcePublicId == refund.PublicId);
        Assert.Equal(1, auditCount);
    }

    [RefundExecutorSqlFact]
    public async Task AStaleRowVersionApprovalWritesNothing()
    {
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context, leaveAsPendingReview: true);

        await using var execute = RefundExecutorSqlFixture.CreateContext();
        var result = await CreateApprover(execute).ApproveAsync(
            ApproveRequest(refund) with { RefundRowVersion = [9, 9, 9, 9, 9, 9, 9, 9] });

        Assert.Equal(RefundErrorCodes.ConcurrencyConflict, result.ErrorCode);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.PendingReview, stored.Status);
        Assert.Null(stored.ApprovedAmount);
        Assert.False(await verify.Set<AuditLog>()
            .AnyAsync(log => log.ResourcePublicId == refund.PublicId));
    }

    [RefundExecutorSqlFact]
    public async Task AnIncompleteTrustedSnapshotApprovalWritesNothing()
    {
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(
            context, withTrustedInputs: false, leaveAsPendingReview: true);

        var result = await CreateApprover(context).ApproveAsync(ApproveRequest(refund));

        Assert.Equal(RefundErrorCodes.RefundSnapshotUnavailable, result.ErrorCode);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.PendingReview, stored.Status);
    }

    [RefundExecutorSqlFact]
    public async Task ARevokedFinanceRoleStopsTheApprovalInsideTheTransaction()
    {
        // 與執行端的同名測試同一個理由：角色重查必須在交易內，不能沿用 Controller
        // 解析當下的舊 Claims。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(context, leaveAsPendingReview: true);
        var revokableAdminId = await SeedFinanceManagerAsync(context);

        await using (var revoke = RefundExecutorSqlFixture.CreateContext())
        {
            var userRoles = await revoke.UserRoles
                .Where(role => role.UserId == revokableAdminId)
                .ToListAsync();
            revoke.UserRoles.RemoveRange(userRoles);
            await revoke.SaveChangesAsync();
        }

        // AuthorizeActorAsync 對撤權丟 Forbidden——不是 RefundRejectedException，
        // 因此不會變成帶錯誤碼的結果，而是原樣冒出讓共用 IIdempotencyExecutor 的
        // 交易整個回滾。
        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => CreateApprover(context).ApproveAsync(ApproveRequest(refund, revokableAdminId)));
        Assert.Equal(403, exception.StatusCode);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.PendingReview, stored.Status);
        Assert.Null(stored.ApprovedAmount);
        Assert.False(await verify.Set<AuditLog>()
            .AnyAsync(log => log.ResourcePublicId == refund.PublicId));
    }

    // ── 核准時重算淨額 <= 0（alex 2026-09-04 #103 裁定，延續 #99 A1）─────────────

    [RefundExecutorSqlFact]
    public async Task AZeroNetApprovalCancelsTheRefundAndCompletesTheReturn()
    {
        // 建立退款當下淨額還是正的；核准前優惠券門檻的追回讓可信快照重算出的淨額
        // 精準落到 0。這是合法終局，不是可重試錯誤：退款終止為 Cancelled，
        // 關聯退貨在同一筆交易被結案，而不是讓退款永遠卡在 PendingReview。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(
            context, leaveAsPendingReview: true, couponClawbackSwallowsRefund: true);
        var returnRequestId = refund.ReturnRequestId!.Value;

        var result = await CreateApprover(context).ApproveAsync(ApproveRequest(refund));

        Assert.True(result.IsSuccess);
        Assert.True(result.WasCancelled);
        Assert.NotNull(result.CancellationPlan);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.Cancelled, stored.Status);
        Assert.Null(stored.ApprovedAmount);

        var order = await verify.Orders.SingleAsync(candidate => candidate.Id == stored.OrderId);
        Assert.Equal(OrderRefundStatus.None, order.OrderRefundStatus);
        Assert.Equal(0m, order.RefundedAmount);

        var orderHistory = Assert.Single(await verify.OrderStatusHistories
            .Where(candidate =>
                candidate.OrderId == stored.OrderId &&
                candidate.StateDimension == OrderStateDimension.OrderRefundStatus &&
                candidate.ReasonCode == RefundApprover.ZeroNetApprovalReasonCode)
            .ToListAsync());
        Assert.Equal(OrderRefundStatus.Pending.ToString(), orderHistory.FromStatus);
        Assert.Equal(OrderRefundStatus.None.ToString(), orderHistory.ToStatus);

        var returnRequest = await verify.ReturnRequests.SingleAsync(r => r.Id == returnRequestId);
        Assert.Equal(ReturnRequestStatus.Completed, returnRequest.Status);

        var history = Assert.Single(await verify.ReturnStatusHistories
            .Where(h => h.ReturnRequestId == returnRequestId && h.ToStatus == ReturnRequestStatus.Completed)
            .ToListAsync());
        Assert.Equal(ReturnRequestStatus.AwaitingRefund, history.FromStatus);
        Assert.Equal(RefundApprover.ZeroNetApprovalReasonCode, history.ReasonCode);
        Assert.Equal(RefundExecutorSqlFixture.AdminUserId, history.ActorUserId);

        var audit = await verify.Set<AuditLog>()
            .SingleAsync(log => log.ResourcePublicId == refund.PublicId);
        Assert.Equal(AuditActions.RefundApprovalCancelled, audit.Action);
        Assert.Equal(AuditResult.Success, audit.Result);
    }

    [RefundExecutorSqlFact]
    public async Task AZeroNetApprovalWhoseReturnAlreadyLeftAwaitingRefundRollsBackTheWholeCancellation()
    {
        // 與執行端 ARefundWhoseReturnAlreadyLeftAwaitingRefundThrowsAndWritesNothing
        // 同一個理由：人為構造「別的原因已經把退貨結案，核准時的取消卻還沒發生」，
        // 證明退款的 Cancel 與退貨的 Complete 真的在同一筆交易——寧可整個回滾，
        // 也不讓退款單獨變成 Cancelled 卻留下一張已經結案的退貨被覆寫歷史。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(
            context, leaveAsPendingReview: true, couponClawbackSwallowsRefund: true);
        var returnRequestId = refund.ReturnRequestId!.Value;

        await using (var corrupt = RefundExecutorSqlFixture.CreateContext())
        {
            var returnRequest = await corrupt.ReturnRequests.SingleAsync(r => r.Id == returnRequestId);
            returnRequest.Transition(ReturnRequestStatus.Completed, NowUtc);
            await corrupt.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateApprover(context).ApproveAsync(ApproveRequest(refund)));

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.PendingReview, stored.Status);
        Assert.False(await verify.Set<AuditLog>()
            .AnyAsync(log => log.ResourcePublicId == refund.PublicId));
    }

    [RefundExecutorSqlFact]
    public async Task TheSameApprovalKeyReplaysTheCancellationWithoutASecondEffect()
    {
        // 同一把冪等金鑰重送不得把已經結案的退貨再結案一次（ReturnRequest.Transition
        // 只允許 AwaitingRefund → Completed，第二次一定丟例外)，也不得寫出第二筆稽核。
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var refund = await SeedRefundAsync(
            context, leaveAsPendingReview: true, couponClawbackSwallowsRefund: true);
        var returnRequestId = refund.ReturnRequestId!.Value;
        var request = ApproveRequest(refund);

        var first = await CreateApprover(context).ApproveAsync(request);
        var second = await CreateApprover(context).ApproveAsync(request);

        Assert.True(first.IsSuccess);
        Assert.True(first.WasCancelled);
        Assert.True(second.IsSuccess);
        Assert.True(second.WasCancelled);

        await using var verify = RefundExecutorSqlFixture.CreateContext();
        var stored = await verify.Refunds.SingleAsync(r => r.PublicId == refund.PublicId);
        Assert.Equal(RefundStatus.Cancelled, stored.Status);

        var returnRequest = await verify.ReturnRequests.SingleAsync(r => r.Id == returnRequestId);
        Assert.Equal(ReturnRequestStatus.Completed, returnRequest.Status);

        var historyCount = await verify.ReturnStatusHistories
            .CountAsync(h => h.ReturnRequestId == returnRequestId && h.ToStatus == ReturnRequestStatus.Completed);
        Assert.Equal(1, historyCount);

        var auditCount = await verify.Set<AuditLog>()
            .CountAsync(log => log.ResourcePublicId == refund.PublicId);
        Assert.Equal(1, auditCount);

        var orderProjectionHistoryCount = await verify.OrderStatusHistories
            .CountAsync(candidate =>
                candidate.OrderId == stored.OrderId &&
                candidate.StateDimension == OrderStateDimension.OrderRefundStatus &&
                candidate.ReasonCode == RefundApprover.ZeroNetApprovalReasonCode);
        Assert.Equal(1, orderProjectionHistoryCount);
    }

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
        // 執行成功後要能把這張退貨推到 Completed，前提是它真的先到了 AwaitingRefund——
        // 這些測試先前只停在 UnderReview，Refund 因此掛在一張退貨狀態機不允許的
        // 前置狀態上。
        returnRequest.Approve(
            RefundExecutorSqlFixture.AdminUserId, ReturnApprovalOutcome.RefundDue, createdAtUtc);
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
        order.ApplyPaymentProjection(PaymentStatus.Paid, 1460m, createdAtUtc);
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
        returnRequest.Approve(
            RefundExecutorSqlFixture.AdminUserId, ReturnApprovalOutcome.RefundDue, createdAtUtc);
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
        if (order.OrderRefundStatus == OrderRefundStatus.None)
        {
            order.ApplyRefundProjection(OrderRefundStatus.Pending, 0m, createdAtUtc.AddHours(1));
            context.OrderStatusHistories.Add(new OrderStatusHistory(
                Guid.CreateVersion7(),
                order.Id,
                OrderStateDimension.OrderRefundStatus,
                OrderRefundStatus.None.ToString(),
                OrderRefundStatus.Pending.ToString(),
                "test-refund-seeded",
                RefundExecutorSqlFixture.AdminUserId,
                createdAtUtc.AddHours(1),
                new string('b', 32)));
        }
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
        int returnedQuantity = 1,
        // #98 WP2：核准測試需要一筆還沒被核准過的退款，其餘所有既有呼叫端都需要
        // 一筆已核准、可執行的退款——預設值維持後者，不改變任何既有測試的前置狀態。
        bool leaveAsPendingReview = false,
        // #103 A1：核准當下重算，優惠券扣回吃光整筆退款（複製
        // RefundCalculatorTests.WhenTheClawbackSwallowsTheWholeRefund_TheAmountIsRejected
        // 的同一組數據）。只有搭配預設的 returnedQuantity/returnableQuantity 時淨額才會
        // 精準落在 0，其他呼叫端維持 false 不受影響。
        bool couponClawbackSwallowsRefund = false)
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
        order.ApplyPaymentProjection(
            PaymentStatus.Paid,
            Math.Min(paidAmount, order.GrandTotal),
            createdAtUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var item = new OrderItem(
            Guid.NewGuid(), order.Id, null, "SKU-1", "Product", "Sku",
            quantity: 2, listUnitPrice: 500m, saleUnitPrice: 500m, finalUnitPrice: 500m,
            unitCostSnapshot: 300m, lineSubtotal: 1000m, discountAllocation: 0m,
            lineTotal: 1000m, assemblyGroupKey: null, returnableQuantity: returnableQuantity,
            createdAtUtc: createdAtUtc, isCouponEligible: couponClawbackSwallowsRefund,
            specificationSnapshot: new OrderItemSpecificationSnapshot("{}", "{}", 1));
        context.OrderItems.Add(item);

        if (couponClawbackSwallowsRefund)
        {
            context.Add(new OrderCoupon(
                Guid.NewGuid(), order.Id, couponId: null, redemptionId: null,
                couponCodeSnapshot: "CLAWBACK500", nameSnapshot: "Test Coupon",
                CouponDiscountType.FixedAmount, ruleVersion: 1, discountValue: 500m,
                minimumSpendAmount: 3000m, appliedAmount: 500m, eligibleSubtotal: 1000m,
                isFreeShipping: false, createdAtUtc));
        }

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
            // 只有可信快照齊全時才能核准到 AwaitingRefund——沒有這三項，
            // Approve() 本身雖然放行，但這張退貨永遠不會是真的可執行退款的前置狀態。
            returnRequest.Approve(
                RefundExecutorSqlFixture.AdminUserId, ReturnApprovalOutcome.RefundDue, createdAtUtc);
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
        if (!leaveAsPendingReview)
        {
            refund.Approve(approvedAmount, RefundExecutorSqlFixture.AdminUserId, createdAtUtc.AddHours(1));
        }
        order.ApplyRefundProjection(OrderRefundStatus.Pending, 0m, createdAtUtc.AddHours(1));
        context.OrderStatusHistories.Add(new OrderStatusHistory(
            Guid.CreateVersion7(),
            order.Id,
            OrderStateDimension.OrderRefundStatus,
            OrderRefundStatus.None.ToString(),
            OrderRefundStatus.Pending.ToString(),
            "test-refund-seeded",
            RefundExecutorSqlFixture.AdminUserId,
            createdAtUtc.AddHours(1),
            new string('b', 32)));
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
