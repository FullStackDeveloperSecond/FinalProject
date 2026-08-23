namespace DoSelect.Domain.Inventory;

public enum InventoryReservationStatus
{
    Active,
    Consumed,
    Released,
    Expired,
}

public enum InventoryReconciliationStatus
{
    Open,
    Acknowledged,
    Resolved,
    Dismissed,
}

/// <summary>InventoryMovement.MovementType values (庫存規則.md "庫存異動類型").</summary>
public static class InventoryMovementTypes
{
    public const string StockIn = "StockIn";
    public const string Reserve = "Reserve";
    public const string Release = "Release";
    public const string Ship = "Ship";
    public const string ReturnToStock = "ReturnToStock";
    public const string ManualIncrease = "ManualIncrease";
    public const string ManualDecrease = "ManualDecrease";
    public const string Damage = "Damage";
    public const string Adjustment = "Adjustment";
}
