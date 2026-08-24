using DoSelect.Domain.Support;

namespace DoSelect.Application.Support;

/// <summary>
/// The four confirmed SLA tiers (24x7 calendar time). Both targets are measured
/// independently from ticket creation time, not chained off one another.
/// </summary>
public static class SupportSlaPolicy
{
    public static (TimeSpan FirstResponse, TimeSpan Resolution) GetTargets(CasePriority priority) => priority switch
    {
        CasePriority.Low => (TimeSpan.FromHours(24), TimeSpan.FromDays(5)),
        CasePriority.Normal => (TimeSpan.FromHours(8), TimeSpan.FromDays(3)),
        CasePriority.High => (TimeSpan.FromHours(4), TimeSpan.FromHours(24)),
        CasePriority.Urgent => (TimeSpan.FromHours(1), TimeSpan.FromHours(8)),
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unsupported priority."),
    };

    public static (DateTime FirstResponseDueAtUtc, DateTime ResolutionDueAtUtc) ComputeDueDates(
        CasePriority priority,
        DateTime createdAtUtc)
    {
        var targets = GetTargets(priority);
        return (createdAtUtc.Add(targets.FirstResponse), createdAtUtc.Add(targets.Resolution));
    }
}
