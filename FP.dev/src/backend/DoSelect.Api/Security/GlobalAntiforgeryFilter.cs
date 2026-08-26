using DoSelect.Api.Common;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DoSelect.Api.Security;

/// <summary>
/// ⚠ ASP.NET Core Antiforgery 會把 token 綁定到「產生當下」的 HttpContext.User。
/// [Authorize] 端點會由 Authorization Middleware 先把 HttpContext.User 換成對應 scheme
/// 的身分，但 [AllowAnonymous] 端點（例如 /login）預設沒有人做這件事——若
/// SecurityController 簽發 token 當下剛好偵測到一個仍然有效的 Member／Admin Cookie 而
/// 綁定了那個身分，這裡卻用匿名身分驗證，兩邊不一致就會讓「已登入使用者重新提交登入
/// 表單」這種完全合法的操作跟著失敗（實測發現的真實 bug）。這裡比照 SecurityController
/// 的 best-effort 邏輯（同樣刻意不含 AdminChallenge，理由見該檔案），在驗證前把
/// HttpContext.User 補齊，確保簽發與驗證兩端看到的身分一致。
/// </summary>
public sealed class GlobalAntiforgeryFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase)
        {
            HttpMethods.Get,
            HttpMethods.Head,
            HttpMethods.Options,
            HttpMethods.Trace,
        };

    private static readonly string[] AuthenticationSchemesToTry =
    [
        DoSelectAuthenticationSchemes.Member,
        DoSelectAuthenticationSchemes.Admin,
    ];

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (SafeMethods.Contains(context.HttpContext.Request.Method))
        {
            return;
        }

        await ResolveCurrentUserAsync(context.HttpContext);

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            var problemDetails = ApiProblemDetailsFactory.Create(
                context.HttpContext,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.AntiforgeryValidationFailed);
            var result = new BadRequestObjectResult(problemDetails);
            result.ContentTypes.Add("application/problem+json");
            context.Result = result;
        }
    }

    private static async Task ResolveCurrentUserAsync(HttpContext httpContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            return;
        }

        foreach (var scheme in AuthenticationSchemesToTry)
        {
            var result = await httpContext.AuthenticateAsync(scheme);
            if (result.Succeeded && result.Principal is not null)
            {
                httpContext.User = result.Principal;
                return;
            }
        }
    }
}
