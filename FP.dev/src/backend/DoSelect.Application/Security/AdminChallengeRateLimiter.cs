using System.Threading.RateLimiting;
using DoSelect.Application.Common;
using Microsoft.Extensions.Options;

namespace DoSelect.Application.Security;

/// <summary>
/// In-memory、單一 process 內的固定視窗限流實作，跟 <see cref="EmailRequestThrottle"/> 同一套
/// BCL PartitionedRateLimiter 手法。不持久化——重啟後配額重置。已確認這個取捨在本專案
/// 成立：[[知識點/07-基礎設施與交付/CI與CD]] 記載 V1 只在單一 Windows 展示電腦執行、
/// 不部署公網、無水平擴展，不會有多個 API instance 各自維護獨立記憶體配額導致限流被
/// 繞過的問題（alex review 第二輪 P1#2 確認事項）。
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

        // ⚠ alex review 第二輪 P1#2：原本用單一組合 key（ip:challenge:userId），換一個
        // 維度（重新登入拿到新 challenge、或換 IP）就能拿到全新額度，形成繞過。改成三個
        // 各自獨立的 bucket，同一次嘗試三個都要消耗，任一超限就整體拒絕。前綴避免不同
        // 維度的原始值剛好相同時互相污染彼此的桶。
        using var ipLease = _limiter.AttemptAcquire($"ip:{ipAddress}");
        using var challengeLease = _limiter.AttemptAcquire($"challenge:{challengeKey}");
        using var accountLease = _limiter.AttemptAcquire($"account:{userId}");

        return ipLease.IsAcquired && challengeLease.IsAcquired && accountLease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
