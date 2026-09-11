using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.Admin.Accounts;

[ApiController]
[AllowAnonymous]
[Route("api/v1/admin/auth/invitations")]
public sealed class AdminInvitationsController(
    DoSelectDbContext db,
    UserManager<ApplicationUser> userManager,
    IAuditWriter auditWriter,
    TimeProvider clock) : ControllerBase
{
    [HttpPost("accept")]
    [EnableRateLimiting(RateLimitPolicies.AuthForgotPassword)]
    public async Task<IActionResult> Accept(
        [FromBody] AcceptAdminInvitationRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(
            row => row.PublicId == request.PublicId && row.AccountType == AccountType.Admin,
            cancellationToken);
        var profileActive = user is not null && await db.AdminProfiles
            .AnyAsync(row => row.UserId == user.Id && row.IsActive, cancellationToken);
        if (user is null || !profileActive || user.AccountStatus != AccountStatus.PendingEmailVerification)
            return InvalidInvitation();

        var reset = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!reset.Succeeded)
        {
            if (reset.Errors.Any(error => error.Code.Contains("InvalidToken", StringComparison.OrdinalIgnoreCase)))
                return InvalidInvitation();
            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["newPassword"] = reset.Errors.Select(error => error.Description).ToArray(),
            };
            return BadRequest(ApiProblemDetailsFactory.CreateValidation(HttpContext, ToModelState(errors)));
        }

        var previousStatus = user.AccountStatus;
        user.ConfirmEmail(clock.GetUtcNow().UtcDateTime);
        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
            return InvalidInvitation();

        auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            AuditActor.Create(AuditActorType.System, null, []),
            AuditActions.AdminInvitationAccept,
            AuditResourceTypes.AdminAccount,
            user.PublicId,
            AuditResult.Success,
            null,
            [AuditFieldChange.Code("accountStatus", previousStatus.ToString(), user.AccountStatus.ToString())],
            "admin_invitation_accepted",
            CorrelationIdMiddleware.GetCorrelationId(HttpContext),
            Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(),
            null,
            HttpContext.Connection.RemoteIpAddress));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok();
    }

    private ObjectResult InvalidInvitation() =>
        BadRequest(ApiProblemDetailsFactory.Create(
            HttpContext,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.AdminInvitationInvalid));

    private static Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary ToModelState(
        IReadOnlyDictionary<string, string[]> errors)
    {
        var modelState = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
        foreach (var (field, messages) in errors)
            foreach (var message in messages)
                modelState.AddModelError(field, message);
        return modelState;
    }
}

public sealed class AcceptAdminInvitationRequest
{
    public Guid PublicId { get; init; }
    [Required, StringLength(4_000, MinimumLength = 1)] public required string Token { get; init; }
    [Required, StringLength(128, MinimumLength = 12)] public required string NewPassword { get; init; }
}
