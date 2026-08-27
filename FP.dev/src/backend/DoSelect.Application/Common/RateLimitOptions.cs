namespace DoSelect.Application.Common;

/// <summary>
/// V1 展示版限流門檻，經 Alex 裁定定版（2026-08-24 review，方案 A1）：
/// 每 Email／用途 3 次／小時；register／resend-verification／forgot-password 每 IP 5 次／小時；
/// login 每 IP 20 次／小時。正式上線前依監控數據重新評估（login 對共用 NAT 可能偏嚴格）。
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Per email address + purpose (register / resend-verification / forgot-password).</summary>
    public int EmailPurposePermitLimit { get; set; } = 3;

    public int EmailPurposeWindowHours { get; set; } = 1;

    /// <summary>Per IP, for register / resend-verification / forgot-password.</summary>
    public int PerIpPermitLimit { get; set; } = 5;

    public int PerIpWindowHours { get; set; } = 1;

    /// <summary>Per IP, for login.</summary>
    public int LoginPerIpPermitLimit { get; set; } = 20;

    public int LoginPerIpWindowHours { get; set; } = 1;

    /// <summary>
    /// Per (IP, challenge, admin account), for the 2FA challenge-guessing endpoints (TOTP verify,
    /// recovery-code redeem, enrollment confirm, rebind confirm) — alex review P1#3.
    /// </summary>
    public int AdminChallengePermitLimit { get; set; } = 5;

    public int AdminChallengeWindowMinutes { get; set; } = 15;
}
