using DoSelect.Application.Auditing;
using DoSelect.Application.Common;

namespace DoSelect.Application.Imports;

/// <summary>
/// 庫存調整匯入的上傳請求。商品匯入是三個資料集三個檔，庫存只有一組——
/// 匯入暫存與庫存調整設計.md：「庫存調整只使用第 1 組來源，第 2／3 組 Hash 與檔名為 Null」。
/// </summary>
public sealed record PreviewInventoryImportRequest(
    IncomingImportFile AdjustmentsFile,
    int TemplateVersion);

/// <summary>
/// 與 <see cref="ProductImportBatchDto"/> 同形狀，但刻意是另一個具名 Schema：兩種匯入的
/// Batch 生命週期一樣，可用的動作與 Policy 卻不同，共用一個名字會讓 OpenAPI 的呼叫端誤以為
/// 可以互換。
/// </summary>
public sealed record InventoryImportBatchDto(
    Guid PublicId,
    string ImportType,
    int TemplateVersion,
    string Status,
    string CreatedByAdminUserId,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    int RowCount,
    int NewCount,
    int UpdatedCount,
    int UnchangedCount,
    int ErrorCount,
    DateTime? ConfirmedAtUtc,
    byte[] RowVersion);

/// <summary>
/// UC-ADM-INV-01 匯入（匯入暫存與庫存調整設計.md「庫存匯入確認」）。
///
/// Preview 依目前 Balance 算出每一列的 Adjustment Delta 並暫存；Confirm 重新檢查 Preview 當時的
/// Balance 是否被動過，任一 SKU 已變動就整批拒絕並要求重新 Preview——盤點結果是對著某一個時點的
/// 庫存算出來的，底下的數字換了，那份差異就不再成立。
///
/// 每列在同一個交易裡產生一筆 Adjustment InventoryMovement 並更新 InventoryBalance；不允許造成負
/// OnHand、負 Reserved、Reserved 大於 OnHand，或覆蓋 Active Reservation。
/// </summary>
public interface IInventoryImportService
{
    Task<InventoryImportBatchDto> PreviewAsync(
        PreviewInventoryImportRequest request,
        string createdByAdminUserId,
        CancellationToken cancellationToken);

    Task<InventoryImportBatchDto?> GetAsync(Guid batchPublicId, CancellationToken cancellationToken);

    Task<CursorPage<ImportRowDto>> GetRowsAsync(
        Guid batchPublicId,
        ImportRowsQuery query,
        CancellationToken cancellationToken);

    /// <summary>批次不存在回 null；否則回錯誤 CSV（ErrorCount 為 0 時是只有標題列的 CSV）。</summary>
    Task<byte[]?> GetErrorsCsvAsync(Guid batchPublicId, CancellationToken cancellationToken);

    /// <summary>
    /// 套用整批調整。錯誤碼與商品匯入一致：`import_already_committed`（409）、
    /// `import_batch_expired`（410）、`inventory_import_validation_failed`（409，含 Balance 已變動）、
    /// `concurrency_conflict`（409，Batch RowVersion 過期）。
    /// </summary>
    Task<InventoryImportBatchDto> ConfirmAsync(
        Guid batchPublicId,
        string adminUserId,
        byte[] rowVersion,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken);
}

/// <summary>POST /api/v1/admin/inventory-imports/{id}/actions/confirm 的 Body。</summary>
public sealed record ConfirmInventoryImportRequest(byte[] RowVersion);
