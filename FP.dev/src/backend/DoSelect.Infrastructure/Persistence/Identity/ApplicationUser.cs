using DoSelect.Domain.Members;
using Microsoft.AspNetCore.Identity;

namespace DoSelect.Infrastructure.Persistence.Identity;

public sealed class ApplicationUser : IdentityUser
{
    private ApplicationUser()
    {
    }

    private ApplicationUser(
        Guid publicId,
        AccountType accountType,
        string email,
        DateTime createdAtUtc)
    {
        if (publicId == Guid.Empty)
        {
            throw new ArgumentException("PublicId is required.", nameof(publicId));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        PublicId = publicId;
        AccountType = accountType;
        AccountStatus = AccountStatus.PendingEmailVerification;
        PreferredLocale = SupportedLocale.ZhTw;
        Email = email.Trim();
        UserName = Email;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid PublicId { get; private set; }

    public AccountType AccountType { get; private set; }

    public AccountStatus AccountStatus { get; private set; }

    public SupportedLocale PreferredLocale { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public DateTime? AnonymizedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static ApplicationUser CreateMember(
        Guid publicId,
        string email,
        DateTime createdAtUtc) =>
        new(publicId, AccountType.Member, email, createdAtUtc);

    public static ApplicationUser CreateAdmin(
        Guid publicId,
        string email,
        DateTime createdAtUtc) =>
        new(publicId, AccountType.Admin, email, createdAtUtc);

    public void ConfirmEmail(DateTime confirmedAtUtc)
    {
        EnsureNotTerminal();
        EnsureUtc(confirmedAtUtc, nameof(confirmedAtUtc));
        EmailConfirmed = true;
        AccountStatus = AccountStatus.Active;
        UpdatedAtUtc = confirmedAtUtc;
    }

    public void Suspend(DateTime suspendedAtUtc)
    {
        EnsureNotTerminal();
        EnsureUtc(suspendedAtUtc, nameof(suspendedAtUtc));
        AccountStatus = AccountStatus.Suspended;
        UpdatedAtUtc = suspendedAtUtc;
    }

    public void Reactivate(DateTime reactivatedAtUtc)
    {
        if (AccountStatus != AccountStatus.Suspended)
        {
            throw new InvalidOperationException("Only a suspended account can be reactivated.");
        }

        EnsureUtc(reactivatedAtUtc, nameof(reactivatedAtUtc));
        AccountStatus = AccountStatus.Active;
        UpdatedAtUtc = reactivatedAtUtc;
    }

    public void Anonymize(DateTime anonymizedAtUtc)
    {
        EnsureNotTerminal();
        EnsureUtc(anonymizedAtUtc, nameof(anonymizedAtUtc));
        AccountStatus = AccountStatus.Anonymized;
        AnonymizedAtUtc = anonymizedAtUtc;
        UpdatedAtUtc = anonymizedAtUtc;
        Email = null;
        NormalizedEmail = null;
        PhoneNumber = null;
        UserName = $"anonymized-{PublicId:D}";
        NormalizedUserName = UserName.ToUpperInvariant();
    }

    public void ChangePreferredLocale(SupportedLocale locale, DateTime updatedAtUtc)
    {
        EnsureNotTerminal();
        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        PreferredLocale = locale;
        UpdatedAtUtc = updatedAtUtc;
    }

    private void EnsureNotTerminal()
    {
        if (AccountStatus is AccountStatus.Anonymized or AccountStatus.Disabled)
        {
            throw new InvalidOperationException("The account is in a terminal state.");
        }
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The value must use UTC.", parameterName);
        }
    }
}
