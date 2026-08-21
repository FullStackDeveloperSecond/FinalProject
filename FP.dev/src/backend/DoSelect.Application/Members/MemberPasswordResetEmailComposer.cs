using DoSelect.Application.Notifications;

namespace DoSelect.Application.Members;

internal static class MemberPasswordResetEmailComposer
{
    public static EmailMessage Compose(
        string email,
        Guid publicId,
        string token,
        string frontendBaseUrl)
    {
        var resetLink =
            $"{frontendBaseUrl.TrimEnd('/')}/reset-password" +
            $"?publicId={publicId:D}" +
            $"&token={Uri.EscapeDataString(token)}";

        return new EmailMessage(
            email,
            "重設您的懂選帳號密碼",
            $"我們收到您的密碼重設請求。請於 1 小時內點擊以下連結設定新密碼：\n{resetLink}\n\n若您沒有提出這個請求，請忽略此信。",
            $"<p>我們收到您的密碼重設請求。請於 1 小時內點擊以下連結設定新密碼：</p><p><a href=\"{resetLink}\">{resetLink}</a></p><p>若您沒有提出這個請求，請忽略此信。</p>");
    }
}
