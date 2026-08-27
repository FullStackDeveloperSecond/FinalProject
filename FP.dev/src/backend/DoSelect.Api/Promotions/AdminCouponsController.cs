using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Promotions;
using DoSelect.Domain.Promotions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Promotions;

/// <summary>
/// 後台優惠券管理（API Endpoint目錄第 113 行）。
/// </summary>
/// <remarks>
/// <c>Coupon.Manage</c> 只負責授權；狀態機、RowVersion 與名額規則仍由 Use Case 負責
/// （API Endpoint目錄第 119 行）。前台 <c>POST/DELETE /api/v1/cart/coupon</c> 不套用
/// 本 Policy。
/// </remarks>
[ApiController]
[Authorize(Policy = DoSelectPolicies.CouponManage)]
[Route("api/v1/admin/coupons")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminCouponsController : ControllerBase
{
    private readonly IAdminCouponService _couponService;

    public AdminCouponsController(IAdminCouponService couponService)
    {
        ArgumentNullException.ThrowIfNull(couponService);
        _couponService = couponService;
    }

    [HttpGet]
    [ProducesResponseType<PageResult<CouponDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PageResult<CouponDto>>> List(
        [FromQuery] AdminCouponListRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _couponService.ListAsync(
            new AdminCouponQuery(
                request.Q,
                request.Statuses,
                request.Sort,
                request.PageNumber,
                request.PageSize),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<CouponDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CouponDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var coupon = await _couponService.FindByPublicIdAsync(id, cancellationToken);
        if (coupon is null)
        {
            return NotFound(ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.ResourceNotFound));
        }

        return Ok(coupon);
    }

    [HttpPost]
    [ProducesResponseType<CouponDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CouponDto>> Create(
        [FromBody] CreateCouponRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _couponService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.PublicId }, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<CouponDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CouponDto>> Update(
        Guid id,
        [FromBody] UpdateCouponRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _couponService.UpdateAsync(id, request, cancellationToken));

    /// <summary>
    /// 執行 <c>activate</c>／<c>pause</c>／<c>disable</c>。
    /// </summary>
    /// <remarks>
    /// 白名單外的動作回 404，不落到任何預設分支 —— 讓未知動作靜默成功或回 400
    /// 都會讓呼叫端誤以為存在這個能力。
    /// </remarks>
    /// <remarks>
    /// 路由 token 刻意命名為 <c>couponAction</c> 而不是 <c>action</c>：<c>action</c> 是 MVC
    /// 保留的路由值（對應 Action Method 名稱），用它會讓這條路由永遠比對不到而回 404。
    /// URL 仍然是 <c>/actions/{activate|pause|disable}</c>。
    /// </remarks>
    [HttpPost("{id:guid}/actions/{couponAction}")]
    [ProducesResponseType<CouponDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CouponDto>> ExecuteAction(
        Guid id,
        [FromRoute] string couponAction,
        [FromBody] CouponActionRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _couponService.ExecuteActionAsync(id, couponAction, request, cancellationToken));
}

/// <summary>
/// 列表查詢字串。分頁預設值見 API共通規範第 72 行；超出範圍由 Application 層回 400，
/// 不在此夾擠成合法值。
/// </summary>
public sealed record AdminCouponListRequest
{
    public string? Q { get; init; }

    public IReadOnlyList<CouponStatus>? Statuses { get; init; }

    public string? Sort { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
