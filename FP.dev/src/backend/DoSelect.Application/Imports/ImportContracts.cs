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
/// CSV upload for 商品匯入 (product import). XLSX upload (the spec's other supported format,
/// a single file with three named sheets) is not yet implemented — see 待實作 in
/// 匯入暫存與庫存調整設計.md; this covers the "or three matching CSVs" path only for now.
/// </summary>
public sealed record PreviewProductImportRequest(
    IncomingImportFile ProductsFile,
    IncomingImportFile SkusFile,
    IncomingImportFile SpecificationsFile,
    int TemplateVersion);

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
/// 商品匯入 Preview／Status／Rows／Errors per UC-IMPORT-01 (匯入暫存與庫存調整設計.md).
/// The Confirm/commit endpoint (POST .../actions/confirm) is deliberately not implemented —
/// the acceptance spec requires it to write AuditLog and Outbox entries on success, and neither
/// subsystem exists in this codebase yet (same gap that made PR #36 round-3 withdraw
/// AdminInventoryController's manual-release endpoint). Wire it once alex's shared AuditLog/
/// Outbox infrastructure lands; do not fake it with a partial write.
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
}
