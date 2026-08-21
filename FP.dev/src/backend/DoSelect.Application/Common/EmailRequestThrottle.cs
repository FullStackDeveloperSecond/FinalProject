using System.Threading.RateLimiting;

namespace DoSelect.Application.Common;

/// <summary>
/// In-memory, per-process implementation of <see cref="IEmailRequestThrottle"/> built on the BCL
/// fixed-window rate limiter. No persistence: on process restart every budget resets. That is an
/// accepted tradeoff — a single-instance deployment does not need a shared store, and adding one
/// would mean a schema change that is out of scope here.
/// </summary>
public sealed class EmailRequestThrottle : IEmailRequestThrottle, IDisposable
{
    // 3 requests per purpose per email per hour (register / resend-verification /
    // forgot-password). Placeholder default pending a product decision — see PR discussion.
    private const int PermitLimit = 3;
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly PartitionedRateLimiter<string> _limiter =
        PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PermitLimit,
                Window = Window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    public bool TryAcquire(string purpose, string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var key = $"{purpose}:{email.Trim().ToUpperInvariant()}";
        using var lease = _limiter.AttemptAcquire(key);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
