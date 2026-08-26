using DoSelect.Domain.Orders;

namespace DoSelect.Application.Orders;

/// <summary>
/// 訪客查單時比對到的訂單。<see cref="OrderNumber"/>／<see cref="GuestEmailNormalized"/>
/// 只給 Resend 重新組信件用——Challenge／Token 表本身仍只存 Hash，這裡讀的是 Order
/// 自己的欄位（訂單聯絡資訊本來就需要明文，跟 Challenge 專表的雜湊規則是兩回事）。
/// </summary>
public sealed record GuestOrderLookup(
    long OrderId,
    Guid OrderPublicId,
    string OrderNumber,
    string? GuestEmailNormalized);

/// <summary>
/// 查到 Token 時一併回傳其所屬訂單的 PublicId——Scope 比對一律用 PublicId
/// （Route／DTO 只使用 PublicId 的既有規則），不直接把內部 long Id 暴露到 Application 以外。
/// </summary>
public sealed record GuestOrderAccessTokenContext(GuestOrderAccessToken Token, Guid OrderPublicId);

/// <summary>
/// 訪客查單 Challenge／Token 的讀寫埠。刻意直接操作 Domain Entity（而非像
/// <c>IAdminAuthGateway</c> 那樣回傳 Snapshot）——這兩個 Entity 是純 Domain 物件，不像
/// Admin／Member 綁定 ASP.NET Identity 的 UserManager，沒有跨型別轉換的必要，維持 EF
/// Unit-of-Work 風格（呼叫端改完 Entity 狀態後呼叫 <see cref="SaveChangesAsync"/>）。
/// </summary>
public interface IGuestOrderAccessGateway
{
    /// <summary>
    /// 依訂單編號＋正規化 Email 找「訪客訂單」。只匹配 <c>GuestEmailNormalized</c>
    /// 不為 null 且相符的訂單——會員訂單一律視為不存在，會員應改用登入身分查詢，
    /// 避免兩條身分路徑互相洩漏「這個 Email 是會員還是訪客」的存在性。
    /// </summary>
    Task<GuestOrderLookup?> FindGuestOrderAsync(
        string orderNumber, string emailNormalized, CancellationToken cancellationToken = default);

    /// <summary>Resend／驗證成功後用內部 Id 反查訂單 PublicId，用於回應與 Cookie Claim。</summary>
    Task<GuestOrderLookup?> FindGuestOrderByIdAsync(
        long orderId, CancellationToken cancellationToken = default);

    Task AddRequestAsync(
        GuestOrderAccessRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 找一筆「仍然有效」的 Request（未過期、未消耗、未鎖定、未撤銷、AttemptCount &lt; 5）。
    /// 呼叫端仍須自行處理 Entity 方法（<c>RecordSend</c>／<c>RecordFailedAttempt</c>／
    /// <c>Consume</c>）拋出的 <see cref="InvalidOperationException"/>——那是 Entity
    /// 自身在併發下重新檢查失效條件用的防線,不是這個查詢方法的責任。
    /// </summary>
    Task<GuestOrderAccessRequest?> FindActiveRequestAsync(
        Guid requestPublicId, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task AddTokenAsync(
        GuestOrderAccessToken token, CancellationToken cancellationToken = default);

    Task<GuestOrderAccessTokenContext?> FindTokenByHashAsync(
        byte[] tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// 依主鍵分批刪除到期滿 30 天的 Request／Token（DEC-P267）。回傳實際刪除筆數；
    /// 呼叫端（背景服務）用 0 判斷是否已清完當次可清的資料。
    /// </summary>
    Task<int> PurgeExpiredAsync(
        DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// HMAC-SHA256（金鑰＝ <c>GuestOrderAccess:Pepper</c>）雜湊 IP、Email、訂單查找鍵、
/// 六位數驗證碼與限單存取權杖明文。DEC-P263：Challenge 專表只保存 HMAC／Hash 與
/// 安全中繼資料，不保存任何明文。
/// </summary>
public interface IGuestOrderAccessHasher
{
    byte[] HashIp(string ipAddress);

    byte[] HashEmail(string emailNormalized);

    /// <summary>對應 Entity 的 <c>OrderLookupKeyHash</c>——訂單編號＋正規化 Email 的組合鍵。</summary>
    byte[] HashOrderLookup(string orderNumber, string emailNormalized);

    byte[] HashCode(string sixDigitCode);

    byte[] HashToken(string rawToken);
}

/// <summary>
/// 訪客查單 Challenge 建立／重寄的三 Scope 限流（DEC-P266：15 分鐘視窗，每 IP Hash
/// 10 次、每 Email HMAC 5 次、每訂單 Lookup Hash 5 次，三者同時通過才建立／寄送）。
/// 純 Application 層服務，不透過 ASP.NET RateLimiter Middleware——Email／OrderLookup
/// 兩個 Scope 的 Key 來自 Request Body，Middleware 在 Model Binding 前難以乾淨取得；
/// 比照 <c>IEmailRequestThrottle</c> 的既有分工（Middleware 只管跟連線相關的 Scope，
/// 其餘 Scope 由 Application 服務接手）。
/// </summary>
public interface IGuestOrderAccessThrottle
{
    bool TryAcquireIp(byte[] ipHash);

    bool TryAcquireEmail(byte[] emailHash);

    bool TryAcquireOrderLookup(byte[] orderLookupHash);
}
