using System.Security.Claims;
using DoSelect.Api.Security;
using DoSelect.Application.Files;
using DoSelect.Application.Reviews;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Reviews;

[ApiController]
[Route("api/v1/reviews")]
[Authorize(Policy = DoSelectPolicies.Member)]
public sealed class ReviewsController(IReviewService reviewService) : ControllerBase
{
    [HttpGet("eligible-order-items")]
    public async Task<ActionResult<IReadOnlyList<EligibleReviewOrderItemDto>>> EligibleOrderItems(
        CancellationToken cancellationToken) =>
        Ok(await reviewService.ListEligibleOrderItemsAsync(MemberUserId(), cancellationToken));

    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<MemberReviewDto>>> Mine(
        CancellationToken cancellationToken) =>
        Ok(await reviewService.ListMineAsync(MemberUserId(), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<MemberReviewDto>> Create(
        [FromBody] CreateReviewRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await reviewService.CreateAsync(MemberUserId(), request, cancellationToken);
            return CreatedAtAction(nameof(Mine), result);
        }
        catch (ReviewWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MemberReviewDto>> Update(
        Guid id,
        [FromBody] UpdateReviewRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await reviewService.UpdateAsync(MemberUserId(), id, request, cancellationToken));
        }
        catch (ReviewWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost("{id:guid}/actions/submit")]
    public async Task<ActionResult<MemberReviewDto>> Submit(
        Guid id,
        [FromBody] ReviewRowVersionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await reviewService.SubmitAsync(MemberUserId(), id, request, cancellationToken));
        }
        catch (ReviewWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Withdraw(
        Guid id,
        [FromQuery] string rowVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            await reviewService.WithdrawAsync(MemberUserId(), id, rowVersion, cancellationToken);
            return NoContent();
        }
        catch (ReviewWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost("{id:guid}/images")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ReviewLimits.MaximumImageSizeBytes + 65_536)]
    public async Task<ActionResult<MemberReviewDto>> UploadImage(
        Guid id,
        IFormFile file,
        [FromForm] string rowVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = file.OpenReadStream();
            var upload = new ProductImageUpload(stream, file.FileName, file.ContentType);
            return Ok(await reviewService.UploadImageAsync(
                MemberUserId(), id, upload, file.Length, rowVersion, cancellationToken));
        }
        catch (ReviewWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpDelete("{id:guid}/images/{sortOrder:int}")]
    public async Task<ActionResult<MemberReviewDto>> DeleteImage(
        Guid id,
        int sortOrder,
        [FromQuery] string rowVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await reviewService.DeleteImageAsync(
                MemberUserId(), id, sortOrder, rowVersion, cancellationToken));
        }
        catch (ReviewWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    private string MemberUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
}
