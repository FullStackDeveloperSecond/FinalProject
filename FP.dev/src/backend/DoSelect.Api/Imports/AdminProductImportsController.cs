using System.Security.Claims;
using DoSelect.Api.Security;
using DoSelect.Api.Common;
using DoSelect.Application.Auditing;
using DoSelect.Application.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Imports;

/// <summary>
/// UC-IMPORT-01 商品匯入: Preview stages three CSV datasets into a 24-hour batch, the read
/// endpoints expose its status/rows/errors, and Confirm applies the whole batch in one SQL
/// transaction with a same-transaction central Audit entry (匯入暫存與庫存調整設計.md 商品匯入確認).
/// Only the batch's creator may confirm it.
/// </summary>
[ApiController]
[Route("api/v1/admin/product-imports")]
public sealed class AdminProductImportsController : ControllerBase
{
    // +65KiB slack for multipart boundary/header overhead across three parts — the authoritative
    // per-file 10MB check happens in EfProductImportService against the actual bytes; this is
    // just a cheap framework-level backstop against a wildly oversized request.
    private const long MultipartBodyLengthLimit = 3 * 10 * 1024 * 1024 + 65_536;

    private readonly IProductImportService _service;

    public AdminProductImportsController(IProductImportService service)
    {
        _service = service;
    }

    [HttpPost("preview")]
    [Authorize(Policy = DoSelectPolicies.CatalogImportExecute)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ProductImportBatchDto>(StatusCodes.Status202Accepted)]
    [RequestSizeLimit(MultipartBodyLengthLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = MultipartBodyLengthLimit, ValueCountLimit = 4)]
    public async Task<ActionResult<ProductImportBatchDto>> Preview(
        IFormFile? productsFile,
        IFormFile? skusFile,
        IFormFile? specificationsFile,
        [FromForm] int templateVersion,
        CancellationToken cancellationToken)
    {
        var request = new PreviewProductImportRequest(
            ToIncomingFile(productsFile),
            ToIncomingFile(skusFile),
            ToIncomingFile(specificationsFile),
            templateVersion);

        var result = await _service.PreviewAsync(request, GetAdminUserId(), cancellationToken);
        return StatusCode(StatusCodes.Status202Accepted, result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = DoSelectPolicies.CatalogImportReadAll)]
    public async Task<ActionResult<ProductImportBatchDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/rows")]
    [Authorize(Policy = DoSelectPolicies.CatalogImportReadAll)]
    public async Task<ActionResult> GetRows(
        Guid id,
        [FromQuery] string? dataset,
        [FromQuery] bool errorsOnly,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetRowsAsync(
            id,
            new ImportRowsQuery(dataset, errorsOnly, cursor, pageSize),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}/errors")]
    [Authorize(Policy = DoSelectPolicies.CatalogImportReadAll)]
    [Produces("text/csv")]
    public async Task<ActionResult> GetErrors(Guid id, CancellationToken cancellationToken)
    {
        var csv = await _service.GetErrorsCsvAsync(id, cancellationToken);
        return csv is null
            ? NotFound()
            : File(csv, "text/csv", $"product-import-{id}-errors.csv");
    }

    private static IncomingImportFile ToIncomingFile(IFormFile? file) => new(
        file?.FileName ?? string.Empty,
        file?.ContentType ?? string.Empty,
        file?.Length,
        file is not null,
        () => file?.OpenReadStream() ?? Stream.Null);

    /// <summary>UC-IMPORT-01 商品匯入確認 — 200 with the commit summary on success; 409
    /// import_already_committed on a re-send; 410 import_batch_expired past the 24-hour window;
    /// 409 import_validation_failed when the catalog drifted since Preview.</summary>
    [HttpPost("{id:guid}/actions/confirm")]
    [Authorize(Policy = DoSelectPolicies.CatalogImportExecute)]
    [ProducesResponseType<ProductImportBatchDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductImportBatchDto>> Confirm(
        Guid id,
        [FromBody] ConfirmProductImportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.ConfirmAsync(
            id, GetAdminUserId(), request.RowVersion, BuildAuditContext(), cancellationToken);
        return Ok(result);
    }

    private AuditRequestContext BuildAuditContext()
    {
        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString()
            ?? System.Diagnostics.ActivityTraceId.CreateRandom().ToString();
        return new AuditRequestContext(
            CorrelationIdMiddleware.GetCorrelationId(HttpContext),
            traceId,
            HttpContext.Connection.RemoteIpAddress);
    }

    private string GetAdminUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated request is missing a NameIdentifier claim.");
}
