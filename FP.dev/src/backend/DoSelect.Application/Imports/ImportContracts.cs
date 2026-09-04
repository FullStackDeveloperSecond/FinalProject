using DoSelect.Application.Common;

namespace DoSelect.Application.Imports;

/// <summary>
/// The multipart file part as seen by the Api layer, decoupled from ASP.NET Core's IFormFile so
/// Application stays framework-agnostic — mirrors Support's IncomingAttachmentFile. OpenRead is a
/// lazy delegate so a request rejected before the file is even needed (missing dataset, quota
/// already exceeded) never triggers a read.
/// </summary>
public sealed record IncomingImportFile(
    string OriginalFileName,
    string ClaimedContentType,
    long? DeclaredLength,
    bool HasFile,
    Func<Stream> OpenRead);

/// <summary>
/// 商品匯入的上傳（匯入暫存與庫存調整設計.md API 契約：「上傳 XLSX，或三份 CSV 加 templateVersion」）。
/// 兩條路只能走一條：<see cref="WorkbookFile"/> 有檔時必須是單一 XLSX（工作表 Products／Skus／
/// Specifications），三個 CSV 欄位都要是空的；反之亦然。XLSX 的工作表讀成字串列之後走與 CSV
/// 完全相同的 Parser，所以兩種格式的驗證結果對等是結構上保證的，不是靠兩份程式碼寫得一樣。
/// </summary>
public sealed record PreviewProductImportRequest(
    IncomingImportFile ProductsFile,
    IncomingImportFile SkusFile,
    IncomingImportFile SpecificationsFile,
    int TemplateVersion,
    IncomingImportFile? WorkbookFile = null);

public sealed record ProductImportBatchDto(
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

public sealed record ImportRowDto(
    string Dataset,
    int SourceRowNumber,
    string ImportKey,
    string Action,
    IReadOnlyList<string> ErrorCodes,
    string NormalizedPayloadJson);

public sealed record ImportRowsQuery(
    string? Dataset,
    bool ErrorsOnly,
    string? Cursor,
    int PageSize);

/// <summary>
/// 商品匯入 Preview／Status／Rows／Errors／Confirm per UC-IMPORT-01 (匯入暫存與庫存調整設計.md).
/// Confirm was originally withheld because no central AuditLog existed; dev now has IAuditWriter,
/// and ConfirmAsync writes the audit entry in the same transaction as the catalog writes. No
/// Outbox entry is written: the integration-event catalog defines no event for a committed
/// catalog import, and inventing one here would be a contract change (the spec's "必要 Outbox"
/// resolves to none today).
///
/// GetAsync/GetRowsAsync/GetErrorsCsvAsync currently authorize purely at the controller's policy
/// attribute (CatalogImport.ReadAll — see AdminProductImportsController) rather than also
/// checking "batch belongs to the caller" per-resource, because under the current role scheme
/// CatalogImport.Execute and CatalogImport.ReadAll are granted to the identical role set
/// (CatalogManager, SuperAdmin) — every caller who can create/preview a batch can already see
/// every other admin's batches too, so per-resource owner scoping has no observable effect today.
/// If a narrower "import execute only, no ReadAll" role is introduced later, this needs a real
/// ownership check (compare ImportBatch.CreatedByAdminUserId to the caller) added here.
/// </summary>
public interface IProductImportService
{
    Task<ProductImportBatchDto> PreviewAsync(
        PreviewProductImportRequest request,
        string createdByAdminUserId,
        CancellationToken cancellationToken);

    Task<ProductImportBatchDto?> GetAsync(Guid batchPublicId, CancellationToken cancellationToken);

    Task<CursorPage<ImportRowDto>> GetRowsAsync(
        Guid batchPublicId,
        ImportRowsQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns null if the batch does not exist; the CSV bytes otherwise (empty-row CSV if ErrorCount is 0).</summary>
    Task<byte[]?> GetErrorsCsvAsync(Guid batchPublicId, CancellationToken cancellationToken);

    /// <summary>
    /// 商品匯入確認 (匯入暫存與庫存調整設計.md 商品匯入確認 steps 1–6): re-validates the staged rows
    /// against the current catalog with the same resolvers Preview used, then applies every
    /// Product／SKU／Specification change in a single SQL Server transaction — any failure rolls
    /// the whole batch back, marks it Failed, and never leaves a partial success. Rejects with
    /// import_already_committed (409) for a Committed batch, import_batch_expired (410) past the
    /// 24-hour window, import_validation_failed (409) when the batch is not Ready or the catalog
    /// drifted since Preview, and concurrency_conflict (409) on a stale RowVersion.
    /// </summary>
    Task<ProductImportBatchDto> ConfirmAsync(
        Guid batchPublicId,
        string adminUserId,
        byte[] rowVersion,
        DoSelect.Application.Auditing.AuditRequestContext auditContext,
        CancellationToken cancellationToken);
}

/// <summary>Body of POST /api/v1/admin/product-imports/{id}/actions/confirm — the RowVersion the
/// admin's preview screen last saw, so a concurrent confirm/expiry loses cleanly with a 409.</summary>
public sealed record ConfirmProductImportRequest(byte[] RowVersion);
