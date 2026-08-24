using DoSelect.Application.Inventory;
using DoSelect.Application.Shipping;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shipping;

/// <summary>UC-ADM-SHIP-02. See <see cref="IBatchShipmentService"/> for the "no ShipmentBatch entity yet" CSV-retrieval gap flagged for 組長.</summary>
public sealed class EfBatchShipmentService : IBatchShipmentService
{
    private const int MaxBatchSize = 100;

    private readonly DoSelectDbContext _dbContext;
    private readonly IInventoryReservationService _reservationService;

    public EfBatchShipmentService(DoSelectDbContext dbContext, IInventoryReservationService reservationService)
    {
        _dbContext = dbContext;
        _reservationService = reservationService;
    }

    public async Task<BatchShipmentResultDto> ShipBatchAsync(
        BatchShipmentRequest request, string adminUserId, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderPublicIds = request.OrderPublicIds.Distinct().ToList();
        if (orderPublicIds.Count == 0 || orderPublicIds.Count > MaxBatchSize)
        {
            throw new ShippingWriteException(
                ShippingWriteException.ErrorCodes.ShippingBatchLimitExceeded,
                $"A batch must contain between 1 and {MaxBatchSize} orders.");
        }

        var results = new List<BatchShipmentLineResultDto>(orderPublicIds.Count);
        foreach (var orderPublicId in orderPublicIds)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await ShipOneAsync(orderPublicId, adminUserId, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                results.Add(result);
            }
            catch (ShippingWriteException exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                results.Add(new BatchShipmentLineResultDto(orderPublicId, false, null, null, exception.ErrorCode));
            }
            finally
            {
                _dbContext.ChangeTracker.Clear();
            }
        }

        return new BatchShipmentResultDto(results);
    }

    private async Task<BatchShipmentLineResultDto> ShipOneAsync(
        Guid orderPublicId, string adminUserId, DateTime now, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(candidate => candidate.PublicId == orderPublicId, cancellationToken);
        if (order is null)
        {
            throw new ShippingWriteException(
                ShippingWriteException.ErrorCodes.ShippingOrderNotReady,
                $"Order '{orderPublicId}' was not found.");
        }

        if (order.FulfillmentStatus != FulfillmentStatus.Pending ||
            order.OrderStatus is not (OrderStatus.Confirmed or OrderStatus.Processing) ||
            order.AssemblyStatus is not (AssemblyStatus.NotRequired or AssemblyStatus.ReadyToShip))
        {
            throw new ShippingWriteException(
                ShippingWriteException.ErrorCodes.ShippingOrderNotReady,
                $"Order '{orderPublicId}' is not ready to ship.");
        }

        var alreadyShipped = await _dbContext.Shipments
            .AnyAsync(shipment => shipment.OrderId == order.Id, cancellationToken);
        if (alreadyShipped)
        {
            throw new ShippingWriteException(
                ShippingWriteException.ErrorCodes.ShippingOrderNotReady,
                $"Order '{orderPublicId}' already has a shipment.");
        }

        var shippingMethod = await _dbContext.ShippingMethods
            .FirstOrDefaultAsync(method => method.Code == order.ShippingMethodCode, cancellationToken);
        if (shippingMethod is null || !shippingMethod.IsActive)
        {
            throw new ShippingWriteException(
                ShippingWriteException.ErrorCodes.ShippingMethodNotAllowed,
                $"Shipping method '{order.ShippingMethodCode}' is not currently allowed.");
        }

        var paymentSatisfied = shippingMethod.AllowsCod
            ? order.PaymentStatus is not (PaymentStatus.Failed or PaymentStatus.Cancelled or PaymentStatus.Expired)
            : order.PaymentStatus == PaymentStatus.Paid;
        if (!paymentSatisfied)
        {
            throw new ShippingWriteException(
                ShippingWriteException.ErrorCodes.ShippingOrderNotReady,
                $"Order '{orderPublicId}' has not satisfied its payment condition.");
        }

        var reservationStatuses = await _dbContext.InventoryReservations
            .Where(reservation => reservation.OrderId == order.Id)
            .Select(reservation => reservation.Status)
            .ToListAsync(cancellationToken);
        if (reservationStatuses.Count == 0 || reservationStatuses.Any(status => status != InventoryReservationStatus.Active))
        {
            throw new ShippingWriteException(
                ShippingWriteException.ErrorCodes.ShippingOrderNotReady,
                $"Order '{orderPublicId}' does not have every line reserved as Active.");
        }

        long? convenienceStoreId = null;
        if (shippingMethod.Kind == ShippingProviderCodes.ConvenienceStore)
        {
            if (string.IsNullOrWhiteSpace(order.StoreCode))
            {
                throw new ShippingWriteException(
                    ShippingWriteException.ErrorCodes.ShippingOrderNotReady,
                    $"Order '{orderPublicId}' selected convenience-store pickup but has no store on file.");
            }

            // Order only snapshots StoreCode／StoreName／StoreAddress, not the ConvenienceStore's
            // ProviderCode brand — Checkout (haru／yinyin's module) hasn't been built yet, so no
            // real order ever set this snapshot yet. StoreCode alone resolves uniquely under this
            // seed's SEVEN-*/FAMI-* convention, but that's not schema-enforced; flagged for 組長.
            var store = await _dbContext.ConvenienceStores
                .FirstOrDefaultAsync(candidate => candidate.StoreCode == order.StoreCode, cancellationToken);
            if (store is null)
            {
                throw new ShippingWriteException(
                    ShippingWriteException.ErrorCodes.ShippingOrderNotReady,
                    $"Order '{orderPublicId}''s store '{order.StoreCode}' could not be resolved.");
            }

            convenienceStoreId = store.Id;
        }

        var shipmentNumber = $"SHP-{order.OrderNumber}";
        var trackingNumber = $"SIMTRK-{Guid.CreateVersion7():N}"[..20];

        var shipment = new Shipment(
            Guid.CreateVersion7(), order.Id, shippingMethod.Id, order.ShippingProviderProfileVersionId,
            convenienceStoreId, shipmentNumber, order.ShippingFee, now);
        _dbContext.Shipments.Add(shipment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        RecordTransition(shipment, FulfillmentStatus.Preparing, now, adminUserId);
        shipment.SetTrackingNumber(trackingNumber, now);
        RecordTransition(shipment, FulfillmentStatus.Shipped, now, adminUserId);

        order.ApplyFulfillmentProjection(FulfillmentStatus.Shipped, now);

        await _reservationService.ConsumeAllForOrderAsync(order.Id, now, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new BatchShipmentLineResultDto(orderPublicId, true, shipment.PublicId, trackingNumber, null);
    }

    private void RecordTransition(Shipment shipment, FulfillmentStatus toStatus, DateTime now, string adminUserId)
    {
        var fromStatus = shipment.Status;
        shipment.ChangeStatus(toStatus, now);
        _dbContext.ShipmentStatusHistories.Add(new ShipmentStatusHistory(
            Guid.CreateVersion7(), shipment.Id, fromStatus, toStatus,
            externalEventId: null, now, adminUserId));
    }
}
