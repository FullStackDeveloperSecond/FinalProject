using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Application.Security;
using DoSelect.Domain.Auditing;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
    IAuditWriter auditWriter,
    IAdminChallengeRateLimiter rateLimiter,
    DoSelectDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider,
    ILogger<AdminAuthController> logger) : ControllerBase
{
    // DEC-P296：Reason 是固定、跨事件共用的稽核分類碼，刻意不帶事件細節（細節放
    // ChangedFieldsJson／ErrorCode），也避免踩到 AuditFieldChange.RequireSafeCode 的禁用字清單
    // （例如 "totp"、"recovery"、"token" 都不能出現在 reason／errorCode 裡）。
    private const string AuditReasonCode = "admin_auth_state_change";

    /// <summary>
    /// Rebind step-up（BeginRebind）在拿到真正的 rebind challenge 之前就要限流；沒有
    /// challengePublicId 可用，用這個字首＋userId 當三桶限流的「challenge」維度。
    /// ⚠ alex review 第三輪 P2#3：原本是固定字串常數，跟「帳號」桶疊在一起看似三桶，
    /// 實際上等於「所有管理員共用同一個 challenge 桶」——管理員 A 用完額度後，帳號、
    /// Session、IP 都不同的管理員 B 也會一起被擋 15 分鐘。改成每帳號各自獨立（雖然因此
    /// 跟「帳號」桶用同一個 userId，實質只剩兩個真正獨立的維度：IP 與帳號），至少先修掉
    /// 「跨管理員互相拖累」這個立即可利用的問題。
    /// </summary>
    private const string RebindStepUpChallengeKeyPrefix = "rebind-step-up";

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

    /// <summary>
    /// ⚠ alex review：狀態碼依正式契約（API錯誤碼目錄.md）調整——invalid_credentials 回 401、
    /// 密碼正確後才可揭露的 account_suspended 回 403，取代原本一律 400。這裡開交易是為了
    /// RegisterFailedAttemptAsync 的鎖定寫入與觸發鎖定當下那筆中央 Audit 能同一交易邊界，
    /// Audit 失敗時鎖定也一併 rollback（alex review）。
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthLogin)]
    [ProducesResponseType(typeof(AdminLoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login(
        [FromBody] AdminLoginRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var result = await loginUseCase.ExecuteAsync(request.Email, request.Password, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.LockoutAuditResourcePublicId is { } lockedOutAdminPublicId)
            {
                // ⚠ alex review：這一刻的呼叫者是匿名的（連續猜密碼的人，可能是真正的管理員，
                // 也可能是攻擊者）——Actor 必須是 System，不能把被鎖定的管理員自己記成施暴的
                // Actor。也不能沿用 RecordAdminAudit（那個 overload 硬性把傳入的
                // AdminAuthUserSnapshot 同時當 Actor 與 Resource，且要求 Admin Actor 至少一個
                // 角色——零角色管理員被鎖定時會直接拋例外，讓鎖定寫入一併 rollback，鎖定機制
                // 形同虛設）。
                RecordSystemAdminAudit(
                    AuditActions.AdminAccountLockout,
                    lockedOutAdminPublicId,
                    AuditResult.Rejected,
                    AdminAuthErrorCodes.AccountLocked,
                    [AuditFieldChange.Changed("lockoutEnd")]);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var statusCode = result.ErrorCode == AdminAuthErrorCodes.AccountSuspended
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status401Unauthorized;
            return StatusCode(
                statusCode,
                ApiProblemDetailsFactory.Create(HttpContext, statusCode, result.ErrorCode!));
        }

        await transaction.CommitAsync(cancellationToken);

        var challengeKind = result.RequiresEnrollment ? "enroll" : "totp";
        var challengePublicId = Guid.CreateVersion7();
        await SignInChallengeAsync(result.UserId!, challengeKind, challengePublicId);

        return Ok(new AdminLoginResponseDto(result.RequiresTwoFactor, result.RequiresEnrollment, challengePublicId));
    }

    /// <summary>⚠ 新增端點：規格 4 端點沒有登出，但後台總要有登出功能。</summary>
    [HttpPost("logout")]
    [Authorize(Policy = DoSelectPolicies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(DoSelectAuthenticationSchemes.Admin);
        return NoContent();
    }

    [HttpPost("totp/verify")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AdminAuthResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
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
            return await RejectChallengeRateLimitedAsync(challenge, cancellationToken);
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

    /// <summary>
    /// ⚠ alex review P1#5：資格重驗＋兌換都在 AdminTwoFactorUseCase.RedeemRecoveryCodeAsync
    /// 完成（見該檔案），這裡的交易保護的是「兌換」與「稽核紀錄」的原子性——稽核寫入失敗時
    /// 這組 Recovery Code 的兌換也要一併回滾，不能讓使用者白白燒掉一組單次有效碼卻沒有
    /// 對應的可稽核紀錄（DEC-P296）。
    /// </summary>
    [HttpPost("recovery-codes/use")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AdminAuthResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
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
            return await RejectChallengeRateLimitedAsync(challenge, cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var result = await twoFactorUseCase.RedeemRecoveryCodeAsync(challenge.UserId, request.Code, cancellationToken);
        if (!result.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, result.ErrorCode!));
        }

        RecordAdminAudit(
            AuditActions.AdminRecoveryCodeRedeem,
            result.User!,
            AuditResult.Success,
            errorCode: null,
            [AuditFieldChange.Changed("recoveryCodesRemaining")]);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

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
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
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
            return await RejectChallengeRateLimitedAsync(challenge, cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var result = await twoFactorUseCase.ConfirmEnrollmentAsync(challenge.UserId, request.Code, cancellationToken);
        if (!result.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, result.ErrorCode!));
        }

        // ⚠ DEC-P296：稽核寫入必須跟「啟用 2FA」同一交易——寫在 CommitAsync 之前，Audit 失敗
        // 就讓整筆 2FA 啟用一併 rollback，不會出現「2FA 已啟用但完全沒有稽核紀錄」的狀態
        // （原本 WriteAudit 只是寫一般 log，且發生在 CommitAsync 之後，兩者不在同一原子邊界）。
        RecordAdminAudit(
            AuditActions.AdminTotpEnrollmentConfirm,
            result.User!,
            AuditResult.Success,
            errorCode: null,
            [AuditFieldChange.Code("twoFactorEnabled", "false", "true")]);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var authResult = await CompleteAdminSignInAsync(result.User!);
        return Ok(new TotpEnrollConfirmResponseDto(result.RecoveryCodes!, ToCurrentUserDto(result.User!), authResult));
    }

    /// <summary>
    /// ⚠ 新增端點：讓已登入管理員重新綁定 TOTP（例如換手機）。完成 Session 撤銷情境
    /// （UC-ADMIN-AUTH-01：「TOTP 重新綁定，既有 Session 失效」），見 ConfirmRebind。
    /// DEC-P297：光有既有 Admin Cookie 不夠——另外簽發一張跟登入流程同一套機制的短效
    /// （10 分鐘）、單次、綁定使用者的 Challenge，Confirm 必須附上同一組 ChallengePublicId
    /// 才算數（alex review P1#3：原本只驗 Admin Cookie，pending secret 沒有效期）。
    /// 最新一輪 review（裁定 A1）再指出：只驗 Admin Cookie 仍不足以簽發這張 rebind challenge——
    /// Session Cookie 被偷就能整套重綁走人。這裡改成必須先完成 step-up：驗證現有 TOTP，或消耗
    /// 一組 Recovery Code；兩者都沒有時不允許自助重綁（改走 SuperAdmin／人工安全重設流程）。
    /// Step-up 失敗一律 rollback，不會呼叫 BeginRebindAsync——不會建立或替換待確認秘鑰。
    /// 再一輪 review 又指出：光靠 `[EnableRateLimiting(AuthLogin)]`（單純 per-IP）不夠——Session
    /// 被偷後換 IP 就能對同一帳號無限猜舊 TOTP／Recovery Code，密碼 Lockout 也保護不到這個
    /// 端點。在驗證憑證「之前」重用既有三桶限流器（IP＋step-up 專用桶＋帳號），任一超限即拒絕
    /// 並寫中央 Audit，不建立 pending secret。
    /// </summary>
    [HttpPost("totp/rebind/begin")]
    [Authorize(Policy = DoSelectPolicies.Admin)]
    [EnableRateLimiting(RateLimitPolicies.AuthLogin)]
    [ProducesResponseType(typeof(TotpRebindBeginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> BeginRebind(
        [FromBody] TotpRebindBeginRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireCurrentAdminUserId();

        var hasTotpCode = !string.IsNullOrWhiteSpace(request.TotpCode);
        var hasRecoveryCode = !string.IsNullOrWhiteSpace(request.RecoveryCode);
        if (hasTotpCode == hasRecoveryCode)
        {
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, AdminAuthErrorCodes.RebindStepUpRequired));
        }

        // 三桶限流必須在驗證憑證之前消耗——跟 TryAcquireChallengeAttempt 同一套機制，這裡還
        // 沒有真正的 challenge（要 step-up 通過才會簽發），所以用固定的
        // RebindStepUpChallengeKey 當「challenge」維度；IP 與帳號兩個維度仍然各自獨立。
        if (!rateLimiter.TryAcquire(GetClientIpAddress(), $"{RebindStepUpChallengeKeyPrefix}:{userId}", userId))
        {
            return await RejectRebindStepUpRateLimitedAsync(userId, cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (hasRecoveryCode)
        {
            if (!await authGateway.RedeemRecoveryCodeAsync(userId, request.RecoveryCode!.Trim(), cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return BadRequest(ApiProblemDetailsFactory.Create(
                    HttpContext, StatusCodes.Status400BadRequest, AdminAuthErrorCodes.RecoveryCodeInvalid));
            }

            var user = await authGateway.FindAdminByIdAsync(userId, cancellationToken);
            if (user is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return BadRequest(ApiProblemDetailsFactory.Create(
                    HttpContext, StatusCodes.Status400BadRequest, AdminAuthErrorCodes.RecoveryCodeInvalid));
            }

            // 沿用既有 AdminRecoveryCodeRedeem action——語意上跟登入流程兌換 Recovery Code是
            // 同一件事：單次有效的救援碼被消耗掉一組，必須同一交易寫入中央 Audit。
            RecordAdminAudit(
                AuditActions.AdminRecoveryCodeRedeem,
                user,
                AuditResult.Success,
                errorCode: null,
                [AuditFieldChange.Changed("recoveryCodesRemaining")]);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (!await authGateway.VerifyTotpCodeAsync(userId, request.TotpCode!.Trim(), cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, AdminAuthErrorCodes.TwoFactorInvalid));
        }

        // Step-up 通過後才建立待確認秘鑰——驗證失敗的分支都已經在上面 return，不會執行到這裡。
        var begin = await twoFactorUseCase.BeginRebindAsync(userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var challengePublicId = Guid.CreateVersion7();
        await SignInChallengeAsync(userId, "rebind", challengePublicId);

        return Ok(new TotpRebindBeginResponseDto(
            begin.SecretKey, begin.OtpAuthUri, begin.QrCodeDataUri, challengePublicId));
    }

    /// <summary>
    /// 驗證新裝置算出的碼、重新產生 Recovery Code，接著 bump SecurityStamp 撤銷所有
    /// 其他既有 Session，並用新 Stamp 重新簽發「這個」請求所在的 Session（不會把自己登出）。
    /// 秘鑰提升、SecurityStamp 變更與稽核紀錄都在同一個交易內，任何一步失敗都整個回滾，
    /// 不會出現「Session 已撤銷但沒有稽核」或「稽核寫了但 Stamp 沒真的更新」的半成功狀態
    /// （alex review P1#5）。既有 Admin Cookie 與 DEC-P297 的短效 rebind Challenge 兩者都要成立
    /// （alex review P1#3）；不論成功或失敗，這張 rebind Challenge 用過即簽出，不能重放。
    /// </summary>
    [HttpPost("totp/rebind/confirm")]
    [Authorize(Policy = DoSelectPolicies.Admin)]
    [ProducesResponseType(typeof(TotpEnrollConfirmResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConfirmRebind(
        [FromBody] TotpRebindConfirmRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireCurrentAdminUserId();

        var challenge = await RequireChallengeAsync("rebind", request.ChallengePublicId);
        if (challenge is null || !string.Equals(challenge.UserId, userId, StringComparison.Ordinal))
        {
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, AdminAuthErrorCodes.ChallengeInvalid));
        }

        // 用真正的 challengeId（而不是固定字串）當 challengeKey——跟登入流程的 4 個
        // Challenge 端點共用同一套三桶限流機制，不再讓所有管理員共用同一個 "rebind" 桶
        // （alex review P1#3；原設計見 TryAcquireChallengeAttempt）。
        if (!TryAcquireChallengeAttempt(challenge))
        {
            return await RejectChallengeRateLimitedAsync(challenge, cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var result = await twoFactorUseCase.ConfirmRebindAsync(userId, request.Code, cancellationToken);
        if (!result.IsSuccess)
        {
            // Rollback 也復原了 ConfirmRebindAsync 內把待確認秘鑰提升為正式 key 的動作——
            // 舊裝置的 authenticator key 維持有效（見 IdentityAdminAuthGateway 的實作說明）。
            await transaction.RollbackAsync(cancellationToken);
            await HttpContext.SignOutAsync(DoSelectAuthenticationSchemes.AdminChallenge);
            await RecordAdminAuditForUserIdAsync(
                AuditActions.AdminTotpRebindFailed, userId, AuditResult.Failed, result.ErrorCode, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
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

        // DEC-P296：稽核紀錄寫在 CommitAsync 之前——秘鑰提升、SecurityStamp 變更、稽核紀錄
        // 三者同一交易，任一失敗整筆 rollback。
        RecordAdminAudit(
            AuditActions.AdminTotpRebindConfirm,
            result.User!,
            AuditResult.Success,
            errorCode: null,
            [AuditFieldChange.Changed("securityStamp")]);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await HttpContext.SignOutAsync(DoSelectAuthenticationSchemes.AdminChallenge);

        logger.LogWarning(
            "Admin {AdminUserId} rebound TOTP; existing sessions on other devices are now revoked.",
            userId);

        var authResult = await CompleteAdminSignInAsync(result.User!);
        return Ok(new TotpEnrollConfirmResponseDto(result.RecoveryCodes!, ToCurrentUserDto(result.User!), authResult));
    }

    /// <summary>
    /// 寫入中央 AuditLog（DEC-P296）。只呼叫 <see cref="IAuditWriter.Add"/>——這只是把
    /// AuditLog 實體加進 <see cref="dbContext"/> 的 ChangeTracker，還沒有 SaveChanges。呼叫端
    /// 必須確保這行執行完之後、在同一個交易的 <c>CommitAsync</c>（或另外呼叫
    /// <c>SaveChangesAsync</c>）之前，讓這筆稽核紀錄真的落地，高風險狀態變更與稽核紀錄才會是
    /// 同一個原子邊界：Audit 沒寫成功，狀態變更也不會提交。
    /// </summary>
    /// <remarks>
    /// ⚠ alex 裁定 A1（第三輪 P1#2）：正常情況下 <see cref="AdminLoginUseCase"/> 與
    /// <see cref="AdminTwoFactorUseCase"/> 的資格檢查已經擋下零角色管理員，這裡不應該收到
    /// 零角色的 <paramref name="user"/>。但 <see cref="RejectChallengeRateLimitedAsync"/>／
    /// <see cref="RejectRebindStepUpRateLimitedAsync"/> 是限流拒絕路徑，發生在任何資格檢查
    /// 之前——如果角色剛好在這個時間點被清空，仍會走到這裡。防禦性地退回 System Actor
    /// （帳號只當 Resource），而不是讓 <see cref="AuditActor.Create"/> 對零角色 Admin
    /// 拋例外，把一個限流拒絕變成 500。
    /// </remarks>
    private void RecordAdminAudit(
        string action,
        AdminAuthUserSnapshot user,
        AuditResult result,
        string? errorCode,
        IReadOnlyCollection<AuditFieldChange> changes) =>
        auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            user.Roles.Count > 0
                ? AuditActor.Create(AuditActorType.Admin, user.PublicId, user.Roles)
                : AuditActor.Create(AuditActorType.System, publicId: null, roles: []),
            action,
            AuditResourceTypes.AdminAccount,
            user.PublicId,
            result,
            errorCode,
            changes,
            AuditReasonCode,
            CorrelationIdMiddleware.GetCorrelationId(HttpContext),
            GetTraceId(),
            jobPublicId: null,
            HttpContext.Connection.RemoteIpAddress));

    /// <summary>
    /// 寫入中央 AuditLog，Actor 是 <see cref="AuditActorType.System"/>（不帶 PublicId／Roles）——
    /// 用在系統自動判定、沒有真正登入使用者當 Actor 的事件（目前只有匿名密碼嘗試觸發的
    /// 30 分鐘 Lockout）。被影響的管理員只當 Resource，不會被誤記成施暴的 Actor，也不會因為
    /// 該管理員零角色而讓 <see cref="AuditActor.Create"/> 拋例外（alex review 最新一輪 P1#2）。
    /// </summary>
    private void RecordSystemAdminAudit(
        string action,
        Guid resourcePublicId,
        AuditResult result,
        string? errorCode,
        IReadOnlyCollection<AuditFieldChange> changes) =>
        auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            AuditActor.Create(AuditActorType.System, publicId: null, roles: []),
            action,
            AuditResourceTypes.AdminAccount,
            resourcePublicId,
            result,
            errorCode,
            changes,
            AuditReasonCode,
            CorrelationIdMiddleware.GetCorrelationId(HttpContext),
            GetTraceId(),
            jobPublicId: null,
            HttpContext.Connection.RemoteIpAddress));

    /// <summary>
    /// 少數呼叫點（挑戰限流拒絕、Rebind 驗證失敗）在拿到完整 <see cref="AdminAuthUserSnapshot"/>
    /// 之前就要記一筆稽核，只有 userId 可用——重新查一次快照取得真正的 Actor（PublicId／
    /// Roles），而不是隨便編一個。查無此人（帳號在流程中被刪除的極端情況）就放棄寫入這筆
    /// 次要的失敗紀錄，不讓稽核的邊角情況拖垮原本的失敗回應。
    /// </summary>
    private async Task RecordAdminAuditForUserIdAsync(
        string action,
        string userId,
        AuditResult result,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        var user = await authGateway.FindAdminByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            logger.LogWarning(
                "Skipped writing audit action {Action} for admin {AdminUserId}: the account no longer exists.",
                action,
                userId);
            return;
        }

        RecordAdminAudit(action, user, result, errorCode, changes: []);
    }

    private static string GetTraceId() =>
        Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString();

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
    private async Task<IActionResult> RejectChallengeRateLimitedAsync(
        AdminChallengeContext challenge, CancellationToken cancellationToken)
    {
        await HttpContext.SignOutAsync(DoSelectAuthenticationSchemes.AdminChallenge);
        await RecordAdminAuditForUserIdAsync(
            AuditActions.AdminChallengeRateLimited,
            challenge.UserId,
            AuditResult.Rejected,
            AdminAuthErrorCodes.ChallengeRateLimited,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(
            StatusCodes.Status429TooManyRequests,
            ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status429TooManyRequests, AdminAuthErrorCodes.ChallengeRateLimited));
    }

    /// <summary>
    /// Rebind step-up 超過嘗試上限：寫入稽核、回 429。跟 <see cref="RejectChallengeRateLimitedAsync"/>
    /// 不同之處是這裡還沒有 rebind challenge 可以簽出（step-up 通過才會簽發），也沒有既有
    /// AdminChallenge Cookie 需要作廢——呼叫者的完整 Admin Session 本身不受影響，只是這次
    /// 重新綁定的嘗試被拒絕。
    /// </summary>
    private async Task<IActionResult> RejectRebindStepUpRateLimitedAsync(
        string userId, CancellationToken cancellationToken)
    {
        await RecordAdminAuditForUserIdAsync(
            AuditActions.AdminChallengeRateLimited,
            userId,
            AuditResult.Rejected,
            AdminAuthErrorCodes.ChallengeRateLimited,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

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
