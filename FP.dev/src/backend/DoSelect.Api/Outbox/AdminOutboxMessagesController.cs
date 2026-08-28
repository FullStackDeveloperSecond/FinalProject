using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Outbox;
using DoSelect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.Outbox;

[ApiController]
[Authorize(Policy = DoSelectPolicies.OutboxRetry)]
[Route("api/v1/admin/outbox-messages")]
public sealed class AdminOutboxMessagesController : ControllerBase
{
    private readonly DoSelectDbContext _dbContext;
    private readonly IAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    public AdminOutboxMessagesController(
        DoSelectDbContext dbContext,
        IAuditWriter auditWriter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _dbContext = dbContext;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
    }

    [HttpPost("{publicId:guid}/actions/retry")]
    [ProducesResponseType<RetryOutboxMessageResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RetryOutboxMessageResponse>> Retry(
        Guid publicId,
        [FromBody] RetryOutboxMessageRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var admin = string.IsNullOrWhiteSpace(userId)
            ? null
            : await _dbContext.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
        if (admin is null)
        {
            return Problem(StatusCodes.Status401Unauthorized, ApiErrorCodes.AuthenticationRequired);
        }

        var message = await _dbContext.OutboxMessages
            .SingleOrDefaultAsync(candidate => candidate.PublicId == publicId, cancellationToken);
        if (message is null)
        {
            return Problem(StatusCodes.Status404NotFound, ApiErrorCodes.OutboxMessageNotFound);
        }

        if (message.Status != OutboxMessageStatus.Failed)
        {
            return Problem(StatusCodes.Status409Conflict, ApiErrorCodes.OutboxMessageNotRetryable);
        }

        AuditWriteRequest auditRequest;
        try
        {
            auditRequest = AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                AuditActor.Create(
                    AuditActorType.Admin,
                    admin.PublicId,
                    User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray()),
                AuditActions.OutboxRetry,
                AuditResourceTypes.OutboxMessage,
                message.PublicId,
                AuditResult.Success,
                errorCode: null,
                [AuditFieldChange.Code("status", "failed", "pending")],
                request.ReasonCode,
                CorrelationIdMiddleware.GetCorrelationId(HttpContext),
                Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(),
                jobPublicId: null,
                HttpContext.Connection.RemoteIpAddress);
        }
        catch (ArgumentException)
        {
            return Problem(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationFailed);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        message.RetryManually(now);
        _auditWriter.Add(auditRequest);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(StatusCodes.Status409Conflict, ApiErrorCodes.ConcurrencyConflict);
        }

        return Accepted(new RetryOutboxMessageResponse(
            message.PublicId,
            message.Status.ToString(),
            message.AvailableAtUtc));
    }

    private ObjectResult Problem(int statusCode, string code) =>
        StatusCode(statusCode, ApiProblemDetailsFactory.Create(HttpContext, statusCode, code));
}

public sealed class RetryOutboxMessageRequest
{
    [Required]
    [MaxLength(64)]
    [RegularExpression("^[A-Za-z0-9][A-Za-z0-9._:-]*$")]
    public required string ReasonCode { get; init; }
}

public sealed record RetryOutboxMessageResponse(
    Guid PublicId,
    string Status,
    DateTime AvailableAtUtc);
