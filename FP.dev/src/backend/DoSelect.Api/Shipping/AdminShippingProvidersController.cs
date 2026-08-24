using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Shipping;

/// <summary>UC-ADM-SHIP-01. {providerId} is ProviderCode itself — see IPackageLimitVersionAdminService's routing-design comment.</summary>
[ApiController]
[Authorize(Policy = DoSelectPolicies.OrderManager)]
[Route("api/v1/admin/shipping-providers/{providerId}/package-limit-versions")]
public sealed class AdminShippingProvidersController : ControllerBase
{
    private readonly IPackageLimitVersionAdminService _adminService;

    public AdminShippingProvidersController(IPackageLimitVersionAdminService adminService)
    {
        _adminService = adminService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PackageLimitVersionDto>>> List(
        string providerId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _adminService.ListAsync(providerId, cancellationToken));
        }
        catch (ShippingWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost]
    public async Task<ActionResult<PackageLimitVersionDto>> CreateDraft(
        string providerId, [FromBody] CreatePackageLimitVersionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _adminService.CreateDraftAsync(providerId, request, DateTime.UtcNow, cancellationToken);
            return CreatedAtAction(nameof(List), new { providerId }, dto);
        }
        catch (ShippingWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost("{versionId:guid}/actions/publish")]
    public async Task<ActionResult<PackageLimitVersionDto>> Publish(
        string providerId, Guid versionId, [FromBody] PublishPackageLimitVersionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _adminService.PublishAsync(providerId, versionId, request, DateTime.UtcNow, cancellationToken));
        }
        catch (ShippingWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }
}
