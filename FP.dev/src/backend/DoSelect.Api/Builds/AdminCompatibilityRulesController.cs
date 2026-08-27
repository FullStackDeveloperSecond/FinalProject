using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Application.Builds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Builds;

/// <summary>相容性規則後台設計.md: admin view/tune of the 5 warning thresholds, per-rule activation, and no-write rule testing.</summary>
[ApiController]
[Route("api/v1/admin/compatibility-rules")]
public sealed class AdminCompatibilityRulesController : ControllerBase
{
    private readonly ICompatibilityRuleAdminService _adminService;
    private readonly ISkuCompatibilityAttributeAdminService _skuAttributeAdminService;

    public AdminCompatibilityRulesController(
        ICompatibilityRuleAdminService adminService,
        ISkuCompatibilityAttributeAdminService skuAttributeAdminService)
    {
        _adminService = adminService;
        _skuAttributeAdminService = skuAttributeAdminService;
    }

    [HttpGet]
    [Authorize(Policy = DoSelectPolicies.CompatibilityRuleView)]
    public async Task<ActionResult<CompatibilityRuleListDto>> List(CancellationToken cancellationToken)
    {
        var result = await _adminService.ListAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPatch("{ruleCode}/warning-settings")]
    [Authorize(Policy = DoSelectPolicies.CompatibilityRuleManageWarnings)]
    public async Task<ActionResult<CompatibilityRuleAdminDto>> UpdateWarningSetting(
        string ruleCode,
        [FromBody] UpdateWarningSettingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminUserId(out var adminUserId))
        {
            return IdentityRequiredProblem();
        }

        try
        {
            var result = await _adminService.UpdateWarningSettingAsync(
                ruleCode, adminUserId, request, BuildAuditContext(), cancellationToken);
            return Ok(result);
        }
        catch (BuildWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    /// <summary>DEC-BATCH-026 (DEC-P311): replaces the old <c>POST {ruleCode}/actions/set-activation</c> route — no parallel legacy route is kept.</summary>
    [HttpPatch("{ruleCode}/activation")]
    [Authorize(Policy = DoSelectPolicies.CompatibilityRuleManageActivation)]
    public async Task<ActionResult<CompatibilityRuleAdminDto>> SetActivation(
        string ruleCode,
        [FromBody] SetRuleActivationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminUserId(out var adminUserId))
        {
            return IdentityRequiredProblem();
        }

        try
        {
            var result = await _adminService.SetActivationAsync(
                ruleCode, adminUserId, request, BuildAuditContext(), cancellationToken);
            return Ok(result);
        }
        catch (BuildWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost("test")]
    [Authorize(Policy = DoSelectPolicies.CompatibilityRuleTest)]
    public async Task<ActionResult<CompatibilityRuleTestResultDto>> Test(
        [FromBody] CompatibilityRuleTestRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminUserId(out var adminUserId))
        {
            return IdentityRequiredProblem();
        }

        try
        {
            var result = await _adminService.TestAsync(request, adminUserId, BuildAuditContext(), cancellationToken);
            return Ok(result);
        }
        catch (BuildWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    /// <summary>
    /// 組長 PR #34 review, item 4: the only real production read/write path for a SKU's
    /// multi-value compatibility facts — previously only test fixtures and the dev seeder wrote
    /// to these tables, so a real product entered through normal SKU admin flows could never
    /// escape `insufficientData`.
    /// </summary>
    [HttpGet("skus/{skuPublicId:guid}/attributes")]
    [Authorize(Policy = DoSelectPolicies.CompatibilityRuleView)]
    public async Task<ActionResult<SkuCompatibilityAttributesDto>> GetSkuAttributes(
        Guid skuPublicId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _skuAttributeAdminService.GetAsync(skuPublicId, cancellationToken);
            return Ok(result);
        }
        catch (BuildWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    /// <summary>Full-replace semantics (資源本身即是完整快照，不是差異合併) — Request must echo back the Sku's own RowVersion from the last read (round-4 review: reuses the Sku's existing concurrency token).</summary>
    [HttpPut("skus/{skuPublicId:guid}/attributes")]
    [Authorize(Policy = DoSelectPolicies.CompatibilityRuleManageWarnings)]
    public async Task<ActionResult<SkuCompatibilityAttributesDto>> SetSkuAttributes(
        Guid skuPublicId,
        [FromBody] SetSkuCompatibilityAttributesRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminUserId(out var adminUserId))
        {
            return IdentityRequiredProblem();
        }

        try
        {
            var result = await _skuAttributeAdminService.SetAsync(
                skuPublicId, adminUserId, request, BuildAuditContext(), cancellationToken);
            return Ok(result);
        }
        catch (BuildWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    private bool TryGetAdminUserId(out string adminUserId)
    {
        adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(adminUserId);
    }

    private AuditRequestContext BuildAuditContext()
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString();
        return new AuditRequestContext(
            CorrelationIdMiddleware.GetCorrelationId(HttpContext),
            traceId,
            HttpContext.Connection.RemoteIpAddress);
    }

    private ActionResult IdentityRequiredProblem()
    {
        var problem = ApiProblemDetailsFactory.Create(
            HttpContext,
            StatusCodes.Status400BadRequest,
            BuildWriteException.ErrorCodes.ValidationFailed,
            detail: "An admin session is required.");
        return BadRequest(problem);
    }
}
