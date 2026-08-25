using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Shipping;

/// <summary>
/// UC-ADM-SHIP-01. "{id}" in the route is the provider *code* (e.g. "StorePickup",
/// "HomeDelivery") — there's no separate stable "provider" identity that predates a specific
/// versioned profile row, see EfPackageLimitService's remarks.
/// </summary>
[ApiController]
[Route("api/v1/admin/shipping-providers/{id}/package-limit-versions")]
public sealed class AdminPackageLimitsController : ControllerBase
{
    private readonly IPackageLimitService _packageLimitService;

    public AdminPackageLimitsController(IPackageLimitService packageLimitService)
    {
        _packageLimitService = packageLimitService;
    }

    [HttpGet]
    [Authorize(Policy = DoSelectPolicies.ShippingRead)]
    public async Task<ActionResult<IReadOnlyList<PackageLimitVersionDto>>> List(
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _packageLimitService.ListAsync(id, cancellationToken));
        }
        catch (ArgumentOutOfRangeException)
        {
            var problem = ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, ShippingAdminErrorCodes.ValidationFailed,
                detail: $"Unknown providerCode '{id}'.");
            return BadRequest(problem);
        }
    }

    [HttpPost]
    [Authorize(Policy = DoSelectPolicies.ShippingManage)]
    public async Task<ActionResult<PackageLimitVersionDto>> CreateDraft(
        string id,
        [FromBody] CreatePackageLimitVersionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ProviderCode != id)
        {
            var mismatch = ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, ShippingAdminErrorCodes.ValidationFailed,
                detail: "Route providerCode and request body providerCode must match.");
            return BadRequest(mismatch);
        }

        try
        {
            var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var created = await _packageLimitService.CreateDraftAsync(request, actorUserId, cancellationToken);
            return CreatedAtAction(nameof(List), new { id }, created);
        }
        catch (ShippingAdminWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost("{versionId:guid}/actions/publish")]
    [Authorize(Policy = DoSelectPolicies.ShippingManage)]
    public async Task<ActionResult<PackageLimitVersionDto>> Publish(
        string id,
        Guid versionId,
        [FromBody] PublishPackageLimitVersionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var published = await _packageLimitService.PublishAsync(versionId, request, actorUserId, cancellationToken);
            return Ok(published);
        }
        catch (ShippingAdminWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }
}
