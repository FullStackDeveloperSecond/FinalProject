using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using DoSelect.Application.Imports;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// 商品匯入模板（UC-IMPORT-01、A-07 頁「模板下載」；契約：「下載目前 XLSX／CSV 模板」）。
///
/// ZIP 裡同時有三份 CSV 與一份三張工作表的 XLSX（組長 PR #89 round 2 item 2）：選 XLSX 上傳的
/// 管理員要拿得到官方的 XLSX，而不是自己憑三份 CSV 湊一本。
///
/// 標題列不是另外抄一份，而是直接引用
/// <see cref="ProductRowParser.Header"/>／<see cref="SkuRowParser.Header"/>／
/// <see cref="SpecificationRowParser.Header"/>——也就是上傳時真正拿來驗證的同一份常數。CSV 與 XLSX
/// 兩種模板都引用它，抄第二份的話，哪天契約改了而模板沒跟上，管理員會下載到一個通不過驗證的
/// 模板，還完全看不出哪裡錯。
/// </summary>
public sealed class ImportTemplateService : IImportTemplateService
{
    /// <summary>與 EfProductImportService.CurrentTemplateVersion 相同的版本號。</summary>
    public const int CurrentProductTemplateVersion = 1;

    public const string WorkbookEntryName = "product-import-template.xlsx";

    public ImportTemplateDownload GetCurrentProductTemplate()
    {
        // 商品匯入是三個資料集三個檔，所以模板是一個 ZIP。單一 CSV 塞不下三組不同的標題列，而
        // 讓管理員自己拼三個檔比直接給一包更容易出錯。
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddCsv(archive, "products.csv", ProductRowParser.Header);
            AddCsv(archive, "skus.csv", SkuRowParser.Header);
            AddCsv(archive, "specifications.csv", SpecificationRowParser.Header);
            AddWorkbook(archive, WorkbookEntryName);
        }

        var version = CurrentProductTemplateVersion.ToString(CultureInfo.InvariantCulture);
        return new ImportTemplateDownload(
            $"doselect-product-import-template-v{version}.zip",
            "application/zip",
            buffer.ToArray());
    }

    /// <summary>
    /// 只寫標題列，不放範例資料：範例值一旦被管理員原樣送出就是髒資料，而且範例會過期
    /// （引用到已停用的品牌代碼之類的）。DelimitedTextWriter 是上傳端解析的對應寫入器，所以
    /// 引號、換行與 UTF-8 BOM 的處理與驗證端一致。
    /// </summary>
    private static void AddCsv(ZipArchive archive, string entryName, IReadOnlyList<string> header)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var content = DelimitedTextWriter.Write(header, []);
        stream.Write(content, 0, content.Length);
    }

    /// <summary>
    /// 三張固定名稱的工作表（與 <see cref="EfProductImportService.WorkbookSheetNames"/> 相同），
    /// 各自只有標題列。工作表名稱與上傳端的讀取器對同一份常數，少一張或改名都會在上傳時被拒。
    /// </summary>
    private static void AddWorkbook(ZipArchive archive, string entryName)
    {
        using var workbook = new XLWorkbook();
        WriteHeader(workbook.Worksheets.Add(EfProductImportService.WorkbookSheetNames[0]), ProductRowParser.Header);
        WriteHeader(workbook.Worksheets.Add(EfProductImportService.WorkbookSheetNames[1]), SkuRowParser.Header);
        WriteHeader(workbook.Worksheets.Add(EfProductImportService.WorkbookSheetNames[2]), SpecificationRowParser.Header);

        using var workbookBytes = new MemoryStream();
        workbook.SaveAs(workbookBytes);

        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        workbookBytes.Position = 0;
        workbookBytes.CopyTo(stream);
    }

    private static void WriteHeader(IXLWorksheet sheet, IReadOnlyList<string> header)
    {
        for (var column = 0; column < header.Count; column++)
        {
            sheet.Cell(1, column + 1).Value = header[column];
        }
    }

}
