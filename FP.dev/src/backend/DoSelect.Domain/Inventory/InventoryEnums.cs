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

    /// <summary>
    /// Written by the SKU cost-change flow (EfSkuAdminService) with zero quantity deltas — it records
    /// the new unit cost against the balance of the moment so the M-15 turnover report has a cost
    /// basis, which is why that report excludes it from quantity movements. 組長 PR #36 ruling A1:
    /// it is a first-class movement type, not an internal one. The admin movement list already shows
    /// these rows unfiltered, so rejecting them as an unknown `movementTypes` filter value would make
    /// the API contract disagree with the data it just returned.
    /// </summary>
    public const string CostChange = "CostChange";

    public static readonly IReadOnlyCollection<string> All =
        [StockIn, Reserve, Release, Ship, ReturnToStock, ManualIncrease, ManualDecrease, Damage, Adjustment, CostChange];
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
