namespace DoSelect.Domain.Members;

public enum AccountType
{
    Member,
    Admin,
}

public enum AccountStatus
{
    PendingEmailVerification,
    Active,
    Suspended,
    Anonymized,
    Disabled,
}

public enum SupportedLocale
{
    ZhTw,
    JaJp,
    KoKr,
}
