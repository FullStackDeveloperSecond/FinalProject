using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Shipping;

/// <summary>UC-ADM-SHIP-02. The GET .../batches/{id}/result.csv re-download endpoint from API Endpoint目錄 is not implemented here — see IBatchShipmentService's doc comment for why.</summary>
[ApiController]
[Authorize(Policy = DoSelectPolicies.OrderManager)]
[Route("api/v1/admin/shipments")]
public sealed class AdminShipmentsController : ControllerBase
{
    private readonly IBatchShipmentService _batchShipmentService;

    public AdminShipmentsController(IBatchShipmentService batchShipmentService)
    {
        _batchShipmentService = batchShipmentService;
    }

    [HttpPost("batches")]
    public async Task<ActionResult<BatchShipmentResultDto>> ShipBatch(
        [FromBody] BatchShipmentRequest request, CancellationToken cancellationToken)
    {
        var adminUserId = RequireAdminUserId();
        try
        {
            return Ok(await _batchShipmentService.ShipBatchAsync(request, adminUserId, DateTime.UtcNow, cancellationToken));
        }
        catch (ShippingWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    private string RequireAdminUserId()
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            throw new InvalidOperationException("An authenticated admin request must carry a NameIdentifier claim.");
        }

        return adminUserId;
    }
}
