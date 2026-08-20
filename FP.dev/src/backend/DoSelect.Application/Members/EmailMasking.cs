namespace DoSelect.Application.Members;

public static class EmailMasking
{
    public static string Mask(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
        {
            return "***";
        }

        var localPart = email[..atIndex];
        var domainPart = email[atIndex..];
        var visible = localPart[..1];
        return $"{visible}{new string('*', Math.Max(localPart.Length - 1, 1))}{domainPart}";
    }
}
