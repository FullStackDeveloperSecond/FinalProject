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
