using System.Globalization;
using System.IO.Compression;
using DoSelect.Application.Imports;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// 商品匯入模板（UC-IMPORT-01、A-07 頁「模板下載」）。
///
/// 標題列不是另外抄一份，而是直接引用
/// <see cref="ProductRowParser.Header"/>／<see cref="SkuRowParser.Header"/>／
/// <see cref="SpecificationRowParser.Header"/>——也就是上傳時真正拿來驗證的同一份常數。抄第二份的
/// 話，哪天契約改了而模板沒跟上，管理員會下載到一個通不過驗證的模板，還完全看不出哪裡錯。
/// </summary>
public sealed class ImportTemplateService : IImportTemplateService
{
    /// <summary>與 EfProductImportService.CurrentTemplateVersion 相同的版本號。</summary>
    public const int CurrentProductTemplateVersion = 1;

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
}
