using DoSelect.Domain.Common;
using DoSelect.Domain.Members;

namespace DoSelect.Domain.Ai;

public enum AiConsentRecordStatus
{
    Granted,
    Withdrawn,
}

public enum AiUsageFeature
{
    ProductSearch,
    Support,
}

/// <summary>
/// Append-only evidence of an AI data-processing consent decision. Withdrawal creates a new
/// record; an earlier grant is never updated or deleted.
/// </summary>
public sealed class AiConsentRecord : Entity
{
    private AiConsentRecord()
    {
    }

    private AiConsentRecord(
        string memberUserId,
        int policyVersion,
        SupportedLocale locale,
        AiConsentRecordStatus status,
        DateTime grantedAtUtc,
        DateTime? withdrawnAtUtc,
        string source,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        if (policyVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(policyVersion));
        }

        if (!Enum.IsDefined(locale))
        {
            throw new ArgumentOutOfRangeException(nameof(locale));
        }

        grantedAtUtc = RequireUtc(grantedAtUtc, nameof(grantedAtUtc));
        if (withdrawnAtUtc.HasValue)
        {
            withdrawnAtUtc = RequireUtc(withdrawnAtUtc.Value, nameof(withdrawnAtUtc));
        }

        if (status == AiConsentRecordStatus.Granted && withdrawnAtUtc is not null)
        {
            throw new ArgumentException(
                "A granted consent record cannot have a withdrawal timestamp.",
                nameof(withdrawnAtUtc));
        }

        if (status == AiConsentRecordStatus.Withdrawn &&
            (withdrawnAtUtc is null || withdrawnAtUtc < grantedAtUtc))
        {
            throw new ArgumentException(
                "A withdrawn consent record requires a timestamp after the grant.",
                nameof(withdrawnAtUtc));
        }

        MemberUserId = RequireBoundedText(memberUserId, nameof(memberUserId), 450);
        PolicyVersion = policyVersion;
        Locale = locale;
        Status = status;
        GrantedAtUtc = grantedAtUtc;
        WithdrawnAtUtc = withdrawnAtUtc;
        Source = RequireBoundedText(source, nameof(source), 32);
    }

    public string MemberUserId { get; private set; } = string.Empty;

    public int PolicyVersion { get; private set; }

    public SupportedLocale Locale { get; private set; }

    public AiConsentRecordStatus Status { get; private set; }

    public DateTime GrantedAtUtc { get; private set; }

    public DateTime? WithdrawnAtUtc { get; private set; }

    public string Source { get; private set; } = string.Empty;

    public static AiConsentRecord Grant(
        string memberUserId,
        int policyVersion,
        SupportedLocale locale,
        string source,
        DateTime grantedAtUtc) =>
        new(
            memberUserId,
            policyVersion,
            locale,
            AiConsentRecordStatus.Granted,
            grantedAtUtc,
            withdrawnAtUtc: null,
            source,
            createdAtUtc: grantedAtUtc);

    public static AiConsentRecord Withdraw(
        string memberUserId,
        int policyVersion,
        SupportedLocale locale,
        string source,
        DateTime grantedAtUtc,
        DateTime withdrawnAtUtc) =>
        new(
            memberUserId,
            policyVersion,
            locale,
            AiConsentRecordStatus.Withdrawn,
            grantedAtUtc,
            withdrawnAtUtc,
            source,
            createdAtUtc: withdrawnAtUtc);

    private static string RequireBoundedText(
        string value,
        string parameterName,
        int maximumLength)
    {
        value = RequireText(value, parameterName);
        return value.Length <= maximumLength
            ? value
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }
}

/// <summary>
/// Append-only usage record. A successful reservation consumes quota even if the downstream
/// model later fails; <see cref="Succeeded"/> therefore records reservation success.
/// </summary>
public sealed class AiUsageLedgerEntry : Entity
{
    private AiUsageLedgerEntry()
    {
    }

    private AiUsageLedgerEntry(
        string? memberUserId,
        byte[]? anonymousSessionKeyHash,
        AiUsageFeature feature,
        Guid requestPublicId,
        int inputTokens,
        int outputTokens,
        decimal estimatedCostUsd,
        bool succeeded,
        DateTime occurredAtUtc)
        : base(occurredAtUtc)
    {
        var hasMember = !string.IsNullOrWhiteSpace(memberUserId);
        var hasAnonymousSession = anonymousSessionKeyHash is not null;
        if (hasMember == hasAnonymousSession)
        {
            throw new ArgumentException(
                "Exactly one member or anonymous session owner is required.");
        }

        if (anonymousSessionKeyHash is not null && anonymousSessionKeyHash.Length != 32)
        {
            throw new ArgumentException(
                "The anonymous session hash must contain 32 bytes.",
                nameof(anonymousSessionKeyHash));
        }

        if (requestPublicId == Guid.Empty)
        {
            throw new ArgumentException("RequestPublicId is required.", nameof(requestPublicId));
        }

        if (inputTokens < 0 || outputTokens < 0 || estimatedCostUsd < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputTokens));
        }

        MemberUserId = hasMember
            ? RequireBoundedText(memberUserId!, nameof(memberUserId), 450)
            : null;
        AnonymousSessionKeyHash = anonymousSessionKeyHash?.ToArray();
        Feature = feature;
        RequestPublicId = requestPublicId;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        EstimatedCostUsd = estimatedCostUsd;
        Succeeded = succeeded;
        OccurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
    }

    public string? MemberUserId { get; private set; }

    public byte[]? AnonymousSessionKeyHash { get; private set; }

    public AiUsageFeature Feature { get; private set; }

    public Guid RequestPublicId { get; private set; }

    public int InputTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public decimal EstimatedCostUsd { get; private set; }

    public bool Succeeded { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public static AiUsageLedgerEntry ReserveSupport(
        string memberUserId,
        Guid requestPublicId,
        DateTime occurredAtUtc) =>
        new(
            memberUserId,
            anonymousSessionKeyHash: null,
            AiUsageFeature.Support,
            requestPublicId,
            inputTokens: 0,
            outputTokens: 0,
            estimatedCostUsd: 0m,
            succeeded: true,
            occurredAtUtc);

    private static string RequireBoundedText(
        string value,
        string parameterName,
        int maximumLength)
    {
        value = RequireText(value, parameterName);
        return value.Length <= maximumLength
            ? value
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }
}
