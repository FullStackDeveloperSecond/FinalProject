using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Contracts.Auth;
using DoSelect.Api.Security;
using DoSelect.Application.Members;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DoSelect.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    RegisterMemberService registerMemberService,
    ConfirmEmailVerificationService confirmEmailVerificationService,
    RequestEmailVerificationService requestEmailVerificationService,
    RequestPasswordResetService requestPasswordResetService,
    ResetPasswordService resetPasswordService,
    LoginMemberService loginMemberService) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.AuthRegister)]
    [ProducesResponseType(typeof(RegisterAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await registerMemberService.RegisterAsync(request.ToCommand(), cancellationToken);

        // Non-enumerable by design: an already-registered email produces the same 202 shape as a
        // fresh registration (see RegisterMemberService), so there is no separate "already in
        // use" branch here — one would itself be the enumeration leak.
        return result switch
        {
            RegisterMemberResult.Success success => Accepted(new RegisterAcceptedResponse(
                success.PublicId,
                success.EmailMasked,
                AccountStatusTokens.ToToken(success.AccountStatus))),

            RegisterMemberResult.ValidationFailed validationFailed =>
                BadRequest(ToValidationProblem(validationFailed.Errors)),

            RegisterMemberResult.RateLimited => RateLimitedResult(),

            _ => Problem(),
        };
    }

    [HttpPost("email-verifications")]
    [EnableRateLimiting(RateLimitPolicies.AuthResendVerification)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestEmailVerification(
        [FromBody] EmailVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await requestEmailVerificationService.RequestAsync(request.ToCommand(), cancellationToken);
        return result == RequestEmailVerificationResult.RateLimited
            ? RateLimitedResult()
            : Accepted();
    }

    [HttpPost("email-verifications/confirm")]
    [ProducesResponseType(typeof(EmailVerificationConfirmedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmEmailVerification(
        [FromBody] EmailVerificationConfirmRequest request,
        CancellationToken cancellationToken)
    {
        var result = await confirmEmailVerificationService.ConfirmAsync(
            request.ToCommand(),
            cancellationToken);

        return result switch
        {
            ConfirmEmailVerificationResult.Success success => Ok(new EmailVerificationConfirmedResponse(
                AccountStatusTokens.ToToken(success.AccountStatus))),

            ConfirmEmailVerificationResult.TokenInvalid => ProblemResult(
                StatusCodes.Status400BadRequest,
                AuthErrorCodes.EmailTokenInvalid,
                "The email verification token is invalid, used, or revoked."),

            _ => Problem(),
        };
    }

    [HttpPost("password-resets")]
    [EnableRateLimiting(RateLimitPolicies.AuthForgotPassword)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestPasswordReset(
        [FromBody] PasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        var result = await requestPasswordResetService.RequestAsync(request.ToCommand(), cancellationToken);
        return result == RequestPasswordResetResult.RateLimited
            ? RateLimitedResult()
            : Accepted();
    }

    [HttpPost("password-resets/confirm")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmPasswordReset(
        [FromBody] PasswordResetConfirmRequest request,
        CancellationToken cancellationToken)
    {
        var result = await resetPasswordService.ResetAsync(request.ToCommand(), cancellationToken);

        return result switch
        {
            ResetPasswordResult.Success => Ok(),

            ResetPasswordResult.TokenInvalid => ProblemResult(
                StatusCodes.Status400BadRequest,
                AuthErrorCodes.PasswordResetTokenInvalid,
                "The password reset token is invalid, used, or expired."),

            ResetPasswordResult.PasswordRejected passwordRejected =>
                BadRequest(ToValidationProblem(passwordRejected.Errors)),

            _ => Problem(),
        };
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.AuthLogin)]
    [ProducesResponseType(typeof(AuthSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await loginMemberService.LoginAsync(request.ToCommand(), cancellationToken);

        switch (result)
        {
            case LoginMemberResult.Success success:
                await SignInMemberAsync(success);
                return Ok(new AuthSessionDto(
                    true,
                    new CurrentUserDto(
                        success.PublicId,
                        success.DisplayName,
                        success.EmailMasked,
                        true,
                        LocaleTokens.ToToken(success.Locale))));

            // LockedOut is intentionally folded into the same response as InvalidCredentials: an
            // existing account that is locked and a nonexistent/wrong-password account must be
            // indistinguishable from the outside, otherwise 423 itself becomes an oracle an
            // attacker can use to both enumerate accounts and confirm a lockout attack landed
            // (Alex review, 2026-08-21). Identity's own lockout (MemberLoginGateway) still denies
            // the login internally regardless of what is reported externally.
            case LoginMemberResult.InvalidCredentials:
            case LoginMemberResult.LockedOut:
                return ProblemResult(
                    StatusCodes.Status401Unauthorized,
                    AuthErrorCodes.InvalidCredentials,
                    "The email or password is incorrect.");

            case LoginMemberResult.EmailUnverified:
                return ProblemResult(
                    StatusCodes.Status403Forbidden,
                    AuthErrorCodes.AccountEmailUnverified,
                    "The email address has not been verified yet.");

            case LoginMemberResult.Suspended:
                return ProblemResult(
                    StatusCodes.Status403Forbidden,
                    AuthErrorCodes.AccountSuspended,
                    "The account has been suspended.");

            default:
                return Problem();
        }
    }

    [HttpPost("logout")]
    [Authorize(Policy = DoSelectPolicies.Member)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(DoSelectAuthenticationSchemes.Member);
        return NoContent();
    }

    [HttpGet("session")]
    [ProducesResponseType(typeof(AuthSessionDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSession(CancellationToken cancellationToken)
    {
        var authenticateResult = await HttpContext.AuthenticateAsync(DoSelectAuthenticationSchemes.Member);
        var userId = authenticateResult.Succeeded
            ? authenticateResult.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;

        var result = await loginMemberService.GetSessionAsync(userId, cancellationToken);

        return result switch
        {
            MemberSessionResult.Authenticated authenticated => Ok(new AuthSessionDto(
                true,
                new CurrentUserDto(
                    authenticated.PublicId,
                    authenticated.DisplayName,
                    authenticated.EmailMasked,
                    authenticated.EmailVerified,
                    LocaleTokens.ToToken(authenticated.Locale)))),

            _ => Ok(new AuthSessionDto(false)),
        };
    }

    private async Task SignInMemberAsync(LoginMemberResult.Success success)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, success.UserId),
            new(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Member),
            new(DoSelectClaimTypes.SecurityStamp, success.SecurityStamp),
        };
        var identity = new ClaimsIdentity(claims, DoSelectAuthenticationSchemes.Member);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            DoSelectAuthenticationSchemes.Member,
            principal,
            new AuthenticationProperties { IsPersistent = success.RememberMe });
    }

    private ValidationProblemDetails ToValidationProblem(
        IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(field, message);
            }
        }

        return ApiProblemDetailsFactory.CreateValidation(HttpContext, ModelState);
    }

    private ObjectResult ProblemResult(int statusCode, string code, string detail)
    {
        var problemDetails = ApiProblemDetailsFactory.Create(HttpContext, statusCode, code, detail: detail);
        var result = new ObjectResult(problemDetails) { StatusCode = statusCode };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    private ObjectResult RateLimitedResult() =>
        ProblemResult(
            StatusCodes.Status429TooManyRequests,
            ApiErrorCodes.RateLimitExceeded,
            "Too many requests for this email address. Try again later.");
}
