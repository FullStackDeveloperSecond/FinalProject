using DoSelect.Application.Reviews;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Reviews;

[ApiController]
[Route("api/v1/products/{productId:guid}/reviews")]
public sealed class PublicProductReviewsController(IReviewService reviewService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicProductReviewDto>>> List(
        Guid productId,
        CancellationToken cancellationToken) =>
        Ok(await reviewService.ListPublicAsync(productId, cancellationToken));
}
