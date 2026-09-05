namespace DoSelect.Application.Common;

/// <summary>
/// Stable snake_case error codes returned to callers via Problem Details "code" extension.
/// Mirrors the project-wide error code catalog (API錯誤碼目錄). Values are append-only.
/// </summary>
public static class DomainErrorCodes
{
    public const string ResourceNotFound = "resource_not_found";
    public const string AuthorizationForbidden = "authorization_forbidden";
    public const string ConcurrencyConflict = "concurrency_conflict";
    public const string ValidationFailed = "validation_failed";
    public const string OrderTotalBelowMinimum = "order_total_below_minimum";

    public const string SupportTicketStateConflict = "support_ticket_state_conflict";
    public const string SupportTicketCancelNotAllowed = "support_ticket_cancel_not_allowed";
    public const string SupportTicketAssignmentConflict = "support_ticket_assignment_conflict";
    public const string SupportTicketNumberGenerationFailed = "support_ticket_number_generation_failed";

    public const string FileCountExceeded = "file_count_exceeded";
    public const string FileSizeExceeded = "file_size_exceeded";
    public const string FileFormatInvalid = "file_format_invalid";
    public const string FileMalwareDetected = "file_malware_detected";
    public const string FileScanUnavailable = "file_scan_unavailable";
    public const string ImageProcessingFailed = "image_processing_failed";
    public const string ImageMetadataIncomplete = "image_metadata_incomplete";

    public const string ImportFormatUnsupported = "import_format_unsupported";
    public const string ImportDatasetMissing = "import_dataset_missing";
    public const string ImportLookupNotFound = "import_lookup_not_found";
    public const string ImportSkuCodeDuplicate = "import_sku_code_duplicate";
    public const string ImportSkuUpdateNotFound = "import_sku_update_not_found";
    public const string ImportValidationFailed = "import_validation_failed";
    public const string ImportPreviewExpired = "import_preview_expired";
    public const string ImportAlreadyCommitted = "import_already_committed";
    public const string ImportBatchExpired = "import_batch_expired";
    public const string ImportBatchInProgress = "import_batch_in_progress";

    /// <summary>
    /// UC-ADM-INV-01 匯入。與商品匯入分開的錯誤碼（Endpoint 目錄「UC-ADM-INV-01 匯入」列指定）：
    /// 庫存匯入還會因為「Preview 之後 Balance 被動過」而整批拒絕，那不是商品匯入會有的情形。
    /// </summary>
    public const string InventoryImportValidationFailed = "inventory_import_validation_failed";
}
