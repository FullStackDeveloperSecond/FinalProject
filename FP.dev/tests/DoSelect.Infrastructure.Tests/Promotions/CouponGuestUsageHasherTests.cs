using System.Security.Cryptography;
using System.Text;
using DoSelect.Infrastructure.Promotions;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests.Promotions;

public sealed class CouponGuestUsageHasherTests
{
    private const string Key = "coupon-guest-usage-v1-test-key-32-bytes-minimum";

    [Fact]
    public void HashEmail_NormalizesWhitespaceAndCaseBeforeHmacSha256()
    {
        var hasher = CreateHasher(Key);

        var actual = hasher.HashEmail("  Guest@Example.Test  ");
        var expected = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Key),
            Encoding.UTF8.GetBytes("guest@example.test"));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void HashEmail_ProducesDifferentKeysForDifferentNormalizedEmails()
    {
        var hasher = CreateHasher(Key);

        var first = hasher.HashEmail("first@example.test");
        var second = hasher.HashEmail("second@example.test");

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    public void HashEmail_WhenV1SecretIsMissingOrTooShort_FailsClosed(string key)
    {
        var hasher = CreateHasher(key);

        var exception = Assert.Throws<InvalidOperationException>(
            () => hasher.HashEmail("guest@example.test"));

        Assert.Contains("Security:CouponGuestUsageHmacKeyV1", exception.Message);
    }

    private static CouponGuestUsageHasher CreateHasher(string key) =>
        new(Options.Create(new CouponGuestUsageOptions
        {
            CouponGuestUsageHmacKeyV1 = key,
        }));
}
