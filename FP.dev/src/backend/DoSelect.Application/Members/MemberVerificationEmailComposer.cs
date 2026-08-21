using DoSelect.Application.Notifications;

namespace DoSelect.Application.Members;

internal static class MemberVerificationEmailComposer
{
    public static EmailMessage Compose(
        string email,
        Guid publicId,
        string token,
        string frontendBaseUrl)
    {
        var verificationLink =
            $"{frontendBaseUrl.TrimEnd('/')}/verify-email" +
            $"?publicId={publicId:D}" +
            $"&token={Uri.EscapeDataString(token)}";

        return new EmailMessage(
            email,
            "請驗證您的懂選帳號 Email",
            $"感謝您註冊懂選會員。請於 24 小時內點擊以下連結完成 Email 驗證：\n{verificationLink}",
            $"<p>感謝您註冊懂選會員。請於 24 小時內點擊以下連結完成 Email 驗證：</p><p><a href=\"{verificationLink}\">{verificationLink}</a></p>");
    }
}
