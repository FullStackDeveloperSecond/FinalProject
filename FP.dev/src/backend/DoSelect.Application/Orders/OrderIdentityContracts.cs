namespace DoSelect.Application.Orders;

/// <summary>
/// 訂單的對外識別與擁有者摘要。
/// </summary>
/// <remarks>
/// 這是跨模組契約：由 haru 的 Orders 模組提供實作，其他模組不得直接讀取 <c>Orders</c> 資料表
/// （工程包第 7 節「跨模組契約」）。刻意只有識別與擁有者，沒有金額或付款期限 ——
/// 目前的使用端（發票端點）用不到，先不猜形狀，之後有需要再擴充。
/// </remarks>
/// <param name="OrderId">內部鍵，用來與其他模組保存的 <c>OrderId</c> 對應。不得對外回傳。</param>
/// <param name="PublicId">對外識別。</param>
/// <param name="OrderNumber">顯示用訂單編號。</param>
/// <param name="MemberUserId">
/// 會員的 Identity Id。訪客訂單為 <c>null</c>，因此擁有者比對必須同時處理這種情況。
/// </param>
public sealed record OrderIdentitySummary(
    long OrderId,
    Guid PublicId,
    string OrderNumber,
    string? MemberUserId);

/// <summary>
/// 訂單識別的讀取埠。實作屬於 Orders 模組的 Infrastructure。
/// </summary>
/// <remarks>
/// 本埠**只提供資料，不做授權判斷**。呼叫端自行以 <see cref="OrderIdentitySummary.MemberUserId"/>
/// 與登入者 claim 比對，並決定要回 404 還是 403 —— 授權邏輯屬各模組 endpoint 的職責。
/// </remarks>
public interface IOrderIdentityReader
{
    /// <summary>
    /// 以內部鍵批次換出識別摘要。清單頁用，避免逐列查詢造成 N+1。
    /// </summary>
    /// <returns>只包含存在的訂單；查無的鍵不會出現在結果中。</returns>
    Task<IReadOnlyDictionary<long, OrderIdentitySummary>> FindByIdsAsync(
        IReadOnlyCollection<long> orderIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 以對外識別找出單一訂單。
    /// </summary>
    /// <returns>
    /// 查無此單時回 <c>null</c>。呼叫端必須在這一步就擋掉，
    /// 不得先取出資料再由 DTO 或前端隱藏。
    /// </returns>
    Task<OrderIdentitySummary?> FindByPublicIdAsync(
        Guid orderPublicId,
        CancellationToken cancellationToken = default);
}
