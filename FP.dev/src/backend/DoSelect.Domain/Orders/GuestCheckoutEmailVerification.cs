using System.Security.Cryptography;
using DoSelect.Domain.Common;

namespace DoSelect.Domain.Orders;

/// <summary>
/// 訪客在建立訂單前的信箱驗證挑戰。驗證證明同時綁定正規化信箱與訪客購物車，
/// 且只能被一次 Checkout 消耗。
/// </summary>
public sealed class GuestCheckoutEmailVerification : MutablePublicEntity
{
    public const int MaximumAttempts = 5;

    private GuestCheckoutEmailVerification()
    {
    }

    public GuestCheckoutEmailVerification(
        Guid publicId,
        string emailNormalized,
        byte[] emailHash,
        byte[] guestCartKeyHash,
        byte[] requesterIpHash,
        byte[] codeHash,
        DateTime expiresAtUtc,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        EmailNormalized = RequireText(emailNormalized, nameof(emailNormalized));
        if (EmailNormalized.Length > 320)
        {
            throw new ArgumentOutOfRangeException(nameof(emailNormalized));
        }

        EmailHash = RequireHash(emailHash, nameof(emailHash));
        GuestCartKeyHash = RequireHash(guestCartKeyHash, nameof(guestCartKeyHash));
        RequesterIpHash = RequireHash(requesterIpHash, nameof(requesterIpHash));
        CodeHash = RequireHash(codeHash, nameof(codeHash));
        ExpiresAtUtc = RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (ExpiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }
    }

    public string EmailNormalized { get; private set; } = string.Empty;
    public byte[] EmailHash { get; private set; } = [];
    public byte[] GuestCartKeyHash { get; private set; } = [];
    public byte[] RequesterIpHash { get; private set; } = [];
    public byte[] CodeHash { get; private set; } = [];
    public byte[]? ProofTokenHash { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? VerifiedAtUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }
    public DateTime? LockedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    public bool TryVerify(byte[] submittedCodeHash, byte[] proofTokenHash, DateTime verifiedAtUtc)
    {
        EnsureActive(verifiedAtUtc);
        var matches = CryptographicOperations.FixedTimeEquals(
            RequireHash(submittedCodeHash, nameof(submittedCodeHash)), CodeHash);
        if (!matches)
        {
            AttemptCount++;
            if (AttemptCount >= MaximumAttempts)
            {
                LockedAtUtc = verifiedAtUtc;
            }
            MarkUpdated(verifiedAtUtc);
            return false;
        }

        ProofTokenHash = RequireHash(proofTokenHash, nameof(proofTokenHash));
        VerifiedAtUtc = verifiedAtUtc;
        MarkUpdated(verifiedAtUtc);
        return true;
    }

    public bool MatchesProof(
        byte[] proofTokenHash,
        byte[] guestCartKeyHash,
        byte[] emailHash,
        DateTime nowUtc) =>
        IsVerifiedAndActive(nowUtc) &&
        CryptographicOperations.FixedTimeEquals(ProofTokenHash!, RequireHash(proofTokenHash, nameof(proofTokenHash))) &&
        CryptographicOperations.FixedTimeEquals(GuestCartKeyHash, RequireHash(guestCartKeyHash, nameof(guestCartKeyHash))) &&
        CryptographicOperations.FixedTimeEquals(EmailHash, RequireHash(emailHash, nameof(emailHash)));

    public void Consume(DateTime consumedAtUtc)
    {
        if (!IsVerifiedAndActive(consumedAtUtc))
        {
            throw new InvalidOperationException("The guest Checkout email verification is not active.");
        }

        ConsumedAtUtc = consumedAtUtc;
        MarkUpdated(consumedAtUtc);
    }

    public void Revoke(DateTime revokedAtUtc)
    {
        revokedAtUtc = RequireUtc(revokedAtUtc, nameof(revokedAtUtc));
        RevokedAtUtc ??= revokedAtUtc;
        MarkUpdated(revokedAtUtc);
    }

    private bool IsVerifiedAndActive(DateTime nowUtc)
    {
        nowUtc = RequireUtc(nowUtc, nameof(nowUtc));
        return VerifiedAtUtc.HasValue && ProofTokenHash is not null && nowUtc < ExpiresAtUtc &&
            !ConsumedAtUtc.HasValue && !LockedAtUtc.HasValue && !RevokedAtUtc.HasValue &&
            AttemptCount < MaximumAttempts;
    }

    private void EnsureActive(DateTime nowUtc)
    {
        nowUtc = RequireUtc(nowUtc, nameof(nowUtc));
        if (nowUtc >= ExpiresAtUtc || VerifiedAtUtc.HasValue || ConsumedAtUtc.HasValue ||
            LockedAtUtc.HasValue || RevokedAtUtc.HasValue || AttemptCount >= MaximumAttempts)
        {
            throw new InvalidOperationException("The guest Checkout email verification is not active.");
        }
    }

    private static byte[] RequireHash(byte[] value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length != 32)
        {
            throw new ArgumentException("The hash must contain 32 bytes.", parameterName);
        }
        return value.ToArray();
    }
}
