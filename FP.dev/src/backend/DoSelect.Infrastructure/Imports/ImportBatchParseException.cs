namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// A whole-batch failure that happens before per-row staging even starts (bad header, malformed
/// CSV, empty dataset) — 匯入暫存與庫存調整設計.md's "任何未知、缺少、重複或順序不符合契約
/// 的 Header 整批拒絕". Distinct from a per-row error, which still produces a Ready/Invalid batch
/// with row-level ErrorCodes; this instead means no batch is created at all.
/// </summary>
internal sealed class ImportBatchParseException : Exception
{
    public ImportBatchParseException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
