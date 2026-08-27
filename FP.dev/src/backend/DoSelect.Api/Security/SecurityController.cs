using DoSelect.Api.Common;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Security;

[ApiController]
[Route("api/v1/security")]
public sealed class SecurityController(IAntiforgery antiforgery) : ControllerBase
{
    public const string ClientHeaderName = "X-DoSelect-Client";

    /// <summary>
    /// 把 <see cref="ClientHeaderName"/> 的值對應到唯一一個對應的驗證 Scheme——「唯一」是重點：
    /// 呼叫端（這裡的 token 簽發，以及 <see cref="GlobalAntiforgeryFilter"/> 的驗證）
    /// 都必須用同一個對應表，簽發與驗證才會看到同一個 HttpContext.User（alex review 第三輪
    /// P1#1）。不合法或缺少的值回傳 null，呼叫端自行決定 fallback 行為。
    /// </summary>
    public static string? ResolveAuthenticationScheme(string? client) => client switch
    {
        DoSelectClaimValues.Member => DoSelectAuthenticationSchemes.Member,
        DoSelectClaimValues.Admin => DoSelectAuthenticationSchemes.Admin,
        _ => null,
    };

    [HttpGet("antiforgery-token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AntiforgeryTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AntiforgeryTokenResponse>> GetAntiforgeryToken(
        [FromHeader(Name = ClientHeaderName)] string client)
    {
        var authenticationScheme = ResolveAuthenticationScheme(client);
        if (authenticationScheme is null)
        {
            ModelState.AddModelError(
                "client",
                $"{ClientHeaderName} must be either 'member' or 'admin'.");
            return BadRequest(ApiProblemDetailsFactory.CreateValidation(HttpContext, ModelState));
        }

        var authenticationResult = await HttpContext.AuthenticateAsync(authenticationScheme);
        if (authenticationResult.Succeeded && authenticationResult.Principal is not null)
        {
            HttpContext.User = authenticationResult.Principal;
        }
        // ⚠ 刻意不 fallback 到 AdminChallenge：ASP.NET Core Antiforgery 會把 token 綁定到
        // 產生當下的 HttpContext.User。若這裡 best-effort 撈到殘留的舊 AdminChallenge
        // Cookie（例如使用者放棄了上一次 2FA 流程），會把 token 綁到那個「不相干」的身分，
        // 導致下一次真正的登入請求（本身是匿名動作）反而驗證失敗。管理員 2FA 挑戰階段的
        // 端點（totp/verify 等）改用 challengePublicId 本身當作等效的防偽金鑰，
        // 不依賴這裡的身分綁定，見 AdminAuthController。

        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        if (string.IsNullOrWhiteSpace(tokens.RequestToken))
        {
            throw new InvalidOperationException("The antiforgery request token could not be generated.");
        }

        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return Ok(new AntiforgeryTokenResponse(tokens.RequestToken));
    }
}

public sealed record AntiforgeryTokenResponse(string RequestToken);
