using DoSelect.Domain.Common;

namespace DoSelect.Domain.Inventory;

public sealed class InventoryBalance : MutablePublicEntity
{
    private InventoryBalance() { }

    public InventoryBalance(
        Guid publicId,
        long skuId,
        int onHandQuantity,
        int reorderLevel,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (skuId <= 0 || onHandQuantity < 0 || reorderLevel < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(onHandQuantity));
        }

        SkuId = skuId;
        OnHandQuantity = onHandQuantity;
        ReservedQuantity = 0;
        AvailableQuantity = onHandQuantity;
        ReorderLevel = reorderLevel;
    }

    public long SkuId { get; private set; }
    public int OnHandQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }
    public int AvailableQuantity { get; private set; }
    public int ReorderLevel { get; private set; }

    public void ApplyQuantities(
        int onHandQuantity,
        int reservedQuantity,
        DateTime updatedAtUtc)
    {
        if (onHandQuantity < 0 || reservedQuantity < 0 || reservedQuantity > onHandQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(onHandQuantity));
        }

        OnHandQuantity = onHandQuantity;
        ReservedQuantity = reservedQuantity;
        AvailableQuantity = onHandQuantity - reservedQuantity;
        MarkUpdated(updatedAtUtc);
    }

    public void ChangeReorderLevel(int reorderLevel, DateTime updatedAtUtc)
    {
        if (reorderLevel < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reorderLevel));
        }

        ReorderLevel = reorderLevel;
        MarkUpdated(updatedAtUtc);
    }
}

public sealed class InventoryReservation : MutablePublicEntity
{
    private InventoryReservation() { }

    public InventoryReservation(
        Guid publicId,
        long skuId,
        long orderId,
        int quantity,
        DateTime? expiresAtUtc,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (skuId <= 0 || orderId <= 0 || quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        if (expiresAtUtc.HasValue &&
            (expiresAtUtc.Value.Kind != DateTimeKind.Utc || expiresAtUtc.Value <= createdAtUtc))
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        SkuId = skuId;
        OrderId = orderId;
        Quantity = quantity;
        Status = InventoryReservationStatus.Active;
        ExpiresAtUtc = expiresAtUtc;
    }

    public long SkuId { get; private set; }
    public long OrderId { get; private set; }
    public int Quantity { get; private set; }
    public InventoryReservationStatus Status { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }
    public string? ReleaseReason { get; private set; }

    public void Consume(DateTime consumedAtUtc)
    {
        EnsureActive();
        Status = InventoryReservationStatus.Consumed;
        MarkUpdated(consumedAtUtc);
    }

    public void Release(string reason, bool expired, DateTime releasedAtUtc)
    {
        EnsureActive();
        releasedAtUtc = RequireUtc(releasedAtUtc, nameof(releasedAtUtc));
        Status = expired
            ? InventoryReservationStatus.Expired
            : InventoryReservationStatus.Released;
        ReleaseReason = RequireText(reason, nameof(reason));
        ReleasedAtUtc = releasedAtUtc;
        MarkUpdated(releasedAtUtc);
    }

    private void EnsureActive()
    {
        if (Status != InventoryReservationStatus.Active)
        {
            throw new InvalidOperationException("Only an active reservation can be changed.");
        }
    }
}

public sealed class InventoryMovement : PublicEntity
{
    private InventoryMovement() { }

    public InventoryMovement(
        Guid publicId,
        long skuId,
        long? reservationId,
        string movementType,
        int onHandDelta,
        int reservedDelta,
        int beforeOnHand,
        int afterOnHand,
        int beforeReserved,
        int afterReserved,
        string reasonCode,
        string referenceType,
        Guid? referencePublicId,
        string? actorUserId,
        DateTime occurredAtUtc)
        : base(publicId, occurredAtUtc)
    {
        if (skuId <= 0 || reservationId is <= 0 ||
            beforeOnHand + onHandDelta != afterOnHand ||
            beforeReserved + reservedDelta != afterReserved ||
            beforeOnHand < 0 || afterOnHand < 0 ||
            beforeReserved < 0 || afterReserved < 0 ||
            beforeReserved > beforeOnHand || afterReserved > afterOnHand)
        {
            throw new ArgumentException("The inventory movement balances are invalid.");
        }

        SkuId = skuId;
        ReservationId = reservationId;
        MovementType = RequireText(movementType, nameof(movementType));
        OnHandDelta = onHandDelta;
        ReservedDelta = reservedDelta;
        BeforeOnHand = beforeOnHand;
        AfterOnHand = afterOnHand;
        BeforeReserved = beforeReserved;
        AfterReserved = afterReserved;
        ReasonCode = RequireText(reasonCode, nameof(reasonCode));
        ReferenceType = RequireText(referenceType, nameof(referenceType));
        ReferencePublicId = referencePublicId;
        ActorUserId = string.IsNullOrWhiteSpace(actorUserId) ? null : actorUserId.Trim();
        OccurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
    }

    public long SkuId { get; private set; }
    public long? ReservationId { get; private set; }
    public string MovementType { get; private set; } = string.Empty;
    public int OnHandDelta { get; private set; }
    public int ReservedDelta { get; private set; }
    public int BeforeOnHand { get; private set; }
    public int AfterOnHand { get; private set; }
    public int BeforeReserved { get; private set; }
    public int AfterReserved { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public string ReferenceType { get; private set; } = string.Empty;
    public Guid? ReferencePublicId { get; private set; }
    public string? ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
}

public sealed class InventoryReconciliationCase : MutablePublicEntity
{
    private InventoryReconciliationCase() { }

    public InventoryReconciliationCase(
        Guid publicId,
        long skuId,
        int expectedOnHand,
        int actualOnHand,
        int expectedReserved,
        int actualReserved,
        DateTime detectedAtUtc,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (skuId <= 0 || new[]
            {
                expectedOnHand,
                actualOnHand,
                expectedReserved,
                actualReserved,
            }.Any(value => value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(expectedOnHand));
        }

        SkuId = skuId;
        Status = InventoryReconciliationStatus.Open;
        ExpectedOnHand = expectedOnHand;
        ActualOnHand = actualOnHand;
        ExpectedReserved = expectedReserved;
        ActualReserved = actualReserved;
        DetectedAtUtc = RequireUtc(detectedAtUtc, nameof(detectedAtUtc));
    }

    public long SkuId { get; private set; }
    public InventoryReconciliationStatus Status { get; private set; }
    public int ExpectedOnHand { get; private set; }
    public int ActualOnHand { get; private set; }
    public int ExpectedReserved { get; private set; }
    public int ActualReserved { get; private set; }
    public DateTime DetectedAtUtc { get; private set; }
    public string? AcknowledgedBy { get; private set; }
    public string? ResolvedByAdminUserId { get; private set; }
    public long? ResolutionMovementId { get; private set; }
    public string? ResolutionReason { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }

    public void Acknowledge(string adminUserId, DateTime acknowledgedAtUtc)
    {
        if (Status != InventoryReconciliationStatus.Open)
        {
            throw new InvalidOperationException("Only an open case can be acknowledged.");
        }

        AcknowledgedBy = RequireText(adminUserId, nameof(adminUserId));
        Status = InventoryReconciliationStatus.Acknowledged;
        MarkUpdated(acknowledgedAtUtc);
    }

    public void Resolve(
        string adminUserId,
        long? resolutionMovementId,
        string? reason,
        bool dismissed,
        DateTime resolvedAtUtc)
    {
        if (Status is not (InventoryReconciliationStatus.Open or
            InventoryReconciliationStatus.Acknowledged) ||
            (!dismissed && resolutionMovementId is null or <= 0) ||
            (dismissed && resolutionMovementId.HasValue) ||
            (dismissed && string.IsNullOrWhiteSpace(reason)))
        {
            throw new InvalidOperationException("The reconciliation resolution is invalid.");
        }

        ResolvedByAdminUserId = RequireText(adminUserId, nameof(adminUserId));
        ResolutionMovementId = resolutionMovementId;
        ResolutionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Status = dismissed
            ? InventoryReconciliationStatus.Dismissed
            : InventoryReconciliationStatus.Resolved;
        ResolvedAtUtc = RequireUtc(resolvedAtUtc, nameof(resolvedAtUtc));
        MarkUpdated(resolvedAtUtc);
    }
}
