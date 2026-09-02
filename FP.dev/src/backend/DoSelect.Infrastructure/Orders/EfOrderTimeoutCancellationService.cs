using System.Diagnostics;
using DoSelect.Application.Auditing;
using DoSelect.Application.Orders;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Orders;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DoSelect.Infrastructure.Orders;

/// <summary>
/// M-10 逾時取消的實作。形狀刻意貼齊 <see cref="EfOrderService"/> 的自助取消與
/// <c>EfAdminOrderService</c> 的後台取消：三條路徑都經過同一個
/// <see cref="OrderCancellationResourceReleaser"/>，資源回收才不會有三套語意。
/// </summary>
public sealed class EfOrderTimeoutCancellationService : IOrderTimeoutCancellationService
{
    /// <summary>
    /// 付款逾時取消的穩定原因碼。刻意不叫 `payment_timeout`：中央 Audit 的安全碼詞彙表把
    /// `payment` 列為禁用詞（AuditFieldChange.RequireSafeCode），送進去會直接丟 ArgumentException。
    /// 庫存異動那一側的原因碼則沿用共用取消流程的 `order_cancelled`，三條取消路徑一致。
    /// </summary>
    public const string ReasonCode = "order_timeout";

    private readonly DoSelectDbContext _dbContext;
    private readonly IAuditWriter _auditWriter;
    private readonly ILogger<EfOrderTimeoutCancellationService> _logger;

    public EfOrderTimeoutCancellationService(
        DoSelectDbContext dbContext,
        IAuditWriter auditWriter,
        ILogger<EfOrderTimeoutCancellationService> logger)
    {
        _dbContext = dbContext;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<OrderTimeoutSweepResult> CancelOverduePendingPaymentOrdersAsync(
        DateTime now,
        int batchSize,
        OrderTimeoutCursor? after,
        CancellationToken cancellationToken)
    {
        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "The batch size must be positive.");
        }

        // 排序鍵必須穩定：PaymentDueAtUtc 會撞在一起（同一分鐘結帳的訂單），只靠它排序時連續兩輪
        // 可能反覆拿到同一批而讓 backlog 永遠清不完，所以再加上唯一的 Id。
        //
        // 貨到付款沒有付款期限（PaymentDueAtUtc 為 null），本來就不該被逾時取消——EF 對
        // `x <= now` 的 null 比較會產生 SQL NULL 而不成立，這裡再明寫一次條件讓意圖不必靠推導。
        //
        // 組長 PR #85 round-2 review [P1]：這裡只取「識別碼」而不是 tracked 的 Order 實體。前一版
        // 一次把整批 Order 當成 tracked entity 載進來，而 TryCancelAsync 的清理路徑會呼叫
        // ChangeTracker.Clear()——那不只清掉當下這一筆，而是把整批還沒處理的 Order 全部 detach。
        // 後面幾筆照樣跑完流程：Releaser 查出來的 Reservation／Balance／Coupon 以及新增的 History
        // 與 Audit 都是這一輪才追蹤的，會被 SaveChanges 提交，唯獨 detached 的
        // order.ChangeOrderStatus(Cancelled) 不會。結果正是這個修正要消滅的狀態——資源釋放了、
        // 訂單卻還停在 PendingPayment，付款競態的窗口也跟著重新打開。
        var candidates = _dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.OrderStatus == OrderStatus.PendingPayment &&
                order.PaymentDueAtUtc != null &&
                order.PaymentDueAtUtc <= now);

        // 組長 PR #85 round-3 review [P2]：同一輪必須往後推進。庫存不一致的訂單這一輪修不好，
        // 但它們仍然符合查詢條件——沒有游標的話，下一批會再度撈到最舊的同一群，排在後面的健康
        // 逾時訂單永遠等不到處理。游標用的就是排序鍵本身。
        if (after is not null)
        {
            var afterDue = after.PaymentDueAtUtc;
            var afterId = after.OrderId;
            candidates = candidates.Where(order =>
                order.PaymentDueAtUtc > afterDue ||
                (order.PaymentDueAtUtc == afterDue && order.Id > afterId));
        }

        var batch = await candidates
            .OrderBy(order => order.PaymentDueAtUtc)
            .ThenBy(order => order.Id)
            .Take(batchSize)
            .Select(order => new { order.Id, order.PaymentDueAtUtc })
            .ToListAsync(cancellationToken);

        var cancelled = 0;
        var failed = 0;
        foreach (var candidate in batch)
        {
            var outcome = await TryCancelAsync(candidate.Id, now, cancellationToken);
            if (outcome == CancelOutcome.Cancelled)
            {
                cancelled++;
            }
            else if (outcome == CancelOutcome.Failed)
            {
                failed++;
            }
        }

        // 游標指向這一批的最後一筆，不論它成功、失敗還是被跳過——「檢視過」就要往前走。
        var last = batch.Count > 0 ? batch[^1] : null;
        return new OrderTimeoutSweepResult(
            batch.Count,
            cancelled,
            failed,
            last is null ? null : new OrderTimeoutCursor(last.PaymentDueAtUtc!.Value, last.Id));
    }

    /// <summary>
    /// 一筆訂單的處理結果。跳過（付款先到、已被人工處理）與失敗（資料不一致）要分開：前者是正常
    /// 的併發結局，後者需要人工介入。
    /// </summary>
    private enum CancelOutcome
    {
        Skipped,
        Cancelled,
        Failed,
    }

    /// <summary>
    /// 取消一筆訂單。整段是一次 SaveChanges，所以訂單狀態、庫存保留、優惠券座位與組裝資源同生共死。
    /// 回傳是否真的取消了——輸給付款或被別人搶先處理都只是跳過，不拋例外：這是排程，一筆的競態不該
    /// 讓整輪掃描失敗。
    /// </summary>
    private async Task<CancelOutcome> TryCancelAsync(long orderId, DateTime now, CancellationToken cancellationToken)
    {
        // 每一筆都從乾淨的 ChangeTracker 開始，再自己載入 tracked 的 Order。這樣一來清理路徑的
        // ChangeTracker.Clear() 影響範圍就只有這一筆，不可能波及批次裡的其他訂單；同時也讓一輪
        // 跑好幾百筆時追蹤的實體數量不會一路累積。
        _dbContext.ChangeTracker.Clear();

        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);

        // 這一筆在查詢與處理之間已經被別人改掉了（付款成功、人工取消）——重新讀到的狀態才算數。
        if (order is null || order.OrderStatus != OrderStatus.PendingPayment)
        {
            return CancelOutcome.Skipped;
        }

        var traceId = Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString();
        var correlationId = $"order-timeout-{Guid.CreateVersion7():N}";

        bool assemblyChanged;
        try
        {
            assemblyChanged = await OrderCancellationResourceReleaser.ReleaseAsync(
                _dbContext,
                order,
                actorUserId: null,
                now,
                traceId,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            // 庫存狀態不一致（Balance 對不上這筆訂單的保留）。這是資料層面的問題，不是這一輪掃描
            // 能修的；把這筆留給人工處理，其餘訂單照常——下一筆一開始就會清掉這裡的半套追蹤狀態。
            //
            // 組長 PR #85 round-3 review [P2]：先前這裡是靜默回 false，沒有訂單識別、沒有等級、
            // 沒有關聯 id——「留給人工處理」實務上做不到，因為沒有人找得到是哪一筆。現在留下帶
            // 訂單 PublicId 與 correlation id 的結構化 Warning。
            _logger.LogWarning(
                exception,
                "Timeout cancellation skipped order {OrderPublicId} because its inventory state is inconsistent; it needs manual repair. CorrelationId={CorrelationId}",
                order.PublicId,
                correlationId);
            return CancelOutcome.Failed;
        }

        order.ChangeOrderStatus(OrderStatus.Cancelled, now);

        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory(
            Guid.CreateVersion7(),
            order.Id,
            OrderStateDimension.OrderStatus,
            fromStatus: OrderStatus.PendingPayment.ToString(),
            toStatus: OrderStatus.Cancelled.ToString(),
            reasonCode: ReasonCode,
            actorUserId: null,
            occurredAtUtc: now,
            traceId: traceId));

        List<AuditFieldChange> fieldChanges =
        [
            AuditFieldChange.Changed("orderStatus"),
            AuditFieldChange.Changed("inventoryReservations"),
            AuditFieldChange.Changed("couponRedemptions"),
        ];
        if (assemblyChanged)
        {
            fieldChanges.Add(AuditFieldChange.Changed("assemblyStatus"));
        }

        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            AuditActor.Create(AuditActorType.System, publicId: null, roles: []),
            AuditActions.OrderCancel,
            AuditResourceTypes.Order,
            order.PublicId,
            AuditResult.Success,
            errorCode: null,
            fieldChanges,
            ReasonCode,
            correlationId,
            traceId,
            jobPublicId: null,
            remoteIpAddress: null));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return CancelOutcome.Cancelled;
        }
        catch (DbUpdateConcurrencyException)
        {
            // 這正是 [P1] 描述的競態，而且是它應有的結局：付款在這一輪讀取之後把同一筆訂單推進了
            // Confirmed，訂單列的 RowVersion 因此改變，這次取消整批回滾——庫存保留、優惠券座位與
            // 訂單狀態沒有任何一項生效。付款那邊看到的仍是一筆有效保留的已付款訂單。
            //
            // 反過來若排程先提交，付款端會拿到同一個例外並回報衝突，同樣不會產生「已付款但沒有保留」
            // 的訂單。仲裁者是訂單列本身，所以兩個方向都成立。
            //
            // 這是正常的併發結局而不是需要人工處理的資料問題，所以是 Skipped 不是 Failed，
            // 也只留 Debug 等級——每分鐘掃描一次，把它記成 Warning 只會淹掉真正要看的訊息。
            _logger.LogDebug(
                "Timeout cancellation lost the race for order {OrderPublicId}; payment or another writer committed first.",
                order.PublicId);
            return CancelOutcome.Skipped;
        }
    }
}
