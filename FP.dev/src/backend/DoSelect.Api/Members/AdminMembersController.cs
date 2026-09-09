using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Application.Members;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.Members;

[ApiController]
[Authorize(Policy = DoSelectPolicies.MemberView)]
[Route("api/v1/admin/members")]
public sealed class AdminMembersController(DoSelectDbContext db, IAuditWriter auditWriter, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AdminMemberPage>> List([FromQuery] AdminMemberQuery request, CancellationToken cancellationToken)
    {
        if (request.Status is not null && !Enum.IsDefined(request.Status.Value))
            return Error(400, ApiErrorCodes.ValidationFailed);
        var query = from profile in db.MemberProfiles.AsNoTracking()
                    join user in db.Users.AsNoTracking() on profile.UserId equals user.Id
                    where user.AccountType == AccountType.Member
                    select new { Profile = profile, User = user };
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(row => row.Profile.DisplayName.Contains(search) || (row.User.Email != null && row.User.Email.Contains(search)));
        }
        if (request.Status is { } status) query = query.Where(row => row.User.AccountStatus == status);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(row => row.Profile.CreatedAtUtc).ThenBy(row => row.Profile.PublicId)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(row => new { row.Profile.PublicId, row.Profile.DisplayName, row.User.Email, row.User.AccountStatus, row.User.EmailConfirmed, row.User.CreatedAtUtc, row.User.UpdatedAtUtc, row.User.RowVersion })
            .ToListAsync(cancellationToken);
        return Ok(new AdminMemberPage(rows.Select(row => new AdminMemberDto(row.PublicId, row.DisplayName,
            row.Email is null ? "—" : EmailMasking.Mask(row.Email), row.AccountStatus.ToString(), row.EmailConfirmed,
            row.CreatedAtUtc, row.UpdatedAtUtc, row.RowVersion)).ToArray(), total, request.Page, request.PageSize));
    }

    [HttpGet("{publicId:guid}")]
    public async Task<ActionResult<AdminMemberDto>> Detail(Guid publicId, CancellationToken cancellationToken)
    {
        var row = await (from profile in db.MemberProfiles.AsNoTracking()
                         join user in db.Users.AsNoTracking() on profile.UserId equals user.Id
                         where profile.PublicId == publicId && user.AccountType == AccountType.Member
                         select new { profile.PublicId, profile.DisplayName, user.Email, user.AccountStatus, user.EmailConfirmed, user.CreatedAtUtc, user.UpdatedAtUtc, user.RowVersion })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? Error(404, ApiErrorCodes.ResourceNotFound) : Ok(new AdminMemberDto(row.PublicId, row.DisplayName,
            row.Email is null ? "—" : EmailMasking.Mask(row.Email), row.AccountStatus.ToString(), row.EmailConfirmed,
            row.CreatedAtUtc, row.UpdatedAtUtc, row.RowVersion));
    }

    [HttpPost("{publicId:guid}/status")]
    [Authorize(Policy = DoSelectPolicies.MemberManage)]
    public async Task<IActionResult> ChangeStatus(Guid publicId, [FromBody] AdminMemberStatusRequest request, CancellationToken cancellationToken)
    {
        var profile = await db.MemberProfiles.AsNoTracking().SingleOrDefaultAsync(row => row.PublicId == publicId, cancellationToken);
        if (profile is null) return Error(404, ApiErrorCodes.ResourceNotFound);
        var user = await db.Users.SingleOrDefaultAsync(row => row.Id == profile.UserId && row.AccountType == AccountType.Member, cancellationToken);
        if (user is null) return Error(404, ApiErrorCodes.ResourceNotFound);
        // Never turn email-unverified, anonymized or terminally disabled identities into active accounts.
        if (request.Active ? user.AccountStatus != AccountStatus.Suspended || !user.EmailConfirmed : user.AccountStatus != AccountStatus.Active)
            return Error(409, ApiErrorCodes.ConcurrencyConflict);
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == actorId && row.AccountType == AccountType.Admin, cancellationToken);
        if (actor is null) return Error(401, ApiErrorCodes.AuthenticationRequired);
        db.Entry(user).Property(row => row.RowVersion).OriginalValue = request.RowVersion;
        var previous = user.AccountStatus;
        var now = clock.GetUtcNow().UtcDateTime;
        if (request.Active) user.Reactivate(now); else user.Suspend(now);
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        auditWriter.Add(AuditWriteRequest.Create(Guid.CreateVersion7(),
            AuditActor.Create(AuditActorType.Admin, actor.PublicId, User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray()),
            request.Active ? AuditActions.MemberRestore : AuditActions.MemberSuspend, AuditResourceTypes.Member, publicId,
            AuditResult.Success, null, [AuditFieldChange.Code("status", previous.ToString(), user.AccountStatus.ToString())],
            request.ReasonCode, CorrelationIdMiddleware.GetCorrelationId(HttpContext),
            Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(), null, HttpContext.Connection.RemoteIpAddress));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Error(409, ApiErrorCodes.ConcurrencyConflict); }
        return NoContent();
    }

    private ObjectResult Error(int status, string code) => StatusCode(status, ApiProblemDetailsFactory.Create(HttpContext, status, code));
}

public sealed class AdminMemberQuery
{
    [StringLength(100)] public string? Search { get; init; }
    public AccountStatus? Status { get; init; }
    [Range(1, 1000000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}
public sealed record AdminMemberDto(Guid PublicId, string DisplayName, string EmailMasked, string Status, bool EmailVerified, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, byte[] RowVersion);
public sealed record AdminMemberPage(IReadOnlyList<AdminMemberDto> Items, int TotalCount, int Page, int PageSize);
public sealed class AdminMemberStatusRequest
{
    public bool Active { get; init; }
    [Required, MinLength(8), MaxLength(8)] public required byte[] RowVersion { get; init; }
    [Required, RegularExpression("^(user_request|policy_violation|resolved)$")] public required string ReasonCode { get; init; }
}
