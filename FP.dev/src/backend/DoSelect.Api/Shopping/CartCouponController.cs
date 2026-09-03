using DoSelect.Api.Common;
using DoSelect.Application.Promotions;
using DoSelect.Application.Shopping;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Shopping;

/// <summary>
/// 前台購物車套用與移除優惠碼（`API Endpoint目錄` 第 73 行，UC-COUPON-01）。
/// </summary>
/// <remarks>
/// <para>
/// 與 <see cref="CartController"/> 一樣同時接受訪客（購物車金鑰標頭）與會員，所以沒有
/// 類別層級的 <c>[Authorize]</c>／<c>[AllowAnonymous]</c>，改由 action 自己解析呼叫者。
/// <b>前台不套用 <c>Coupon.Manage</c></b>（`API DTO與Schema契約` 第 126 行）—— 那是後台
/// 管理優惠券的權限，不是顧客用券的權限。
/// </para>
/// <para>
/// 這一層很薄：金額、名額、適用範圍與錯誤碼全部由 <see cref="ApplyCartCouponService"/>
/// 決定，失敗時丟 <c>DomainProblemException</c> 交給全域處理器轉成 Problem Details。
/// 前端不重算折扣。
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/cart/coupon")]
public sealed class CartCouponController : ControllerBase
{
    private readonly ApplyCartCouponService _coupons;

    public CartCouponController(ApplyCartCouponService coupons)
    {
        ArgumentNullException.ThrowIfNull(coupons);
        _coupons = coupons;
    }

    [HttpPost]
    [ProducesResponseType<CartDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CartDto>> Apply(
        [FromBody] ApplyCartCouponRequest request,
        CancellationToken cancellationToken)
    {
        var identity = await CartIdentityResolver.ResolveAsync(HttpContext);
        if (identity is null)
        {
            return IdentityRequiredProblem();
        }

        return Ok(await _coupons.ApplyAsync(identity, request, cancellationToken));
    }

    /// <remarks>
    /// 移除只是回傳目前的購物車 —— 折扣沒有被保存下來，套用是每次重算的。端點存在是
    /// 為了讓前端有對稱的 API，而且金額一律由伺服器算，不讓前端自己把折扣扣掉。
    /// </remarks>
    [HttpDelete]
    [ProducesResponseType<CartDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CartDto>> Remove(CancellationToken cancellationToken)
    {
        var identity = await CartIdentityResolver.ResolveAsync(HttpContext);
        if (identity is null)
        {
            return IdentityRequiredProblem();
        }

        return Ok(await _coupons.RemoveAsync(identity, cancellationToken));
    }

    /// <remarks>與 <see cref="CartController"/> 用同一個錯誤碼與訊息形狀。</remarks>
    private ActionResult IdentityRequiredProblem() =>
        BadRequest(ApiProblemDetailsFactory.Create(
            HttpContext,
            StatusCodes.Status400BadRequest,
            ShoppingWriteException.ErrorCodes.ValidationFailed,
            detail: $"A member session or the '{CartIdentityResolver.GuestCartKeyHeaderName}' header is required."));
}
