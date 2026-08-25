using DoSelect.Application.Security;
using Microsoft.Extensions.Options;

namespace DoSelect.Application.Tests;

/// <summary>
/// alex review 第二輪 P1#2 核心回歸測試：IP、challenge、帳號必須是三個各自獨立的
/// bucket，不能只用組合 key（換一個維度就能繞過限流）。
/// </summary>
public sealed class AdminChallengeRateLimiterTests
{
    [Fact]
    public void TryAcquire_WhenAccountBudgetIsExhausted_ChangingIpAndChallengeDoesNotBypassIt()
    {
        using var limiter = CreateLimiter(permitLimit: 2);

        Assert.True(limiter.TryAcquire("1.1.1.1", "challenge-a", "user-1"));
        Assert.True(limiter.TryAcquire("1.1.1.1", "challenge-a", "user-1"));

        // 換了全新的 IP 跟 challenge，但帳號還是同一個——如果實作只用組合 key，這裡會
        // 因為組合整體是新的而重新拿到額度；三個 bucket 各自獨立時，帳號那個 bucket
        // 已經用完，整體應該被拒絕。
        Assert.False(limiter.TryAcquire("2.2.2.2", "challenge-b", "user-1"));
    }

    [Fact]
    public void TryAcquire_WhenIpBudgetIsExhausted_ChangingChallengeAndAccountDoesNotBypassIt()
    {
        using var limiter = CreateLimiter(permitLimit: 2);

        Assert.True(limiter.TryAcquire("1.1.1.1", "challenge-a", "user-1"));
        Assert.True(limiter.TryAcquire("1.1.1.1", "challenge-b", "user-2"));

        // 同一個 IP，但換了全新的 challenge 跟帳號——IP 那個 bucket 已經用完。
        Assert.False(limiter.TryAcquire("1.1.1.1", "challenge-c", "user-3"));
    }

    [Fact]
    public void TryAcquire_WhenChallengeBudgetIsExhausted_ChangingIpAndAccountDoesNotBypassIt()
    {
        using var limiter = CreateLimiter(permitLimit: 2);

        Assert.True(limiter.TryAcquire("1.1.1.1", "challenge-shared", "user-1"));
        Assert.True(limiter.TryAcquire("2.2.2.2", "challenge-shared", "user-2"));

        Assert.False(limiter.TryAcquire("3.3.3.3", "challenge-shared", "user-3"));
    }

    [Fact]
    public void TryAcquire_DifferentIpsChallengesAndAccounts_EachGetsItsOwnBudget()
    {
        using var limiter = CreateLimiter(permitLimit: 1);

        Assert.True(limiter.TryAcquire("1.1.1.1", "challenge-a", "user-1"));
        Assert.True(limiter.TryAcquire("2.2.2.2", "challenge-b", "user-2"));
    }

    private static AdminChallengeRateLimiter CreateLimiter(int permitLimit) =>
        new(Options.Create(new RateLimitOptions
        {
            AdminChallengePermitLimit = permitLimit,
            AdminChallengeWindowMinutes = 15,
        }));
}
