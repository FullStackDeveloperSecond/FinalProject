using System.Net;
using DoSelect.Domain.Auditing;

namespace DoSelect.Application.Auditing;

public static class AuditActions
{
    public const string RefundExecute = "refund.execute";
    public const string InvoiceAllowanceCreate = "invoice.allowance.create";
    public const string PersonalDataView = "personal_data.view";
    public const string MemberSuspend = "member.suspend";
    public const string MemberRestore = "member.restore";
    public const string MemberUpdate = "member.update";
    public const string AuditQuery = "audit.query";
    public const string AuditExport = "audit.export";
    public const string CouponCreate = "coupon.create";
    public const string CouponUpdate = "coupon.update";
    public const string CouponActivate = "coupon.activate";
    public const string CouponPause = "coupon.pause";
    public const string CouponDisable = "coupon.disable";
    public const string OrderCancel = "order.cancel";

    // PR #38（M-01B 管理員登入／TOTP／Recovery Code）用，DEC-P296：高風險安全狀態變更
    // 與稽核紀錄同一交易，Audit 失敗整筆 rollback。
    public const string AdminTotpEnrollmentConfirm = "admin.totp_enrollment.confirm";
    public const string AdminTotpRebindConfirm = "admin.totp_rebind.confirm";
    public const string AdminTotpRebindFailed = "admin.totp_rebind.failed";
    public const string AdminRecoveryCodeRedeem = "admin.recovery_code.redeem";
    public const string AdminChallengeRateLimited = "admin.challenge.rate_limited";
    public const string AdminSessionsRevoked = "admin.sessions.revoked";
    public const string GuestOrderScopeViolation = "guest_order.scope_violation";

    // ⚠ alex review：30 分鐘 Lockout 必須跟中央 Audit 同一交易——見 AdminAuthController.Login。
    public const string AdminAccountLockout = "admin.account.lockout";
    // DES-23: SupportTicket.Supervise/Handle admin actions. Each records only structured,
    // safe-code before/after markers (never free-text reasons or assignee identities) — the
    // admin-supplied free-text reason and the specific from/to admin identities live in the
    // existing SupportAssignmentHistory/SupportStatusHistory tables, not in this audit trail.
    public const string SupportTicketAssign = "support_ticket.assign";
    public const string SupportTicketTransfer = "support_ticket.transfer";
    public const string SupportTicketChangePriority = "support_ticket.change_priority";
    public const string SupportTicketChangeStatus = "support_ticket.change_status";
    public const string SupportTicketCancel = "support_ticket.cancel";
    public const string SupportTicketReopen = "support_ticket.reopen";
    public const string SupportTicketInternalNote = "support_ticket.internal_note";

    /// <summary>DEC-BATCH-026 (DEC-P309): compatibility rule admin surface writes to the central Audit Log instead of adding its own Actor/IP/TraceId columns.</summary>
    public const string CompatibilityRuleWarningSettingUpdate = "compatibility_rule.warning_setting.update";
    public const string CompatibilityRuleActivationUpdate = "compatibility_rule.activation.update";
    public const string CompatibilityRuleTest = "compatibility_rule.test";
    public const string OutboxRetry = "outbox.retry";
}

public static class AuditResourceTypes
{
    public const string Refund = "Refund";
    public const string SimulatedInvoiceAllowance = "SimulatedInvoiceAllowance";
    public const string Member = "Member";
    public const string AuditLog = "AuditLog";
    public const string AdminAccount = "AdminAccount";
    public const string Order = "Order";
    public const string Coupon = "Coupon";
    public const string SupportTicket = "SupportTicket";
    public const string CompatibilityRuleSetting = "CompatibilityRuleSetting";
    public const string CompatibilityCheckRun = "CompatibilityCheckRun";
    public const string OutboxMessage = "OutboxMessage";
}

public static class AuditRoleNames
{
    public const string SuperAdmin = "SuperAdmin";
    public const string CatalogManager = "CatalogManager";
    public const string InventoryManager = "InventoryManager";
    public const string OrderManager = "OrderManager";
    public const string FinanceManager = "FinanceManager";
    public const string CustomerService = "CustomerService";
    public const string CustomerServiceSupervisor = "CustomerServiceSupervisor";
    public const string MarketingAnalyst = "MarketingAnalyst";
    public const string PrivacyAdmin = "PrivacyAdmin";
    public const string SecurityAdmin = "SecurityAdmin";

    internal static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [
            SuperAdmin,
            CatalogManager,
            InventoryManager,
            OrderManager,
            FinanceManager,
            CustomerService,
            CustomerServiceSupervisor,
            MarketingAnalyst,
            PrivacyAdmin,
            SecurityAdmin,
        ],
        StringComparer.Ordinal);
}

public sealed class AuditActor
{
    private AuditActor(
        AuditActorType type,
        Guid? publicId,
        IReadOnlyList<string> roles)
    {
        Type = type;
        PublicId = publicId;
        Roles = roles;
    }

    public AuditActorType Type { get; }
    public Guid? PublicId { get; }
    public IReadOnlyList<string> Roles { get; }

    public static AuditActor Create(
        AuditActorType type,
        Guid? publicId,
        IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        if (type == AuditActorType.System)
        {
            if (publicId is not null || roles.Count != 0)
            {
                throw new ArgumentException(
                    "A system actor cannot have a user PublicId or role snapshot.",
                    nameof(publicId));
            }
        }
        else if (publicId is null || publicId == Guid.Empty)
        {
            throw new ArgumentException(
                "A non-system actor requires a PublicId.",
                nameof(publicId));
        }

        var normalizedRoles = roles
            .Select(role => role?.Trim() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalizedRoles.Any(role => !AuditRoleNames.Allowed.Contains(role)))
        {
            throw new ArgumentOutOfRangeException(nameof(roles), "The actor role is not allowed.");
        }

        if (type == AuditActorType.Admin && normalizedRoles.Length == 0)
        {
            throw new ArgumentException(
                "An administrator actor requires at least one role.",
                nameof(roles));
        }

        if (type != AuditActorType.Admin && normalizedRoles.Length != 0)
        {
            throw new ArgumentException(
                "Only an administrator actor can have a role snapshot.",
                nameof(roles));
        }

        return new AuditActor(type, publicId, Array.AsReadOnly(normalizedRoles));
    }
}

public sealed record AuditFieldChange
{
    private AuditFieldChange(
        string field,
        string? beforeCode,
        string? afterCode,
        bool changedOnly)
    {
        Field = RequireIdentifier(field, nameof(field), 64);
        BeforeCode = beforeCode is null
            ? null
            : RequireSafeCode(beforeCode, nameof(beforeCode), 64);
        AfterCode = afterCode is null
            ? null
            : RequireSafeCode(afterCode, nameof(afterCode), 64);
        ChangedOnly = changedOnly;
    }

    public string Field { get; }
    public string? BeforeCode { get; }
    public string? AfterCode { get; }
    public bool ChangedOnly { get; }

    public static AuditFieldChange Changed(string field) =>
        new(field, null, null, changedOnly: true);

    public static AuditFieldChange Code(
        string field,
        string? beforeCode,
        string? afterCode)
    {
        if (beforeCode is null && afterCode is null)
        {
            throw new ArgumentException("At least one safe code is required.", nameof(afterCode));
        }

        return new AuditFieldChange(field, beforeCode, afterCode, changedOnly: false);
    }

    internal static string RequireSafeCode(
        string value,
        string parameterName,
        int maximumLength)
    {
        value = RequireIdentifier(value, parameterName, maximumLength);
        var forbidden = new[]
        {
            "password",
            "token",
            "cookie",
            "apikey",
            "api-key",
            "totp",
            "recovery",
            "payment",
            "card",
            "cvv",
        };
        if (forbidden.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("The value contains a forbidden audit term.", parameterName);
        }

        return value;
    }

    internal static string RequireIdentifier(
        string value,
        string parameterName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value is required.", parameterName);
        }

        value = value.Trim();
        if (value.Length > maximumLength ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) &&
                character is not '.' and not '_' and not '-' and not ':'))
        {
            throw new ArgumentException("The value is not a stable audit identifier.", parameterName);
        }

        return value;
    }
}

public sealed class AuditWriteRequest
{
    private AuditWriteRequest(
        Guid auditPublicId,
        AuditActor actor,
        string action,
        string resourceType,
        Guid resourcePublicId,
        AuditResult result,
        string? errorCode,
        IReadOnlyList<AuditFieldChange> changes,
        string reason,
        string? note,
        string correlationId,
        string traceId,
        Guid? jobPublicId,
        IPAddress? remoteIpAddress)
    {
        AuditPublicId = auditPublicId;
        Actor = actor;
        Action = action;
        ResourceType = resourceType;
        ResourcePublicId = resourcePublicId;
        Result = result;
        ErrorCode = errorCode;
        Changes = changes;
        Reason = reason;
        Note = note;
        CorrelationId = correlationId;
        TraceId = traceId;
        JobPublicId = jobPublicId;
        RemoteIpAddress = remoteIpAddress;
    }

    public Guid AuditPublicId { get; }
    public AuditActor Actor { get; }
    public string Action { get; }
    public string ResourceType { get; }
    public Guid ResourcePublicId { get; }
    public AuditResult Result { get; }
    public string? ErrorCode { get; }
    public IReadOnlyList<AuditFieldChange> Changes { get; }
    public string Reason { get; }
    public string? Note { get; }
    public string CorrelationId { get; }
    public string TraceId { get; }
    public Guid? JobPublicId { get; }
    public IPAddress? RemoteIpAddress { get; }

    public static AuditWriteRequest Create(
        Guid auditPublicId,
        AuditActor actor,
        string action,
        string resourceType,
        Guid resourcePublicId,
        AuditResult result,
        string? errorCode,
        IReadOnlyCollection<AuditFieldChange> changes,
        string reason,
        string correlationId,
        string traceId,
        Guid? jobPublicId,
        IPAddress? remoteIpAddress,
        string? note = null)
    {
        if (auditPublicId == Guid.Empty)
        {
            throw new ArgumentException("Audit PublicId is required.", nameof(auditPublicId));
        }

        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(changes);
        var definition = AuditWritePolicy.RequireDefinition(action);
        if (!string.Equals(resourceType, definition.ResourceType, StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(resourceType),
                "The resource type is not allowed for this action.");
        }

        if (resourcePublicId == Guid.Empty)
        {
            throw new ArgumentException("Resource PublicId is required.", nameof(resourcePublicId));
        }

        var changeArray = changes.ToArray();
        if (changeArray.Select(change => change.Field).Distinct(StringComparer.Ordinal).Count() !=
            changeArray.Length ||
            changeArray.Any(change => !definition.AllowedFields.Contains(change.Field)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(changes),
                "A changed field is duplicated or is not allowed for this action.");
        }

        errorCode = string.IsNullOrWhiteSpace(errorCode)
            ? null
            : AuditFieldChange.RequireSafeCode(errorCode, nameof(errorCode), 128);
        if (result == AuditResult.Success ? errorCode is not null : errorCode is null)
        {
            throw new ArgumentException(
                "Successful results cannot have an error code; other results require one.",
                nameof(errorCode));
        }

        reason = AuditFieldChange.RequireSafeCode(reason, nameof(reason), 64);
        note = RequireSafeNote(note, definition.AllowsNote);
        correlationId = AuditFieldChange.RequireIdentifier(
            correlationId,
            nameof(correlationId),
            64);
        traceId = RequireTraceId(traceId);

        return new AuditWriteRequest(
            auditPublicId,
            actor,
            definition.Action,
            definition.ResourceType,
            resourcePublicId,
            result,
            errorCode,
            Array.AsReadOnly(changeArray),
            reason,
            note,
            correlationId,
            traceId,
            jobPublicId,
            remoteIpAddress);
    }

    private static string? RequireSafeNote(string? note, bool allowsNote)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        if (!allowsNote)
        {
            throw new ArgumentOutOfRangeException(
                nameof(note),
                "The audit action does not allow a free-form note.");
        }

        note = note.Trim();
        if (note.Length > 1_000)
        {
            throw new ArgumentException("The audit note cannot exceed 1000 characters.", nameof(note));
        }

        if (note.IndexOfAny(['@', '<', '>', '&', '\\', '"', '\'']) >= 0 ||
            note.Any(character => char.IsControl(character) &&
                character is not '\r' and not '\n' and not '\t'))
        {
            throw new ArgumentException(
                "The audit note contains a disallowed character.",
                nameof(note));
        }

        var forbidden = new[]
        {
            "password",
            "token",
            "cookie",
            "apikey",
            "api-key",
            "totp",
            "recovery code",
            "card number",
            "cvv",
        };
        if (forbidden.Any(term => note.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("The audit note contains a forbidden audit term.", nameof(note));
        }

        return note;
    }

    private static string RequireTraceId(string traceId)
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            throw new ArgumentException("TraceId is required.", nameof(traceId));
        }

        traceId = traceId.Trim();
        if (traceId.Length != 32 || traceId.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("TraceId must be a W3C 32-character hex value.", nameof(traceId));
        }

        return traceId.ToLowerInvariant();
    }
}

public interface IAuditWriter
{
    AuditLog Add(AuditWriteRequest request);
}

/// <summary>Per-request context an API layer captures once and passes down to any writer that needs to build an <see cref="AuditWriteRequest"/> — mirrors what <c>CreateInvoiceAllowanceCommand</c> carries inline, factored out for callers with more than one audited action per request.</summary>
public sealed record AuditRequestContext(string CorrelationId, string TraceId, IPAddress? RemoteIpAddress);

internal static class AuditWritePolicy
{
    private static readonly IReadOnlyDictionary<string, AuditActionDefinition> Definitions =
        new Dictionary<string, AuditActionDefinition>(StringComparer.Ordinal)
        {
            [AuditActions.RefundExecute] = DefinitionWithNote(
                AuditActions.RefundExecute,
                AuditResourceTypes.Refund,
                "status", "succeededAmount", "allocationCount"),
            [AuditActions.InvoiceAllowanceCreate] = Definition(
                AuditActions.InvoiceAllowanceCreate,
                AuditResourceTypes.SimulatedInvoiceAllowance,
                "status", "allowanceAmount", "allowanceItemCount"),
            [AuditActions.PersonalDataView] = Definition(
                AuditActions.PersonalDataView,
                AuditResourceTypes.Member,
                "viewedFields"),
            [AuditActions.MemberSuspend] = Definition(
                AuditActions.MemberSuspend,
                AuditResourceTypes.Member,
                "status", "sessionState"),
            [AuditActions.MemberRestore] = Definition(
                AuditActions.MemberRestore,
                AuditResourceTypes.Member,
                "status"),
            [AuditActions.MemberUpdate] = Definition(
                AuditActions.MemberUpdate,
                AuditResourceTypes.Member,
                "changedFields"),
            [AuditActions.AuditQuery] = Definition(
                AuditActions.AuditQuery,
                AuditResourceTypes.AuditLog,
                "filter"),
            [AuditActions.AuditExport] = Definition(
                AuditActions.AuditExport,
                AuditResourceTypes.AuditLog,
                "exportedFields"),
            [AuditActions.CouponCreate] = Definition(
                AuditActions.CouponCreate,
                AuditResourceTypes.Coupon,
                "status", "ruleVersion", "changedFields"),
            [AuditActions.CouponUpdate] = Definition(
                AuditActions.CouponUpdate,
                AuditResourceTypes.Coupon,
                "status", "ruleVersion", "changedFields"),
            [AuditActions.CouponActivate] = DefinitionWithNote(
                AuditActions.CouponActivate,
                AuditResourceTypes.Coupon,
                "status", "ruleVersion", "changedFields"),
            [AuditActions.CouponPause] = DefinitionWithNote(
                AuditActions.CouponPause,
                AuditResourceTypes.Coupon,
                "status", "ruleVersion", "changedFields"),
            [AuditActions.CouponDisable] = DefinitionWithNote(
                AuditActions.CouponDisable,
                AuditResourceTypes.Coupon,
                "status", "ruleVersion", "changedFields"),
            [AuditActions.OrderCancel] = DefinitionWithNote(
                AuditActions.OrderCancel,
                AuditResourceTypes.Order,
                "orderStatus", "inventoryReservations", "couponRedemptions"),
            [AuditActions.AdminTotpEnrollmentConfirm] = Definition(
                AuditActions.AdminTotpEnrollmentConfirm,
                AuditResourceTypes.AdminAccount,
                "twoFactorEnabled"),
            [AuditActions.AdminTotpRebindConfirm] = Definition(
                AuditActions.AdminTotpRebindConfirm,
                AuditResourceTypes.AdminAccount,
                "securityStamp"),
            [AuditActions.AdminTotpRebindFailed] = Definition(
                AuditActions.AdminTotpRebindFailed,
                AuditResourceTypes.AdminAccount),
            [AuditActions.AdminRecoveryCodeRedeem] = Definition(
                AuditActions.AdminRecoveryCodeRedeem,
                AuditResourceTypes.AdminAccount,
                "recoveryCodesRemaining"),
            [AuditActions.AdminChallengeRateLimited] = Definition(
                AuditActions.AdminChallengeRateLimited,
                AuditResourceTypes.AdminAccount),
            [AuditActions.AdminSessionsRevoked] = Definition(
                AuditActions.AdminSessionsRevoked,
                AuditResourceTypes.AdminAccount,
                "securityStamp"),
            [AuditActions.AdminAccountLockout] = Definition(
                AuditActions.AdminAccountLockout,
                AuditResourceTypes.AdminAccount,
                "lockoutEnd"),
            [AuditActions.GuestOrderScopeViolation] = Definition(
                AuditActions.GuestOrderScopeViolation,
                AuditResourceTypes.Order,
                "scopeViolationCount"),
            [AuditActions.SupportTicketAssign] = Definition(
                AuditActions.SupportTicketAssign,
                AuditResourceTypes.SupportTicket,
                "assignee", "status"),
            [AuditActions.SupportTicketTransfer] = Definition(
                AuditActions.SupportTicketTransfer,
                AuditResourceTypes.SupportTicket,
                "assignee"),
            [AuditActions.SupportTicketChangePriority] = Definition(
                AuditActions.SupportTicketChangePriority,
                AuditResourceTypes.SupportTicket,
                "priority"),
            [AuditActions.SupportTicketChangeStatus] = Definition(
                AuditActions.SupportTicketChangeStatus,
                AuditResourceTypes.SupportTicket,
                "status"),
            [AuditActions.SupportTicketCancel] = Definition(
                AuditActions.SupportTicketCancel,
                AuditResourceTypes.SupportTicket,
                "status"),
            [AuditActions.SupportTicketReopen] = Definition(
                AuditActions.SupportTicketReopen,
                AuditResourceTypes.SupportTicket,
                "status"),
            [AuditActions.SupportTicketInternalNote] = Definition(
                AuditActions.SupportTicketInternalNote,
                AuditResourceTypes.SupportTicket,
                "note"),
            [AuditActions.CompatibilityRuleWarningSettingUpdate] = Definition(
                AuditActions.CompatibilityRuleWarningSettingUpdate,
                AuditResourceTypes.CompatibilityRuleSetting,
                "ruleCode", "settingCode", "value", "settingsVersion"),
            [AuditActions.CompatibilityRuleActivationUpdate] = Definition(
                AuditActions.CompatibilityRuleActivationUpdate,
                AuditResourceTypes.CompatibilityRuleSetting,
                "ruleCode", "settingCode", "isActive", "settingsVersion"),
            [AuditActions.CompatibilityRuleTest] = Definition(
                AuditActions.CompatibilityRuleTest,
                AuditResourceTypes.CompatibilityCheckRun,
                "inputHash", "overall", "settingsVersion"),
            [AuditActions.OutboxRetry] = Definition(
                AuditActions.OutboxRetry,
                AuditResourceTypes.OutboxMessage,
                "status"),
        };

    public static AuditActionDefinition RequireDefinition(string action)
    {
        if (string.IsNullOrWhiteSpace(action) ||
            !Definitions.TryGetValue(action.Trim(), out var definition))
        {
            throw new ArgumentOutOfRangeException(nameof(action), "The audit action is not allowed.");
        }

        return definition;
    }

    private static AuditActionDefinition Definition(
        string action,
        string resourceType,
        params string[] fields) =>
        new(action, resourceType, new HashSet<string>(fields, StringComparer.Ordinal), AllowsNote: false);

    private static AuditActionDefinition DefinitionWithNote(
        string action,
        string resourceType,
        params string[] fields) =>
        new(action, resourceType, new HashSet<string>(fields, StringComparer.Ordinal), AllowsNote: true);
}

internal sealed record AuditActionDefinition(
    string Action,
    string ResourceType,
    IReadOnlySet<string> AllowedFields,
    bool AllowsNote);
