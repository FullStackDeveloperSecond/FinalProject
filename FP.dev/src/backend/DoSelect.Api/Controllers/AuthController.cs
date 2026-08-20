using DoSelect.Api.Common;
using DoSelect.Api.Contracts.Auth;
using DoSelect.Application.Members;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    RegisterMemberService registerMemberService,
    ConfirmEmailVerificationService confirmEmailVerificationService) : ControllerBase
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
