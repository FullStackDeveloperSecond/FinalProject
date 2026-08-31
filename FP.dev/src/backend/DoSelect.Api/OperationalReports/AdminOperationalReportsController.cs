using DoSelect.Api.Security;
using DoSelect.Application.OperationalReports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.OperationalReports;

[ApiController]
[Authorize(Policy = DoSelectPolicies.OperationalReportView)]
[Route("api/v1/admin/reports")]
public sealed class AdminOperationalReportsController(
    IOperationalReportQueryService queryService,
    IOperationalReportCsvExporter csvExporter,
    IOperationalReportXlsxExporter xlsxExporter,
    IAuthorizationService authorizationService) : ControllerBase
{
    [HttpGet("{reportKey}")]
    [ProducesResponseType<ReportResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<ActionResult<ReportResultDto>> Get(
        string reportKey,
        [FromQuery] ReportQuery query,
        CancellationToken cancellationToken)
    {
        var definition = OperationalReportCatalog.Require(reportKey);
        if (!await MayViewAsync(definition))
        {
            return Forbid();
        }

        var validated = OperationalReportQueryValidator.Normalize(query);
        return Ok(await queryService.QueryAsync(definition, validated, cancellationToken));
    }

    [HttpGet("{reportKey}/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> Export(
        string reportKey,
        [FromQuery] ReportQuery query,
        CancellationToken cancellationToken)
    {
        var definition = OperationalReportCatalog.Require(reportKey);
        if (!await MayViewAsync(definition))
        {
            return Forbid();
        }

        var validated = OperationalReportQueryValidator.Normalize(query) with { Cursor = null };
        var export = await csvExporter.ExportAsync(definition, validated, cancellationToken);
        return File(export.Content, "text/csv; charset=utf-8", export.FileName);
    }

    [HttpGet("{reportKey}/export/xlsx")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> ExportXlsx(
        string reportKey,
        [FromQuery] ReportQuery query,
        CancellationToken cancellationToken)
    {
        var definition = OperationalReportCatalog.Require(reportKey);
        if (!await MayViewAsync(definition))
        {
            return Forbid();
        }

        var validated = OperationalReportQueryValidator.Normalize(query) with { Cursor = null };
        var export = await xlsxExporter.ExportAsync(definition, validated, cancellationToken);
        return File(
            export.Content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            export.FileName);
    }

    private async Task<bool> MayViewAsync(OperationalReportDefinition definition)
    {
        if (definition.Sensitivity != OperationalReportSensitivity.Financial)
        {
            return true;
        }

        var result = await authorizationService.AuthorizeAsync(
            User,
            resource: null,
            DoSelectPolicies.OperationalReportFinanceView);
        return result.Succeeded;
    }
}
