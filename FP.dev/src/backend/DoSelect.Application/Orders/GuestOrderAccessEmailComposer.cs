using DoSelect.Application.Notifications;

namespace DoSelect.Application.Orders;

/// <summary>
/// 組訪客查單六位數驗證碼信件，比照 <c>MemberVerificationEmailComposer</c>。明碼只在這裡
/// 組進信件內容，呼叫端算完 Hash 後即可捨棄——不得寫入任何 Log 或 AuditLog Reason 欄位。
/// </summary>
public static class GuestOrderAccessEmailComposer
{
    public static EmailMessage Compose(string email, string orderNumber, string sixDigitCode) =>
        new(
            email,
            "您的懂選訪客查單驗證碼",
            $"您正在查詢訂單 {orderNumber} 的進度。驗證碼為：{sixDigitCode}\n" +
            "此驗證碼將於 10 分鐘後失效，請勿提供給任何人。若非您本人操作，請忽略此信。",
            $"<p>您正在查詢訂單 {orderNumber} 的進度。驗證碼為：<strong>{sixDigitCode}</strong></p>" +
            "<p>此驗證碼將於 10 分鐘後失效，請勿提供給任何人。若非您本人操作，請忽略此信。</p>");
}

public static class GuestOrderAccessNotificationContract
{
    public const string TemplateKey = "guest.order_access.verification_code";
    public const string RecipientPurpose = "guest_order_access.email";
    public const string ResourceType = "GuestOrderAccessRequest";
    public const string Locale = "zh-TW";
}
