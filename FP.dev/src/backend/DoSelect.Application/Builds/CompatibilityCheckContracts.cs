namespace DoSelect.Application.Builds;

public sealed record BuildItemInput(Guid SkuPublicId, int Quantity);

public sealed record CompatibilityFindingDto(
    string RuleCode,
    string Severity,
    string MessageKey,
    IReadOnlyList<Guid> SubjectSkuPublicIds,
    IReadOnlyDictionary<string, object?> Facts);

public sealed record CompatibilityCheckDto(
    string Overall,
    int RuleSetVersion,
    int SettingsVersion,
    IReadOnlyList<CompatibilityFindingDto> Results,
    DateTime EvaluatedAtUtc);

public sealed record CompatibilityCheckRequest(IReadOnlyList<BuildItemInput> Items);

public interface ICompatibilityCheckService
{
    /// <summary>
    /// UC-COMPAT-01: evaluates the fixed rule set against the given SKUs using the current
    /// (non-draft) admin-tunable warning settings. <paramref name="request"/> items are 1..20
    /// entries; same-SKU entries are merged by the caller's Api layer before this is called.
    /// <paramref name="buildListId"/> is the persisted BuildList this check belongs to, if any —
    /// null for an ad-hoc check (the public/general endpoint, the admin rule-test tool). Every
    /// call persists an immutable CompatibilityCheckRun/Result snapshot (組長 PR #34 round-3
    /// review), so this is required rather than optional: callers must consciously choose.
    /// No request-level dedup/merge for repeated identical input in a short window (組長 PR #34
    /// round-4 review, item 3, evaluated not implemented): the admin-tunable settings this
    /// depends on can change between two calls with the same SkuPublicId/Quantity input, so a
    /// cached/merged result could silently disagree with what the rule engine would return right
    /// now — and every call is meant to be an individually auditable Run. Growth from anonymous
    /// callers is bounded by <see cref="PurgeExpiredRunsAsync"/> and rate limiting
    /// (RateLimiterPolicies.PublicBuildsAnonymous) instead.
    /// </summary>
    Task<CompatibilityCheckDto> CheckAsync(
        CompatibilityCheckRequest request,
        long? buildListId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes CompatibilityCheckRun/Result rows older than <paramref name="olderThanUtc"/>, up to
    /// <paramref name="batchSize"/> Runs per call (and their Results) — an anonymous, unbounded
    /// endpoint (public compatibility-checks, share re-validation) persists a Run on every call, so
    /// without a retention job the tables grow indefinitely (組長 PR #34 round-4 review, item 3).
    /// Bounded per call and safe to call repeatedly until it returns 0 (retryable/monitorable batch
    /// shape — no recurring-job scheduler is wired up to call this automatically yet; that is
    /// shared background-job infrastructure this PR does not introduce, mirroring
    /// IInventoryReservationService.ExpireOverdueReservationsAsync's same "job logic exists, caller
    /// decides when to invoke it" shape). Never deletes a Run referenced by a still-live
    /// InventoryReconciliationCase-style FK — Runs have no such external reference, so a plain
    /// age-based delete is safe. Returns the number of Runs deleted.
    /// </summary>
    Task<int> PurgeExpiredRunsAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken);
}
