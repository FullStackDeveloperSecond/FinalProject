using System.Diagnostics;
using System.Text.RegularExpressions;
using DoSelect.Application.Members;
using DoSelect.Application.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests;

/// <summary>
/// Both password-reset and email-verification requests always return the same 202/void shape
/// regardless of whether the target email belongs to an eligible account (API DTO與Schema契約.md:
/// 永遠回 202，不揭露帳號). That only holds if the two code paths also take the same amount of
/// *time* — otherwise response latency itself becomes an oracle an attacker can use to enumerate
/// accounts. These tests drive the Application services directly (bypassing HTTP, antiforgery,
/// and rate-limiting middleware, none of which differ between the eligible/ineligible branches)
/// against the real SQL-Server-backed gateway, and compare measured latency.
///
/// The 20-sample / 20ms-tolerance / median-comparison approach is the finalized V1 acceptance
/// threshold (Alex review decision A1, 2026-08-25): it accepts "reduced distinguishability" on
/// SQL Server with 20 interleaved samples and a 20ms median-difference budget, not a strict
/// timing-attack-proof guarantee — CI runners are noisy shared machines, so this deliberately
/// trades sensitivity for stability rather than chasing sub-millisecond precision. Register
/// additionally still carries a small residual gap from the fresh path's extra MemberProfile
/// INSERT (see MemberRegistrationGateway.CreateMemberAsync), which the per-email/IP throttles in
/// EmailRequestThrottle/RateLimitPolicies bound the practical exploitability of.
/// </summary>
public sealed class TimingSideChannelTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const int SampleCount = 20;
    private const double ToleranceMilliseconds = 20;

    [Fact]
    public async Task RequestPasswordReset_EligibleAndIneligibleAccounts_HaveNoReliablyDistinguishableLatency()
    {
        var capturingEmailSender = new CapturingEmailSender();
        using var isolatedFactory = CreateIsolatedFactory(capturingEmailSender);

        var eligibleEmails = await CreateActivatedMembersAsync(isolatedFactory, capturingEmailSender, SampleCount);
        var ineligibleEmails = Enumerable.Range(0, SampleCount).Select(_ => UniqueEmail()).ToList();

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var requestService = scope.ServiceProvider.GetRequiredService<RequestPasswordResetService>();

        var (eligibleLatencies, ineligibleLatencies) = await MeasureInterleavedAsync(
            eligibleEmails,
            ineligibleEmails,
            email => requestService.RequestAsync(new RequestPasswordResetCommand(email)));

        AssertNoReliableTimingDifference(eligibleLatencies, ineligibleLatencies);
    }

    [Fact]
    public async Task RequestEmailVerification_EligibleAndIneligibleAccounts_HaveNoReliablyDistinguishableLatency()
    {
        var capturingEmailSender = new CapturingEmailSender();
        using var isolatedFactory = CreateIsolatedFactory(capturingEmailSender);

        var eligibleEmails = await CreateUnconfirmedMembersAsync(isolatedFactory, SampleCount);
        var ineligibleEmails = Enumerable.Range(0, SampleCount).Select(_ => UniqueEmail()).ToList();

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var requestService = scope.ServiceProvider.GetRequiredService<RequestEmailVerificationService>();

        var (eligibleLatencies, ineligibleLatencies) = await MeasureInterleavedAsync(
            eligibleEmails,
            ineligibleEmails,
            email => requestService.RequestAsync(new RequestEmailVerificationCommand(email)));

        AssertNoReliableTimingDifference(eligibleLatencies, ineligibleLatencies);
    }

    [Fact]
    public async Task Register_FreshAndAlreadyRegisteredEmails_HaveNoReliablyDistinguishableLatency()
    {
        // A fresh registration hashes the password, opens a transaction, inserts the User and
        // MemberProfile rows, and generates a token; an already-registered email must pay a
        // comparable cost rather than short-circuiting, or response latency itself becomes an
        // account-enumeration oracle even though both responses have the same shape, status, and
        // synthetic-PublicId UUID version (Alex review, 2026-08-24). MemberRegistrationGateway no
        // longer pre-checks FindByEmailAsync for exactly this reason — see its CreateMemberAsync.
        var capturingEmailSender = new CapturingEmailSender();
        using var isolatedFactory = CreateIsolatedFactory(capturingEmailSender);

        var alreadyRegisteredEmails = await CreateUnconfirmedMembersAsync(isolatedFactory, SampleCount);
        var freshEmails = Enumerable.Range(0, SampleCount).Select(_ => UniqueEmail()).ToList();

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var registerService = scope.ServiceProvider.GetRequiredService<RegisterMemberService>();

        var (freshLatencies, alreadyRegisteredLatencies) = await MeasureInterleavedAsync(
            freshEmails,
            alreadyRegisteredEmails,
            email => registerService.RegisterAsync(new RegisterMemberCommand(
                email,
                "correct-horse-battery-staple",
                "整合測試會員",
                null,
                RegisterMemberService.CurrentTermsVersion)));

        AssertNoReliableTimingDifference(freshLatencies, alreadyRegisteredLatencies);
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(CapturingEmailSender capturingEmailSender) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
            });
        });

    private static async Task<List<string>> CreateActivatedMembersAsync(
        WebApplicationFactory<Program> isolatedFactory,
        CapturingEmailSender capturingEmailSender,
        int count)
    {
        var emails = new List<string>();
        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var registerService = scope.ServiceProvider.GetRequiredService<RegisterMemberService>();
        var confirmService = scope.ServiceProvider.GetRequiredService<ConfirmEmailVerificationService>();

        for (var i = 0; i < count; i++)
        {
            var email = UniqueEmail();
            var registerResult = await registerService.RegisterAsync(new RegisterMemberCommand(
                email,
                "correct-horse-battery-staple",
                "整合測試會員",
                null,
                RegisterMemberService.CurrentTermsVersion));
            Assert.IsType<RegisterMemberResult.Success>(registerResult);

            var message = await capturingEmailSender.WaitForMessageAtIndexAsync(i);
            var (publicId, token) = ExtractVerificationLink(message.TextBody);

            var confirmResult = await confirmService.ConfirmAsync(new ConfirmEmailVerificationCommand(publicId, token));
            Assert.IsType<ConfirmEmailVerificationResult.Success>(confirmResult);

            emails.Add(email);
        }

        return emails;
    }

    private static async Task<List<string>> CreateUnconfirmedMembersAsync(
        WebApplicationFactory<Program> isolatedFactory,
        int count)
    {
        var emails = new List<string>();
        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var registerService = scope.ServiceProvider.GetRequiredService<RegisterMemberService>();

        for (var i = 0; i < count; i++)
        {
            var email = UniqueEmail();
            var registerResult = await registerService.RegisterAsync(new RegisterMemberCommand(
                email,
                "correct-horse-battery-staple",
                "整合測試會員",
                null,
                RegisterMemberService.CurrentTermsVersion));
            Assert.IsType<RegisterMemberResult.Success>(registerResult);
            emails.Add(email);
        }

        return emails;
    }

    private static async Task<(List<double> Eligible, List<double> Ineligible)> MeasureInterleavedAsync(
        IReadOnlyList<string> eligibleEmails,
        IReadOnlyList<string> ineligibleEmails,
        Func<string, Task> requestAsync)
    {
        // Warm up the JIT / connection pool / query plan cache once for each group before
        // measuring, so first-call overhead does not asymmetrically skew the comparison.
        await requestAsync(UniqueEmail());
        await requestAsync(UniqueEmail());

        var eligibleLatencies = new List<double>();
        var ineligibleLatencies = new List<double>();

        // Interleaved (not "measure all of group A, then all of group B") to resist systematic
        // drift across the run — e.g. a GC pause landing entirely inside one group's block would
        // otherwise masquerade as a real difference between the groups.
        for (var i = 0; i < eligibleEmails.Count; i++)
        {
            eligibleLatencies.Add(await MeasureAsync(() => requestAsync(eligibleEmails[i])));
            ineligibleLatencies.Add(await MeasureAsync(() => requestAsync(ineligibleEmails[i])));
        }

        return (eligibleLatencies, ineligibleLatencies);
    }

    private static async Task<double> MeasureAsync(Func<Task> action)
    {
        var stopwatch = Stopwatch.StartNew();
        await action();
        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static void AssertNoReliableTimingDifference(List<double> eligible, List<double> ineligible)
    {
        var eligibleMedian = Median(eligible);
        var ineligibleMedian = Median(ineligible);
        var difference = Math.Abs(eligibleMedian - ineligibleMedian);

        Assert.True(
            difference <= ToleranceMilliseconds,
            $"Median latency differs by {difference:F2}ms (eligible={eligibleMedian:F2}ms, " +
            $"ineligible={ineligibleMedian:F2}ms) — this exceeds the {ToleranceMilliseconds}ms tolerance and " +
            "may be a distinguishable timing side channel between an eligible and an ineligible account.");
    }

    private static double Median(List<double> samples)
    {
        var sorted = samples.OrderBy(sample => sample).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2
            : sorted[mid];
    }

    private static (Guid PublicId, string Token) ExtractVerificationLink(string emailTextBody)
    {
        var match = Regex.Match(
            emailTextBody,
            @"publicId=(?<publicId>[0-9a-fA-F-]{36})&token=(?<token>\S+)");
        Assert.True(match.Success, $"No verification link found in email body: {emailTextBody}");

        return (
            Guid.Parse(match.Groups["publicId"].Value),
            Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    private static string UniqueEmail() => $"timing-test-{Guid.NewGuid():N}@example.com";

    private sealed class CapturingEmailSender : IEmailSender
    {
        private readonly List<EmailMessage> _sentMessages = [];

        public Task<EmailDeliveryResult> SendAsync(
            EmailMessage message,
            CancellationToken cancellationToken = default)
        {
            lock (_sentMessages)
            {
                _sentMessages.Add(message);
            }

            return Task.FromResult(new EmailDeliveryResult(EmailDeliveryStatus.Sent));
        }

        // Email is dispatched via EmailDispatchBackgroundService (an in-memory Channel consumer
        // running outside the caller), so it can arrive a beat after RegisterAsync returns. Poll
        // for the specific index instead of asserting immediately to avoid flaking.
        public async Task<EmailMessage> WaitForMessageAtIndexAsync(int index, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
            while (DateTime.UtcNow < deadline)
            {
                lock (_sentMessages)
                {
                    if (_sentMessages.Count > index)
                    {
                        return _sentMessages[index];
                    }
                }

                await Task.Delay(10);
            }

            lock (_sentMessages)
            {
                return _sentMessages[index];
            }
        }
    }
}
