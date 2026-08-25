using DoSelect.Api.Common;
using DoSelect.Application.Files;
using DoSelect.Application.Returns;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Returns;

/// <summary>Accepts both authenticated members and validated guests — same manual-resolution
/// shape as OrderReturnsController/CartController; no [Authorize] can express that mix.</summary>
[ApiController]
[Route("api/v1/returns")]
public sealed class ReturnsController : ControllerBase
{
    private readonly IReturnService _returnService;
    private readonly ReturnActorResolver _actorResolver;

    public ReturnsController(IReturnService returnService, ReturnActorResolver actorResolver)
    {
        _returnService = returnService;
        _actorResolver = actorResolver;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReturnRequestDto>> GetDetail(Guid id, CancellationToken cancellationToken)
    {
        var actor = await _actorResolver.ResolveAsync(HttpContext, cancellationToken);
        if (actor is null)
        {
            return IdentityRequiredProblem();
        }

        try
        {
            var result = await _returnService.GetDetailAsync(actor, id, cancellationToken);
            return Ok(result);
        }
        catch (ReturnsWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    [HttpPost("{id:guid}/attachments")]
    [RequestSizeLimit(PrivateFileConstraints.MaximumFileSizeBytes)]
    public async Task<ActionResult<ReturnAttachmentDto>> UploadAttachment(
        Guid id,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var actor = await _actorResolver.ResolveAsync(HttpContext, cancellationToken);
        if (actor is null)
        {
            return IdentityRequiredProblem();
        }

        if (file is null || file.Length == 0)
        {
            var problem = ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                ReturnsWriteException.ErrorCodes.ValidationFailed,
                detail: "A file is required.");
            return BadRequest(problem);
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var upload = new PrivateFileUpload(stream, file.FileName, file.ContentType);
            var result = await _returnService.UploadAttachmentAsync(actor, id, upload, cancellationToken);
            return Ok(result);
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
            detail: "The return request was not found.");
        return NotFound(problem);
    }
}
