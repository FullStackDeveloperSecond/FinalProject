using DoSelect.Api.Security;
using DoSelect.Application.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Promotions;

/// <summary>
/// 優惠券適用範圍挑選器的目錄參考資料。
/// </summary>
/// <remarks>
/// <para>
/// alex 2026-08-29 於 PR #64 的 P2#3 裁定：<b>不放寬 <c>CatalogManager</c></b>，
/// 也不建立 <c>/api/v1/admin/catalog/reference</c> 這種通用高權限參考端點；
/// 改用這個掛在優惠券底下、以 <c>Coupon.Manage</c> 保護的用途限定唯讀端點。
/// </para>
/// <para>
/// 路由掛在 Promotions 是因為它的授權與用途都屬優惠券；查詢契約與實作則屬 Catalog
/// （<c>DoSelect.Application/Catalog</c>、<c>DoSelect.Infrastructure/Catalog</c>），
/// 由 Catalog owner 做 domain review。
/// </para>
/// <para>
/// <b>唯讀。</b>這裡沒有任何寫入動作，也不回傳 Catalog 的管理 DTO ——
/// 這個端點的授權比目錄管理寬，所以它看得到的東西必須比目錄管理窄。
/// </para>
/// </remarks>
[ApiController]
[Authorize(Policy = DoSelectPolicies.CouponManage)]
[Route("api/v1/admin/coupons/catalog-options")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminCouponCatalogOptionsController : ControllerBase
{
    private readonly ICouponCatalogOptionsReader _reader;

    public AdminCouponCatalogOptionsController(ICouponCatalogOptionsReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    /// <summary>
    /// 一次取回整棵分類樹。
    /// </summary>
    /// <remarks>
    /// 停用的分類也會回，並帶 <c>isActive</c>：既有優惠券可能已經綁在上面，
    /// 查不到就會讓那筆設定在介面上靜默消失。
    /// </remarks>
    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyList<CouponCategoryOption>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CouponCategoryOption>>> Categories(
        CancellationToken cancellationToken)
    {
        return Ok(await _reader.ListCategoriesAsync(cancellationToken));
    }

    /// <summary>
    /// 關鍵字搜尋可新增的商品。
    /// </summary>
    /// <remarks>
    /// 只回可新增的狀態（<c>Draft</c>／<c>Published</c>／<c>Unpublished</c>）——
    /// 搜尋結果是「可以加進來的東西」，已停售的商品不該出現在這裡。
    /// </remarks>
    [HttpGet("products")]
    [ProducesResponseType<CouponProductSearchResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CouponProductSearchResult>> Products(
        [FromQuery] CouponProductSearchRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await _reader.SearchProductsAsync(request.Q, request.PageSize, cancellationToken));
    }

    /// <summary>
    /// 一次解析一組商品 <c>PublicId</c>，用於載入既有的適用／排除範圍。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用 <c>POST</c> 而不是把 200 個 GUID 塞進 query string：那會超過多數
    /// 反向代理的 URL 長度上限，而且 <c>PublicId</c> 不該進網址（會留在存取紀錄裡）。
    /// 這支沒有副作用，只是查詢。
    /// </para>
    /// <para>
    /// 已停售的商品**會**回傳，並帶 <c>isSelectable: false</c> ——
    /// 已經寫在券上的參考不能因為挑選器查不到就消失（alex C1）。
    /// </para>
    /// </remarks>
    [HttpPost("products/resolve")]
    [ProducesResponseType<IReadOnlyList<CouponProductOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<CouponProductOption>>> ResolveProducts(
        [FromBody] CouponProductResolveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Ok(await _reader.ResolveProductsAsync(request.PublicIds, cancellationToken));
    }
}
