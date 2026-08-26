using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Security;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Admin.Auth;

/// <summary>
/// 管理員登入（M-01B）：帳密登入、TOTP 驗證、Recovery Code 兌換，以及規格未列出但
/// 流程必須有的首次 TOTP 綁定（totp/enroll/begin、totp/enroll/confirm，⚠ 已於 PR 中標註）。
/// </summary>
[ApiController]
[Route("api/v1/admin/auth")]
public sealed class AdminAuthController(
    AdminLoginUseCase loginUseCase,
    AdminTwoFactorUseCase twoFactorUseCase,
    IAdminAuthGateway authGateway,
    IAdminSecurityAuditWriter auditWriter,
    IAdminChallengeRateLimiter rateLimiter,
    DoSelectDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider,
    ILogger<AdminAuthController> logger) : ControllerBase
{
    [HttpGet("session")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthSessionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthSessionDto>> GetSession(CancellationToken cancellationToken)
    {
        var adminAuth = await HttpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.Admin);
        if (adminAuth.Succeeded && adminAuth.Principal is not null)
        {
            var userId = adminAuth.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = userId is null ? null : await authGateway.FindAdminByIdAsync(userId, cancellationToken);
            if (user is not null)
            {
                return Ok(new AuthSessionDto(
                    true,
                    ToCurrentUserDto(user),
                    adminAuth.Properties?.ExpiresUtc,
                    null));
            }
        }

        // 未登入或未完成 2FA 均不回管理權限；若有進行中的挑戰，仍只回 requiresTwoFactor 旗標。
        var challengeAuth = await HttpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.AdminChallenge);
        var requiresTwoFactor = challengeAuth.Succeeded &&
            challengeAuth.Principal?.FindFirstValue(DoSelectClaimTypes.ChallengeKind) == "totp";

        return Ok(new AuthSessionDto(false, null, null, requiresTwoFactor ? true : null));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AdminLoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Login(
        [FromBody] AdminLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await loginUseCase.ExecuteAsync(request.Email, request.Password, cancellationToken);
        if (!result.IsSuccess)
        {
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, result.ErrorCode!));
        }

        var challengeKind = result.RequiresEnrollment ? "enroll" : "totp";
        var challengePublicId = Guid.CreateVersion7();
        await SignInChallengeAsync(result.UserId!, challengeKind, challengePublicId);

        return Ok(new AdminLoginResponseDto(result.RequiresTwoFactor, result.RequiresEnrollment, challengePublicId));
    }

    /// <summary>⚠ 新增端點：規格 4 端點沒有登出，但後台總要有登出功能。</summary>
    [HttpPost("logout")]
    [Authorize(Policy = DoSelectPolicies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(DoSelectAuthenticationSchemes.Admin);
        return NoContent();
    }

    [HttpPost("totp/verify")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AdminAuthResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyTotp(
        [FromBody] TotpVerifyRequest request, CancellationToken cancellationToken)
    {
        var challenge = await RequireChallengeAsync("totp", request.ChallengePublicId);
        if (challenge is null)
        {
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, AdminAuthErrorCodes.ChallengeInvalid));
        }

        if (!TryAcquireChallengeAttempt(challenge))
        {
            return await RejectChallengeRateLimitedAsync(challenge);
        }

        var result = await twoFactorUseCase.VerifyTotpAsync(challenge.UserId, request.Code, cancellationToken);
        if (!result.IsSuccess)
        {
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, result.ErrorCode!));
        }

        var authResult = await CompleteAdminSignInAsync(result.User!);
        return Ok(new AdminAuthResultDto(ToCurrentUserDto(result.User!), authResult));
    }

    [HttpPost("recovery-codes/use")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AdminAuthResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UseRecoveryCode(
        [FromBody] RecoveryCodeUseRequest request, CancellationToken cancellationToken)
    {
        var challenge = await RequireChallengeAsync("totp", request.ChallengePublicId);
        if (challenge is null)
        {
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, AdminAuthErrorCodes.ChallengeInvalid));
        }

        if (!TryAcquireChallengeAttempt(challenge))
        {
            return await RejectChallengeRateLimitedAsync(challenge);
        }

        var result = await twoFactorUseCase.RedeemRecoveryCodeAsync(challenge.UserId, request.Code, cancellationToken);
        if (!result.IsSuccess)
        {
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, result.ErrorCode!));
        }

        WriteAudit(AdminSecurityAuditEventType.RecoveryCodeRedeemed, challenge.UserId, null);

        var authResult = await CompleteAdminSignInAsync(result.User!);
        return Ok(new AdminAuthResultDto(ToCurrentUserDto(result.User!), authResult));
    }

    /// <summary>⚠ 新增端點：規格 4 端點沒有涵蓋，首次登入的管理員必須先綁定 TOTP 才能繼續。</summary>
    [HttpPost("totp/enroll/begin")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TotpEnrollBeginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BeginEnrollment(
        [FromQuery] Guid challengePublicId, CancellationToken cancellationToken)
    {
        var challenge = await RequireChallengeAsync("enroll", challengePublicId);
        if (challenge is null)
        {
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, AdminAuthErrorCodes.ChallengeInvalid));
        }

        var begin = await twoFactorUseCase.BeginEnrollmentAsync(challenge.UserId, cancellationToken);

        // ⚠ 首次建立 authenticator key 會經由 Identity 的 ResetAuthenticatorKeyAsync，
        // 副作用是 bump SecurityStamp——若不重新簽發這張 challenge cookie，等一下呼叫
        // enroll/confirm 時 RequireChallengeAsync 比對到的還是舊 Stamp，會被誤判成
        // 「Session 已撤銷」而回 admin_challenge_invalid，first-time enrollment 因此
        // 永遠無法完成（實測發現的真實 bug）。
        await SignInChallengeAsync(challenge.UserId, "enroll", challengePublicId);

        return Ok(new TotpEnrollBeginResponseDto(begin.SecretKey, begin.OtpAuthUri, begin.QrCodeDataUri));
    }

    /// <summary>
    /// ⚠ 新增端點：確認 TOTP 綁定，回傳僅顯示一次的 Recovery Code 並完成登入。
    /// 資格檢查（帳號是否仍 Active／AdminProfile 是否仍啟用）在啟用 2FA 之前就完成
    /// （見 AdminTwoFactorUseCase.ConfirmEnrollmentAsync），這裡的交易則保護「啟用
    /// 2FA」與「產生 Recovery Codes」兩個各自獨立的 Identity 呼叫本身的原子性——
    /// 任一失敗都不留下部分完成的 2FA 狀態（alex review 第二輪 P1#4）。
    /// </summary>
    [HttpPost("totp/enroll/confirm")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TotpEnrollConfirmResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmEnrollment(
        [FromBody] TotpEnrollConfirmRequest request, CancellationToken cancellationToken)
    {
        var challenge = await RequireChallengeAsync("enroll", request.ChallengePublicId);
        if (challenge is null)
        {
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, AdminAuthErrorCodes.ChallengeInvalid));
        }

        if (!TryAcquireChallengeAttempt(challenge))
        {
            return await RejectChallengeRateLimitedAsync(challenge);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var result = await twoFactorUseCase.ConfirmEnrollmentAsync(challenge.UserId, request.Code, cancellationToken);
        if (!result.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, result.ErrorCode!));
        }

        await transaction.CommitAsync(cancellationToken);
        WriteAudit(AdminSecurityAuditEventType.EnrollmentConfirmed, challenge.UserId, null);

        var authResult = await CompleteAdminSignInAsync(result.User!);
        return Ok(new TotpEnrollConfirmResponseDto(result.RecoveryCodes!, ToCurrentUserDto(result.User!), authResult));
    }

    /// <summary>
    /// ⚠ 新增端點：讓已登入管理員重新綁定 TOTP（例如換手機）。完成 Session 撤銷情境
    /// （UC-ADMIN-AUTH-01：「TOTP 重新綁定，既有 Session 失效」），見 ConfirmRebind。
    /// </summary>
    [HttpPost("totp/rebind/begin")]
    [Authorize(Policy = DoSelectPolicies.Admin)]
    [ProducesResponseType(typeof(TotpEnrollBeginResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> BeginRebind(CancellationToken cancellationToken)
    {
        var userId = RequireCurrentAdminUserId();
        var begin = await twoFactorUseCase.BeginRebindAsync(userId, cancellationToken);
        return Ok(new TotpEnrollBeginResponseDto(begin.SecretKey, begin.OtpAuthUri, begin.QrCodeDataUri));
    }

    /// <summary>
    /// 驗證新裝置算出的碼、重新產生 Recovery Code，接著 bump SecurityStamp 撤銷所有
    /// 其他既有 Session，並用新 Stamp 重新簽發「這個」請求所在的 Session（不會把自己登出）。
    /// 秘鑰提升、SecurityStamp 變更與稽核紀錄都在同一個交易內，任何一步失敗都整個回滾，
    /// 不會出現「Session 已撤銷但沒有稽核」或「稽核寫了但 Stamp 沒真的更新」的半成功狀態
    /// （alex review P1#5）。
    /// </summary>
    [HttpPost("totp/rebind/confirm")]
    [Authorize(Policy = DoSelectPolicies.Admin)]
    [ProducesResponseType(typeof(TotpEnrollConfirmResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmRebind(
        [FromBody] TotpRebindConfirmRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireCurrentAdminUserId();

        // Rebind 不是 Challenge Cookie 流程（已是完整登入的管理員），沒有 challengeId 可用，
        // 固定用 "rebind" 當作 challengeKey——限流仍依 IP＋帳號＋challengeKey 三個獨立
        // bucket 生效（alex review 第二輪 P1#2）。
        if (!rateLimiter.TryAcquire(GetClientIpAddress(), "rebind", userId))
        {
            WriteAudit(AdminSecurityAuditEventType.ChallengeInvalidatedRateLimit, userId, "rebind/confirm");

            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                ApiProblemDetailsFactory.Create(
                    HttpContext, StatusCodes.Status429TooManyRequests, AdminAuthErrorCodes.ChallengeRateLimited));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var result = await twoFactorUseCase.ConfirmRebindAsync(userId, request.Code, cancellationToken);
        if (!result.IsSuccess)
        {
            // Rollback 也復原了 ConfirmRebindAsync 內把待確認秘鑰提升為正式 key 的動作——
            // 舊裝置的 authenticator key 維持有效（見 IdentityAdminAuthGateway 的實作說明）。
            await transaction.RollbackAsync(cancellationToken);
            WriteAudit(AdminSecurityAuditEventType.RebindFailed, userId, result.ErrorCode);
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, result.ErrorCode!));
        }

        var applicationUser = await userManager.FindByIdAsync(userId);
        if (applicationUser is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, AdminAuthErrorCodes.TwoFactorInvalid));
        }

        var stampResult = await userManager.UpdateSecurityStampAsync(applicationUser);
        if (!stampResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, AdminAuthErrorCodes.TwoFactorInvalid));
        }

        await transaction.CommitAsync(cancellationToken);
        WriteAudit(AdminSecurityAuditEventType.RebindConfirmed, userId, null);

        logger.LogWarning(
            "Admin {AdminUserId} rebound TOTP; existing sessions on other devices are now revoked.",
            userId);

        var authResult = await CompleteAdminSignInAsync(result.User!);
        return Ok(new TotpEnrollConfirmResponseDto(result.RecoveryCodes!, ToCurrentUserDto(result.User!), authResult));
    }

    /// <summary>寫一筆結構化 Log 形式的安全事件（不落地資料表，見 IAdminSecurityAuditWriter）。</summary>
    private void WriteAudit(AdminSecurityAuditEventType eventType, string? userId, string? detail) =>
        auditWriter.Write(new AdminSecurityAuditEvent(
            eventType, userId, GetClientIpAddress(), detail, timeProvider.GetUtcNow()));

    /// <summary>`[Authorize(Policy = DoSelectPolicies.Admin)]` 已保證 User 是完整登入的管理員。</summary>
    private string RequireCurrentAdminUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated admin principal has no NameIdentifier claim.");

    /// <summary>
    /// 密碼驗證成功後簽發短效 AdminChallenge Cookie，代表「密碼已驗證、2FA 尚未完成」。
    /// 同時嵌入當下的 SecurityStamp——若這段期間管理員在別處被停權、被撤銷 Session 或
    /// 完成另一次 Rebind，Stamp 會變動，讓進行中的 challenge 自動失效（見
    /// RequireChallengeAsync；沿用 Admin Cookie 已驗證過的同一套撤銷機制，alex review P1#4）。
    /// </summary>
    private async Task SignInChallengeAsync(string userId, string challengeKind, Guid challengePublicId)
    {
        var identity = new ClaimsIdentity(DoSelectAuthenticationSchemes.AdminChallenge);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        identity.AddClaim(new Claim(DoSelectClaimTypes.ChallengeKind, challengeKind));
        identity.AddClaim(new Claim(DoSelectClaimTypes.ChallengeId, challengePublicId.ToString()));

        var applicationUser = await userManager.FindByIdAsync(userId);
        var securityStamp = applicationUser is null ? null : await userManager.GetSecurityStampAsync(applicationUser);
        if (!string.IsNullOrEmpty(securityStamp))
        {
            identity.AddClaim(new Claim(DoSelectClaimTypes.SecurityStamp, securityStamp));
        }

        await HttpContext.SignInAsync(
            DoSelectAuthenticationSchemes.AdminChallenge,
            new ClaimsPrincipal(identity));
    }

    /// <summary>
    /// 明確驗證 AdminChallenge Cookie（不透過 <c>[Authorize]</c> 觸發，見下方 ⚠ 註解），
    /// 確認屬於預期的 Kind 且 challengePublicId 相符。回傳使用者 Id；
    /// 不符合則回傳 null，呼叫端回 admin_challenge_invalid。
    /// </summary>
    /// <remarks>
    /// ⚠ 這 4 個 2FA 挑戰階段的端點刻意用 [AllowAnonymous] + 這裡手動 AuthenticateAsync，
    /// 不用 [Authorize(AuthenticationSchemes = AdminChallenge)]。原因：若用 [Authorize]，
    /// ASP.NET Core 會在 Authorization Middleware（早於 GlobalAntiforgeryFilter）就把
    /// HttpContext.User 換成 AdminChallenge principal；但取得 antiforgery token的
    /// SecurityController 端點是共用、匿名的，兩邊身分狀態不一致時 Antiforgery 的
    /// claim 綁定比對會失敗（實測發現的真實 bug）。改成全程以匿名身分讓 Antiforgery
    /// 驗證通過，實際授權改用這裡的 challengePublicId 比對——這組值只在登入回應中
    /// 揭露過一次，等同專屬於這次挑戰的防偽金鑰，安全性不因此下降。
    /// </remarks>
    private async Task<AdminChallengeContext?> RequireChallengeAsync(string expectedKind, Guid challengePublicId)
    {
        var challengeAuth = await HttpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.AdminChallenge);
        if (!challengeAuth.Succeeded || challengeAuth.Principal is null)
        {
            return null;
        }

        var principal = challengeAuth.Principal;
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var kind = principal.FindFirstValue(DoSelectClaimTypes.ChallengeKind);
        var idClaim = principal.FindFirstValue(DoSelectClaimTypes.ChallengeId);
        var stampClaim = principal.FindFirstValue(DoSelectClaimTypes.SecurityStamp);

        if (string.IsNullOrEmpty(userId) ||
            !string.Equals(kind, expectedKind, StringComparison.Ordinal) ||
            !Guid.TryParse(idClaim, out var challengeId) ||
            challengeId != challengePublicId)
        {
            return null;
        }

        // ⚠ 沒有 SecurityStamp claim 就略過檢查而不拒絕——理由跟 Admin Cookie 的
        // OnValidatePrincipal 一致：只是為了跟舊測試簽發相容，本次登入流程一定會設這個
        // claim（見上方 SignInChallengeAsync），撤銷檢查仍會生效。
        if (!string.IsNullOrEmpty(stampClaim))
        {
            var applicationUser = await userManager.FindByIdAsync(userId);
            var currentStamp = applicationUser is null
                ? null
                : await userManager.GetSecurityStampAsync(applicationUser);
            if (applicationUser is null || !string.Equals(stampClaim, currentStamp, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return new AdminChallengeContext(userId, challengeId);
    }

    /// <summary>解出的 AdminChallenge 內容：使用者 Id 與這次挑戰的公開識別碼。</summary>
    private sealed record AdminChallengeContext(string UserId, Guid ChallengeId);

    private string GetClientIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// 消耗一次 2FA 挑戰嘗試配額（alex review P1#3）。回傳 false 代表已超過門檻，
    /// 呼叫端應改呼叫 <see cref="RejectChallengeRateLimitedAsync"/>。
    /// </summary>
    private bool TryAcquireChallengeAttempt(AdminChallengeContext challenge) =>
        rateLimiter.TryAcquire(GetClientIpAddress(), challenge.ChallengeId.ToString(), challenge.UserId);

    /// <summary>超過嘗試上限：簽出 AdminChallenge（讓 challenge 立即失效）、寫入稽核、回 429。</summary>
    private async Task<IActionResult> RejectChallengeRateLimitedAsync(AdminChallengeContext challenge)
    {
        await HttpContext.SignOutAsync(DoSelectAuthenticationSchemes.AdminChallenge);
        WriteAudit(
            AdminSecurityAuditEventType.ChallengeInvalidatedRateLimit,
            challenge.UserId,
            $"challengeId={challenge.ChallengeId}");

        return StatusCode(
            StatusCodes.Status429TooManyRequests,
            ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status429TooManyRequests, AdminAuthErrorCodes.ChallengeRateLimited));
    }

    /// <summary>2FA 完成後：簽出 AdminChallenge、簽入完整 Admin Cookie，回傳實際到期時間。</summary>
    private async Task<DateTimeOffset> CompleteAdminSignInAsync(AdminAuthUserSnapshot user)
    {
        var principal = await BuildAdminPrincipalAsync(user);

        await HttpContext.SignOutAsync(DoSelectAuthenticationSchemes.AdminChallenge);
        await HttpContext.SignInAsync(DoSelectAuthenticationSchemes.Admin, principal);

        var authResult = await HttpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.Admin);
        return authResult.Properties?.ExpiresUtc ?? timeProvider.GetUtcNow().AddHours(2);
    }

    private async Task<ClaimsPrincipal> BuildAdminPrincipalAsync(AdminAuthUserSnapshot user)
    {
        var identity = new ClaimsIdentity(
            DoSelectAuthenticationSchemes.Admin, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.UserId));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Email));
        identity.AddClaim(new Claim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Admin));
        identity.AddClaim(new Claim(DoSelectClaimTypes.AuthenticationMethod, DoSelectClaimValues.MultiFactor));

        var applicationUser = await userManager.FindByIdAsync(user.UserId);
        var securityStamp = applicationUser is null
            ? null
            : await userManager.GetSecurityStampAsync(applicationUser);
        if (!string.IsNullOrEmpty(securityStamp))
        {
            identity.AddClaim(new Claim(DoSelectClaimTypes.SecurityStamp, securityStamp));
        }

        foreach (var role in user.Roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(identity);
    }

    private static CurrentUserDto ToCurrentUserDto(AdminAuthUserSnapshot user) =>
        new(
            user.PublicId,
            user.DisplayName,
            EmailMasking.Mask(user.Email),
            user.EmailConfirmed,
            LocaleCodes.ToCode(user.PreferredLocale),
            user.Roles);
}
