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
/// 的邏輯，在驗證前把 HttpContext.User 補齊，確保簽發與驗證兩端看到的身分一致。
/// </summary>
/// <remarks>
/// ⚠ alex review 第三輪 P1#1：原本不管前端實際要打哪個 client，一律固定依序嘗試
/// Member、Admin，取第一個驗證成功的當作身分——但 SecurityController 簽發 token 時是依
/// <see cref="SecurityController.ClientHeaderName"/> 精準指定唯一一個 scheme。兩邊選擇邏輯
/// 不一致，會在「兩種 Cookie 同時存在」或「只有『另一種』Cookie 存在」時，讓這裡選到的身分
/// 跟 token 簽發當下綁定的身分對不上，導致合法的登入／操作被誤判成 antiforgery 驗證失敗。
/// 這裡改成優先讀同一個 <see cref="SecurityController.ClientHeaderName"/>（前端會在每個
/// unsafe request 上都附帶，不只 token 簽發那次），用
/// <see cref="SecurityController.ResolveAuthenticationScheme"/> 精準選同一個 scheme，
/// 只嘗試那一個——就算驗證失敗也維持匿名，不會退而求其次去試另一個 scheme（試了就等於走回
/// 舊的、會選錯身分的邏輯）。沒有這個 header（例如尚未更新的呼叫端、或本檔案外的其他測試）
/// 才 fallback 回舊的 best-effort 依序嘗試，維持既有行為相容。
/// </remarks>
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

        var requestedClient = httpContext.Request.Headers[SecurityController.ClientHeaderName].ToString();
        var requestedScheme = SecurityController.ResolveAuthenticationScheme(
            string.IsNullOrEmpty(requestedClient) ? null : requestedClient);
        if (requestedScheme is not null)
        {
            // 只試呼叫端實際宣告的那一個 scheme，不論成功或失敗都不再嘗試另一個——這正是
            // 跟 token 簽發端保持一致的關鍵：失敗就維持匿名，跟 SecurityController 在同一個
            // client 值下驗證失敗時的行為一致。
            var result = await httpContext.AuthenticateAsync(requestedScheme);
            if (result.Succeeded && result.Principal is not null)
            {
                httpContext.User = result.Principal;
            }

            return;
        }

        // 沒有帶（或帶了無法辨識的）client header：維持原本的 best-effort 依序嘗試，
        // 相容尚未附帶這個 header 的既有呼叫端與測試。
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
