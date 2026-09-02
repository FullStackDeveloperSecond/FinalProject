using System.Globalization;
using ClosedXML.Excel;
using DoSelect.Application.Common;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// 商品匯入的 XLSX 路徑（匯入暫存與庫存調整設計.md「商品模板 v1」：工作表名稱固定為 Products、
/// Skus、Specifications）。
///
/// 這裡只做一件事：把每張工作表讀成與 <see cref="DelimitedTextReader"/> 相同形狀的
/// <c>string[]</c> 列，然後交給同一組 Row Parser。XLSX 與三份 CSV 的對等不是靠兩套驗證寫得一樣，
/// 而是靠根本只有一套——工作表一旦變成字串列，後面的 Header 驗證、型別、長度、Null Token 與
/// 重複鍵處理就沒有第二條路可走。
/// </summary>
internal static class XlsxWorkbookReader
{
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>「檔案簽章檢查」：XLSX 是 ZIP 容器，副檔名說了不算。</summary>
    public static bool HasXlsxSignature(byte[] content) =>
        content.Length >= ZipSignature.Length &&
        content.AsSpan(0, ZipSignature.Length).SequenceEqual(ZipSignature);

    /// <summary>
    /// 讀出指定名稱的工作表。少任何一張是 <c>import_dataset_missing</c>；打不開、或任何儲存格帶
    /// 公式，是 <c>import_format_unsupported</c>。「公式不執行」在這裡的實作是根本不接受：CSV 那條路
    /// 沒有公式這種東西，接受公式就等於 XLSX 多了一種 CSV 沒有的輸入，對等就破了。
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string[]>> ReadSheets(
        byte[] content,
        IReadOnlyList<string> sheetNames)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!HasXlsxSignature(content))
        {
            throw new ImportBatchParseException(
                DomainErrorCodes.ImportFormatUnsupported,
                "The workbook is not an XLSX file.");
        }

        using var stream = new MemoryStream(content, writable: false);
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new ImportBatchParseException(
                DomainErrorCodes.ImportFormatUnsupported,
                "The workbook could not be opened as XLSX.");
        }

        using (workbook)
        {
            var sheets = new Dictionary<string, IReadOnlyList<string[]>>(StringComparer.Ordinal);
            foreach (var name in sheetNames)
            {
                if (!workbook.TryGetWorksheet(name, out var worksheet))
                {
                    throw new ImportBatchParseException(
                        DomainErrorCodes.ImportDatasetMissing,
                        $"The workbook has no '{name}' sheet. Required sheets: {string.Join(", ", sheetNames)}.");
                }

                sheets[name] = ReadSheet(worksheet);
            }

            return sheets;
        }
    }

    private static IReadOnlyList<string[]> ReadSheet(IXLWorksheet sheet)
    {
        var used = sheet.RangeUsed();
        if (used is null)
        {
            return [];
        }

        // 欄從 A 開始數而不是從「第一個有內容的欄」：Header 驗證比的是位置，一張從 B 欄開始的
        // 工作表本來就對不上模板。
        var lastColumn = used.LastColumn().ColumnNumber();
        var rows = new List<string[]>();
        for (var rowNumber = used.FirstRow().RowNumber(); rowNumber <= used.LastRow().RowNumber(); rowNumber++)
        {
            var fields = new string[lastColumn];
            var hasContent = false;
            for (var column = 1; column <= lastColumn; column++)
            {
                var cell = sheet.Cell(rowNumber, column);
                if (cell.HasFormula)
                {
                    throw new ImportBatchParseException(
                        DomainErrorCodes.ImportFormatUnsupported,
                        $"Cell {cell.Address} on sheet '{sheet.Name}' contains a formula; formulas are not accepted.");
                }

                var text = CellText(cell, sheet.Name);
                fields[column - 1] = text;
                hasContent |= text.Length > 0;
            }

            // 與 DelimitedTextReader 相同：完全空白的列不算資料列。
            if (hasContent)
            {
                rows.Add(fields);
            }
        }

        return rows;
    }

    /// <summary>
    /// 儲存格 → 與 CSV 同義的字串。數字用 Invariant 且不走科學記號（1500 就是 "1500"），布林是
    /// 小寫 true／false，日期是 ISO 8601——都是 CSV 契約已經規定的 Token。
    /// </summary>
    private static string CellText(IXLCell cell, string sheetName)
    {
        var value = cell.Value;
        return value.Type switch
        {
            XLDataType.Blank => string.Empty,
            XLDataType.Text => value.GetText(),
            XLDataType.Number => value.GetNumber().ToString("0.############################", CultureInfo.InvariantCulture),
            XLDataType.Boolean => value.GetBoolean() ? "true" : "false",
            XLDataType.DateTime => value.GetDateTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            XLDataType.TimeSpan => value.GetTimeSpan().ToString(),
            _ => throw new ImportBatchParseException(
                DomainErrorCodes.ImportFormatUnsupported,
                $"Cell {cell.Address} on sheet '{sheetName}' holds an error value."),
        };
    }
}
