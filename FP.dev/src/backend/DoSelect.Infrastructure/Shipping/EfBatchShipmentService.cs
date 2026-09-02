using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Inventory;
using DoSelect.Application.Outbox;
using DoSelect.Application.Shipping;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Catalog;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DoSelect.Infrastructure.Shipping;

/// <summary>
/// UC-ADM-SHIP-02 批次出貨。
///
/// 這個服務刻意**不是**整批一個交易——與匯入、批次調價那些流程正好相反。購物車、訂單、付款與物流.md
/// 寫得很直接：「每筆訂單獨立驗證、獨立交易及獨立回傳結果」「一筆失敗不回滾其他已成功出貨的訂單」。
/// 理由是出貨是不可逆的實體動作：已經送出倉庫的貨，不會因為同一份清單裡另一張訂單有問題就回來。
/// </summary>
public sealed class EfBatchShipmentService : IBatchShipmentService
{
    private const int MaxBatchSize = 100;

    private readonly DoSelectDbContext _dbContext;
    private readonly IInventoryReservationService _reservationService;
    private readonly IOutboxWriter _outboxWriter;
    private readonly ILogger<EfBatchShipmentService> _logger;

    public EfBatchShipmentService(
        DoSelectDbContext dbContext,
        IInventoryReservationService reservationService,
        IOutboxWriter outboxWriter,
        ILogger<EfBatchShipmentService> logger)
    {
        _dbContext = dbContext;
        _reservationService = reservationService;
        _outboxWriter = outboxWriter;
        _logger = logger;
    }

    public async Task<BatchShipmentResultDto> ShipBatchAsync(
        BatchShipmentRequest request,
        string adminUserId,
        string correlationId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orders = request.Orders ?? [];

        // 「超過上限時整個 Request 回傳驗證錯誤，不開始逐筆出貨」——先擋，一筆都不碰。
        if (orders.Count > MaxBatchSize)
        {
            throw DomainProblemException.BadRequest(
                ShippingErrorCodes.ShippingBatchLimitExceeded,
                $"A batch ships at most {MaxBatchSize} orders; this request has {orders.Count}.");
        }

        if (orders.Count == 0)
        {
            throw DomainProblemException.Validation("A batch must contain at least one order.");
        }

        var action = (request.ShippingAction ?? string.Empty).Trim();
        if (!BatchShipmentActions.All.Contains(action, StringComparer.Ordinal))
        {
            throw DomainProblemException.Validation(
                $"'{request.ShippingAction}' is not a supported shipping action. Valid values: {string.Join(", ", BatchShipmentActions.All)}.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw DomainProblemException.Validation("An idempotency key is required.");
        }

        // 同一張訂單在一批裡出現兩次，第二次必定失敗（訂單已有出貨），但那是個沒必要浪費的往返，
        // 而且逐筆結果會出現兩列讓管理員困惑。整批擋下來說清楚。
        if (orders.Select(order => order.OrderPublicId).Distinct().Count() != orders.Count)
        {
            throw DomainProblemException.Validation("The batch contains the same order more than once.");
        }

        var items = new List<BatchShipmentItemResultDto>(orders.Count);
        for (var index = 0; index < orders.Count; index++)
        {
            items.Add(await ShipOneAsync(
                sourceRowNumber: index + 1,
                orders[index],
                action,
                adminUserId,
                correlationId,
                now,
                cancellationToken));
        }

        var succeeded = items.Count(item => item.ErrorCode is null);
        return new BatchShipmentResultDto(
            Guid.CreateVersion7(),
            items.Count,
            succeeded,
            items.Count - succeeded,
            items,
            now);
    }

    /// <summary>
    /// 一張訂單的完整流程，含它自己的交易。任何失敗都變成這一列的錯誤碼，絕不往外拋——拋出去
    /// 就會中斷整批，而那正是規格禁止的。
    /// </summary>
    private async Task<BatchShipmentItemResultDto> ShipOneAsync(
        int sourceRowNumber,
        BatchShipmentOrderInput input,
        string action,
        string adminUserId,
        string correlationId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // 每一筆都從乾淨的 ChangeTracker 開始：前一筆若在中途失敗，它追蹤到一半的實體不該跟著
        // 這一筆一起提交。（與逾時取消排程踩過的同一個坑。）
        _dbContext.ChangeTracker.Clear();

        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(candidate => candidate.PublicId == input.OrderPublicId, cancellationToken);
        if (order is null)
        {
            return Failure(sourceRowNumber, input.OrderPublicId, null,
                DomainErrorCodes.ResourceNotFound, "The order was not found.");
        }

        var readiness = await CheckReadinessAsync(order, cancellationToken);
        if (readiness is not null)
        {
            return Failure(sourceRowNumber, input.OrderPublicId, order.OrderNumber,
                readiness.Value.Code, readiness.Value.Message);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var method = await _dbContext.ShippingMethods
                .SingleAsync(candidate => candidate.Code == order.ShippingMethodCode, cancellationToken);
            var storeId = await ResolveStoreIdAsync(order, cancellationToken);

            var trackingNumber = GenerateTrackingNumber(order, now);
            if (await _dbContext.Shipments.AnyAsync(
                    candidate => candidate.TrackingNumber == trackingNumber, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Failure(sourceRowNumber, input.OrderPublicId, order.OrderNumber,
                    ShippingErrorCodes.ShippingTrackingDuplicate,
                    "The generated tracking number is already in use.");
            }

            var shipment = new Shipment(
                Guid.CreateVersion7(),
                order.Id,
                method.Id,
                order.ShippingProviderProfileVersionId,
                storeId,
                GenerateShipmentNumber(order, now),
                order.ShippingFee,
                now);
            shipment.SetTrackingNumber(trackingNumber, now);

            // Shipment 的狀態機（ShippingEntities.cs）只認單步邊 Pending→Preparing→Shipped，所以
            // markShipped 要真的走完兩步。直接跳到 Shipped 會被實體擋下來，而且那個例外會被下面的
            // catch 收成一筆 shipping_order_not_ready，看起來像業務拒絕、其實是程式錯誤。
            FulfillmentStatus[] path = action == BatchShipmentActions.MarkShipped
                ? [FulfillmentStatus.Preparing, FulfillmentStatus.Shipped]
                : [FulfillmentStatus.Preparing];
            var targetStatus = path[^1];

            var previousStatus = order.FulfillmentStatus;
            foreach (var step in path)
            {
                shipment.ChangeStatus(step, now);
            }

            order.ApplyFulfillmentProjection(targetStatus, now);

            // 呼叫端送來的 RowVersion 必須在「訂單真正被寫出去的那一次 SaveChanges」之前蓋上原始值，
            // 否則 UPDATE 的 WHERE 會用本次讀到的新版本，過期的清單就靜靜覆蓋掉別人剛做的變更。
            // 這也是為什麼訂單要在這一批第一個存——後面的 ConsumeAllForOrderAsync 自己會 SaveChanges，
            // 一旦讓它先把訂單沖出去，之後再設原始值就完全沒有作用了。
            _dbContext.Entry(order).Property(candidate => candidate.RowVersion).OriginalValue = input.RowVersion;

            _dbContext.Shipments.Add(shipment);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // 每一步各留一列，出貨歷程才不會出現實體狀態機根本不允許的 Pending→Shipped。
            var fromStatus = FulfillmentStatus.Pending;
            foreach (var step in path)
            {
                _dbContext.ShipmentStatusHistories.Add(new ShipmentStatusHistory(
                    Guid.CreateVersion7(),
                    shipment.Id,
                    fromStatus,
                    step,
                    externalEventId: null,
                    now,
                    adminUserId));
                fromStatus = step;
            }

            _dbContext.OrderStatusHistories.Add(new OrderStatusHistory(
                Guid.CreateVersion7(),
                order.Id,
                OrderStateDimension.FulfillmentStatus,
                fromStatus: previousStatus.ToString(),
                toStatus: targetStatus.ToString(),
                reasonCode: "batch_shipment",
                adminUserId,
                now,
                correlationId));

            if (action == BatchShipmentActions.MarkShipped)
            {
                // 「出貨才把 Active Reservation 轉 Consumed，並同時調整 OnHand／Reserved」——
                // createLabel 只是印單，貨還在倉庫裡，這一步不能提前做。
                await _reservationService.ConsumeAllForOrderAsync(order.Id, now, cancellationToken);
                await AddShippedNotificationsAsync(order, correlationId, now, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new BatchShipmentItemResultDto(
                sourceRowNumber,
                order.PublicId,
                order.OrderNumber,
                targetStatus.ToString(),
                trackingNumber,
                ErrorCode: null,
                Message: null);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Failure(sourceRowNumber, input.OrderPublicId, order.OrderNumber,
                DomainErrorCodes.ConcurrencyConflict,
                "The order changed after this list was loaded. Reload and try again.");
        }
        catch (DbUpdateException exception) when (IsShipmentNumberDuplicate(exception))
        {
            // 唯一索引是最後一道：兩位管理員同時送出含同一張訂單的批次時，先前的存在性檢查會
            // 兩邊都通過，真正的仲裁在資料庫。
            await transaction.RollbackAsync(cancellationToken);
            return Failure(sourceRowNumber, input.OrderPublicId, order.OrderNumber,
                ShippingErrorCodes.ShippingTrackingDuplicate,
                "The generated shipment number is already in use.");
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);

            // 這一筆的失敗不能中斷整批，但也不能無聲無息——沒有訂單識別的話，沒有人找得到要處理哪一筆。
            _logger.LogError(
                exception,
                "Batch shipment failed for order {OrderPublicId}. CorrelationId={CorrelationId}",
                order.PublicId,
                correlationId);
            return Failure(sourceRowNumber, input.OrderPublicId, order.OrderNumber,
                ShippingErrorCodes.ShippingOrderNotReady,
                "The order could not be shipped. Check its payment, assembly and inventory state.");
        }
    }

    /// <summary>
    /// 出貨前的逐筆檢查：「訂單可履約、付款條件已滿足或為合法 COD、組裝工作已可出貨、保留庫存
    /// 仍為 Active、配送方式有效」。回 null 代表通過。
    /// </summary>
    private async Task<(string Code, string Message)?> CheckReadinessAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        if (order.OrderStatus is not (OrderStatus.Confirmed or OrderStatus.Processing))
        {
            return (ShippingErrorCodes.ShippingOrderNotReady,
                $"The order is {order.OrderStatus}; only a confirmed or processing order can ship.");
        }

        if (order.FulfillmentStatus != FulfillmentStatus.Pending)
        {
            return (ShippingErrorCodes.ShippingOrderNotReady,
                $"The order is already {order.FulfillmentStatus}.");
        }

        // 貨到付款在出貨時仍是 Pending，那是合法的；其餘方式必須已付款。
        var isCashOnDelivery = order.PaymentDueAtUtc is null;
        if (!isCashOnDelivery && order.PaymentStatus != PaymentStatus.Paid)
        {
            return (ShippingErrorCodes.ShippingOrderNotReady,
                "The order is not paid and is not a cash-on-delivery order.");
        }

        // 需要組裝的訂單要等組裝走到 ReadyToShip；NotRequired 直接放行。狀態機沒有 Completed——
        // 組裝的終點就是 ReadyToShip，之後由出貨本身接手。
        if (order.AssemblyStatus is not (AssemblyStatus.NotRequired or AssemblyStatus.ReadyToShip))
        {
            return (ShippingErrorCodes.ShippingOrderNotReady,
                $"The order's assembly is {order.AssemblyStatus}.");
        }

        var hasActiveReservation = await _dbContext.InventoryReservations
            .AnyAsync(candidate => candidate.OrderId == order.Id &&
                candidate.Status == DoSelect.Domain.Inventory.InventoryReservationStatus.Active, cancellationToken);
        if (!hasActiveReservation)
        {
            return (ShippingErrorCodes.ShippingOrderNotReady,
                "The order has no active inventory reservation left to consume.");
        }

        var method = await _dbContext.ShippingMethods.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Code == order.ShippingMethodCode, cancellationToken);
        if (method is null || !method.IsActive)
        {
            return (ShippingErrorCodes.ShippingMethodNotAllowed,
                "The order's shipping method is no longer available.");
        }

        if (await _dbContext.Shipments.AnyAsync(candidate => candidate.OrderId == order.Id, cancellationToken))
        {
            return (ShippingErrorCodes.ShippingOrderNotReady, "The order already has a shipment.");
        }

        // 超商取貨的門市不見了或已停用，是「這一筆還不能出貨」，不是伺服器出錯。留在
        // ResolveStoreIdAsync 裡以例外表達的話，會被 catch 收成同一個錯誤碼、卻多寫一筆 Error 記錄，
        // 讓真正的缺陷淹沒在業務拒絕裡。
        if (!string.IsNullOrEmpty(order.StoreCode))
        {
            var store = await _dbContext.ConvenienceStores.AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.StoreCode == order.StoreCode, cancellationToken);
            if (store is null || !store.IsActive)
            {
                return (ShippingErrorCodes.ShippingOrderNotReady,
                    "The order's pickup store is no longer available.");
            }
        }

        return null;
    }

    /// <summary>
    /// 超商取貨要對回門市列。訂單只快照了 StoreCode／StoreName／StoreAddress，沒有品牌代碼，所以
    /// 以 StoreCode 查；查不到或門市已停用都是這一筆失敗，而不是靜靜出一張沒有門市的貨。
    /// </summary>
    /// <summary>
    /// 訂單只快照了 StoreCode／StoreName／StoreAddress，沒有門市的資料庫 Id，所以出貨時要以
    /// StoreCode 對回門市列。查不到或已停用在 CheckReadinessAsync 就擋掉了，這裡只負責取 Id。
    /// </summary>
    private async Task<long?> ResolveStoreIdAsync(Order order, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(order.StoreCode))
        {
            return null;
        }

        return await _dbContext.ConvenienceStores.AsNoTracking()
            .Where(candidate => candidate.StoreCode == order.StoreCode)
            .Select(candidate => (long?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// 「未提供物流單號時由 SimulatedShippingProvider 產生唯一單號」。這個專案沒有真實物流商，
    /// 所以單號在這裡以訂單編號＋時間戳合成——固定前綴讓它在資料裡一眼看得出是模擬產生的。
    /// </summary>
    private static string GenerateTrackingNumber(Order order, DateTime now) =>
        $"SIM{now:yyyyMMddHHmmss}{order.Id:D8}";

    private static string GenerateShipmentNumber(Order order, DateTime now) =>
        $"SH{now:yyyyMMdd}{order.Id:D10}";

    private async Task AddShippedNotificationsAsync(
        Order order, string correlationId, DateTime now, CancellationToken cancellationToken)
    {
        // 出貨沒有專屬的整合事件型別，而整合事件目錄是共用契約——照 #74 對匯入的處理，不自行發明
        // 一個新的事件型別，改用既有的通知事件。
        _outboxWriter.Add(OutboxWriteRequest.Create(
            Guid.CreateVersion7(),
            AuditResourceTypes.Order,
            order.PublicId,
            new EmailNotificationRequestedV1(
                Guid.CreateVersion7(),
                "shipment.shipped",
                "shipment.customer",
                AuditResourceTypes.Order,
                order.PublicId,
                "zh-TW",
                1),
            now,
            now,
            correlationId));

        if (order.MemberUserId is null)
        {
            return;
        }

        var memberPublicId = await _dbContext.Users.AsNoTracking()
            .Where(user => user.Id == order.MemberUserId)
            .Select(user => user.PublicId)
            .FirstOrDefaultAsync(cancellationToken);
        if (memberPublicId != Guid.Empty)
        {
            _outboxWriter.Add(OutboxWriteRequest.Create(
                Guid.CreateVersion7(),
                AuditResourceTypes.Order,
                order.PublicId,
                new InAppNotificationRequestedV1(
                    Guid.CreateVersion7(),
                    memberPublicId,
                    "shipment.shipped",
                    AuditResourceTypes.Order,
                    order.PublicId,
                    "zh-TW",
                    1),
                now,
                now,
                correlationId));
        }
    }

    private static BatchShipmentItemResultDto Failure(
        int sourceRowNumber, Guid orderPublicId, string? orderNumber, string code, string message) =>
        new(sourceRowNumber, orderPublicId, orderNumber, "Failed", null, code, message);

    /// <summary>
    /// 出貨單號是 SH+日期+訂單 Id，所以同一張訂單在同一天被出兩次一定撞 UX_Shipments_ShipmentNumber。
    /// TrackingNumber 上沒有唯一索引（ShipmentConfiguration 只對 ShipmentNumber 建），資料庫這一關
    /// 守的是出貨單號；比對索引名而不是訊息裡有沒有「Shipments」，才不會把別的約束衝突
    /// 也認成重複單號。
    /// </summary>
    private static bool IsShipmentNumberDuplicate(DbUpdateException exception) =>
        SqlUniqueIndexViolations.Matches(exception, "UX_Shipments_ShipmentNumber");
}
