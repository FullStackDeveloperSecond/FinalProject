using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Contracts.Auth;
using DoSelect.Api.Security;
using DoSelect.Application.Members;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    [ProducesResponseType(typeof(RegisterAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await registerMemberService.RegisterAsync(request.ToCommand(), cancellationToken);

        return result switch
        {
            RegisterMemberResult.Success success => Accepted(new RegisterAcceptedResponse(
                success.PublicId,
                success.EmailMasked,
                AccountStatusTokens.ToToken(success.AccountStatus))),

            RegisterMemberResult.EmailInUse => ProblemResult(
                StatusCodes.Status409Conflict,
                AuthErrorCodes.AccountEmailInUse,
                "This email address is already registered."),

            RegisterMemberResult.ValidationFailed validationFailed =>
                BadRequest(ToValidationProblem(validationFailed.Errors)),

            _ => Problem(),
        };
    }

    [HttpPost("email-verifications")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestEmailVerification(
        [FromBody] EmailVerificationRequest request,
        CancellationToken cancellationToken)
    {
        await requestEmailVerificationService.RequestAsync(request.ToCommand(), cancellationToken);
        return Accepted();
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
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestPasswordReset(
        [FromBody] PasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        await requestPasswordResetService.RequestAsync(request.ToCommand(), cancellationToken);
        return Accepted();
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
    [ProducesResponseType(typeof(AuthSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status423Locked)]
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

            case LoginMemberResult.InvalidCredentials:
                return ProblemResult(
                    StatusCodes.Status401Unauthorized,
                    AuthErrorCodes.InvalidCredentials,
                    "The email or password is incorrect.");

            case LoginMemberResult.LockedOut:
                return ProblemResult(
                    StatusCodes.Status423Locked,
                    AuthErrorCodes.AccountLocked,
                    "Too many failed login attempts. Try again later.");

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
}
