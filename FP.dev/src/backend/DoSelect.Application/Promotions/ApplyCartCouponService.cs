using DoSelect.Application.Common;
using DoSelect.Application.Shopping;
using DoSelect.Domain.Promotions;

namespace DoSelect.Application.Promotions;

/// <summary>
/// 購物車套用與移除優惠券的試算 Use Case（UC-COUPON-01）。
/// </summary>
/// <remarks>
/// <para>
/// <b>完全無狀態。</b> 購物車不保存優惠碼、試算不建立 <c>CouponRedemption</c>
/// （優惠券規則第 99～100 行），因此這裡只讀不寫，也不需要交易。
/// 真正占用名額發生在 Checkout。
/// </para>
/// <para>
/// 金額由後端重算：前端只送優惠碼與購物車版本，不送任何價格。
/// </para>
/// </remarks>
public sealed class ApplyCartCouponService
{
    private readonly ICartService _cartService;
    private readonly ICartCouponLineReader _lineReader;
    private readonly CouponQuoteService _quoteService;

    public ApplyCartCouponService(
        ICartService cartService,
        ICartCouponLineReader lineReader,
        CouponQuoteService quoteService)
    {
        ArgumentNullException.ThrowIfNull(cartService);
        ArgumentNullException.ThrowIfNull(lineReader);
        ArgumentNullException.ThrowIfNull(quoteService);

        _cartService = cartService;
        _lineReader = lineReader;
        _quoteService = quoteService;
    }

    /// <summary>
    /// 套用優惠碼並回傳更新後的購物車。不合用時丟 <see cref="DomainProblemException"/>。
    /// </summary>
    public async Task<CartDto> ApplyAsync(
        CartIdentity identity,
        ApplyCartCouponRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(request);

        var input = await _lineReader.FindAsync(identity, cancellationToken)
            ?? throw DomainProblemException.NotFound("The cart was not found.");

        RequireCurrentCart(input, request.CartRowVersion);

        var quote = await _quoteService.QuoteAsync(
            new CouponQuoteRequest(
                request.Code,
                input.Lines,
                identity.MemberUserId,
                // 訪客預覽不帶每人身分（DEC-P262）。詳見 GuestUsageKeyHash 的說明。
                GuestUsageKeyHash: null,
                input.IsAssemblyDelivery),
            cancellationToken);

        if (!quote.IsSuccess)
        {
            // 計算器的錯誤碼原樣回傳。coupon_invalid 是「這張券的資料本身不合法」，
            // 屬呼叫端輸入問題回 400；其餘三種是「券存在但目前不適用」，回 409。
            throw quote.ErrorCode == CouponCalculationErrorCodes.CouponInvalid
                ? DomainProblemException.BadRequest(
                    quote.ErrorCode, "The coupon code is not valid.")
                : DomainProblemException.Conflict(
                    quote.ErrorCode!, "The coupon cannot be applied to this cart.");
        }

        var cart = await _cartService.GetCartAsync(identity, cancellationToken);
        RequireSameCart(input, cart);
        return WithCoupon(cart, request.Code, quote);
    }

    /// <summary>
    /// 移除優惠碼並回傳購物車。
    /// </summary>
    /// <remarks>
    /// 購物車本來就沒有保存優惠碼，所以這裡沒有東西可以刪 —— 回傳的是「不帶優惠券」
    /// 的當下購物車。端點存在是為了讓前端有對稱的 API，而且金額一律由伺服器算，
    /// 不讓前端自己把折扣扣掉。
    /// </remarks>
    public Task<CartDto> RemoveAsync(
        CartIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return _cartService.GetCartAsync(identity, cancellationToken);
    }

    /// <summary>
    /// 比對呼叫端看到的購物車版本。
    /// </summary>
    /// <remarks>
    /// 使用者輸入優惠碼的期間購物車若被改過（例如另一個分頁加了商品），
    /// 試算結果對應的就不是他眼前那個購物車。回一個對不上的金額比直接拒絕更危險，
    /// 因為畫面會顯示一個永遠結不了帳的折扣。
    /// </remarks>
    private static void RequireCurrentCart(CartCouponLines input, byte[] expectedRowVersion)
    {
        if (expectedRowVersion is null ||
            !input.CartRowVersion.AsSpan().SequenceEqual(expectedRowVersion))
        {
            throw DomainProblemException.Conflict(
                "concurrency_conflict",
                "The cart changed after the coupon code was entered.");
        }
    }

    /// <summary>
    /// 確認第二次讀到的購物車，與試算所依據的是同一個。
    /// </summary>
    /// <remarks>
    /// 這個 Use Case 讀了兩次：先用讀取埠取計算列，最後再用 <c>ICartService</c> 取回應用的
    /// <c>CartDto</c>。兩次之間購物車若被另一個分頁修改、合併或逾期重建，就會把**舊計算列
    /// 算出的折扣疊到新購物車的金額上** —— 一份看起來完整、其實兩邊對不起來的快照。
    /// <para>
    /// <c>PublicId</c> 與 <c>RowVersion</c> 兩個都要比：合併或逾期重建會換掉整個購物車，
    /// 那時 RowVersion 來自另一列，比它沒有意義。
    /// </para>
    /// </remarks>
    private static void RequireSameCart(CartCouponLines input, CartDto cart)
    {
        if (cart.PublicId != input.CartPublicId ||
            !cart.RowVersion.AsSpan().SequenceEqual(input.CartRowVersion))
        {
            throw DomainProblemException.Conflict(
                "concurrency_conflict",
                "The cart changed while the coupon was being quoted.");
        }
    }

    /// <summary>
    /// 訪客預覽**不帶**每人身分。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 依 <c>DEC-P262</c> 與優惠券規則，訪客的每人限制是以伺服器 Secret 對**正規化訂單
    /// Email** 計算 HMAC-SHA-256，不是購物車金鑰的雜湊。購物車預覽階段還沒有訂單 Email，
    /// 因此 <c>GuestUsageKeyHash</c> 傳 <c>null</c>：預覽只檢查總名額，
    /// 每人次數由 Checkout 取得 Email 後依正式規則權威重驗。
    /// </para>
    /// <para>
    /// <c>CouponRuleReader</c> 在兩種身分都是 null 時會讓 owner count 維持 0，
    /// 正好是這個預覽語意。
    /// </para>
    /// <para>
    /// 先前這裡用 <c>SHA256(guestCartKey)</c>，理由是「與 Checkout 一致」——
    /// 但 Checkout 目前用的就是錯的做法，那是既有上游偏差、另案修正。
    /// **對齊既有程式碼不等於對齊規格**；照著錯的抄只會多出第二份錯誤。
    /// </para>
    /// </remarks>
    /// <summary>
    /// 把試算出來的折扣疊到購物車回應上。
    /// </summary>
    /// <remarks>
    /// 只覆寫 <c>couponDiscount</c> 與 <c>totalEstimate</c>；其餘金額仍由 Cart 模組負責，
    /// 這一層不重算小計或運費。
    /// </remarks>
    private static CartDto WithCoupon(CartDto cart, string code, CouponCalculationResult quote) =>
        cart with
        {
            Coupon = new CouponAppliedDto(
                CouponCode.Normalize(code),
                quote.DiscountAmount,
                quote.IsFreeShipping,
                quote.IsAssemblyFreeShipping),
            Amounts = cart.Amounts with
            {
                CouponDiscount = quote.DiscountAmount,
                TotalEstimate = cart.Amounts.TotalEstimate - quote.DiscountAmount,
            },
        };
}
