namespace DoSelect.Application.Security;

/// <summary>
/// 純顯示用的 Email 遮蔽（例如 <c>ab***@doselect.local</c>）。
/// 對應 API DTO 契約 CurrentUserDto.emailMasked——回應一律不含明文 Email。
/// </summary>
public static class EmailMasking
{
    public static string Mask(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
        {
            return "***";
        }

        var localPart = email[..atIndex];
        var domainPart = email[atIndex..];
        var visibleLength = Math.Min(2, localPart.Length);
        var visible = localPart[..visibleLength];
        var maskedLength = Math.Max(localPart.Length - visibleLength, 1);

        return $"{visible}{new string('*', maskedLength)}{domainPart}";
    }
}
