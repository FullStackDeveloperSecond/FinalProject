using DoSelect.Domain.Orders;

namespace DoSelect.Domain.Tests;

public sealed class GuestCheckoutEmailVerificationTests
{
    private static readonly DateTime Now = new(2026, 9, 9, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CorrectCode_BindsProofToEmailAndCart_AndCanOnlyBeConsumedOnce()
    {
        var codeHash = Hash(1);
        var verification = Create(codeHash);

        Assert.True(verification.TryVerify(codeHash, Hash(5), Now.AddMinutes(1)));
        Assert.True(verification.MatchesProof(Hash(5), Hash(3), Hash(2), Now.AddMinutes(2)));
        Assert.False(verification.MatchesProof(Hash(5), Hash(4), Hash(2), Now.AddMinutes(2)));

        verification.Consume(Now.AddMinutes(2));
        Assert.Throws<InvalidOperationException>(() => verification.Consume(Now.AddMinutes(3)));
    }

    [Fact]
    public void FifthWrongCode_LocksChallenge()
    {
        var verification = Create(Hash(1));

        for (var attempt = 0; attempt < GuestCheckoutEmailVerification.MaximumAttempts; attempt++)
        {
            Assert.False(verification.TryVerify(Hash(9), Hash(5), Now.AddSeconds(attempt + 1)));
        }

        Assert.NotNull(verification.LockedAtUtc);
        Assert.Throws<InvalidOperationException>(() =>
            verification.TryVerify(Hash(1), Hash(5), Now.AddMinutes(1)));
    }

    private static GuestCheckoutEmailVerification Create(byte[] codeHash) => new(
        Guid.NewGuid(), "BUYER@EXAMPLE.COM", Hash(2), Hash(3), Hash(4), codeHash,
        Now.AddMinutes(10), Now);

    private static byte[] Hash(byte value) => Enumerable.Repeat(value, 32).ToArray();
}
