using System.ComponentModel.DataAnnotations;

namespace DoSelect.Application.Inventory;

public sealed record InventorySkuSummaryDto(Guid PublicId, string SkuCode, string NameZhTw);

public sealed record InventoryActorSummaryDto(Guid? PublicId, string? Email)
{
    /// <summary>
    /// Builds a display-safe actor summary — the raw ASP.NET Identity string Id must never reach a
    /// client, and the email is masked (組長 PR #36 round review: "回傳完整 Email" was flagged) so an
    /// inventory movement/reconciliation viewer can recognize an actor without seeing their full
    /// address. Keeps the first character of the local part plus the domain, e.g.
    /// <c>a***@doselect.test</c>; a one-character local part masks to <c>*</c> alone.
    /// </summary>
    public static InventoryActorSummaryDto FromIdentity(Guid publicId, string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new InventoryActorSummaryDto(publicId, null);
        }

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
        {
            return new InventoryActorSummaryDto(publicId, "***");
        }

        var masked = email[..1] + "***" + email[atIndex..];
        return new InventoryActorSummaryDto(publicId, masked);
    }
}

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

/// <summary>
/// <see cref="InventoryReservationDto.AvailableActions"/> 的合法值。A-12 頁只依這份清單顯示按鈕，
/// 後端只在 Active 保留上列出 release（UC-ADM-INV-01：其餘三個狀態都是終態）。
/// </summary>
public static class InventoryReservationActions
{
    public const string Release = "release";

    public static readonly IReadOnlyList<string> ForActive = [Release];
}

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
    InventoryActorSummaryDto? AcknowledgedBy,
    InventoryActorSummaryDto? ResolvedBy,
    Guid? ResolutionMovementPublicId,
    string? ResolutionReason,
    DateTime? ResolvedAtUtc,
    byte[] RowVersion);

/// <summary>
/// Resolving a case always uses the already-recorded Actual* values as the corrected truth (a
/// physical recount already produced them) — there is no separate "enter the correct number"
/// field. Dismissing means the detection itself was wrong; Reason is required and no Movement is
/// created (資料字典-商品庫存與組裝.md: "Dismissed 需說明核對基準錯誤原因，不得用來隱藏未解差異").
/// 組長對帳裁定 C1／D1：兩條路由共用同一個 Request，reasonCode 依動作各有白名單
/// （<see cref="DoSelect.Domain.Inventory.InventoryReconciliationReasonCodes"/>），note 兩邊都必填。
/// </summary>
public sealed record ReconciliationCaseResolutionCommand(
    [Required][StringLength(32, MinimumLength = 1)] string ReasonCode,
    [Required][StringLength(ReconciliationCaseResolutionCommand.NoteMaxLength, MinimumLength = 1)] string Note,
    [Required] byte[] RowVersion)
{
    /// <summary>與人工釋放的 note 同上限（API DTO與Schema契約），不擴充到中央稽核的 1000。</summary>
    public const int NoteMaxLength = 500;
}

public readonly record struct ReservationLine(Guid SkuPublicId, int Quantity);
