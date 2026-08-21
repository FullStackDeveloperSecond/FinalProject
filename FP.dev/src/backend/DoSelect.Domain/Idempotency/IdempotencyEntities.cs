using DoSelect.Domain.Common;

namespace DoSelect.Domain.Idempotency;

public enum IdempotencyStatus
{
    Processing,
    Succeeded,
    Failed,
}

/// <summary>
/// 資料一致性、Outbox與冪等設計.md's <c>IdempotencyRecord</c>: a high-risk command (order create,
/// refund execute, cart merge, ...) is uniquely identified by ActorScopeHash + Operation + Key.
/// A genuinely concurrent duplicate request is blocked from re-executing the command by racing
/// to INSERT this row first (unique index on those three fields, enforced in
/// IdempotencyRecordConfiguration) — the loser's whole transaction rolls back and it re-queries
/// this table to decide: same RequestHash → return the cached ResponseSummary; different hash →
/// 409 idempotency_payload_conflict; still Processing → 409 asking the caller to retry shortly.
///
/// Simplification from the doc: ActorScope is hashed with plain SHA-256, not "SHA-256 + server
/// Pepper" — no pepper secret exists anywhere in this codebase's configuration yet, and adding
/// one is a separate cross-cutting decision out of proportion to wiring up this table's first
/// consumer (Cart merge). Matches the existing unpeppered convention already used for
/// <c>Cart.GuestCartKeyHash</c>.
/// </summary>
public sealed class IdempotencyRecord : Entity
{
    private IdempotencyRecord() { }

    public IdempotencyRecord(
        byte[] actorScopeHash,
        string operation,
        string key,
        byte[] requestHash,
        DateTime expiresAtUtc,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        if (actorScopeHash is null || actorScopeHash.Length != 32)
        {
            throw new ArgumentException("The actor scope hash must contain 32 bytes.", nameof(actorScopeHash));
        }

        if (requestHash is null || requestHash.Length != 32)
        {
            throw new ArgumentException("The request hash must contain 32 bytes.", nameof(requestHash));
        }

        expiresAtUtc = RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        ActorScopeHash = actorScopeHash.ToArray();
        Operation = RequireText(operation, nameof(operation));
        Key = RequireText(key, nameof(key));
        RequestHash = requestHash.ToArray();
        Status = IdempotencyStatus.Processing;
        ExpiresAtUtc = expiresAtUtc;
    }

    public byte[] ActorScopeHash { get; private set; } = [];
    public string Operation { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public byte[] RequestHash { get; private set; } = [];
    public IdempotencyStatus Status { get; private set; }
    public int? ResponseStatusCode { get; private set; }
    public string? ResponseSummary { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>Response summary caps at 32 KB per the design doc — a version-JSON snapshot of the safely-replayable result, never a raw entity dump.</summary>
    public void Complete(int responseStatusCode, string responseSummary, DateTime completedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(responseSummary))
        {
            throw new ArgumentException("A response summary is required.", nameof(responseSummary));
        }

        if (System.Text.Encoding.UTF8.GetByteCount(responseSummary) > 32 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(responseSummary), "The response summary must not exceed 32 KB.");
        }

        ResponseStatusCode = responseStatusCode;
        ResponseSummary = responseSummary;
        Status = IdempotencyStatus.Succeeded;
    }

    public void Fail(DateTime failedAtUtc) => Status = IdempotencyStatus.Failed;
}
