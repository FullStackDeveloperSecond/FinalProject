using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace DoSelect.Application.Common;

/// <summary>
/// In-memory, per-process implementation of <see cref="IEmailRequestThrottle"/> built on the BCL
/// fixed-window rate limiter. No persistence: on process restart every budget resets. That is an
/// accepted tradeoff — a single-instance deployment does not need a shared store, and adding one
/// would mean a schema change that is out of scope here.
/// </summary>
public sealed class EmailRequestThrottle : IEmailRequestThrottle, IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public EmailRequestThrottle(IOptions<RateLimitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var permitLimit = options.Value.EmailPurposePermitLimit;
        var window = TimeSpan.FromHours(options.Value.EmailPurposeWindowHours);

        _limiter = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

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
