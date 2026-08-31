using System.Data;
using DoSelect.Application.Idempotency;
using DoSelect.Domain.Idempotency;
using DoSelect.Domain.Shopping;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests.Idempotency;

[CollectionDefinition(nameof(IdempotencyExecutorCollection))]
public sealed class IdempotencyExecutorCollection : ICollectionFixture<IdempotencyExecutorFixture>;

[Collection(nameof(IdempotencyExecutorCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class IdempotencyExecutorTests
{
    private readonly IdempotencyExecutorFixture _fixture;

    public IdempotencyExecutorTests(IdempotencyExecutorFixture fixture) => _fixture = fixture;

    [SqlServerFact]
    public async Task ExecuteAsync_WhenSameCommandIsReplayed_RunsHandlerOnceAndReturnsStoredResult()
    {
        await using var context = IdempotencyExecutorFixture.CreateContext();
        var (user, cart) = await SeedMemberCartAsync(context);
        var executor = CreateExecutor(context);
        var command = Command(user.PublicId, "replay-key-001", new { cart.PublicId });
        var handlerCalls = 0;

        var first = await executor.ExecuteAsync(
            command,
            async cancellationToken =>
            {
                handlerCalls++;
                cart.ChangeStatus(CartStatus.Converted, DateTime.UtcNow);
                await context.SaveChangesAsync(cancellationToken);
                return new IdempotencyResponse<string>(200, "first", "{\"version\":1,\"value\":\"first\"}");
            },
            (stored, _) => Task.FromResult("replayed:" + stored.ResponseSummary),
            CancellationToken.None);

        var replay = await executor.ExecuteAsync(
            command,
            _ => throw new InvalidOperationException("The replay must not execute the handler."),
            (stored, _) => Task.FromResult("replayed:" + stored.ResponseSummary),
            CancellationToken.None);

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(1, handlerCalls);
        Assert.StartsWith("replayed:", replay.Body, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task ExecuteAsync_WhenHandlerFails_RollsBackBusinessDataAndReservationSoRetryCanSucceed()
    {
        Guid userPublicId;
        Guid cartPublicId;
        await using (var setup = IdempotencyExecutorFixture.CreateContext())
        {
            var seeded = await SeedMemberCartAsync(setup);
            userPublicId = seeded.User.PublicId;
            cartPublicId = seeded.Cart.PublicId;
        }

        var command = Command(userPublicId, "rollback-key-001", new { cartPublicId });
        await using (var failingContext = IdempotencyExecutorFixture.CreateContext())
        {
            var cart = await failingContext.Carts.SingleAsync(candidate => candidate.PublicId == cartPublicId);
            var executor = CreateExecutor(failingContext);

            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync<string>(
                command,
                async cancellationToken =>
                {
                    cart.ChangeStatus(CartStatus.Converted, DateTime.UtcNow);
                    await failingContext.SaveChangesAsync(cancellationToken);
                    throw new InvalidOperationException("simulated failure");
                },
                (stored, _) => Task.FromResult(stored.ResponseSummary),
                CancellationToken.None));
        }

        await using (var verify = IdempotencyExecutorFixture.CreateContext())
        {
            Assert.Equal(CartStatus.Active, await verify.Carts.Where(c => c.PublicId == cartPublicId).Select(c => c.Status).SingleAsync());
            Assert.False(await verify.IdempotencyRecords.AnyAsync(record =>
                record.Operation == command.Operation && record.Key == command.Key));
        }

        await using (var retryContext = IdempotencyExecutorFixture.CreateContext())
        {
            var retry = await CreateExecutor(retryContext).ExecuteAsync(
                command,
                _ => Task.FromResult(new IdempotencyResponse<string>(200, "retry-succeeded", "{\"version\":1}")),
                (stored, _) => Task.FromResult(stored.ResponseSummary),
                CancellationToken.None);
            Assert.Equal("retry-succeeded", retry.Body);
        }
    }

    [SqlServerFact]
    public async Task ExecuteAsync_WhenSameKeyUsesDifferentPayload_ThrowsPayloadConflict()
    {
        var userPublicId = Guid.CreateVersion7();
        await using var context = IdempotencyExecutorFixture.CreateContext();
        var executor = CreateExecutor(context);

        await executor.ExecuteAsync(
            Command(userPublicId, "payload-key-001", new { value = 1 }),
            _ => Task.FromResult(new IdempotencyResponse<string>(200, "one", "{\"version\":1}")),
            (stored, _) => Task.FromResult(stored.ResponseSummary),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<IdempotencyConflictException>(() => executor.ExecuteAsync(
            Command(userPublicId, "payload-key-001", new { value = 2 }),
            _ => Task.FromResult(new IdempotencyResponse<string>(200, "two", "{\"version\":1}")),
            (stored, _) => Task.FromResult(stored.ResponseSummary),
            CancellationToken.None));

        Assert.Equal(IdempotencyErrorCodes.PayloadConflict, exception.ErrorCode);
        Assert.Null(exception.RetryAfterSeconds);
    }

    [SqlServerFact]
    public async Task ExecuteAsync_WhenSameCommandArrivesConcurrently_RejectsLoserWithRetryAfter()
    {
        var userPublicId = Guid.CreateVersion7();
        var command = Command(userPublicId, "concurrent-key-001", new { value = 1 });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var contextA = IdempotencyExecutorFixture.CreateContext();
        await using var contextB = IdempotencyExecutorFixture.CreateContext();
        var firstTask = CreateExecutor(contextA).ExecuteAsync(
            command,
            async _ =>
            {
                entered.SetResult();
                await release.Task;
                return new IdempotencyResponse<string>(200, "winner", "{\"version\":1}");
            },
            (stored, _) => Task.FromResult(stored.ResponseSummary),
            CancellationToken.None);

        await entered.Task;
        var exception = await Assert.ThrowsAsync<IdempotencyConflictException>(() => CreateExecutor(contextB).ExecuteAsync(
            command,
            _ => Task.FromResult(new IdempotencyResponse<string>(200, "loser", "{\"version\":1}")),
            (stored, _) => Task.FromResult(stored.ResponseSummary),
            CancellationToken.None));
        release.SetResult();
        var winner = await firstTask;

        Assert.Equal("winner", winner.Body);
        Assert.Equal(IdempotencyErrorCodes.RequestInProgress, exception.ErrorCode);
        Assert.Equal(3, exception.RetryAfterSeconds);
    }

    [SqlServerFact]
    public async Task ExecuteAsync_WhenSerializableIsRequested_HandlerRunsInsideSerializableTransaction()
    {
        var userPublicId = Guid.CreateVersion7();
        await using var context = IdempotencyExecutorFixture.CreateContext();
        var observedIsolationLevel = IsolationLevel.Unspecified;

        await CreateExecutor(context).ExecuteAsync(
            Command(userPublicId, "serializable-key-001", new { value = 1 }),
            _ =>
            {
                observedIsolationLevel = context.Database.CurrentTransaction!
                    .GetDbTransaction()
                    .IsolationLevel;
                return Task.FromResult(
                    new IdempotencyResponse<string>(200, "ok", "{\"version\":1}"));
            },
            (stored, _) => Task.FromResult(stored.ResponseSummary),
            CancellationToken.None,
            IsolationLevel.Serializable);

        Assert.Equal(IsolationLevel.Serializable, observedIsolationLevel);
    }

    [SqlServerFact]
    public async Task CartMergeConflict_PersistsAsBlockingUntilExplicitResolution()
    {
        Guid conflictPublicId;
        await using (var context = IdempotencyExecutorFixture.CreateContext())
        {
            var now = DateTime.UtcNow;
            var user = ApplicationUser.CreateMember(
                Guid.CreateVersion7(),
                $"{Guid.NewGuid():N}@doselect.test",
                now);
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var memberCart = Cart.CreateForMember(
                Guid.CreateVersion7(),
                user.Id,
                now.AddDays(30),
                now);
            var guestCart = Cart.CreateForGuest(
                Guid.CreateVersion7(),
                Enumerable.Repeat((byte)7, 32).ToArray(),
                now.AddDays(30),
                now);
            context.Carts.AddRange(memberCart, guestCart);
            await context.SaveChangesAsync();

            var conflict = new CartMergeConflict(
                Guid.CreateVersion7(),
                memberCart.Id,
                guestCart.Id,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                guestQuantity: 60,
                memberQuantity: 50,
                acceptedQuantity: 50,
                reason: "cart_quantity_exceeded",
                now);
            conflictPublicId = conflict.PublicId;
            context.CartMergeConflicts.Add(conflict);
            await context.SaveChangesAsync();

            Assert.NotEmpty(conflict.RowVersion);
        }

        await using (var context = IdempotencyExecutorFixture.CreateContext())
        {
            var conflict = await context.CartMergeConflicts.SingleAsync(
                candidate => candidate.PublicId == conflictPublicId);
            Assert.True(conflict.IsBlocking);

            conflict.Resolve("member_quantity_adjusted", DateTime.UtcNow);
            await context.SaveChangesAsync();
        }

        await using (var context = IdempotencyExecutorFixture.CreateContext())
        {
            Assert.False(await context.CartMergeConflicts.AnyAsync(conflict =>
                conflict.PublicId == conflictPublicId && conflict.ResolvedAtUtc == null));
        }
    }

    private static EfIdempotencyExecutor CreateExecutor(DoSelectDbContext context) =>
        new(context, Options.Create(new IdempotencyOptions { ActorScopePepper = IdempotencyExecutorFixture.Pepper }), TimeProvider.System);

    private static IdempotencyCommand Command<TRequest>(Guid userPublicId, string key, TRequest request) =>
        IdempotencyCommand.Create(IdempotencyActorScope.ForUser(userPublicId), "cart.merge", key, request);

    private static async Task<(ApplicationUser User, Cart Cart)> SeedMemberCartAsync(DoSelectDbContext context)
    {
        var now = DateTime.UtcNow;
        var user = ApplicationUser.CreateMember(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", now);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var cart = Cart.CreateForMember(Guid.CreateVersion7(), user.Id, now.AddDays(30), now);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();
        return (user, cart);
    }
}

public sealed class IdempotencyExecutorFixture : IAsyncLifetime
{
    public const string Pepper = "integration-test-pepper-at-least-thirty-two-bytes";
    public const string ConnectionStringEnvironmentVariable =
        "DOSELECT_SQLSERVER_TEST_CONNECTION";

    public static bool IsEnabled =>
        !string.IsNullOrWhiteSpace(GetConfiguredConnectionString()) ||
        (OperatingSystem.IsWindows() &&
         !string.Equals(
             Environment.GetEnvironmentVariable("CI"),
             "true",
             StringComparison.OrdinalIgnoreCase));

    public async Task InitializeAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(
                global::DoSelect.Infrastructure.Tests.SqlServerTestConnection.Build(
                    "DoSelectIdempotencyTests"))
            .Options);

    private static string? GetConfiguredConnectionString() =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
}

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!IdempotencyExecutorFixture.IsEnabled)
        {
            Skip = $"Set {IdempotencyExecutorFixture.ConnectionStringEnvironmentVariable} to run SQL Server integration tests.";
        }
    }
}

/// <summary>
/// <see cref="SqlServerFactAttribute"/> 的 Theory 版本，跳過條件相同。
/// </summary>
public sealed class SqlServerTheoryAttribute : TheoryAttribute
{
    public SqlServerTheoryAttribute()
    {
        if (!IdempotencyExecutorFixture.IsEnabled)
        {
            Skip = $"Set {IdempotencyExecutorFixture.ConnectionStringEnvironmentVariable} to run SQL Server integration tests.";
        }
    }
}
