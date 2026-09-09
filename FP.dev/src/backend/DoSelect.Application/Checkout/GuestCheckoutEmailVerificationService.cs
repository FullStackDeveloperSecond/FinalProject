using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using DoSelect.Application.Common;
using DoSelect.Application.Notifications;
using DoSelect.Application.Orders;
using DoSelect.Application.Outbox;
using DoSelect.Domain.Orders;
using Microsoft.Extensions.Options;

namespace DoSelect.Application.Checkout;

public sealed record GuestCheckoutEmailVerificationRequest(
    [Required, EmailAddress, StringLength(320)] string Email);

public sealed record GuestCheckoutEmailVerificationCodeRequest(
    Guid RequestPublicId,
    [Required, RegularExpression("^[0-9]{6}$")] string Code);

public sealed record GuestCheckoutEmailVerificationAcceptedDto(
    Guid RequestPublicId,
    DateTime ExpiresAtUtc);

public sealed record GuestCheckoutEmailVerifiedDto(bool Verified, DateTime ExpiresAtUtc);

public sealed record GuestCheckoutEmailVerificationStatusDto(
    bool Verified,
    string? Email,
    DateTime? ExpiresAtUtc);

public sealed record GuestCheckoutVerificationRateLimitWindow(
    byte[] IpHash,
    int IpPermitLimit,
    byte[] EmailHash,
    int EmailPermitLimit,
    byte[] GuestCartKeyHash,
    int GuestCartPermitLimit,
    DateTime WindowStartUtc);

public interface IGuestCheckoutEmailVerificationGateway
{
    Task<bool> TryCreateAsync(
        GuestCheckoutVerificationRateLimitWindow window,
        GuestCheckoutEmailVerification verification,
        OutboxWriteRequest notification,
        CancellationToken cancellationToken = default);

    Task<GuestCheckoutEmailVerification?> FindActiveAsync(
        Guid requestPublicId,
        byte[] guestCartKeyHash,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<GuestCheckoutEmailVerification?> FindByProofAsync(
        byte[] proofTokenHash,
        byte[] guestCartKeyHash,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public abstract record GuestCheckoutEmailRequestResult
{
    public sealed record Accepted(Guid RequestPublicId, DateTime ExpiresAtUtc)
        : GuestCheckoutEmailRequestResult;
    public sealed record RateLimited : GuestCheckoutEmailRequestResult;
}

public abstract record GuestCheckoutEmailVerifyResult
{
    public sealed record Success(string RawProofToken, DateTime ExpiresAtUtc)
        : GuestCheckoutEmailVerifyResult;
    public sealed record Failure : GuestCheckoutEmailVerifyResult;
}

public static class GuestCheckoutEmailClaimTypes
{
    public const string ProofToken = "doselect:guest_checkout_email_proof";
}

public static class GuestCheckoutEmailNotificationContract
{
    public const string TemplateKey = "guest.checkout_email.verification_code";
    public const string RecipientPurpose = "guest_checkout.email";
    public const string ResourceType = "GuestCheckoutEmailVerification";
    public const string Locale = "zh-TW";
}

public static class GuestCheckoutEmailComposer
{
    public static EmailMessage Compose(string email, string code, string verificationLink) => new(
        email,
        "您的懂選結帳信箱驗證碼",
        $"驗證碼為：{code}\n請開啟此連結完成結帳信箱驗證：{verificationLink}\n此驗證將於 10 分鐘後失效。",
        $"<p>驗證碼為：<strong>{code}</strong></p>" +
        $"<p><a href=\"{verificationLink}\">驗證信箱並返回結帳</a></p>" +
        "<p>此驗證將於 10 分鐘後失效。若非您本人操作，請忽略此信。</p>");
}

public sealed class GuestCheckoutEmailVerificationService(
    IGuestCheckoutEmailVerificationGateway gateway,
    IGuestOrderAccessHasher hasher,
    IOptions<RateLimitOptions> rateLimitOptions,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public async Task<GuestCheckoutEmailRequestResult> RequestAsync(
        string email,
        string guestCartKey,
        string requesterIp,
        CancellationToken cancellationToken = default)
    {
        var emailNormalized = NormalizeEmail(email);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var publicId = Guid.CreateVersion7();
        const int parameterSetVersion = 1;
        var code = hasher.DeriveVerificationCode(publicId, parameterSetVersion);
        var emailHash = hasher.HashEmail(emailNormalized);
        var cartHash = hasher.HashGuestCartKey(guestCartKey);
        var ipHash = hasher.HashIp(requesterIp);
        var verification = new GuestCheckoutEmailVerification(
            publicId,
            emailNormalized,
            emailHash,
            cartHash,
            ipHash,
            hasher.HashCode(code),
            now.Add(Lifetime),
            now);
        var options = rateLimitOptions.Value;
        var window = new GuestCheckoutVerificationRateLimitWindow(
            ipHash,
            options.GuestOrderAccessIpPermitLimit,
            emailHash,
            options.GuestOrderAccessEmailPermitLimit,
            cartHash,
            options.GuestOrderAccessOrderLookupPermitLimit,
            now.AddMinutes(-options.GuestOrderAccessWindowMinutes));
        var notificationPublicId = Guid.CreateVersion7();
        var notification = OutboxWriteRequest.Create(
            notificationPublicId,
            GuestCheckoutEmailNotificationContract.ResourceType,
            publicId,
            new EmailNotificationRequestedV1(
                notificationPublicId,
                GuestCheckoutEmailNotificationContract.TemplateKey,
                GuestCheckoutEmailNotificationContract.RecipientPurpose,
                GuestCheckoutEmailNotificationContract.ResourceType,
                publicId,
                GuestCheckoutEmailNotificationContract.Locale,
                parameterSetVersion),
            now,
            now,
            publicId.ToString("N"));

        return await gateway.TryCreateAsync(window, verification, notification, cancellationToken)
            ? new GuestCheckoutEmailRequestResult.Accepted(publicId, verification.ExpiresAtUtc)
            : new GuestCheckoutEmailRequestResult.RateLimited();
    }

    public async Task<GuestCheckoutEmailVerifyResult> VerifyAsync(
        Guid requestPublicId,
        string code,
        string guestCartKey,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var verification = await gateway.FindActiveAsync(
            requestPublicId, hasher.HashGuestCartKey(guestCartKey), now, cancellationToken);
        if (verification is null)
        {
            return new GuestCheckoutEmailVerifyResult.Failure();
        }

        var rawProof = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        try
        {
            var verified = verification.TryVerify(
                hasher.HashCode(code), hasher.HashToken(rawProof), now);
            await gateway.SaveChangesAsync(cancellationToken);
            return verified
                ? new GuestCheckoutEmailVerifyResult.Success(rawProof, verification.ExpiresAtUtc)
                : new GuestCheckoutEmailVerifyResult.Failure();
        }
        catch (InvalidOperationException)
        {
            return new GuestCheckoutEmailVerifyResult.Failure();
        }
    }

    public async Task<GuestCheckoutEmailVerificationStatusDto> GetStatusAsync(
        string? rawProofToken,
        string guestCartKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawProofToken))
        {
            return new(false, null, null);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var verification = await gateway.FindByProofAsync(
            hasher.HashToken(rawProofToken), hasher.HashGuestCartKey(guestCartKey), now, cancellationToken);
        return verification is null
            ? new(false, null, null)
            : new(true, verification.EmailNormalized, verification.ExpiresAtUtc);
    }

    private static string NormalizeEmail(string email) =>
        string.IsNullOrWhiteSpace(email)
            ? throw DomainProblemException.Validation("Email is required.")
            : email.Trim().ToLowerInvariant();
}
