namespace DoSelect.Application.Common;

/// <summary>
/// Per-target-email request budget for auth flows that accept an email address (register,
/// resend-verification, forgot-password). This protects a specific account from being email-
/// bombed or repeatedly probed — Identity's Lockout only throttles failed login attempts against
/// an account and does nothing for these other purposes. Complements per-IP limiting (applied via
/// ASP.NET Core's RateLimiter middleware on the endpoints), which protects against a single caller
/// hammering many different accounts.
/// </summary>
public interface IEmailRequestThrottle
{
    /// <summary>
    /// Attempts to consume one unit of the budget for <paramref name="purpose"/> and
    /// <paramref name="email"/>. Returns <see langword="false"/> once the purpose-scoped budget
    /// for that email is exhausted for the current window.
    /// </summary>
    bool TryAcquire(string purpose, string email);
}
