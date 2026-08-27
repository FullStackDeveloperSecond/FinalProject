using DoSelect.Domain.Common;

namespace DoSelect.Domain.Builds;

public enum CompatibilityOverall
{
    Compatible,
    Warning,
    Blocked,
    InsufficientData,
}

public sealed class BuildList : MutablePublicEntity
{
    private BuildList() { }

    public BuildList(
        Guid publicId,
        string ownerUserId,
        string name,
        string status,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        OwnerUserId = RequireText(ownerUserId, nameof(ownerUserId));
        Name = RequireText(name, nameof(name));
        Status = RequireText(status, nameof(status));
    }

    public string OwnerUserId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTime? LastCheckedAtUtc { get; private set; }
    public CompatibilityOverall? CompatibilityStatus { get; private set; }

    public void Rename(string name, DateTime updatedAtUtc)
    {
        Name = RequireText(name, nameof(name));
        MarkUpdated(updatedAtUtc);
    }

    public void ChangeStatus(string status, DateTime updatedAtUtc)
    {
        Status = RequireText(status, nameof(status));
        MarkUpdated(updatedAtUtc);
    }

    public void RecordCompatibility(
        CompatibilityOverall status,
        DateTime checkedAtUtc)
    {
        LastCheckedAtUtc = RequireUtc(checkedAtUtc, nameof(checkedAtUtc));
        CompatibilityStatus = status;
        MarkUpdated(checkedAtUtc);
    }
}

public sealed class BuildListItem : PublicEntity
{
    private BuildListItem() { }

    public BuildListItem(
        Guid publicId,
        long buildListId,
        long skuId,
        int quantity,
        int sortOrder,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (buildListId <= 0 || skuId <= 0 || quantity is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        BuildListId = buildListId;
        SkuId = skuId;
        Quantity = quantity;
        SortOrder = sortOrder;
    }

    public long BuildListId { get; private set; }
    public long SkuId { get; private set; }
    public int Quantity { get; private set; }
    public int SortOrder { get; private set; }
}

public sealed class BuildShareToken : PublicEntity
{
    private BuildShareToken() { }

    public BuildShareToken(
        Guid publicId,
        long buildListId,
        byte[] tokenHash,
        DateTime? expiresAtUtc,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (buildListId <= 0 || tokenHash is null || tokenHash.Length != 32 ||
            expiresAtUtc.HasValue &&
            (expiresAtUtc.Value.Kind != DateTimeKind.Utc || expiresAtUtc <= createdAtUtc))
        {
            throw new ArgumentException("The build share token is invalid.");
        }

        BuildListId = buildListId;
        TokenHash = tokenHash.ToArray();
        ExpiresAtUtc = expiresAtUtc;
    }

    public long BuildListId { get; private set; }
    public byte[] TokenHash { get; private set; } = [];
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public DateTime? LastAccessedAtUtc { get; private set; }

    public void RecordAccess(DateTime accessedAtUtc)
    {
        LastAccessedAtUtc = RequireUtc(accessedAtUtc, nameof(accessedAtUtc));
    }

    public void Revoke(DateTime revokedAtUtc)
    {
        RevokedAtUtc ??= RequireUtc(revokedAtUtc, nameof(revokedAtUtc));
    }
}

public sealed class CompatibilityRuleSetting : MutablePublicEntity
{
    private CompatibilityRuleSetting() { }

    public CompatibilityRuleSetting(
        Guid publicId,
        string ruleCode,
        string settingCode,
        decimal? decimalValue,
        bool? booleanValue,
        int settingsVersion,
        string reason,
        string changedByAdminUserId,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (decimalValue.HasValue == booleanValue.HasValue || settingsVersion <= 0)
        {
            throw new ArgumentException("Exactly one setting value is required.");
        }

        RuleCode = RequireText(ruleCode, nameof(ruleCode));
        SettingCode = RequireText(settingCode, nameof(settingCode));
        DecimalValue = decimalValue;
        BooleanValue = booleanValue;
        SettingsVersion = settingsVersion;
        Reason = RequireText(reason, nameof(reason));
        ChangedByAdminUserId = RequireText(
            changedByAdminUserId,
            nameof(changedByAdminUserId));
    }

    public string RuleCode { get; private set; } = string.Empty;
    public string SettingCode { get; private set; } = string.Empty;
    public decimal? DecimalValue { get; private set; }
    public bool? BooleanValue { get; private set; }
    public int SettingsVersion { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string ChangedByAdminUserId { get; private set; } = string.Empty;
}

public sealed class CompatibilityCheckRun : PublicEntity
{
    private CompatibilityCheckRun() { }

    public CompatibilityCheckRun(
        Guid publicId,
        long? buildListId,
        int ruleSetVersion,
        int settingsVersion,
        CompatibilityOverall overall,
        byte[] inputHash,
        DateTime evaluatedAtUtc)
        : base(publicId, evaluatedAtUtc)
    {
        if (buildListId is <= 0 || ruleSetVersion <= 0 || settingsVersion <= 0 ||
            inputHash is null || inputHash.Length != 32)
        {
            throw new ArgumentException("The compatibility run is invalid.");
        }

        BuildListId = buildListId;
        RuleSetVersion = ruleSetVersion;
        SettingsVersion = settingsVersion;
        Overall = overall;
        InputHash = inputHash.ToArray();
        EvaluatedAtUtc = RequireUtc(evaluatedAtUtc, nameof(evaluatedAtUtc));
    }

    public long? BuildListId { get; private set; }
    public int RuleSetVersion { get; private set; }
    public int SettingsVersion { get; private set; }
    public CompatibilityOverall Overall { get; private set; }
    public byte[] InputHash { get; private set; } = [];
    public DateTime EvaluatedAtUtc { get; private set; }
}

/// <summary>
/// One multi-value compatibility fact for a Sku (e.g. one of a motherboard's several supported
/// storage interfaces, or one of a PSU's available connector types). Per 商品、組裝與相容性.md:
/// "標籤、介面類型等真正多值資料使用明確 Join Entity，不把多值塞入同一規格值" — genuinely multi-valued
/// facts get one row each here rather than being packed into a single <c>SkuSpecificationValue</c>.
/// Scoped to the Builds bounded context: the compatibility engine's own read model over Sku facts,
/// not a general-purpose Catalog display attribute.
/// </summary>
public sealed class SkuCompatibilityAttribute : Entity
{
    private SkuCompatibilityAttribute() { }

    public SkuCompatibilityAttribute(
        long skuId,
        string attributeKey,
        string attributeValue,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        if (skuId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(skuId));
        }

        SkuId = skuId;
        AttributeKey = RequireText(attributeKey, nameof(attributeKey));
        AttributeValue = RequireText(attributeValue, nameof(attributeValue));
    }

    public long SkuId { get; private set; }
    public string AttributeKey { get; private set; } = string.Empty;
    public string AttributeValue { get; private set; } = string.Empty;
}

/// <summary>
/// One storage interface a motherboard exposes, with the real count of physical ports it offers
/// (e.g. a board with 4 SATA and 2 M.2 slots is two rows here). Split out from
/// <see cref="SkuCompatibilityAttribute"/> (組長 PR #34 round-4 review, item 1): packing
/// "{interface}:{portCount}" into that entity's free-text AttributeValue let the same interface
/// appear twice with different counts (its unique index is on the whole value string, e.g.
/// "SATA:4" and "SATA:6" both pass), and the reader had no ordering guarantee for which row
/// would win writing into a Dictionary. The unique index here is scoped to (SkuId, InterfaceCode)
/// specifically so that ambiguity can't exist at the schema level.
/// </summary>
public sealed class SkuStorageInterfacePort : Entity
{
    private SkuStorageInterfacePort() { }

    public SkuStorageInterfacePort(
        long skuId,
        string interfaceCode,
        int portCount,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        if (skuId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(skuId));
        }

        if (portCount is < 1 or > CompatibilityAttributeLimits.MaxStorageInterfacePortCount)
        {
            throw new ArgumentOutOfRangeException(nameof(portCount));
        }

        SkuId = skuId;
        InterfaceCode = RequireText(interfaceCode, nameof(interfaceCode));
        PortCount = portCount;
    }

    public long SkuId { get; private set; }
    public string InterfaceCode { get; private set; } = string.Empty;
    public int PortCount { get; private set; }
}

public sealed class CompatibilityCheckResult
{
    private CompatibilityCheckResult() { }

    public CompatibilityCheckResult(
        long compatibilityCheckRunId,
        string ruleCode,
        string severity,
        string messageKey,
        string? factsJson)
    {
        if (compatibilityCheckRunId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(compatibilityCheckRunId));
        }

        CompatibilityCheckRunId = compatibilityCheckRunId;
        RuleCode = Require(ruleCode, nameof(ruleCode));
        Severity = Require(severity, nameof(severity));
        MessageKey = Require(messageKey, nameof(messageKey));
        FactsJson = string.IsNullOrWhiteSpace(factsJson) ? null : factsJson.Trim();
    }

    public long Id { get; private set; }
    public long CompatibilityCheckRunId { get; private set; }
    public string RuleCode { get; private set; } = string.Empty;
    public string Severity { get; private set; } = string.Empty;
    public string MessageKey { get; private set; } = string.Empty;
    public string? FactsJson { get; private set; }

    private static string Require(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The value is required.", parameterName)
            : value.Trim();
}
