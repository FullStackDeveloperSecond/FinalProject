using System.Security.Cryptography;
using System.Text;
using DoSelect.Application.Checkout;
using DoSelect.Application.Common;
using DoSelect.Application.Orders;
using DoSelect.Application.Outbox;
using DoSelect.Domain.Orders;
using Microsoft.Extensions.Options;

namespace DoSelect.Application.Tests.Checkout;

public sealed class GuestCheckoutEmailVerificationServiceTests
{
    private static readonly DateTimeOffset Now = new(
        2026, 9, 9, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RequestAsync_BindsNormalizedEmailCartAndRateLimitScopes()
    {
        var gateway = new FakeGateway();
        var service = CreateService(gateway);

        var result = Assert.IsType<GuestCheckoutEmailRequestResult.Accepted>(
            await service.RequestAsync(
                " Buyer@Example.COM ", "guest-cart-key", "127.0.0.1"));

        Assert.Equal(Now.UtcDateTime.AddMinutes(10), result.ExpiresAtUtc);
        Assert.Equal("buyer@example.com", gateway.Verification?.EmailNormalized);
        Assert.Equal(10, gateway.Window?.IpPermitLimit);
        Assert.Equal(5, gateway.Window?.EmailPermitLimit);
        Assert.Equal(5, gateway.Window?.GuestCartPermitLimit);
        Assert.Equal(Now.UtcDateTime.AddMinutes(-15), gateway.Window?.WindowStartUtc);
        var payload = Assert.IsType<EmailNotificationRequestedV1>(gateway.Notification?.Payload);
        Assert.Equal(GuestCheckoutEmailNotificationContract.TemplateKey, payload.TemplateKey);
        Assert.Equal(result.RequestPublicId, payload.ResourcePublicId);
    }

    [Fact]
    public async Task VerifyAsync_WrongCodePersistsAttemptWithoutIssuingProof()
    {
        var gateway = new FakeGateway();
        var service = CreateService(gateway);
        var accepted = Assert.IsType<GuestCheckoutEmailRequestResult.Accepted>(
            await service.RequestAsync("buyer@example.com", "guest-cart-key", "127.0.0.1"));

        var result = await service.VerifyAsync(
            accepted.RequestPublicId, "000000", "guest-cart-key");

        Assert.IsType<GuestCheckoutEmailVerifyResult.Failure>(result);
        Assert.Equal(1, gateway.SaveCount);
        Assert.Equal(1, gateway.Verification?.AttemptCount);
        Assert.Null(gateway.Verification?.ProofTokenHash);
    }

    [Fact]
    public async Task VerifyAndStatus_RequireTheSameGuestCart()
    {
        var gateway = new FakeGateway();
        var service = CreateService(gateway);
        var accepted = Assert.IsType<GuestCheckoutEmailRequestResult.Accepted>(
            await service.RequestAsync("buyer@example.com", "guest-cart-key", "127.0.0.1"));
        var verified = Assert.IsType<GuestCheckoutEmailVerifyResult.Success>(
            await service.VerifyAsync(
                accepted.RequestPublicId, StubHasher.VerificationCode, "guest-cart-key"));

        var matching = await service.GetStatusAsync(verified.RawProofToken, "guest-cart-key");
        var differentCart = await service.GetStatusAsync(verified.RawProofToken, "other-cart-key");

        Assert.True(matching.Verified);
        Assert.Equal("buyer@example.com", matching.Email);
        Assert.False(differentCart.Verified);
        Assert.Null(differentCart.Email);
    }

    private static GuestCheckoutEmailVerificationService CreateService(FakeGateway gateway) =>
        new(
            gateway,
            new StubHasher(),
            Options.Create(new RateLimitOptions()),
            new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubHasher : IGuestOrderAccessHasher
    {
        public const string VerificationCode = "123456";

        public byte[] HashIp(string ipAddress) => Hash("ip", ipAddress);
        public byte[] HashEmail(string emailNormalized) => Hash("email", emailNormalized);
        public byte[] HashOrderLookup(string orderNumber, string emailNormalized) =>
            Hash("lookup", $"{orderNumber}:{emailNormalized}");
        public byte[] HashCode(string sixDigitCode) => Hash("code", sixDigitCode);
        public string DeriveVerificationCode(Guid requestPublicId, int sendNumber) => VerificationCode;
        public byte[] HashToken(string rawToken) => Hash("token", rawToken);
        public byte[] HashGuestCartKey(string guestCartKey) => Hash("cart", guestCartKey);

        private static byte[] Hash(string scope, string value) =>
            SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}:{value}"));
    }

    private sealed class FakeGateway : IGuestCheckoutEmailVerificationGateway
    {
        public GuestCheckoutVerificationRateLimitWindow? Window { get; private set; }
        public GuestCheckoutEmailVerification? Verification { get; private set; }
        public OutboxWriteRequest? Notification { get; private set; }
        public int SaveCount { get; private set; }

        public Task<bool> TryCreateAsync(
            GuestCheckoutVerificationRateLimitWindow window,
            GuestCheckoutEmailVerification verification,
            OutboxWriteRequest notification,
            CancellationToken cancellationToken = default)
        {
            Window = window;
            Verification = verification;
            Notification = notification;
            return Task.FromResult(true);
        }

        public Task<GuestCheckoutEmailVerification?> FindActiveAsync(
            Guid requestPublicId,
            byte[] guestCartKeyHash,
            DateTime nowUtc,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Verification?.PublicId == requestPublicId &&
                Verification.GuestCartKeyHash.SequenceEqual(guestCartKeyHash)
                    ? Verification
                    : null);

        public Task<GuestCheckoutEmailVerification?> FindByProofAsync(
            byte[] proofTokenHash,
            byte[] guestCartKeyHash,
            DateTime nowUtc,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Verification?.ProofTokenHash?.SequenceEqual(proofTokenHash) == true &&
                Verification.GuestCartKeyHash.SequenceEqual(guestCartKeyHash) &&
                Verification.ExpiresAtUtc > nowUtc &&
                Verification.ConsumedAtUtc is null
                    ? Verification
                    : null);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
