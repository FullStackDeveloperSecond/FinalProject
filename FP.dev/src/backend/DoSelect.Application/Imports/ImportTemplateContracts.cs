namespace DoSelect.Application.Imports;

/// <summary>下載用的匯入模板：檔名、內容型別與位元組。</summary>
public sealed record ImportTemplateDownload(string FileName, string ContentType, byte[] Content);

/// <summary>
/// UC-IMPORT-01 的模板下載（Endpoint 目錄：
/// <c>GET /api/v1/admin/import-templates/products/current</c>；A-07 頁的第一個動作）。
///
/// 商品匯入是三個資料集，所以模板是一個 ZIP，內含三個各自帶標題列的 CSV，以及一份三張工作表的
/// XLSX（契約寫的是「XLSX／CSV 模板」，兩種上傳格式的管理員都要拿得到官方模板）。標題列直接取自
/// Parser 用來驗證上傳檔的同一份常數——否則「下載下來的模板通不過自己的驗證」這種事遲早會發生，
/// 而且會發生在管理員身上而不是在測試裡。
/// </summary>
public interface IImportTemplateService
{
    /// <summary>目前版本的商品匯入模板。</summary>
    ImportTemplateDownload GetCurrentProductTemplate();
}
