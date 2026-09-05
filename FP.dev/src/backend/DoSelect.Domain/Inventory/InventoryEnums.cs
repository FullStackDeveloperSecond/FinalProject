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
/// <summary>
/// 庫存匯入（A-13、UC-ADM-INV-01 匯入）的調整原因碼。六個值由
/// 匯入暫存與庫存調整設計.md「庫存匯入確認」固定，不是管理員可自由填寫的欄位；`Other` 必填說明。
/// </summary>
public static class InventoryAdjustmentReasonCodes
{
    public const string StocktakeDifference = "StocktakeDifference";
    public const string Damaged = "Damaged";
    public const string Lost = "Lost";
    public const string ReturnRestock = "ReturnRestock";
    public const string DataCorrection = "DataCorrection";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All =
        [StocktakeDifference, Damaged, Lost, ReturnRestock, DataCorrection, Other];

    /// <summary>Other 之外的原因碼說明可以留白（模板以 \N 表示空值）。</summary>
    public static bool RequiresNote(string reasonCode) =>
        string.Equals(reasonCode, Other, StringComparison.Ordinal);
}

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

/// <summary>
/// 對帳案件結案原因（組長對帳裁定 D1）。dismiss 與 resolve 各有白名單：`false_positive`（核對基準錯誤）
/// 只能 dismiss——差異本來就不存在，沒有東西可修；`count_verified`（實點確認）只能 resolve——已確認
/// 帳本才是對的，不能拿來把差異藏掉。`system_error`／`other` 兩邊都可。
/// </summary>
public static class InventoryReconciliationReasonCodes
{
    public const string FalsePositive = "false_positive";
    public const string CountVerified = "count_verified";
    public const string SystemError = "system_error";
    public const string Other = "other";

    public static readonly IReadOnlyCollection<string> ForDismiss = [FalsePositive, SystemError, Other];
    public static readonly IReadOnlyCollection<string> ForResolve = [CountVerified, SystemError, Other];
}
