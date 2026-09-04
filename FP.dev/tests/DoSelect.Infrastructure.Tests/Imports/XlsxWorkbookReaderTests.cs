using ClosedXML.Excel;
using DoSelect.Application.Common;
using DoSelect.Infrastructure.Imports;

namespace DoSelect.Infrastructure.Tests.Imports;

/// <summary>
/// 組長 PR #89 round 2 item 4：10 MB 是壓縮後的大小，擋不住解壓炸彈，也擋不住「一格放在第 100 萬列」
/// 把使用範圍撐到巨大的工作表。這些上限都要在把工作表展開成陣列**之前**生效，所以直接對讀取器
/// 測，不經過資料庫。
/// </summary>
public sealed class XlsxWorkbookReaderTests
{
    private static readonly string[] Sheets = ["Products", "Skus", "Specifications"];

    [Fact]
    public void ReadSheets_ReadsHeaderAndDataRowsFromEverySheet()
    {
        var bytes = Workbook(book =>
        {
            var products = book.Worksheets.Add("Products");
            products.Cell(1, 1).Value = "product_key";
            products.Cell(1, 2).Value = "product_code";
            products.Cell(2, 1).Value = "PK1";
            products.Cell(2, 2).Value = 1500;
            book.Worksheets.Add("Skus").Cell(1, 1).Value = "sku_key";
            book.Worksheets.Add("Specifications").Cell(1, 1).Value = "sku_key";
        });

        var sheets = XlsxWorkbookReader.ReadSheets(bytes, Sheets);

        Assert.Equal(["product_key", "product_code"], sheets["Products"][0]);
        Assert.Equal(["PK1", "1500"], sheets["Products"][1]);
        Assert.Single(sheets["Skus"]);
    }

    /// <summary>一格放在第 200,000 列：使用範圍就是 200,000 列，展開前就要擋。</summary>
    [Fact]
    public void ReadSheets_RejectsASheetWhoseUsedRangeReachesFarBeyondTheRowLimit()
    {
        var bytes = Workbook(book =>
        {
            var products = book.Worksheets.Add("Products");
            products.Cell(1, 1).Value = "product_key";
            products.Cell(200_000, 1).Value = "stray";
            book.Worksheets.Add("Skus").Cell(1, 1).Value = "sku_key";
            book.Worksheets.Add("Specifications").Cell(1, 1).Value = "sku_key";
        });

        var exception = Assert.Throws<ImportBatchParseException>(() => XlsxWorkbookReader.ReadSheets(bytes, Sheets));

        Assert.Equal(DomainErrorCodes.ImportFormatUnsupported, exception.ErrorCode);
        Assert.Contains("rows", exception.Message);
    }

    [Fact]
    public void ReadSheets_RejectsASheetWithMoreDataRowsThanTheBatchAllows()
    {
        var bytes = Workbook(book =>
        {
            var products = book.Worksheets.Add("Products");
            products.Cell(1, 1).Value = "product_key";
            for (var row = 2; row <= 5_002; row++)
            {
                products.Cell(row, 1).Value = $"PK{row}";
            }

            book.Worksheets.Add("Skus").Cell(1, 1).Value = "sku_key";
            book.Worksheets.Add("Specifications").Cell(1, 1).Value = "sku_key";
        });

        var exception = Assert.Throws<ImportBatchParseException>(() => XlsxWorkbookReader.ReadSheets(bytes, Sheets));

        Assert.Equal(DomainErrorCodes.ImportFormatUnsupported, exception.ErrorCode);
    }

    /// <summary>剛好 5,000 筆資料列＋標題列是合法的上限，不能因為邊界算錯而擋掉正常檔。</summary>
    [Fact]
    public void ReadSheets_AcceptsExactlyFiveThousandDataRows()
    {
        var bytes = Workbook(book =>
        {
            var products = book.Worksheets.Add("Products");
            products.Cell(1, 1).Value = "product_key";
            for (var row = 2; row <= 5_001; row++)
            {
                products.Cell(row, 1).Value = $"PK{row}";
            }

            book.Worksheets.Add("Skus").Cell(1, 1).Value = "sku_key";
            book.Worksheets.Add("Specifications").Cell(1, 1).Value = "sku_key";
        });

        var sheets = XlsxWorkbookReader.ReadSheets(bytes, Sheets);

        Assert.Equal(5_001, sheets["Products"].Count);
    }

    [Fact]
    public void ReadSheets_RejectsASheetThatUsesMoreColumnsThanAnyTemplate()
    {
        var bytes = Workbook(book =>
        {
            var products = book.Worksheets.Add("Products");
            products.Cell(1, 1).Value = "product_key";
            products.Cell(1, 100).Value = "stray";
            book.Worksheets.Add("Skus").Cell(1, 1).Value = "sku_key";
            book.Worksheets.Add("Specifications").Cell(1, 1).Value = "sku_key";
        });

        var exception = Assert.Throws<ImportBatchParseException>(() => XlsxWorkbookReader.ReadSheets(bytes, Sheets));

        Assert.Equal(DomainErrorCodes.ImportFormatUnsupported, exception.ErrorCode);
        Assert.Contains("columns", exception.Message);
    }

    /// <summary>
    /// 解壓上限在**開啟工作簿之前**生效。用縮小的上限證明這一道真的在 ClosedXML 之前擋：
    /// 一個正常的小工作簿在 1 KB 的上限下也會被拒絕。
    /// </summary>
    [Fact]
    public void ReadSheets_RejectsAWorkbookWhoseDecompressedSizeExceedsTheLimitBeforeOpeningIt()
    {
        var bytes = Workbook(book =>
        {
            book.Worksheets.Add("Products").Cell(1, 1).Value = "product_key";
            book.Worksheets.Add("Skus").Cell(1, 1).Value = "sku_key";
            book.Worksheets.Add("Specifications").Cell(1, 1).Value = "sku_key";
        });
        var tinyLimit = XlsxReadLimits.Default with { MaxDecompressedBytes = 1024 };

        var exception = Assert.Throws<ImportBatchParseException>(() =>
            XlsxWorkbookReader.ReadSheets(bytes, Sheets, tinyLimit));

        Assert.Equal(DomainErrorCodes.ImportFormatUnsupported, exception.ErrorCode);
        Assert.Contains("expands", exception.Message);
    }

    [Fact]
    public void ReadSheets_RejectsAFormulaCell()
    {
        var bytes = Workbook(book =>
        {
            var products = book.Worksheets.Add("Products");
            products.Cell(1, 1).Value = "product_key";
            products.Cell(2, 1).FormulaA1 = "=1+1";
            book.Worksheets.Add("Skus").Cell(1, 1).Value = "sku_key";
            book.Worksheets.Add("Specifications").Cell(1, 1).Value = "sku_key";
        });

        var exception = Assert.Throws<ImportBatchParseException>(() => XlsxWorkbookReader.ReadSheets(bytes, Sheets));

        Assert.Equal(DomainErrorCodes.ImportFormatUnsupported, exception.ErrorCode);
    }

    private static byte[] Workbook(Action<XLWorkbook> build)
    {
        using var workbook = new XLWorkbook();
        build(workbook);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
