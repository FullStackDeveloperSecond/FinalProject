using DoSelect.Application.Returns;
using DoSelect.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Returns;

/// <summary>
/// Inventory-owner adapter for inspected returns. It stages changes on the shared scoped
/// DbContext and deliberately leaves SaveChanges to ReturnStore so the return transition,
/// inspection rows, balance and append-only movements commit atomically.
/// </summary>
public sealed class ReturnInventoryRestockWriter : IReturnInventoryPort
{
    private const string MovementType = "ReturnToStock";
    private const string ReasonCode = "return-inspection-resellable";
    private const string ReferenceType = "ReturnItem";

    private readonly DoSelectDbContext _context;

    public ReturnInventoryRestockWriter(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task StageReturnToStockAsync(
        Guid returnPublicId,
        string adminUserId,
        IReadOnlyList<ReturnToStockInstruction> instructions,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        if (returnPublicId == Guid.Empty || string.IsNullOrWhiteSpace(adminUserId))
        {
            throw new ArgumentException("Return and administrator identities are required.");
        }

        if (instructions.Count == 0)
        {
            return;
        }

        var orderItemIds = instructions.Select(instruction => instruction.OrderItemId).Distinct().ToArray();
        var orderItems = await _context.OrderItems
            .Where(item => orderItemIds.Contains(item.Id))
            .Select(item => new
            {
                item.Id,
                item.SkuId,
                item.UnitCostSnapshot,
            })
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        if (orderItems.Count != orderItemIds.Length || orderItems.Values.Any(item => item.SkuId is null))
        {
            throw new InvalidOperationException(
                "A resellable return item must resolve to its original inventory SKU.");
        }

        var skuIds = orderItems.Values.Select(item => item.SkuId!.Value).Distinct().ToArray();
        var balances = await _context.InventoryBalances
            .Where(balance => skuIds.Contains(balance.SkuId))
            .ToDictionaryAsync(balance => balance.SkuId, cancellationToken);

        if (balances.Count != skuIds.Length)
        {
            throw new InvalidOperationException(
                "A resellable return item must resolve to an inventory balance.");
        }

        foreach (var instruction in instructions.OrderBy(instruction => instruction.OrderItemId))
        {
            if (instruction.OrderItemId <= 0 ||
                instruction.ReturnItemPublicId == Guid.Empty ||
                instruction.Quantity <= 0)
            {
                throw new ArgumentException("A return-to-stock instruction is invalid.");
            }

            var orderItem = orderItems[instruction.OrderItemId];
            var balance = balances[orderItem.SkuId!.Value];
            var beforeOnHand = balance.OnHandQuantity;
            var afterOnHand = checked(beforeOnHand + instruction.Quantity);
            var reserved = balance.ReservedQuantity;

            balance.ApplyQuantities(afterOnHand, reserved, occurredAtUtc);
            _context.InventoryMovements.Add(new InventoryMovement(
                Guid.CreateVersion7(),
                balance.SkuId,
                reservationId: null,
                MovementType,
                instruction.Quantity,
                reservedDelta: 0,
                beforeOnHand,
                afterOnHand,
                reserved,
                reserved,
                orderItem.UnitCostSnapshot,
                ReasonCode,
                ReferenceType,
                instruction.ReturnItemPublicId,
                adminUserId.Trim(),
                occurredAtUtc));
        }
    }
}
