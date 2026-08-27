namespace DoSelect.Infrastructure.Security;

public sealed class GuestOrderAccessOptions
{
    public const string SectionName = "GuestOrderAccess";

    /// <summary>
    /// HMAC-SHA256 金鑰，用來雜湊訪客查單流程的 IP、Email、訂單查找鍵、驗證碼與存取權杖。
    /// 比照 <c>Idempotency:ActorScopePepper</c>：以 User Secrets 或部署環境設定，至少 32 UTF-8
    /// bytes，不得寫入範例設定、Repository、Log 或資料庫。
    /// </summary>
    public string Pepper { get; set; } = string.Empty;
}
