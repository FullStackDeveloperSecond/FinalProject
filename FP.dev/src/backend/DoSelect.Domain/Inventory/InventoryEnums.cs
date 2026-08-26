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

    public static readonly IReadOnlyCollection<string> All =
        [StockIn, Reserve, Release, Ship, ReturnToStock, ManualIncrease, ManualDecrease, Damage, Adjustment];
}

/// <summary>
/// Controlled vocabulary for UC-ADM-INV-01's manual release ReasonCode (組長's ruling, PR #36 round
/// 3 review — supersedes an earlier member_cancelled/fraud_review draft). CustomerCancelled, not
/// MemberCancelled: a Guest order's reservation can be manually released too, and "member" is too
/// narrow. RiskRejected, not FraudReview: "under review" doesn't itself explain why a reservation
/// was released.
/// </summary>
public static class InventoryReleaseReasonCodes
{
    public const string CustomerCancelled = "customer_cancelled";
    public const string DuplicateOrder = "duplicate_order";
    public const string RiskRejected = "risk_rejected";
    public const string InventoryCorrection = "inventory_correction";
    public const string Other = "other";

    public static readonly IReadOnlyCollection<string> All =
        [CustomerCancelled, DuplicateOrder, RiskRejected, InventoryCorrection, Other];
}
