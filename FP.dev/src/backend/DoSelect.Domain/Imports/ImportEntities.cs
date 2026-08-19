using DoSelect.Domain.Common;

namespace DoSelect.Domain.Imports;

public sealed class ImportBatch : MutablePublicEntity
{
    private ImportBatch() { }

    public ImportBatch(
        Guid publicId,
        ImportType importType,
        int templateVersion,
        string createdByAdminUserId,
        DateTime expiresAtUtc,
        Guid correlationId,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (templateVersion <= 0 || correlationId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(templateVersion));
        }

        expiresAtUtc = RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        ImportType = importType;
        TemplateVersion = templateVersion;
        Status = ImportBatchStatus.Uploaded;
        CreatedByAdminUserId = RequireText(
            createdByAdminUserId,
            nameof(createdByAdminUserId));
        ExpiresAtUtc = expiresAtUtc;
        CorrelationId = correlationId;
    }

    public ImportType ImportType { get; private set; }
    public int TemplateVersion { get; private set; }
    public ImportBatchStatus Status { get; private set; }
    public string CreatedByAdminUserId { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }
    public byte[]? SourceFileHash1 { get; private set; }
    public byte[]? SourceFileHash2 { get; private set; }
    public byte[]? SourceFileHash3 { get; private set; }
    public string? SourceFileNameDisplay1 { get; private set; }
    public string? SourceFileNameDisplay2 { get; private set; }
    public string? SourceFileNameDisplay3 { get; private set; }
    public int RowCount { get; private set; }
    public int NewCount { get; private set; }
    public int UpdatedCount { get; private set; }
    public int UnchangedCount { get; private set; }
    public int ErrorCount { get; private set; }
    public int NormalizedContentVersion { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public string? ResultSummaryJson { get; private set; }
    public Guid CorrelationId { get; private set; }

    public void SetSources(
        byte[] sourceFileHash1,
        string sourceFileNameDisplay1,
        byte[]? sourceFileHash2,
        string? sourceFileNameDisplay2,
        byte[]? sourceFileHash3,
        string? sourceFileNameDisplay3,
        DateTime updatedAtUtc)
    {
        SourceFileHash1 = CopyHash(sourceFileHash1, nameof(sourceFileHash1));
        SourceFileHash2 = CopyOptionalHash(sourceFileHash2, nameof(sourceFileHash2));
        SourceFileHash3 = CopyOptionalHash(sourceFileHash3, nameof(sourceFileHash3));
        SourceFileNameDisplay1 = RequireText(
            sourceFileNameDisplay1,
            nameof(sourceFileNameDisplay1));
        SourceFileNameDisplay2 = OptionalText(sourceFileNameDisplay2);
        SourceFileNameDisplay3 = OptionalText(sourceFileNameDisplay3);
        MarkUpdated(updatedAtUtc);
    }

    public void SetPreviewStatistics(
        int rowCount,
        int newCount,
        int updatedCount,
        int unchangedCount,
        int errorCount,
        int normalizedContentVersion,
        DateTime updatedAtUtc)
    {
        var counts = new[] { rowCount, newCount, updatedCount, unchangedCount, errorCount };
        if (rowCount > 5_000 || counts.Any(count => count < 0) ||
            newCount + updatedCount + unchangedCount + errorCount != rowCount ||
            normalizedContentVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowCount));
        }

        RowCount = rowCount;
        NewCount = newCount;
        UpdatedCount = updatedCount;
        UnchangedCount = unchangedCount;
        ErrorCount = errorCount;
        NormalizedContentVersion = normalizedContentVersion;
        Status = errorCount == 0 ? ImportBatchStatus.Ready : ImportBatchStatus.Invalid;
        MarkUpdated(updatedAtUtc);
    }

    public void ChangeStatus(ImportBatchStatus status, DateTime updatedAtUtc)
    {
        Status = status;
        MarkUpdated(updatedAtUtc);
    }

    public void Complete(string? resultSummaryJson, DateTime confirmedAtUtc)
    {
        confirmedAtUtc = RequireUtc(confirmedAtUtc, nameof(confirmedAtUtc));
        Status = ImportBatchStatus.Committed;
        ResultSummaryJson = OptionalText(resultSummaryJson);
        ConfirmedAtUtc = confirmedAtUtc;
        MarkUpdated(confirmedAtUtc);
    }

    private static byte[] CopyHash(byte[] value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length != 32)
        {
            throw new ArgumentException("The hash must contain 32 bytes.", parameterName);
        }

        return value.ToArray();
    }

    private static byte[]? CopyOptionalHash(byte[]? value, string parameterName) =>
        value is null ? null : CopyHash(value, parameterName);

    private static string? OptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ImportRow : Entity
{
    private ImportRow() { }

    public ImportRow(
        long importBatchId,
        ImportDataset dataset,
        int sourceRowNumber,
        string importKey,
        ImportRowAction action,
        string normalizedPayloadJson,
        string? errorCodes,
        byte[] rowHash,
        string? rawJson,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        if (importBatchId <= 0 || sourceRowNumber <= 0 || rowHash is null || rowHash.Length != 32)
        {
            throw new ArgumentOutOfRangeException(nameof(importBatchId));
        }

        if (normalizedPayloadJson.Length > 32 * 1024 || rawJson?.Length > 32 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(normalizedPayloadJson));
        }

        ImportBatchId = importBatchId;
        Dataset = dataset;
        SourceRowNumber = sourceRowNumber;
        ImportKey = RequireText(importKey, nameof(importKey));
        Action = action;
        NormalizedPayloadJson = RequireText(
            normalizedPayloadJson,
            nameof(normalizedPayloadJson));
        ErrorCodes = string.IsNullOrWhiteSpace(errorCodes) ? null : errorCodes.Trim();
        RowHash = rowHash.ToArray();
        RawJson = string.IsNullOrWhiteSpace(rawJson) ? null : rawJson;
    }

    public long ImportBatchId { get; private set; }
    public ImportDataset Dataset { get; private set; }
    public int SourceRowNumber { get; private set; }
    public string ImportKey { get; private set; } = string.Empty;
    public ImportRowAction Action { get; private set; }
    public string NormalizedPayloadJson { get; private set; } = string.Empty;
    public string? ErrorCodes { get; private set; }
    public byte[] RowHash { get; private set; } = [];
    public string? RawJson { get; private set; }
}
