using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Application.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Imports;

/// <summary>
/// UC-ADM-INV-01 匯入（A-13 頁）。路由與形狀刻意與
/// <see cref="AdminProductImportsController"/> 對齊——同一位管理員在兩個匯入頁之間切換時，
/// 行為不該有無謂的差異。
///
/// Policy 是 InventoryAdjust.*（匯入暫存與庫存調整設計.md 的 API 契約表），不是 CatalogImport.*：
/// 改庫存與改型錄是不同的授權，能改商品資料的人不當然能盤點庫存。
/// </summary>
[ApiController]
[Route("api/v1/admin/inventory-imports")]
public sealed class AdminInventoryImportsController : ControllerBase
{
    // 單一檔案 10MB，加上 multipart 邊界與標頭的餘裕。真正的上限由服務對實際位元組把關。
    private const long MultipartBodyLengthLimit = 10 * 1024 * 1024 + 65_536;

    private const int DefaultRowsPageSize = 50;

    private readonly IInventoryImportService _service;

    public AdminInventoryImportsController(IInventoryImportService service)
    {
        _service = service;
    }

    [HttpPost("preview")]
    [Authorize(Policy = DoSelectPolicies.InventoryImportExecute)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<InventoryImportBatchDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [RequestSizeLimit(MultipartBodyLengthLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = MultipartBodyLengthLimit, ValueCountLimit = 2)]
    public async Task<ActionResult<InventoryImportBatchDto>> Preview(
        IFormFile? adjustmentsFile,
        [FromForm] int templateVersion,
        CancellationToken cancellationToken)
    {
        var request = new PreviewInventoryImportRequest(ToIncomingFile(adjustmentsFile), templateVersion);
        var result = await _service.PreviewAsync(request, GetAdminUserId(), cancellationToken);
        return StatusCode(StatusCodes.Status202Accepted, result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = DoSelectPolicies.InventoryImportReadAll)]
    public async Task<ActionResult<InventoryImportBatchDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/rows")]
    [Authorize(Policy = DoSelectPolicies.InventoryImportReadAll)]
    public async Task<ActionResult> GetRows(
        Guid id,
        [FromQuery] string? dataset,
        [FromQuery] bool errorsOnly,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        // pageSize 用 nullable 綁定：非 nullable 的 int 會把「沒送」變成 0，然後被服務的 1–200
        // 範圍檢查拒絕——一個不帶 pageSize 的普通 GET 就掛了（組長 PR #74 round-2 P2）。
        var result = await _service.GetRowsAsync(
            id,
            new ImportRowsQuery(dataset, errorsOnly, cursor, pageSize ?? DefaultRowsPageSize),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}/errors")]
    [Authorize(Policy = DoSelectPolicies.InventoryImportReadAll)]
    [Produces("text/csv")]
    public async Task<ActionResult> GetErrors(Guid id, CancellationToken cancellationToken)
    {
        var csv = await _service.GetErrorsCsvAsync(id, cancellationToken);
        return csv is null
            ? NotFound()
            : File(csv, "text/csv", $"inventory-import-{id}-errors.csv");
    }

    /// <summary>
    /// UC-ADM-INV-01 匯入確認 — 成功回 200 與提交摘要；重送回 409 import_already_committed；
    /// 超過 24 小時回 410 import_batch_expired；Preview 之後庫存被動過回 409 concurrency_conflict
    /// 並要求重新 Preview。
    /// </summary>
    [HttpPost("{id:guid}/actions/confirm")]
    [Authorize(Policy = DoSelectPolicies.InventoryImportExecute)]
    [ProducesResponseType<InventoryImportBatchDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone, "application/problem+json")]
    public async Task<ActionResult<InventoryImportBatchDto>> Confirm(
        Guid id,
        [FromBody] ConfirmInventoryImportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.ConfirmAsync(
            id, GetAdminUserId(), request.RowVersion, BuildAuditContext(), cancellationToken);
        return Ok(result);
    }

    private static IncomingImportFile ToIncomingFile(IFormFile? file) => new(
        file?.FileName ?? string.Empty,
        file?.ContentType ?? string.Empty,
        file?.Length,
        file is not null,
        () => file?.OpenReadStream() ?? Stream.Null);

    private string GetAdminUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private AuditRequestContext BuildAuditContext()
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString();
        return new AuditRequestContext(
            CorrelationIdMiddleware.GetCorrelationId(HttpContext),
            traceId,
            HttpContext.Connection.RemoteIpAddress);
    }
}
