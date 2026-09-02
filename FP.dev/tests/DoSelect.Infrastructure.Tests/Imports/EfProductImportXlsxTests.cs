using ClosedXML.Excel;
using DoSelect.Application.Common;
using DoSelect.Application.Imports;
using DoSelect.Domain.Imports;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Imports;
using DoSelect.Infrastructure.Persistence;

namespace DoSelect.Infrastructure.Tests.Imports;

/// <summary>
/// 組長 PR #89 item 6（裁定 A1）：XLSX 商品匯入與三份 CSV 對等。對等不是靠兩套驗證寫得一樣，而是
/// 工作表讀成字串列之後走同一組 Parser——這裡用同一份資料分別走兩條路，逐列比對暫存下來的
/// 正規化 payload。
/// </summary>
[Collection(nameof(ImportServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfProductImportXlsxTests
{
    private const string ProductsHeader = "product_key,product_code,name_zh_tw,brand_code,category_code,description_zh_tw,warranty_months,status\r\n";
    private const string SkusHeader = "sku_key,sku_code,product_key,name_zh_tw,list_price,unit_cost,weight_kg,length_cm,width_cm,height_cm,requires_prepayment,status\r\n";
    private const string SpecificationsHeader = "sku_key,semantic_key,value_type,string_value,decimal_value,boolean_value,option_code\r\n";

    [Fact]
    public async Task PreviewAsync_WithAWorkbook_StagesExactlyWhatTheThreeCsvsStage()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var csvAdmin = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var xlsxAdmin = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = CreateService(context);

        var productCode = ImportServiceFixture.UniqueCode("PROD");
        var skuCode = ImportServiceFixture.UniqueCode("SKU");
        var productRow = new[] { "PK1", productCode, "對等測試商品", brand.Code, category.Code, "\\N", "24", "Draft" };
        var skuRow = new[] { "SK1", skuCode, "PK1", "對等測試SKU", "1500", "900.5", "\\N", "\\N", "\\N", "\\N", "false", "Draft" };

        var csvPreview = await service.PreviewAsync(new PreviewProductImportRequest(
            Csv(ProductsHeader + string.Join(",", productRow) + "\r\n"),
            Csv(SkusHeader + string.Join(",", skuRow) + "\r\n"),
            Csv(SpecificationsHeader),
            TemplateVersion: 1), csvAdmin, CancellationToken.None);

        // 數字欄在工作表裡是真正的數字儲存格，不是文字——這才是試算表工具實際會產生的東西。
        var workbook = Workbook(
            products: [ProductsHeader.TrimEnd('\r', '\n').Split(','), productRow],
            skus: [SkusHeader.TrimEnd('\r', '\n').Split(','), skuRow],
            specifications: [SpecificationsHeader.TrimEnd('\r', '\n').Split(',')],
            numericColumns: new Dictionary<string, int[]> { ["Skus"] = [4, 5] });
        var xlsxPreview = await service.PreviewAsync(new PreviewProductImportRequest(
            Empty(), Empty(), Empty(), TemplateVersion: 1, Xlsx(workbook)), xlsxAdmin, CancellationToken.None);

        Assert.Equal(csvPreview.Status, xlsxPreview.Status);
        Assert.Equal(ImportBatchStatus.Ready.ToString(), xlsxPreview.Status);
        Assert.Equal(csvPreview.RowCount, xlsxPreview.RowCount);
        Assert.Equal(csvPreview.NewCount, xlsxPreview.NewCount);
        Assert.Equal(csvPreview.ErrorCount, xlsxPreview.ErrorCount);

        var csvRows = await service.GetRowsAsync(csvPreview.PublicId, new ImportRowsQuery(null, false, null, 50), CancellationToken.None);
        var xlsxRows = await service.GetRowsAsync(xlsxPreview.PublicId, new ImportRowsQuery(null, false, null, 50), CancellationToken.None);
        Assert.Equal(csvRows.Items.Count, xlsxRows.Items.Count);
        foreach (var (fromCsv, fromXlsx) in csvRows.Items.Zip(xlsxRows.Items))
        {
            Assert.Equal(fromCsv.Dataset, fromXlsx.Dataset);
            Assert.Equal(fromCsv.ImportKey, fromXlsx.ImportKey);
            Assert.Equal(fromCsv.Action, fromXlsx.Action);
            Assert.Equal(fromCsv.ErrorCodes, fromXlsx.ErrorCodes);
            Assert.Equal(fromCsv.NormalizedPayloadJson, fromXlsx.NormalizedPayloadJson);
        }
    }

    [Fact]
    public async Task PreviewAsync_WithAWorkbookMissingASheet_RejectsWithDatasetMissing()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = CreateService(context);
        var workbook = Workbook(
            products: [ProductsHeader.TrimEnd('\r', '\n').Split(',')],
            skus: null,
            specifications: [SpecificationsHeader.TrimEnd('\r', '\n').Split(',')]);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.PreviewAsync(
            new PreviewProductImportRequest(Empty(), Empty(), Empty(), 1, Xlsx(workbook)), adminId, CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ImportDatasetMissing, exception.Code);
    }

    /// <summary>「公式不執行」：CSV 沒有公式這種輸入，XLSX 接受公式就多了一種 CSV 沒有的路。</summary>
    [Fact]
    public async Task PreviewAsync_WithAFormulaCell_RejectsWithFormatUnsupported()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = CreateService(context);
        var workbook = Workbook(
            products: [ProductsHeader.TrimEnd('\r', '\n').Split(',')],
            skus: [SkusHeader.TrimEnd('\r', '\n').Split(',')],
            specifications: [SpecificationsHeader.TrimEnd('\r', '\n').Split(',')],
            mutate: book => book.Worksheet("Products").Cell(2, 7).FormulaA1 = "=12*2");

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.PreviewAsync(
            new PreviewProductImportRequest(Empty(), Empty(), Empty(), 1, Xlsx(workbook)), adminId, CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ImportFormatUnsupported, exception.Code);
    }

    [Fact]
    public async Task PreviewAsync_WithBothAWorkbookAndCsvs_RejectsTheRequest()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = CreateService(context);
        var workbook = Workbook(
            products: [ProductsHeader.TrimEnd('\r', '\n').Split(',')],
            skus: [SkusHeader.TrimEnd('\r', '\n').Split(',')],
            specifications: [SpecificationsHeader.TrimEnd('\r', '\n').Split(',')]);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.PreviewAsync(
            new PreviewProductImportRequest(Csv(ProductsHeader), Csv(SkusHeader), Csv(SpecificationsHeader), 1, Xlsx(workbook)),
            adminId, CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ValidationFailed, exception.Code);
    }

    /// <summary>「檔案簽章檢查」：副檔名叫 .xlsx 的文字檔不是 XLSX。</summary>
    [Fact]
    public async Task PreviewAsync_WithATextFileNamedXlsx_RejectsWithFormatUnsupported()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = CreateService(context);
        var bytes = ImportServiceFixture.Utf8("this is not a workbook");

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.PreviewAsync(
            new PreviewProductImportRequest(Empty(), Empty(), Empty(), 1,
                new IncomingImportFile("catalog.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", bytes.Length, true, () => new MemoryStream(bytes))),
            adminId, CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ImportFormatUnsupported, exception.Code);
    }

    private static EfProductImportService CreateService(DoSelectDbContext context) =>
        new(context, new EfAuditWriter(context, TimeProvider.System));

    private static IncomingImportFile Csv(string csv)
    {
        var bytes = ImportServiceFixture.Utf8(csv);
        return new IncomingImportFile("upload.csv", "text/csv", bytes.Length, true, () => new MemoryStream(bytes));
    }

    private static IncomingImportFile Empty() =>
        new(string.Empty, string.Empty, null, false, () => Stream.Null);

    private static IncomingImportFile Xlsx(byte[] bytes) =>
        new("catalog.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", bytes.Length, true, () => new MemoryStream(bytes));

    /// <summary>三張固定名稱的工作表；null 表示刻意不建那一張。numericColumns 指定哪些欄寫成數字儲存格（1 起算）。</summary>
    private static byte[] Workbook(
        IReadOnlyList<string[]>? products,
        IReadOnlyList<string[]>? skus,
        IReadOnlyList<string[]>? specifications,
        IReadOnlyDictionary<string, int[]>? numericColumns = null,
        Action<XLWorkbook>? mutate = null)
    {
        using var workbook = new XLWorkbook();
        AddSheet(workbook, "Products", products, numericColumns);
        AddSheet(workbook, "Skus", skus, numericColumns);
        AddSheet(workbook, "Specifications", specifications, numericColumns);
        mutate?.Invoke(workbook);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void AddSheet(
        XLWorkbook workbook,
        string name,
        IReadOnlyList<string[]>? rows,
        IReadOnlyDictionary<string, int[]>? numericColumns)
    {
        if (rows is null)
        {
            return;
        }

        var sheet = workbook.Worksheets.Add(name);
        var numeric = numericColumns?.GetValueOrDefault(name) ?? [];
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            for (var column = 0; column < rows[rowIndex].Length; column++)
            {
                var cell = sheet.Cell(rowIndex + 1, column + 1);
                var text = rows[rowIndex][column];
                if (rowIndex > 0 && numeric.Contains(column + 1) && decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var number))
                {
                    cell.Value = number;
                }
                else
                {
                    cell.Value = text;
                }
            }
        }
    }
}
