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

    public static bool TryParseReasonType(string reasonCode, out ReturnReasonType reasonType) =>
        Enum.TryParse(reasonCode, ignoreCase: false, out reasonType) && Enum.IsDefined(reasonType);

    /// <summary>
    /// "到貨翌日起 7 日內提出申請" — the window starts the calendar day AFTER delivery and runs
    /// 7 full days, so the deadline is the delivery date plus 8 days at UTC midnight.
    /// </summary>
    public static DateTime ComputeCoolingOffDeadlineUtc(DateTime deliveredAtUtc) =>
        deliveredAtUtc.Date.AddDays(8);

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
