using DoSelect.Application.Auditing;
using DoSelect.Application.Outbox;
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
/// 三 Scope 限流的視窗參數（DEC-P266：15 分鐘視窗，預設 IP 10 次、Email 5 次、
/// OrderLookup 5 次，三者同時通過才建立／寄送）。
/// </summary>
public sealed record GuestOrderAccessRateLimitWindow(
    byte[] IpHash,
    int IpPermitLimit,
    byte[] EmailHash,
    int EmailPermitLimit,
    byte[] OrderLookupHash,
    int OrderLookupPermitLimit,
    DateTime WindowStartUtc);

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

    /// <summary>驗證成功後用內部 Id 反查訂單 PublicId，用於回應與 Cookie Claim。</summary>
    Task<GuestOrderLookup?> FindGuestOrderByIdAsync(
        long orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 建立新 Challenge Row前，在同一個 Serializable
    /// 交易內原子核對三 Scope 15 分鐘視窗既有筆數——沿用 <c>GuestOrderAccessRequests</c>
    /// 既有的三組 (Hash, CreatedAtUtc) 索引（DEC-P266），不新增限流表／Migration。任一
    /// Scope 達到上限就整個 rollback、不寫入，回傳 false；三者都通過才新增
    /// <paramref name="newRequest"/> 並 commit，回傳 true。
    /// 有效與 Decoy 都走這個方法，維持恆定 202／429 的回應形狀。實作必須在 SQL Server
    /// 死結／並行衝突時自行重試整段交易，不能把例外原樣往外傳。
    /// </summary>
    Task<bool> TryCreateRequestWithinRateLimitAsync(
        GuestOrderAccessRateLimitWindow window,
        GuestOrderAccessRequest newRequest,
        OutboxWriteRequest? notification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A1 重寄交易：在同一個 Serializable transaction 核對三 Scope，通過後原地更新
    /// <paramref name="request"/> 的碼與寄送狀態，並新增 <paramref name="rateLimitEvent"/>
    /// 消耗這次呼叫的 IP／Email／OrderLookup 額度。任一寫入失敗必須整體 rollback；
    /// Request PublicId 不變，且不得再用 successor lookup 猜測重寄鏈。
    /// </summary>
    Task<bool> TryRecordResendWithinRateLimitAsync(
        GuestOrderAccessRateLimitWindow window,
        GuestOrderAccessRequest request,
        GuestOrderAccessRequest rateLimitEvent,
        byte[]? newCodeHash,
        DateTime sentAtUtc,
        OutboxWriteRequest? notification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resend 對查無此 PublicId／已完全失效的呼叫：沒有任何儲存的 Scope Hash 可用來核對
    /// Email／OrderLookup，也沒有真實 Request 可延續，只能核對並「持久消耗」目前呼叫者
    /// IP 這一個 Scope。跟 <see cref="TryCreateRequestWithinRateLimitAsync"/> 一樣在同一個
    /// Serializable 交易內原子核對＋寫入 <paramref name="sentinelRequest"/>（見
    /// <see cref="GuestOrderAccessRequest.CreateUnknownResendAttempt"/>）——不能只唯讀計數
    /// 既有筆數卻不寫入，否則這個分支永遠不會被限流（review #1）。
    /// </summary>
    Task<bool> TryRecordUnknownResendAttemptAsync(
        byte[] ipHash,
        int ipPermitLimit,
        DateTime windowStartUtc,
        GuestOrderAccessRequest sentinelRequest,
        CancellationToken cancellationToken = default);

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
    /// 原子遞增 <see cref="GuestOrderAccessToken.ScopeViolationCount"/>——<c>GuestOrderAccessToken</c>
    /// 沒有 RowVersion（只繼承 <c>PublicEntity</c>），一般 read-modify-write 在平行跨訂單存取下
    /// 會遺失更新。實作必須用資料庫端原子 UPDATE（例如 EF Core <c>ExecuteUpdateAsync</c>），
    /// 不能先讀出 Entity、呼叫 Domain 方法再 SaveChanges。
    /// </summary>
    Task RecordScopeViolationAsync(
        long tokenId,
        AuditWriteRequest auditRequest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 依主鍵分批刪除到期滿 30 天的 Request／Token（DEC-P267）。回傳實際刪除筆數；
    /// 呼叫端（背景服務）用 0 判斷是否已清完當次可清的資料。
    /// </summary>
    Task<int> PurgeExpiredAsync(
        DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// 樂觀並行寫入衝突（RowVersion 不符）時，實作必須拋出
    /// <see cref="DoSelect.Application.Common.DomainProblemException"/>（Code＝
    /// <see cref="DoSelect.Application.Common.DomainErrorCodes.ConcurrencyConflict"/>），
    /// 不能讓 EF Core 例外原樣往上傳——Application 層不依賴 EF Core，靠這個型別重試。
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 樂觀並行衝突後，重新從資料庫載入 Request 目前欄位值（含 RowVersion），供重試使用。
    /// 不能改用 <see cref="FindActiveRequestAsync"/> 重查——同一個 DbContext 內這個 Entity
    /// 已經在追蹤中，重查預設只會拿回同一個、還停在舊版本的記憶體實例。
    /// </summary>
    Task ReloadRequestAsync(
        GuestOrderAccessRequest request, CancellationToken cancellationToken = default);
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

    /// <summary>
    /// 依穩定 Request PublicId 與寄送序號重建六位數驗證碼。明碼不持久化；Outbox consumer
    /// 只需持有 resource PublicId 與 parameter-set version，即可在寄送時重建同一組碼。
    /// </summary>
    string DeriveVerificationCode(Guid requestPublicId, int sendNumber);

    byte[] HashToken(string rawToken);
}
