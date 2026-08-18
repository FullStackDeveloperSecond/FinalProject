namespace DoSelect.Infrastructure.Email;

public sealed class SmtpEmailOptions
{
    public const string SectionName = "Email";

    public string SmtpHost { get; set; } = string.Empty;

    public int SmtpPort { get; set; } = 587;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string SenderName { get; set; } = "alex";

    public string SenderAddress { get; set; } = string.Empty;

    public int TimeoutMilliseconds { get; set; } = 15000;
}
