using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using DoSelect.Application.Common;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// XLSX 讀取的資源上限（組長 PR #89 round 2 item 4）。10 MB 是**壓縮後**的檔案大小；一個小小的
/// ZIP 可以解壓成幾百 MB 的工作表 XML，或是把一格資料放在第 100 萬列讓「使用範圍」變得巨大。
/// 這三個上限都在把整張工作表展開成陣列**之前**檢查。
/// </summary>
public sealed record XlsxReadLimits(long MaxDecompressedBytes, int MaxRowsPerSheet, int MaxColumnsPerSheet)
{
    /// <summary>
    /// 解壓上限 64 MB：三個資料集合計 5,000 列、最寬 12 欄的正常工作簿解壓後不到 10 MB，這個
    /// 上限留了餘裕給格式化與共用字串表，又遠低於會把 worker 吃垮的量。列上限 = 標題列＋5,000
    /// 筆資料列；欄上限 64 是最寬模板（12 欄）的五倍，容許尾端的空白格式欄。
    /// </summary>
    public static readonly XlsxReadLimits Default = new(
        MaxDecompressedBytes: 64L * 1024 * 1024,
        MaxRowsPerSheet: 5_001,
        MaxColumnsPerSheet: 64);
}

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
    /// 讀出指定名稱的工作表。少任何一張是 <c>import_dataset_missing</c>；打不開、超過資源上限、或
    /// 任何儲存格帶公式，是 <c>import_format_unsupported</c>。「公式不執行」在這裡的實作是根本不接受：
    /// CSV 那條路沒有公式這種東西，接受公式就等於 XLSX 多了一種 CSV 沒有的輸入，對等就破了。
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string[]>> ReadSheets(
        byte[] content,
        IReadOnlyList<string> sheetNames,
        XlsxReadLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        limits ??= XlsxReadLimits.Default;

        if (!HasXlsxSignature(content))
        {
            throw new ImportBatchParseException(
                DomainErrorCodes.ImportFormatUnsupported,
                "The workbook is not an XLSX file.");
        }

        // 在把整本工作簿交給 ClosedXML 之前先看 ZIP 目錄：解壓後的總量超過上限就不開。
        // 目錄裡宣告的大小可以說謊，但 .NET 的 ZipArchive 在讀到超過宣告長度的資料時會丟例外，
        // 所以一個謊報成小檔的炸彈會在解壓中途被擋下，而不是被完整展開。
        EnsureDecompressedSizeWithinLimit(content, limits.MaxDecompressedBytes);

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

                sheets[name] = ReadSheet(worksheet, limits);
            }

            return sheets;
        }
    }

    private static void EnsureDecompressedSizeWithinLimit(byte[] content, long maxDecompressedBytes)
    {
        long total = 0;
        try
        {
            using var container = new MemoryStream(content, writable: false);
            using var archive = new ZipArchive(container, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in archive.Entries)
            {
                total += entry.Length;
                if (total > maxDecompressedBytes)
                {
                    throw new ImportBatchParseException(
                        DomainErrorCodes.ImportFormatUnsupported,
                        $"The workbook expands to more than {maxDecompressedBytes / (1024 * 1024)} MB and was not opened.");
                }
            }
        }
        catch (InvalidDataException)
        {
            throw new ImportBatchParseException(
                DomainErrorCodes.ImportFormatUnsupported,
                "The workbook is not a valid XLSX container.");
        }
    }

    private static IReadOnlyList<string[]> ReadSheet(IXLWorksheet sheet, XlsxReadLimits limits)
    {
        // 只看有內容的範圍。純格式化的儲存格不算資料，但仍會撐大 Dimension——所以維度檢查看的是
        // 內容範圍，而解壓上限才是擋「格式化到極遠位置」的那一道。
        var used = sheet.RangeUsed(XLCellsUsedOptions.AllContents);
        if (used is null)
        {
            return [];
        }

        // 欄從 A 開始數而不是從「第一個有內容的欄」：Header 驗證比的是位置，一張從 B 欄開始的
        // 工作表本來就對不上模板。
        var lastColumn = used.LastColumn().ColumnNumber();
        var lastRow = used.LastRow().RowNumber();
        var firstRow = used.FirstRow().RowNumber();

        // 在配置任何陣列之前先拒絕超出上限的維度：一格放在第 100 萬列的工作表，used range 就是
        // 100 萬列，逐列展開等於替攻擊者配置那麼多陣列。
        if (lastRow - firstRow + 1 > limits.MaxRowsPerSheet)
        {
            throw new ImportBatchParseException(
                DomainErrorCodes.ImportFormatUnsupported,
                $"Sheet '{sheet.Name}' spans {lastRow - firstRow + 1} rows; a sheet may hold at most {limits.MaxRowsPerSheet - 1} data rows plus the header.");
        }

        if (lastColumn > limits.MaxColumnsPerSheet)
        {
            throw new ImportBatchParseException(
                DomainErrorCodes.ImportFormatUnsupported,
                $"Sheet '{sheet.Name}' uses {lastColumn} columns; at most {limits.MaxColumnsPerSheet} are accepted.");
        }

        var rows = new List<string[]>();
        for (var rowNumber = firstRow; rowNumber <= lastRow; rowNumber++)
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
