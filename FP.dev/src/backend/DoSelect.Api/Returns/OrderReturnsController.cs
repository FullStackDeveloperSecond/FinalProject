using DoSelect.Api.Common;
using DoSelect.Application.Returns;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Returns;

/// <summary>
/// Accepts both authenticated members and validated guests on the same route (no [Authorize]
/// can express that mix) — mirrors CartController's manual-resolution shape.
/// </summary>
[ApiController]
[Route("api/v1/orders/{orderId:guid}/returns")]
public sealed class OrderReturnsController : ControllerBase
{
    private readonly IReturnService _returnService;
    private readonly ReturnActorResolver _actorResolver;

    public OrderReturnsController(IReturnService returnService, ReturnActorResolver actorResolver)
    {
        _returnService = returnService;
        _actorResolver = actorResolver;
    }

    [HttpPost]
    public async Task<ActionResult<ReturnRequestDto>> Create(
        Guid orderId,
        [FromBody] CreateReturnRequestBody body,
        CancellationToken cancellationToken)
    {
        var actor = await _actorResolver.ResolveForOrderAsync(HttpContext, orderId, cancellationToken);
        if (actor is null)
        {
            return IdentityRequiredProblem();
        }

        try
        {
            var request = new CreateReturnRequest(body.Items, body.RequestReason, body.OrderRowVersion);
            var result = await _returnService.CreateAsync(actor, orderId, request, cancellationToken);
            return CreatedAtAction(
                nameof(ReturnsController.GetDetail),
                "Returns",
                new { id = result.PublicId },
                result);
        }
        catch (ReturnsWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    private ActionResult IdentityRequiredProblem()
    {
        var problem = ApiProblemDetailsFactory.Create(
            HttpContext,
            StatusCodes.Status404NotFound,
            ReturnsWriteException.ErrorCodes.ResourceNotFound,
            detail: "The referenced order was not found.");
        return NotFound(problem);
    }
}

public sealed record CreateReturnRequestBody(
    IReadOnlyList<CreateReturnItemLine> Items,
    string RequestReason,
    byte[] OrderRowVersion);
