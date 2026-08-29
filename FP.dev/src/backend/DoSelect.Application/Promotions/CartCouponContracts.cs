using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Shopping;
using DoSelect.Application.Support.Dtos;
using DoSelect.Domain.Promotions;

namespace DoSelect.Application.Promotions;

/// <summary>
/// 一個購物車在試算當下的優惠券計算輸入。
/// </summary>
/// <param name="CartPublicId">購物車對外識別，供呼叫端比對回應。</param>
/// <param name="CartRowVersion">試算所依據的購物車版本，供樂觀比對。</param>
/// <param name="Lines">逐列計算輸入，順序不影響結果。</param>
/// <param name="IsAssemblyDelivery">
/// 這個購物車是否含組裝配送。免組裝運費的判定需要它，缺這一項會把不該免的算成免。
/// </param>
public sealed record CartCouponLines(
    Guid CartPublicId,
    byte[] CartRowVersion,
    IReadOnlyList<CouponCalculationLine> Lines,
    bool IsAssemblyDelivery);

/// <summary>
/// 取得購物車優惠券試算輸入的讀取埠。**實作屬於 Shopping 模組（terry）的 Infrastructure。**
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CouponCalculationLine"/> 需要每一列的 <c>ProductId</c>、<c>CategoryIds</c>、
/// <c>FinalUnitPrice</c> 與 <c>IsOnSale</c>，但 <c>CartItem</c> 只有 <c>SkuId</c> 與
/// <c>Quantity</c> —— 其餘都在 Cart／Sku／Product／Category，屬 Shopping 與 Catalog 模組。
/// 依工程包第 7 節，本模組不得直接讀那些表，因此以這個埠取得（DEC-B1 是退款執行的
/// **個別**例外，不可類推到這裡）。
/// </para>
/// <para>
/// 實作要點：
/// </para>
/// <list type="bullet">
/// <item>只回**呼叫者自己的**購物車；找不到或不屬於該身分時回 <c>null</c>。</item>
/// <item>價格取購物車當下的成交單價，與 <c>CartItemDto.UnitPrice</c> 同一來源。</item>
/// <item>只讀不寫。試算不得建立或保留 <c>CouponRedemption</c>（優惠券規則第 100 行）。</item>
/// </list>
/// </remarks>
public interface ICartCouponLineReader
{
    Task<CartCouponLines?> FindAsync(
        CartIdentity identity,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 購物車套用優惠券的試算請求。
/// </summary>
/// <remarks>
/// <para>
/// <b>購物車不保存優惠碼</b>（優惠券規則第 99 行）：這支是無狀態試算，前端每次帶入優惠碼，
/// 伺服器算完直接回傳更新後的金額，不寫入任何資料。重新整理或換裝置會遺失輸入值是預期行為，
/// Checkout 會再驗證一次全部規則。
/// </para>
/// <para>
/// <paramref name="CartRowVersion"/> 用於樂觀比對：購物車在使用者輸入優惠碼之後被改過時，
/// 試算結果對應的已經不是他看到的那個購物車，直接回 <c>concurrency_conflict</c>
/// 比回一個對不上的金額安全。
/// </para>
/// </remarks>
public sealed record ApplyCartCouponRequest(
    [property: Required]
    [property: StringLength(64, MinimumLength = 1)]
    string Code,
    [property: RowVersionRequired]
    byte[] CartRowVersion);
