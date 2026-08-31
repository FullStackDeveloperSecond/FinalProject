using DoSelect.Application.Idempotency;

namespace DoSelect.Application.Tests;

public sealed class IdempotencyActorScopeTests
{
    private const string Pepper = "idempotency-actor-scope-test-pepper";

    [Fact]
    public void ForAdmin_UsesAStableNamespaceDistinctFromAUserScope()
    {
        var publicId = Guid.NewGuid();

        var first = IdempotencyActorScope.ForAdmin(publicId).ComputeHash(Pepper);
        var second = IdempotencyActorScope.ForAdmin(publicId).ComputeHash(Pepper);
        var user = IdempotencyActorScope.ForUser(publicId).ComputeHash(Pepper);

        Assert.Equal(first, second);
        Assert.NotEqual(user, first);
    }

    [Fact]
    public void ForAdmin_RejectsAnEmptyPublicId()
    {
        Assert.Throws<ArgumentException>(() => IdempotencyActorScope.ForAdmin(Guid.Empty));
    }

    [Fact]
    public void ForGuestOrderAccess_IsStableAndDistinctFromOtherGuestScopes()
    {
        var publicId = Guid.NewGuid();

        var first = IdempotencyActorScope.ForGuestOrderAccess(publicId).ComputeHash(Pepper);
        var second = IdempotencyActorScope.ForGuestOrderAccess(publicId).ComputeHash(Pepper);
        var cart = IdempotencyActorScope.ForGuestCart(publicId).ComputeHash(Pepper);

        Assert.Equal(first, second);
        Assert.NotEqual(first, cart);
    }

    [Fact]
    public void ForGuestOrderAccess_RejectsAnEmptyPublicId() =>
        Assert.Throws<ArgumentException>(() =>
            IdempotencyActorScope.ForGuestOrderAccess(Guid.Empty));
}
