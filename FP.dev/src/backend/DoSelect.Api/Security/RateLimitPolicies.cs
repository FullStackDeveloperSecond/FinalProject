namespace DoSelect.Api.Security;

public static class RateLimitPolicies
{
    public const string AuthRegister = "auth-register";
    public const string AuthResendVerification = "auth-resend-verification";
    public const string AuthForgotPassword = "auth-forgot-password";
    public const string AuthLogin = "auth-login";
}
