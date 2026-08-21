using System.Security.Claims;
using DoSelect.Application.Common;
using DoSelect.Application.Support;
using DoSelect.Application.Support.Dtos;
using DoSelect.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Controllers;

/// <summary>
/// Customer-facing support ticket endpoints. Every action is scoped to the caller's own
/// tickets — a member can never see or affect another member's case (Actor Scope).
/// </summary>
[ApiController]
[Authorize(Policy = DoSelectPolicies.Member)]
[Route("api/v1/support-tickets")]
public sealed class SupportTicketsController : ControllerBase
{
    // A few tens of KB above the hard 10 MiB content cap for multipart boundary/header
    // overhead — the authoritative size check still happens in Application against the actual
    // file bytes; this is just a cheap framework-level backstop against oversized requests.
    private const long MultipartBodyLengthLimit = SupportAttachmentUploadLimits.MaxFileSizeBytes + 65_536;

    private readonly ISupportTicketService _service;
    private readonly ISupportAttachmentUploadService _attachmentUploadService;

    public SupportTicketsController(
        ISupportTicketService service,
        ISupportAttachmentUploadService attachmentUploadService)
    {
        _service = service;
        _attachmentUploadService = attachmentUploadService;
    }

    [HttpGet]
    public async Task<ActionResult<PageResult<SupportTicketSummaryDto>>> ListMine(
        [FromQuery] SupportTicketQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _service.ListMyTicketsAsync(GetMemberUserId(), query, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<SupportTicketDto>> Create(
        [FromBody] CreateSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(GetMemberUserId(), request, cancellationToken);
        return CreatedAtAction(nameof(GetDetail), new { id = result.PublicId }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SupportTicketDto>> GetDetail(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.GetDetailAsync(GetMemberUserId(), id, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/messages")]
    public async Task<ActionResult<SupportTicketDto>> AddMessage(
        Guid id,
        [FromBody] CreateSupportMessageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.AddMessageAsync(GetMemberUserId(), id, request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/attachments")]
    [RequestSizeLimit(MultipartBodyLengthLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = MultipartBodyLengthLimit, ValueCountLimit = 2)]
    public async Task<ActionResult<SupportAttachmentDto>> UploadAttachment(
        Guid id,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        // The IFormFile reference itself is only ever produced by ASP.NET Core's model binder
        // here; the underlying content is not read (OpenReadStream is not invoked) until the
        // Application service has already cleared ownership, ticket-state and count checks.
        var incoming = new IncomingAttachmentFile(
            file?.FileName ?? string.Empty,
            file?.ContentType ?? string.Empty,
            file?.Length,
            file is not null,
            () => file?.OpenReadStream() ?? Stream.Null);

        var result = await _attachmentUploadService.UploadAsync(GetMemberUserId(), id, incoming, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("{id:guid}/actions/cancel")]
    public async Task<ActionResult<SupportTicketDto>> Cancel(
        Guid id,
        [FromBody] CancelSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CancelAsync(GetMemberUserId(), id, request, cancellationToken);
        return Ok(result);
    }

    private string GetMemberUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated request is missing a NameIdentifier claim.");
}
