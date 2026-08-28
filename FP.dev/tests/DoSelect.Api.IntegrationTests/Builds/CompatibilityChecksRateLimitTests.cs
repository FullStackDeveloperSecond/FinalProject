using System.Net;

namespace DoSelect.Api.IntegrationTests.Builds;

/// <summary>
/// Deliberately not sharing <see cref="CompatibilityChecksApiCollection"/>'s fixture — this test
/// exhausts the endpoint's own rate limiter, which would otherwise randomly 429 sibling tests in
/// that collection depending on xUnit's run order. Owns a private, disposed-after-the-test
/// <see cref="CompatibilityChecksApiFixture"/> instead (組長 PR #34 round-4 review, item 3).
/// </summary>
[Trait("Category", "RequiresSqlServer")]
public sealed class CompatibilityChecksRateLimitTests
{
    /// <summary>
    /// 組長 PR #34 review: only asserting the 31st call's status can't tell a correctly-configured
    /// 30/minute limiter from a duplicated `UseRateLimiter()` middleware registration silently
    /// halving the real budget — both eventually 429 by call 31, so a "does the 31st call 429"
    /// check passes either way. Recording every call's status and asserting the first 30 are ALL
    /// non-429 (not just "not exactly call 31") is what would have actually caught the duplicate
    /// middleware bug.
    /// </summary>
    [Fact]
    public async Task Check_WhenCalledMoreThanThirtyTimesInOneMinuteFromTheSameClient_Returns429AfterTheLimit()
    {
        var fixture = new CompatibilityChecksApiFixture();
        await fixture.InitializeAsync();
        try
        {
            var payload = new { items = new object[] { new { skuPublicId = Guid.NewGuid(), quantity = 1 } } };
            var statuses = new List<HttpStatusCode>();
            for (var i = 0; i < 31; i++)
            {
                using var response = await CompatibilityChecksApiFixture.PostWithAntiforgeryAsync(
                    fixture.Client, "/api/v1/compatibility-checks", payload);
                statuses.Add(response.StatusCode);
            }

            Assert.All(statuses.Take(30), status => Assert.NotEqual(HttpStatusCode.TooManyRequests, status));
            Assert.Equal(HttpStatusCode.TooManyRequests, statuses[30]);

            using var overLimitResponse = await CompatibilityChecksApiFixture.PostWithAntiforgeryAsync(
                fixture.Client, "/api/v1/compatibility-checks", payload);
            var (status, code, _) = await CompatibilityChecksApiFixture.ReadProblemAsync(overLimitResponse);
            Assert.Equal((int)HttpStatusCode.TooManyRequests, status);
            Assert.Equal("rate_limit_exceeded", code);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }
}
