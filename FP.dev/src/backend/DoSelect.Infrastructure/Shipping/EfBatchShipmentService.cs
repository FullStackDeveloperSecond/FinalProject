using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Inventory;
using DoSelect.Application.Outbox;
using DoSelect.Application.Shipping;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Inventory;
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
///
/// 冪等由 <see cref="BatchShipmentIdempotency"/> 負責，它沿用既有的 IdempotencyRecords 但把交易切成
/// 三段，好讓逐筆出貨維持各自獨立的交易（組長 PR #93 review item 1 與裁定 A1）。
/// </summary>
public sealed class EfBatchShipmentService : IBatchShipmentService
{
    private const int MaxBatchSize = 100;

    private readonly DoSelectDbContext _dbContext;
    private readonly IInventoryReservationService _reservationService;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IAuditWriter _auditWriter;
    private readonly BatchShipmentIdempotency _idempotency;
    private readonly ILogger<EfBatchShipmentService> _logger;

    public EfBatchShipmentService(
        DoSelectDbContext dbContext,
        IInventoryReservationService reservationService,
        IOutboxWriter outboxWriter,
        IAuditWriter auditWriter,
        BatchShipmentIdempotency idempotency,
        ILogger<EfBatchShipmentService> logger)
    {
        _dbContext = dbContext;
        _reservationService = reservationService;
        _outboxWriter = outboxWriter;
        _auditWriter = auditWriter;
        _idempotency = idempotency;
        _logger = logger;
    }

    public async Task<BatchShipmentResultDto> ShipBatchAsync(
        BatchShipmentRequest request,
        string adminUserId,
        AuditRequestContext auditContext,
        DateTime now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(auditContext);

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

        var actor = await ResolveActorAsync(adminUserId, cancellationToken);

        // 冪等鍵的識別包含操作者、操作與 payload：換一位管理員、換一個動作或換一份清單都是另一次
        // 作業，只有「同一個人把同一份請求再送一次」才算重送。
        var idempotencyCommand = IdempotencyCommand.Create(
            IdempotencyActorScope.ForAdmin(actor.PublicId!.Value),
            BatchShipmentIdempotency.Operation,
            request.IdempotencyKey.Trim(),
            new
            {
                ShippingAction = action,
                Orders = orders.Select(order => new
                {
                    order.OrderPublicId,
                    RowVersion = Convert.ToBase64String(order.RowVersion ?? []),
                }).ToArray(),
            });

        var replay = await _idempotency.ClaimAsync(idempotencyCommand, now, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var items = new List<BatchShipmentItemResultDto>(orders.Count);
        for (var index = 0; index < orders.Count; index++)
        {
            items.Add(await ShipOneAsync(
                sourceRowNumber: index + 1,
                orders[index],
                action,
                adminUserId,
                actor,
                auditContext,
                now,
                cancellationToken));
        }

        var succeeded = items.Count(item => item.ErrorCode is null);
        var result = new BatchShipmentResultDto(
            Guid.CreateVersion7(),
            items.Count,
            succeeded,
            items.Count - succeeded,
            items,
            now,
            IsReplay: false);

        await _idempotency.CompleteAsync(idempotencyCommand, result, now, cancellationToken);
        return result;
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
        AuditActor actor,
        AuditRequestContext auditContext,
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

        var readiness = await CheckReadinessAsync(order, action, cancellationToken);
        if (readiness is not null)
        {
            return Failure(sourceRowNumber, input.OrderPublicId, order.OrderNumber,
                readiness.Value.Code, readiness.Value.Message);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // 組長 PR #93 review item 2：保留必須在**交易內**完整驗過。交易外的 AnyAsync 只知道
            // 「至少有一筆」，多品項訂單只保留到一半、或 readiness 之後保留被逾時排程收走，都會通過。
            var reservations = await LoadActiveReservationsAsync(order.Id, cancellationToken);
            var coverage = await CheckReservationCoverageAsync(order.Id, reservations, cancellationToken);
            if (coverage is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Failure(sourceRowNumber, input.OrderPublicId, order.OrderNumber,
                    ShippingErrorCodes.ShippingOrderNotReady, coverage);
            }

            var method = await _dbContext.ShippingMethods
                .SingleAsync(candidate => candidate.Code == order.ShippingMethodCode, cancellationToken);
            var storeId = await ResolveStoreIdAsync(order, cancellationToken);

            // createLabel 之後再 markShipped，要接的是同一張物流單（組長 PR #93 review item 3）。
            // 一張訂單只有一張主要物流單，不能為了推進狀態而再開一張。
            var shipment = await _dbContext.Shipments
                .FirstOrDefaultAsync(candidate => candidate.OrderId == order.Id, cancellationToken);
            var isNewShipment = shipment is null;

            if (shipment is null)
            {
                var trackingNumber = GenerateTrackingNumber(order, now);
                if (await _dbContext.Shipments.AnyAsync(
                        candidate => candidate.TrackingNumber == trackingNumber, cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Failure(sourceRowNumber, input.OrderPublicId, order.OrderNumber,
                        ShippingErrorCodes.ShippingTrackingDuplicate,
                        "The generated tracking number is already in use.");
                }

                shipment = new Shipment(
                    Guid.CreateVersion7(),
                    order.Id,
                    method.Id,
                    order.ShippingProviderProfileVersionId,
                    storeId,
                    GenerateShipmentNumber(order, now),
                    order.ShippingFee,
                    now);
                shipment.SetTrackingNumber(trackingNumber, now);
            }

            // Shipment 的狀態機（ShippingEntities.cs）只認單步邊 Pending→Preparing→Shipped，所以
            // markShipped 要真的走完剩下的每一步。直接跳到 Shipped 會被實體擋下來，而且那個例外會被
            // 下面的 catch 收成一筆 shipping_order_not_ready，看起來像業務拒絕、其實是程式錯誤。
            var targetStatus = action == BatchShipmentActions.MarkShipped
                ? FulfillmentStatus.Shipped
                : FulfillmentStatus.Preparing;
            var path = BuildStatusPath(shipment.Status, targetStatus);

            var previousStatus = order.FulfillmentStatus;
            var previousShipmentStatus = shipment.Status;
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

            if (isNewShipment)
            {
                _dbContext.Shipments.Add(shipment);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            // 每一步各留一列，出貨歷程才不會出現實體狀態機根本不允許的 Pending→Shipped。
            var fromStatus = previousShipmentStatus;
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
                auditContext.CorrelationId));

            if (action == BatchShipmentActions.MarkShipped)
            {
                // 「出貨才把 Active Reservation 轉 Consumed，並同時調整 OnHand／Reserved」——
                // createLabel 只是印單，貨還在倉庫裡，這一步不能提前做。
                await _reservationService.ConsumeAllForOrderAsync(order.Id, now, cancellationToken);

                // 消耗完再驗一次：這一批的每一筆保留都要真的變成 Consumed。ConsumeAllForOrderAsync
                // 查不到 Active 保留時是靜靜返回的，所以少消耗不會自己冒出例外——不驗就等於出了一張
                // 沒有扣庫存的貨。
                var unconsumed = reservations
                    .Count(candidate => candidate.Status != InventoryReservationStatus.Consumed);
                if (unconsumed > 0)
                {
                    // 別人在這筆交易進行中把保留收走了（逾時排程、人工釋放、另一個出貨請求）。這是
                    // 競態不是缺陷，所以回併發衝突讓管理員重載重送，而不是記一筆 Error 說系統壞了。
                    await transaction.RollbackAsync(cancellationToken);
                    return Failure(sourceRowNumber, input.OrderPublicId, order.OrderNumber,
                        DomainErrorCodes.ConcurrencyConflict,
                        "The order's inventory reservation changed while shipping. Reload and try again.");
                }

                await AddShippedNotificationsAsync(order, auditContext.CorrelationId, now, cancellationToken);
            }

            // 組長 PR #93 裁定 B1：每一筆成功的出貨都在自己那筆交易內寫中央 Audit。跟著同一次
            // SaveChanges 落地，「出了貨卻沒有稽核紀錄」這個狀態就不存在。
            _auditWriter.Add(AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                actor,
                action == BatchShipmentActions.MarkShipped
                    ? AuditActions.ShipmentMarkShipped
                    : AuditActions.ShipmentCreateLabel,
                AuditResourceTypes.Order,
                order.PublicId,
                AuditResult.Success,
                errorCode: null,
                [
                    AuditFieldChange.Code("fulfillmentStatus", previousStatus.ToString(), targetStatus.ToString()),
                    AuditFieldChange.Code("shipmentNumber", null, shipment.ShipmentNumber),
                    AuditFieldChange.Code("trackingNumber", null, shipment.TrackingNumber),
                ],
                reason: "batch_shipment",
                auditContext.CorrelationId,
                auditContext.TraceId,
                jobPublicId: null,
                auditContext.RemoteIpAddress));

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new BatchShipmentItemResultDto(
                sourceRowNumber,
                order.PublicId,
                order.OrderNumber,
                targetStatus.ToString(),
                shipment.TrackingNumber,
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
        catch (InventoryWriteException exception)
            when (exception.ErrorCode == InventoryWriteException.ErrorCodes.ConcurrencyConflict)
        {
            // 保留或庫存餘額在這筆出貨的交易期間被別人動過（逾時釋放排程、人工釋放、另一個出貨
            // 請求）。這一筆整體回滾，其他訂單不受影響。
            await transaction.RollbackAsync(cancellationToken);
            return Failure(sourceRowNumber, input.OrderPublicId, order.OrderNumber,
                DomainErrorCodes.ConcurrencyConflict,
                "The order's inventory reservation changed while shipping. Reload and try again.");
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
                auditContext.CorrelationId);
            return Failure(sourceRowNumber, input.OrderPublicId, order.OrderNumber,
                ShippingErrorCodes.ShippingOrderNotReady,
                "The order could not be shipped. Check its payment, assembly and inventory state.");
        }
    }

    /// <summary>
    /// 出貨前的逐筆檢查：「訂單可履約、付款條件已滿足或為合法 COD、組裝工作已可出貨、配送方式
    /// 有效」。保留庫存的完整覆蓋另外在交易內驗（見 <see cref="CheckReservationCoverageAsync"/>），
    /// 因為那件事在交易外驗完就已經可能不成立了。
    /// </summary>
    private async Task<(string Code, string Message)?> CheckReadinessAsync(
        Order order,
        string action,
        CancellationToken cancellationToken)
    {
        if (order.OrderStatus is not (OrderStatus.Confirmed or OrderStatus.Processing))
        {
            return (ShippingErrorCodes.ShippingOrderNotReady,
                $"The order is {order.OrderStatus}; only a confirmed or processing order can ship.");
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

        var existingShipmentStatus = await _dbContext.Shipments.AsNoTracking()
            .Where(candidate => candidate.OrderId == order.Id)
            .Select(candidate => (FulfillmentStatus?)candidate.Status)
            .FirstOrDefaultAsync(cancellationToken);

        // createLabel 是「開單」：訂單必須還沒有物流單，履約狀態還在 Pending。
        if (action == BatchShipmentActions.CreateLabel)
        {
            if (existingShipmentStatus is not null)
            {
                return (ShippingErrorCodes.ShippingOrderNotReady,
                    "The order already has a shipment; use markShipped to complete it.");
            }

            if (order.FulfillmentStatus != FulfillmentStatus.Pending)
            {
                return (ShippingErrorCodes.ShippingOrderNotReady, $"The order is already {order.FulfillmentStatus}.");
            }
        }
        else
        {
            // markShipped 有兩個合法入口：還沒開單（一次走完 Pending→Preparing→Shipped），或是
            // createLabel 已經開好單、停在 Preparing（接著走 Preparing→Shipped）。
            var isFreshOrder = existingShipmentStatus is null &&
                order.FulfillmentStatus == FulfillmentStatus.Pending;
            var isPreparedOrder = existingShipmentStatus == FulfillmentStatus.Preparing &&
                order.FulfillmentStatus == FulfillmentStatus.Preparing;
            if (!isFreshOrder && !isPreparedOrder)
            {
                return (ShippingErrorCodes.ShippingOrderNotReady,
                    existingShipmentStatus is null
                        ? $"The order is already {order.FulfillmentStatus}."
                        : $"The order's shipment is already {existingShipmentStatus}.");
            }
        }

        var method = await _dbContext.ShippingMethods.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Code == order.ShippingMethodCode, cancellationToken);
        if (method is null || !method.IsActive)
        {
            return (ShippingErrorCodes.ShippingMethodNotAllowed,
                "The order's shipping method is no longer available.");
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
    /// 交易內以追蹤查詢讀出這張訂單的 Active 保留。追蹤是刻意的：後面 ConsumeAllForOrderAsync 會
    /// 解析到同一批實體，於是 UPDATE 帶的原始 RowVersion 就是此刻讀到的版本；期間被逾時排程改過的
    /// 話，寫入會撞併發而讓這一筆整體回滾。
    /// </summary>
    private async Task<IReadOnlyList<InventoryReservation>> LoadActiveReservationsAsync(
        long orderId,
        CancellationToken cancellationToken) =>
        await _dbContext.InventoryReservations
            .Where(candidate => candidate.OrderId == orderId &&
                candidate.Status == InventoryReservationStatus.Active)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// 「保留庫存仍為 Active」在多品項訂單上的正確意思是**逐 SKU 覆蓋足量**，不是「至少有一筆」。
    /// 只保留到一半就出貨，等於出了一批帳面上沒有扣掉的貨。回傳 null 代表覆蓋完整。
    /// </summary>
    private async Task<string?> CheckReservationCoverageAsync(
        long orderId,
        IReadOnlyList<InventoryReservation> reservations,
        CancellationToken cancellationToken)
    {
        // SkuId 為 null 的品項沒有對應的庫存列（歷史快照），本來就沒有保留可談。
        var required = await _dbContext.OrderItems.AsNoTracking()
            .Where(item => item.OrderId == orderId && item.SkuId != null)
            .GroupBy(item => item.SkuId!.Value)
            .Select(group => new { SkuId = group.Key, Quantity = group.Sum(item => item.Quantity) })
            .ToDictionaryAsync(entry => entry.SkuId, entry => entry.Quantity, cancellationToken);

        if (required.Count == 0)
        {
            return reservations.Count > 0
                ? null
                : "The order has no active inventory reservation left to consume.";
        }

        var reserved = reservations
            .GroupBy(reservation => reservation.SkuId)
            .ToDictionary(group => group.Key, group => group.Sum(reservation => reservation.Quantity));

        foreach (var (skuId, quantity) in required)
        {
            if (!reserved.TryGetValue(skuId, out var activeQuantity) || activeQuantity < quantity)
            {
                return "The order's active inventory reservations no longer cover every item. Re-check the order's stock and try again.";
            }
        }

        return null;
    }

    /// <summary>
    /// 從物流單目前的狀態走到目標狀態要經過的每一步。Shipment 的狀態機只允許單步邊，所以這裡
    /// 把邊列出來而不是直接跳。
    /// </summary>
    private static FulfillmentStatus[] BuildStatusPath(FulfillmentStatus current, FulfillmentStatus target)
    {
        if (current == FulfillmentStatus.Preparing && target == FulfillmentStatus.Shipped)
        {
            return [FulfillmentStatus.Shipped];
        }

        return target == FulfillmentStatus.Shipped
            ? [FulfillmentStatus.Preparing, FulfillmentStatus.Shipped]
            : [FulfillmentStatus.Preparing];
    }

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

    /// <summary>
    /// 稽核與冪等都要靠這個身分：稽核要 Actor，冪等的 Actor Scope 也是用它算出來的。找不到或角色
    /// 不足時整批拒絕——這不是逐筆的業務問題，而是這個請求根本不該開始。
    /// </summary>
    private async Task<AuditActor> ResolveActorAsync(string adminUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            throw DomainProblemException.Forbidden("The administrator identity is invalid.");
        }

        var admin = await _dbContext.Users.AsNoTracking()
            .Where(user => user.Id == adminUserId &&
                user.AccountType == DoSelect.Domain.Members.AccountType.Admin)
            .Select(user => new { user.Id, user.PublicId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw DomainProblemException.Forbidden("The administrator identity is invalid.");

        var roles = await (
            from userRole in _dbContext.UserRoles.AsNoTracking()
            join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == admin.Id && role.Name != null
            select role.Name!)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (!roles.Contains(AuditRoleNames.OrderManager, StringComparer.Ordinal) &&
            !roles.Contains(AuditRoleNames.SuperAdmin, StringComparer.Ordinal))
        {
            throw DomainProblemException.Forbidden("The administrator is not allowed to ship orders.");
        }

        return AuditActor.Create(AuditActorType.Admin, admin.PublicId, roles);
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
