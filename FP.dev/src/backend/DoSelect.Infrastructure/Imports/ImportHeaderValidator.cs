using DoSelect.Application.Common;

namespace DoSelect.Infrastructure.Imports;

internal static class ImportHeaderValidator
{
    /// <summary>
    /// Rejects the whole file if the header doesn't match the contract's fixed column list
    /// exactly, in the exact order — "任何未知、缺少、重複或順序不符合契約的 Header 整批拒絕，
    /// 不自動猜測語系" (匯入暫存與庫存調整設計.md). Header comparison is case-sensitive: the
    /// contract fixes the English header text, so a differently-cased header is itself a
    /// deviation from the fixed contract, not a value to tolerate.
    /// </summary>
    public static IReadOnlyList<string[]> ValidateAndGetDataRows(
        IReadOnlyList<string[]> rows,
        IReadOnlyList<string> expectedHeader,
        string datasetLabel)
    {
        if (rows.Count == 0)
        {
            throw new ImportBatchParseException(
                DomainErrorCodes.ImportDatasetMissing,
                $"The {datasetLabel} file is empty.");
        }

        var header = rows[0];
        if (header.Length != expectedHeader.Count ||
            !header.Select(column => column.Trim()).SequenceEqual(expectedHeader))
        {
            throw new ImportBatchParseException(
                DomainErrorCodes.ImportFormatUnsupported,
                $"The {datasetLabel} header must be exactly: {string.Join(",", expectedHeader)}.");
        }

        return rows.Skip(1).ToArray();
    }
}
