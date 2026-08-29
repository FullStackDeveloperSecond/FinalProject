using System.Security.Cryptography;
using System.Text;
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
                HashGuestKey(identity.GuestCartKey),
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
        return WithCoupon(cart, request.Code, quote.DiscountAmount);
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
    /// 訪客以購物車金鑰的雜湊計算使用量；會員以 MemberUserId 計。
    /// </summary>
    /// <remarks>
    /// 雜湊方式必須與 Checkout 一致（<c>EfCheckoutTransactionGateway.HashGuestKey</c>），
    /// 否則同一位訪客在預覽與結帳會被算成兩個人，剩餘名額對不起來。
    /// </remarks>
    private static byte[]? HashGuestKey(string? guestCartKey) =>
        string.IsNullOrWhiteSpace(guestCartKey)
            ? null
            : SHA256.HashData(Encoding.UTF8.GetBytes(guestCartKey));

    /// <summary>
    /// 把試算出來的折扣疊到購物車回應上。
    /// </summary>
    /// <remarks>
    /// 只覆寫 <c>couponDiscount</c> 與 <c>totalEstimate</c>；其餘金額仍由 Cart 模組負責，
    /// 這一層不重算小計或運費。
    /// </remarks>
    private static CartDto WithCoupon(CartDto cart, string code, decimal discountAmount) =>
        cart with
        {
            Coupon = new CouponAppliedDto(CouponCode.Normalize(code), discountAmount),
            Amounts = cart.Amounts with
            {
                CouponDiscount = discountAmount,
                TotalEstimate = cart.Amounts.TotalEstimate - discountAmount,
            },
        };
}
