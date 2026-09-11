using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Notifications;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoSelect.Api.Admin.Accounts;

[ApiController]
[Authorize(Policy = DoSelectPolicies.RoleAssignmentManage)]
[Route("api/v1/admin/administrators")]
public sealed class AdminAccountsController(
    DoSelectDbContext db,
    UserManager<ApplicationUser> userManager,
    IAuditWriter auditWriter,
    IEmailDispatchQueue emailDispatchQueue,
    IOptions<FrontendLinkOptions> frontendLinks,
    TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AdminAccountPage>> List(
        [FromQuery] AdminAccountQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Status is not null && !Enum.IsDefined(request.Status.Value))
            return Error(400, ApiErrorCodes.ValidationFailed);
        if (request.Role is not null && !DoSelectRoles.All.Contains(request.Role, StringComparer.Ordinal))
            return Error(400, ApiErrorCodes.AdminRoleInvalid);

        var query = from profile in db.AdminProfiles.AsNoTracking()
                    join user in db.Users.AsNoTracking() on profile.UserId equals user.Id
                    where user.AccountType == AccountType.Admin
                    select new { Profile = profile, User = user };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(row =>
                row.Profile.DisplayName.Contains(search) ||
                row.Profile.EmployeeCode.Contains(search) ||
                (row.User.Email != null && row.User.Email.Contains(search)));
        }
        if (request.Status is { } status)
            query = query.Where(row => row.User.AccountStatus == status);
        if (request.Role is { } role)
        {
            query = query.Where(row =>
                db.UserRoles.Any(userRole =>
                    userRole.UserId == row.User.Id &&
                    db.Roles.Any(identityRole =>
                        identityRole.Id == userRole.RoleId && identityRole.Name == role)));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(row => row.User.CreatedAtUtc)
            .ThenBy(row => row.User.PublicId)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(row => new AdminAccountRow(
                row.User.Id,
                row.User.PublicId,
                row.Profile.DisplayName,
                row.Profile.EmployeeCode,
                row.User.Email!,
                row.User.AccountStatus,
                row.User.EmailConfirmed,
                row.User.TwoFactorEnabled,
                row.User.CreatedAtUtc,
                row.User.UpdatedAtUtc,
                row.User.RowVersion))
            .ToListAsync(cancellationToken);

        var rolesByUserId = await ReadRolesAsync(rows.Select(row => row.UserId), cancellationToken);
        return Ok(new AdminAccountPage(
            rows.Select(row => ToDto(row, rolesByUserId.GetValueOrDefault(row.UserId, []))).ToArray(),
            total,
            request.Page,
            request.PageSize,
            DoSelectRoles.All));
    }

    [HttpGet("{publicId:guid}")]
    public async Task<ActionResult<AdminAccountDto>> Detail(Guid publicId, CancellationToken cancellationToken)
    {
        var row = await ReadRowAsync(publicId, cancellationToken);
        if (row is null)
            return Error(404, ApiErrorCodes.ResourceNotFound);
        var roles = await userManager.GetRolesAsync(await userManager.FindByIdAsync(row.UserId)
            ?? throw new InvalidOperationException("Administrator identity disappeared during the request."));
        return Ok(ToDto(row, roles.ToArray()));
    }

    [HttpPost]
    public async Task<ActionResult<AdminAccountDto>> Create(
        [FromBody] CreateAdminAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return Error(400, ApiErrorCodes.ValidationFailed);
        var roles = NormalizeRoles(request.Roles);
        if (roles is null)
            return Error(400, ApiErrorCodes.AdminRoleInvalid);
        if (roles.Contains(DoSelectRoles.SuperAdmin) && !request.ConfirmSuperAdmin)
            return Error(400, ApiErrorCodes.SuperAdminConfirmationRequired);

        var actor = await ResolveActorAsync(cancellationToken);
        if (actor is null)
            return Error(401, ApiErrorCodes.AuthenticationRequired);

        var email = request.Email.Trim();
        var employeeCode = request.EmployeeCode.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
            return Error(409, ApiErrorCodes.AdminEmailDuplicate);
        if (await db.AdminProfiles.AnyAsync(row => row.EmployeeCode == employeeCode, cancellationToken))
            return Error(409, ApiErrorCodes.AdminEmployeeCodeDuplicate);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var publicId = Guid.CreateVersion7();
        var user = ApplicationUser.CreateAdmin(publicId, email, now);
        user.ConfirmEmail(now);
        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return IdentityError(createResult, ApiErrorCodes.ValidationFailed);
        }

        db.AdminProfiles.Add(new AdminProfile(user.Id, publicId, employeeCode, request.DisplayName, now));
        var roleResult = await userManager.AddToRolesAsync(user, roles);
        if (!roleResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return IdentityError(roleResult, ApiErrorCodes.AdminRoleInvalid);
        }

        var changes = new List<AuditFieldChange>
        {
            AuditFieldChange.Code("accountStatus", null, AccountStatus.Active.ToString()),
            AuditFieldChange.Changed("role"),
        };
        AddAudit(actor.Value, AuditActions.AdminAccountCreate, publicId, changes, "admin_account_created");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreatedAtAction(nameof(Detail), new { publicId }, ToDto(user, request, roles));
    }

    [HttpPut("{publicId:guid}/roles")]
    public async Task<ActionResult<AdminAccountDto>> UpdateRoles(
        Guid publicId,
        [FromBody] UpdateAdminRolesRequest request,
        CancellationToken cancellationToken)
    {
        var requestedRoles = NormalizeRoles(request.Roles);
        if (requestedRoles is null)
            return Error(400, ApiErrorCodes.AdminRoleInvalid);

        var actor = await ResolveActorAsync(cancellationToken);
        if (actor is null)
            return Error(401, ApiErrorCodes.AuthenticationRequired);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var target = await db.Users.SingleOrDefaultAsync(
            row => row.PublicId == publicId && row.AccountType == AccountType.Admin,
            cancellationToken);
        if (target is null)
            return Error(404, ApiErrorCodes.ResourceNotFound);
        var profile = await db.AdminProfiles.SingleOrDefaultAsync(row => row.UserId == target.Id, cancellationToken);
        if (profile is null)
            return Error(404, ApiErrorCodes.ResourceNotFound);
        if (!target.RowVersion.AsSpan().SequenceEqual(request.RowVersion))
            return Error(409, ApiErrorCodes.ConcurrencyConflict);

        var currentRoles = (await userManager.GetRolesAsync(target)).Order(StringComparer.Ordinal).ToArray();
        var removesSuperAdmin = currentRoles.Contains(DoSelectRoles.SuperAdmin) &&
            !requestedRoles.Contains(DoSelectRoles.SuperAdmin);
        var addsSuperAdmin = !currentRoles.Contains(DoSelectRoles.SuperAdmin) &&
            requestedRoles.Contains(DoSelectRoles.SuperAdmin);
        if (addsSuperAdmin && !request.ConfirmSuperAdmin)
            return Error(400, ApiErrorCodes.SuperAdminConfirmationRequired);
        if (actor.Value.UserId == target.Id && removesSuperAdmin)
            return Error(409, ApiErrorCodes.SelfDemotionForbidden);
        if (removesSuperAdmin && await CountActiveSuperAdminsAsync(cancellationToken) <= 1)
            return Error(409, ApiErrorCodes.LastSuperAdminRequired);

        var additions = requestedRoles.Except(currentRoles, StringComparer.Ordinal).ToArray();
        var removals = currentRoles.Except(requestedRoles, StringComparer.Ordinal).ToArray();
        if (additions.Length == 0 && removals.Length == 0)
            return Ok(ToDto(target, profile, currentRoles));

        db.Entry(target).Property(row => row.RowVersion).OriginalValue = request.RowVersion;
        var removeResult = await userManager.RemoveFromRolesAsync(target, removals);
        if (!removeResult.Succeeded)
            return IdentityError(removeResult, ApiErrorCodes.ConcurrencyConflict);
        var addResult = await userManager.AddToRolesAsync(target, additions);
        if (!addResult.Succeeded)
            return IdentityError(addResult, ApiErrorCodes.ConcurrencyConflict);
        var stampResult = await userManager.UpdateSecurityStampAsync(target);
        if (!stampResult.Succeeded)
            return IdentityError(stampResult, ApiErrorCodes.ConcurrencyConflict);

        AuditFieldChange[] changes =
        [
            AuditFieldChange.Changed("role"),
            AuditFieldChange.Changed("securityStamp"),
        ];
        AddAudit(actor.Value, AuditActions.AdminRolesUpdate, publicId, changes, request.ReasonCode);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(ToDto(target, profile, requestedRoles));
    }

    [HttpPost("{publicId:guid}/actions/resend-invitation")]
    public async Task<IActionResult> ResendInvitation(
        Guid publicId,
        [FromBody] ResendAdminInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await ResolveActorAsync(cancellationToken);
        if (actor is null)
            return Error(401, ApiErrorCodes.AuthenticationRequired);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var target = await db.Users.SingleOrDefaultAsync(
            row => row.PublicId == publicId && row.AccountType == AccountType.Admin,
            cancellationToken);
        var profileActive = target is not null && await db.AdminProfiles
            .AnyAsync(row => row.UserId == target.Id && row.IsActive, cancellationToken);
        if (target is null || !profileActive)
            return Error(404, ApiErrorCodes.ResourceNotFound);
        if (target.AccountStatus != AccountStatus.PendingEmailVerification)
            return Error(409, ApiErrorCodes.RequestConflict);
        if (!target.RowVersion.AsSpan().SequenceEqual(request.RowVersion))
            return Error(409, ApiErrorCodes.ConcurrencyConflict);

        db.Entry(target).Property(row => row.RowVersion).OriginalValue = request.RowVersion;
        var stampResult = await userManager.UpdateSecurityStampAsync(target);
        if (!stampResult.Succeeded)
            return IdentityError(stampResult, ApiErrorCodes.ConcurrencyConflict);
        AddAudit(actor.Value, AuditActions.AdminInvitationResend, publicId,
            [AuditFieldChange.Changed("invitation"), AuditFieldChange.Changed("securityStamp")],
            "admin_invitation_resent");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await QueueInvitationAsync(target);
        return Accepted();
    }

    private async Task<Dictionary<string, IReadOnlyList<string>>> ReadRolesAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds.ToArray();
        var rows = await (from userRole in db.UserRoles.AsNoTracking()
                          join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                          where ids.Contains(userRole.UserId) && role.Name != null
                          orderby role.Name
                          select new { userRole.UserId, Role = role.Name! })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.UserId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(row => row.Role).ToArray());
    }

    private async Task<AdminAccountRow?> ReadRowAsync(Guid publicId, CancellationToken cancellationToken) =>
        await (from profile in db.AdminProfiles.AsNoTracking()
               join user in db.Users.AsNoTracking() on profile.UserId equals user.Id
               where user.PublicId == publicId && user.AccountType == AccountType.Admin
               select new AdminAccountRow(user.Id, user.PublicId, profile.DisplayName, profile.EmployeeCode,
                   user.Email!, user.AccountStatus, user.EmailConfirmed, user.TwoFactorEnabled,
                   user.CreatedAtUtc, user.UpdatedAtUtc, user.RowVersion))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<int> CountActiveSuperAdminsAsync(CancellationToken cancellationToken) =>
        await (from userRole in db.UserRoles.AsNoTracking()
               join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
               join user in db.Users.AsNoTracking() on userRole.UserId equals user.Id
               join profile in db.AdminProfiles.AsNoTracking() on user.Id equals profile.UserId
               where role.Name == DoSelectRoles.SuperAdmin &&
                     user.AccountType == AccountType.Admin &&
                     user.AccountStatus == AccountStatus.Active &&
                     profile.IsActive
               select user.Id).CountAsync(cancellationToken);

    private static string[]? NormalizeRoles(IReadOnlyCollection<string> roles)
    {
        var normalized = roles.Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return normalized.Length > 0 && normalized.All(role => DoSelectRoles.All.Contains(role, StringComparer.Ordinal))
            ? normalized
            : null;
    }

    private async Task<(string UserId, AuditActor Actor)?> ResolveActorAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var actor = userId is null ? null : await db.Users.AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == userId && row.AccountType == AccountType.Admin,
            cancellationToken);
        return actor is null ? null : (actor.Id, AuditActor.Create(
            AuditActorType.Admin,
            actor.PublicId,
            User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray()));
    }

    private void AddAudit(
        (string UserId, AuditActor Actor) actor,
        string action,
        Guid resourcePublicId,
        IReadOnlyCollection<AuditFieldChange> changes,
        string reason) =>
        auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(), actor.Actor, action, AuditResourceTypes.AdminAccount, resourcePublicId,
            AuditResult.Success, null, changes, reason,
            CorrelationIdMiddleware.GetCorrelationId(HttpContext),
            Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(),
            null, HttpContext.Connection.RemoteIpAddress));

    private async Task QueueInvitationAsync(ApplicationUser user)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var invitationLink = $"{frontendLinks.Value.AdminBaseUrl.TrimEnd('/')}/login/invitation" +
            $"#publicId={user.PublicId:D}&token={Uri.EscapeDataString(token)}";
        var encodedLink = HtmlEncoder.Default.Encode(invitationLink);
        emailDispatchQueue.Enqueue(new EmailMessage(
            user.Email!,
            "設定您的懂選管理員帳號",
            $"您已受邀成為懂選管理員。請於 1 小時內設定密碼：\n{invitationLink}\n\n若您不認識此邀請，請忽略此信。",
            $"<p>您已受邀成為懂選管理員。請於 1 小時內設定密碼：</p><p><a href=\"{encodedLink}\">設定管理員密碼</a></p><p>若您不認識此邀請，請忽略此信。</p>"));
    }

    private ObjectResult IdentityError(IdentityResult result, string fallbackCode)
    {
        var code = result.Errors.Any(error => error.Code.Contains("Concurrency", StringComparison.OrdinalIgnoreCase))
            ? ApiErrorCodes.ConcurrencyConflict
            : fallbackCode;
        var status = code == ApiErrorCodes.ConcurrencyConflict || code.Contains("duplicate", StringComparison.Ordinal)
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest;
        return Error(status, code);
    }

    private ObjectResult Error(int status, string code) =>
        StatusCode(status, ApiProblemDetailsFactory.Create(HttpContext, status, code));

    private static AdminAccountDto ToDto(AdminAccountRow row, IReadOnlyList<string> roles) =>
        new(row.PublicId, row.DisplayName, row.EmployeeCode, row.Email, row.Status.ToString(),
            row.EmailVerified, row.TwoFactorEnabled, roles, row.CreatedAtUtc, row.UpdatedAtUtc, row.RowVersion);

    private static AdminAccountDto ToDto(ApplicationUser user, CreateAdminAccountRequest request, IReadOnlyList<string> roles) =>
        new(user.PublicId, request.DisplayName.Trim(), request.EmployeeCode.Trim(), user.Email!, user.AccountStatus.ToString(),
            user.EmailConfirmed, user.TwoFactorEnabled, roles, user.CreatedAtUtc, user.UpdatedAtUtc, user.RowVersion);

    private static AdminAccountDto ToDto(ApplicationUser user, AdminProfile profile, IReadOnlyList<string> roles) =>
        new(user.PublicId, profile.DisplayName, profile.EmployeeCode, user.Email!, user.AccountStatus.ToString(),
            user.EmailConfirmed, user.TwoFactorEnabled, roles, user.CreatedAtUtc, user.UpdatedAtUtc, user.RowVersion);

    private sealed record AdminAccountRow(
        string UserId,
        Guid PublicId,
        string DisplayName,
        string EmployeeCode,
        string Email,
        AccountStatus Status,
        bool EmailVerified,
        bool TwoFactorEnabled,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        byte[] RowVersion);
}

public sealed class AdminAccountQuery
{
    [StringLength(100)] public string? Search { get; init; }
    public AccountStatus? Status { get; init; }
    [StringLength(64)] public string? Role { get; init; }
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed record AdminAccountDto(
    Guid PublicId,
    string DisplayName,
    string EmployeeCode,
    string Email,
    string Status,
    bool EmailVerified,
    bool TwoFactorEnabled,
    IReadOnlyList<string> Roles,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[] RowVersion);

public sealed record AdminAccountPage(
    IReadOnlyList<AdminAccountDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<string> AvailableRoles);

public sealed class CreateAdminAccountRequest
{
    [Required, EmailAddress, StringLength(320, MinimumLength = 3)] public required string Email { get; init; }
    [Required, StringLength(128, MinimumLength = 12)] public required string Password { get; init; }
    [Required, StringLength(100, MinimumLength = 1)] public required string DisplayName { get; init; }
    [Required, RegularExpression("^[A-Za-z0-9][A-Za-z0-9_-]{1,63}$")] public required string EmployeeCode { get; init; }
    [Required, MinLength(1)] public required IReadOnlyList<string> Roles { get; init; }
    public bool ConfirmSuperAdmin { get; init; }
}

public sealed class UpdateAdminRolesRequest
{
    [Required, MinLength(1)] public required IReadOnlyList<string> Roles { get; init; }
    [Required, MinLength(8), MaxLength(8)] public required byte[] RowVersion { get; init; }
    [Required, RegularExpression("^(job_change|staffing_change|permission_correction)$")]
    public required string ReasonCode { get; init; }
    public bool ConfirmSuperAdmin { get; init; }
}

public sealed class ResendAdminInvitationRequest
{
    [Required, MinLength(8), MaxLength(8)] public required byte[] RowVersion { get; init; }
}
