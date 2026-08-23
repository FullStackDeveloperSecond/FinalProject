using System.ComponentModel.DataAnnotations;

namespace DoSelect.Application.Inventory;

public sealed record InventorySkuSummaryDto(Guid PublicId, string SkuCode, string NameZhTw);

public sealed record InventoryActorSummaryDto(Guid? PublicId, string? Email);

public sealed record InventoryOrderSummaryDto(Guid PublicId, string OrderNumber);

public sealed record InventoryBalanceQuery(
    string? Q,
    string? StockState,
    string? CategoryCode,
    int PageNumber = 1,
    int PageSize = 20);

public sealed record InventoryBalanceDto(
    Guid SkuPublicId,
    string SkuCode,
    string SkuNameZhTw,
    int OnHand,
    int Reserved,
    int Available,
    int LowStockThreshold,
    byte[] RowVersion);

public sealed record InventoryMovementQuery(
    Guid? SkuPublicId,
    IReadOnlyList<string>? MovementTypes,
    DateTime? From,
    DateTime? To,
    int PageNumber = 1,
    int PageSize = 20);

public sealed record InventoryMovementDto(
    Guid PublicId,
    InventorySkuSummaryDto Sku,
    string MovementType,
    int OnHandDelta,
    int ReservedDelta,
    int BeforeOnHand,
    int AfterOnHand,
    int BeforeReserved,
    int AfterReserved,
    string ReasonCode,
    InventoryActorSummaryDto? Actor,
    string ReferenceType,
    Guid? ReferencePublicId,
    DateTime OccurredAtUtc);

/// <summary>Cursor pagination request — stable sort key is ExpiresAtUtc DESC, ReservationPublicId DESC (API共通規範.md).</summary>
public sealed record InventoryReservationListQuery(string? Cursor, string? Status, int PageSize = 20);

public sealed record InventoryReservationDto(
    Guid PublicId,
    InventoryOrderSummaryDto Order,
    InventorySkuSummaryDto Sku,
    int Quantity,
    string Status,
    DateTime? ExpiresAtUtc,
    DateTime CreatedAtUtc,
    IReadOnlyList<string> AvailableActions,
    byte[] RowVersion);

/// <summary>
/// ReasonCode is capped at 32 chars to match InventoryReservation.ReleaseReason and
/// InventoryMovement.ReasonCode's column width. Note (up to 500 chars, required by the DTO
/// contract) is validated here but not currently persisted — see EfInventoryReservationService's
/// ReleaseCoreAsync comment; there is no free-text Audit Log column/table yet.
/// </summary>
public sealed record ReleaseReservationRequest(
    [Required, StringLength(32, MinimumLength = 1)] string ReasonCode,
    [Required, StringLength(500, MinimumLength = 1)] string Note,
    [Required] byte[] RowVersion);

public sealed record InventoryReconciliationCaseQuery(string? Status, int PageNumber = 1, int PageSize = 20);

public sealed record InventoryReconciliationCaseDto(
    Guid PublicId,
    InventorySkuSummaryDto Sku,
    string Status,
    int ExpectedOnHand,
    int ActualOnHand,
    int ExpectedReserved,
    int ActualReserved,
    DateTime DetectedAtUtc,
    string? AcknowledgedBy,
    string? ResolvedByAdminUserId,
    Guid? ResolutionMovementPublicId,
    string? ResolutionReason,
    DateTime? ResolvedAtUtc,
    byte[] RowVersion);

/// <summary>
/// Resolving a case always uses the already-recorded Actual* values as the corrected truth (a
/// physical recount already produced them) — there is no separate "enter the correct number"
/// field. Dismissing means the detection itself was wrong; Reason is required and no Movement is
/// created (資料字典-商品庫存與組裝.md: "Dismissed 需說明核對基準錯誤原因，不得用來隱藏未解差異").
/// </summary>
public sealed record ResolveReconciliationCaseRequest(
    bool Dismissed,
    [StringLength(1000)] string? Reason,
    [Required] byte[] RowVersion);

public readonly record struct ReservationLine(Guid SkuPublicId, int Quantity);
