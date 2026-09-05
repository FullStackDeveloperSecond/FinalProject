using DoSelect.Api.Security;
using DoSelect.Application.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Imports;

/// <summary>
/// UC-IMPORT-01 模板下載（Endpoint 目錄：
/// <c>GET /api/v1/admin/import-templates/products/current</c>）。A-07 頁的第一個動作——管理員得先
/// 拿到正確的標題列才有辦法準備上傳檔。
///
/// Policy 用 <c>CatalogImport.Read</c> 而不是 Execute：拿模板本身不寫入任何東西，能看匯入的人就
/// 該拿得到模板。
/// </summary>
[ApiController]
[Route("api/v1/admin/import-templates")]
public sealed class AdminImportTemplatesController : ControllerBase
{
    private readonly IImportTemplateService _templates;

    public AdminImportTemplatesController(IImportTemplateService templates)
    {
        _templates = templates;
    }

    [HttpGet("products/current")]
    [Authorize(Policy = DoSelectPolicies.CatalogImportRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public ActionResult GetCurrentProductTemplate()
    {
        var template = _templates.GetCurrentProductTemplate();
        return File(template.Content, template.ContentType, template.FileName);
    }
}
