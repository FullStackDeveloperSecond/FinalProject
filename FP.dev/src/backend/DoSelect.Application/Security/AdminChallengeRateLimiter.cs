using System.Threading.RateLimiting;
using DoSelect.Application.Common;
using Microsoft.Extensions.Options;

namespace DoSelect.Application.Security;

/// <summary>
/// In-memory、單一 process 內的固定視窗限流實作，跟 <see cref="EmailRequestThrottle"/> 同一套
/// BCL PartitionedRateLimiter 手法。不持久化——重啟後配額重置，單一實例部署可接受這個取捨。
/// </summary>
public sealed class AdminChallengeRateLimiter : IAdminChallengeRateLimiter, IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public AdminChallengeRateLimiter(IOptions<RateLimitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var permitLimit = options.Value.AdminChallengePermitLimit;
        var window = TimeSpan.FromMinutes(options.Value.AdminChallengeWindowMinutes);

        _limiter = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    public bool TryAcquire(string ipAddress, string challengeKey, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(challengeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var key = $"{ipAddress}:{challengeKey}:{userId}";
        using var lease = _limiter.AttemptAcquire(key);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
