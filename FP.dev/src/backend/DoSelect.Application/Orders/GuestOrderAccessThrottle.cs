using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using DoSelect.Application.Common;

namespace DoSelect.Application.Orders;

/// <summary>
/// In-memory, per-process 實作，比照 <see cref="EmailRequestThrottle"/>：三個獨立的固定視窗
/// 限流器，Key 直接用呼叫端算好的 Hash（不重算、不接觸明文 IP／Email／訂單編號）。
/// 沒有持久化——Process 重啟會重置預算，跟既有 EmailRequestThrottle／ChallengeAttemptThrottle
/// 的取捨一致。
/// </summary>
public sealed class GuestOrderAccessThrottle : IGuestOrderAccessThrottle, IDisposable
{
    private readonly PartitionedRateLimiter<string> _ipLimiter;
    private readonly PartitionedRateLimiter<string> _emailLimiter;
    private readonly PartitionedRateLimiter<string> _orderLookupLimiter;

    public GuestOrderAccessThrottle(IOptions<RateLimitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var window = TimeSpan.FromMinutes(options.Value.GuestOrderAccessWindowMinutes);
        _ipLimiter = CreateLimiter(options.Value.GuestOrderAccessIpPermitLimit, window);
        _emailLimiter = CreateLimiter(options.Value.GuestOrderAccessEmailPermitLimit, window);
        _orderLookupLimiter = CreateLimiter(
            options.Value.GuestOrderAccessOrderLookupPermitLimit, window);
    }

    public bool TryAcquireIp(byte[] ipHash) => TryAcquire(_ipLimiter, ipHash);

    public bool TryAcquireEmail(byte[] emailHash) => TryAcquire(_emailLimiter, emailHash);

    public bool TryAcquireOrderLookup(byte[] orderLookupHash) =>
        TryAcquire(_orderLookupLimiter, orderLookupHash);

    public void Dispose()
    {
        _ipLimiter.Dispose();
        _emailLimiter.Dispose();
        _orderLookupLimiter.Dispose();
    }

    private static bool TryAcquire(PartitionedRateLimiter<string> limiter, byte[] hash)
    {
        var key = Convert.ToHexStringLower(hash);
        using var lease = limiter.AttemptAcquire(key);
        return lease.IsAcquired;
    }

    private static PartitionedRateLimiter<string> CreateLimiter(int permitLimit, TimeSpan window) =>
        PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
}
