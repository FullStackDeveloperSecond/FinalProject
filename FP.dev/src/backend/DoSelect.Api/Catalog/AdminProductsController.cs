using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Catalog;

[ApiController]
[Authorize(Policy = DoSelectPolicies.CatalogManager)]
[Route("api/v1/admin/products")]
public sealed class AdminProductsController : ControllerBase
{
    private readonly IProductAdminService _productAdminService;

    public AdminProductsController(IProductAdminService productAdminService)
    {
        _productAdminService = productAdminService;
    }

    [HttpGet]
    public async Task<ActionResult<PageResult<AdminProductSummaryDto>>> List(
        [FromQuery] AdminProductListRequest request,
        CancellationToken cancellationToken)
    {
        var query = new AdminProductQuery(
            request.Q,
            request.BrandCodes,
            request.CategoryCodes,
            request.Statuses,
            request.StockState,
            request.Sort,
            request.PageNumber,
            request.PageSize);

        try
        {
            var result = await _productAdminService.ListAsync(query, cancellationToken);
            return Ok(result);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminProductDetailDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var detail = await _productAdminService.GetByPublicIdAsync(id, cancellationToken);
        if (detail is null)
        {
            var problem = ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.ResourceNotFound);
            return NotFound(problem);
        }

        return Ok(detail);
    }

    [HttpPost]
    public async Task<ActionResult<AdminProductDetailDto>> Create(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _productAdminService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.PublicId }, created);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    /// <summary>
    /// UC-ADM-PROD-02 批次上架／下架／調價。動作名稱在路由上，白名單由
    /// <see cref="BulkProductActions"/> 決定；不在白名單的動作是 400 validation_failed，不是 404,
    /// 因為路由本身存在、是請求內容不合法。
    /// </summary>
    // 路由參數不能叫 `action`：那是 ASP.NET Core 路由的保留值（與 controller/action 的環境路由值
    // 撞名），整條路由會比對不到而回 404。URL 形狀不變，只換繫結名稱。
    [HttpPost("actions/{bulkAction}")]
    [ProducesResponseType<BulkProductActionResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<BulkProductActionResultDto>> ApplyBulkAction(
        string bulkAction,
        [FromBody] BulkProductActionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var result = await _productAdminService.ApplyBulkActionAsync(
                bulkAction,
                request,
                BuildAuditContext(),
                actorUserId,
                cancellationToken);
            return Ok(result);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    /// <summary>
    /// A-04 匯出。Query 與列表完全相同（Endpoint 目錄：「匯出沿用目前 Filter」），所以管理員
    /// 匯出的就是他當下看到的那一組商品——只是不分頁。
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<ActionResult> Export(
        [FromQuery] AdminProductExportRequest request,
        CancellationToken cancellationToken)
    {
        var query = new AdminProductQuery(
            request.Q,
            request.BrandCodes,
            request.CategoryCodes,
            request.Statuses,
            request.StockState,
            Sort: null,
            PageNumber: 1,
            PageSize: 1);

        try
        {
            var export = await _productAdminService.ExportAsync(
                query,
                request.Format ?? AdminProductExportFormats.Csv,
                cancellationToken);
            return File(export.Content, export.ContentType, export.FileName);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    private AuditRequestContext BuildAuditContext()
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString();
        return new AuditRequestContext(
            CorrelationIdMiddleware.GetCorrelationId(HttpContext),
            traceId,
            HttpContext.Connection.RemoteIpAddress);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AdminProductDetailDto>> Update(
        Guid id,
        [FromBody] UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _productAdminService.UpdateAsync(id, request, cancellationToken);
            return Ok(updated);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }
}

/// <summary>
/// 匯出的 Query 參數刻意與 <see cref="AdminProductListRequest"/> 的 Filter 欄位一字不差——差一個
/// 欄位就代表匯出的不是管理員正在看的那一組。差別只有它沒有分頁參數，多了 Format。
/// </summary>
public sealed class AdminProductExportRequest
{
    [StringLength(160)]
    public string? Q { get; init; }

    [MaxLength(20)]
    public IReadOnlyList<string>? BrandCodes { get; init; }

    [MaxLength(20)]
    public IReadOnlyList<string>? CategoryCodes { get; init; }

    [MaxLength(10)]
    public IReadOnlyList<string>? Statuses { get; init; }

    public string? StockState { get; init; }

    // 刻意沒有 Sort：匯出的排序固定用商品代碼，不跟著列表的排序走。規格寫的是「匯出沿用目前
    // Filter」而不是排序，而收下一個自己不遵守的參數只會讓呼叫端誤會。
    [StringLength(8)]
    public string? Format { get; init; }
}

public sealed class AdminProductListRequest
{
    [StringLength(160)]
    public string? Q { get; init; }

    [MaxLength(20)]
    public IReadOnlyList<string>? BrandCodes { get; init; }

    [MaxLength(20)]
    public IReadOnlyList<string>? CategoryCodes { get; init; }

    [MaxLength(10)]
    public IReadOnlyList<string>? Statuses { get; init; }

    public string? StockState { get; init; }

    public string? Sort { get; init; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
