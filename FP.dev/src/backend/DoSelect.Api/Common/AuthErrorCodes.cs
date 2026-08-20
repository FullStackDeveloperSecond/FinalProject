namespace DoSelect.Api.Common;

public static class AuthErrorCodes
{
    public const string AccountEmailInUse = "account_email_in_use";
    public const string EmailTokenInvalid = "email_token_invalid";
    public const string AccountEmailUnverified = "account_email_unverified";
    public const string AccountSuspended = "account_suspended";
    public const string AccountLocked = "account_locked";
    public const string InvalidCredentials = "invalid_credentials";
}
