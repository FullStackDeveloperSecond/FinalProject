using DoSelect.Domain.Refunds;

namespace DoSelect.Application.Returns;

/// <summary>
/// The five return-reason paths from 退貨與退款政策.md. Schema stores ReasonCode as a plain
/// varchar (no DB-level enum), so this is an Application-layer controlled vocabulary — the
/// canonical <see cref="System.Enum"/> name (exact case) is what gets persisted as ReasonCode.
/// </summary>
public enum ReturnReasonType
{
    CoolingOff,
    Defective,
    WrongItem,
    ShippingDamage,
    Warranty,
}

/// <summary>
/// Pure policy computation — no I/O. Mirrors the SupportSlaPolicy pattern (Application-layer
/// static policy, no Domain/Infrastructure dependency) since these rules are read-model/
/// validation concerns, not entity invariants.
/// </summary>
public static class ReturnEligibilityPolicy
{
    public const int MaximumLineCount = 20;

    /// <summary>
    /// The project persists UTC but every business/calendar rule is Asia/Taipei local time.
    /// Resolved via the IANA id, which .NET's cross-platform (ICU-backed) time zone database
    /// supports on both Windows and Linux since .NET 6 — no server-locale dependency and no
    /// extra package.
    /// </summary>
    private static readonly TimeZoneInfo TaipeiTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");

    public static bool TryParseReasonType(string reasonCode, out ReturnReasonType reasonType) =>
        Enum.TryParse(reasonCode, ignoreCase: false, out reasonType) && Enum.IsDefined(reasonType);

    /// <summary>
    /// Maps only reasons that the current Returns contract can actually persist. Refund-only
    /// exceptional reasons remain unsupported until Returns gains an explicit input path.
    /// </summary>
    public static bool TryMapRefundReason(string reasonCode, out ReturnReason reason)
    {
        reason = reasonCode switch
        {
            nameof(ReturnReasonType.CoolingOff) => ReturnReason.CoolingOff,
            nameof(ReturnReasonType.Defective) => ReturnReason.Defective,
            nameof(ReturnReasonType.WrongItem) => ReturnReason.WrongItem,
            nameof(ReturnReasonType.ShippingDamage) => ReturnReason.ShippingDamage,
            nameof(ReturnReasonType.Warranty) => ReturnReason.Warranty,
            _ => default,
        };

        return reasonCode is nameof(ReturnReasonType.CoolingOff)
            or nameof(ReturnReasonType.Defective)
            or nameof(ReturnReasonType.WrongItem)
            or nameof(ReturnReasonType.ShippingDamage)
            or nameof(ReturnReasonType.Warranty);
    }

    /// <summary>
    /// "到貨翌日起 7 日內提出申請" — the window starts the calendar day AFTER delivery and runs
    /// 7 full days, so the deadline is the *local* (Asia/Taipei) delivery date plus 8 days at
    /// local midnight, converted back to UTC for comparison. Deliberately not
    /// <c>deliveredAtUtc.Date.AddDays(8)</c>: that computes UTC midnight, which is 08:00 Taipei
    /// time and silently shifts the calendar-day boundary the policy actually describes.
    /// </summary>
    public static DateTime ComputeCoolingOffDeadlineUtc(DateTime deliveredAtUtc)
    {
        var deliveredLocal = TimeZoneInfo.ConvertTimeFromUtc(deliveredAtUtc, TaipeiTimeZone);
        var deadlineLocalMidnight = DateTime.SpecifyKind(deliveredLocal.Date.AddDays(8), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(deadlineLocalMidnight, TaipeiTimeZone);
    }

    /// <summary>
    /// Only the CoolingOff (無理由) path is time-boxed and blocked once custom assembly has
    /// started; Defective/WrongItem/ShippingDamage/Warranty are not subject to the 7-day window
    /// per policy ("不直接受一般無理由退貨期限限制，依各自流程處理") and remain allowed even on
    /// an in-progress custom build (only individual-preference cancellation is blocked there,
    /// not a defect/wrong-item/damage/warranty claim).
    /// </summary>
    public static bool RequiresCoolingOffDeadlineCheck(ReturnReasonType reasonType) =>
        reasonType == ReturnReasonType.CoolingOff;

    public static bool BlocksOnStartedAssembly(ReturnReasonType reasonType) =>
        reasonType == ReturnReasonType.CoolingOff;
}
