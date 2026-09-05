using DoSelect.Application.Orders;
using DoSelect.Application.Shipping;
using DoSelect.Domain.Orders;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shipping;

/// <summary>
/// C1：訂單 DTO 上的物流摘要與歷程。後台與顧客兩種形狀共用同一次查詢；顧客形狀刻意不帶
/// Actor、原因備註與任何內部 ID。第一版一張訂單只有一張主要物流單（狀態機設計.md §5）。
/// </summary>
internal static class ShipmentProjection
{
    public static async Task<AdminShipmentDto?> LoadAdminAsync(
        DoSelectDbContext dbContext,
        Order order,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(dbContext, order.Id, cancellationToken);
        if (loaded is null)
        {
            return null;
        }

        var (shipment, kind, history) = loaded.Value;
        var actorUserIds = history.Select(entry => entry.ActorUserId).Where(id => id is not null).Distinct().ToArray();
        var actorPublicIds = actorUserIds.Length == 0
            ? new Dictionary<string, Guid>()
            : await dbContext.Users.AsNoTracking()
                .Where(user => actorUserIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.PublicId, cancellationToken);

        return new AdminShipmentDto(
            shipment.PublicId,
            shipment.ShipmentNumber,
            shipment.TrackingNumber,
            shipment.Status.ToString(),
            order.ShippingMethodCode,
            shipment.ShippedAtUtc,
            shipment.DeliveredAtUtc,
            history.Select(entry => new AdminShipmentHistoryDto(
                entry.FromStatus?.ToString(),
                entry.ToStatus.ToString(),
                entry.ActorUserId is not null && actorPublicIds.TryGetValue(entry.ActorUserId, out var actorPublicId)
                    ? actorPublicId
                    : null,
                entry.OccurredAtUtc)).ToList(),
            ShipmentStatusPolicy.AvailableActions(shipment.Status, kind),
            shipment.RowVersion);
    }

    public static async Task<OrderShipmentDto?> LoadCustomerAsync(
        DoSelectDbContext dbContext,
        Order order,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(dbContext, order.Id, cancellationToken);
        if (loaded is null)
        {
            return null;
        }

        var (shipment, _, history) = loaded.Value;
        return new OrderShipmentDto(
            shipment.ShipmentNumber,
            shipment.TrackingNumber,
            shipment.Status,
            order.ShippingMethodCode,
            shipment.ShippedAtUtc,
            shipment.DeliveredAtUtc,
            history.Select(entry => new OrderShipmentHistoryDto(entry.FromStatus, entry.ToStatus, entry.OccurredAtUtc)).ToList());
    }

    private static async Task<(DoSelect.Domain.Shipping.Shipment Shipment, string Kind, List<DoSelect.Domain.Shipping.ShipmentStatusHistory> History)?> LoadAsync(
        DoSelectDbContext dbContext,
        long orderId,
        CancellationToken cancellationToken)
    {
        var shipment = await dbContext.Shipments.AsNoTracking()
            .Where(candidate => candidate.OrderId == orderId)
            .OrderBy(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (shipment is null)
        {
            return null;
        }

        var kind = await dbContext.ShippingMethods.AsNoTracking()
            .Where(method => method.Id == shipment.ShippingMethodId)
            .Select(method => method.Kind)
            .SingleAsync(cancellationToken);
        var history = await dbContext.ShipmentStatusHistories.AsNoTracking()
            .Where(entry => entry.ShipmentId == shipment.Id)
            .OrderBy(entry => entry.OccurredAtUtc)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);
        return (shipment, kind, history);
    }
}
