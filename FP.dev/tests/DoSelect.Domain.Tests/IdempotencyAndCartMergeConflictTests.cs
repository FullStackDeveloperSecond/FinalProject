using DoSelect.Domain.Idempotency;
using DoSelect.Domain.Shopping;

namespace DoSelect.Domain.Tests;

public sealed class IdempotencyAndCartMergeConflictTests
{
    [Fact]
    public void IdempotencyRecord_Complete_PreservesReplayMetadataAndUpdatesVersionState()
    {
        var createdAt = DateTime.UtcNow;
        var record = new IdempotencyRecord(
            actorScopeHash: new byte[32],
            operation: "cart.merge",
            key: "merge-request-001",
            requestHash: Enumerable.Repeat((byte)1, 32).ToArray(),
            expiresAtUtc: createdAt.AddHours(24),
            createdAtUtc: createdAt);

        record.Complete(
            responseStatusCode: 200,
            responseHeadersJson: "{\"content-type\":\"application/json\"}",
            responseSummary: "{\"version\":1,\"cartPublicId\":\"00000000-0000-0000-0000-000000000001\"}",
            completedAtUtc: createdAt.AddSeconds(1));

        Assert.Equal(IdempotencyStatus.Succeeded, record.Status);
        Assert.Equal(200, record.ResponseStatusCode);
        Assert.Equal(createdAt.AddSeconds(1), record.UpdatedAtUtc);
        Assert.NotNull(record.ResponseSummary);
    }

    [Fact]
    public void CartMergeConflict_RemainsBlockingUntilExplicitlyResolved()
    {
        var createdAt = DateTime.UtcNow;
        var conflict = new CartMergeConflict(
            Guid.CreateVersion7(),
            memberCartId: 10,
            guestCartId: 20,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            guestQuantity: 60,
            memberQuantity: 50,
            acceptedQuantity: 50,
            reason: "cart_quantity_exceeded",
            createdAt);

        Assert.True(conflict.IsBlocking);

        conflict.Resolve("member_quantity_adjusted", createdAt.AddMinutes(1));

        Assert.False(conflict.IsBlocking);
        Assert.Equal(createdAt.AddMinutes(1), conflict.ResolvedAtUtc);
        Assert.Equal("member_quantity_adjusted", conflict.ResolutionCode);
    }
}
