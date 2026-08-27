using DoSelect.Application.Support;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Support;

/// <summary>
/// Exercises SupportTicketStore.CreateTicketWithArtifactsAsync directly against the real local
/// DoSelectDb (bypassing HTTP/DTO validation) to prove the transaction really is atomic at the
/// database level: a full success writes all four rows, and a failure partway through leaves
/// zero rows behind. Verification always happens through a brand-new DbContext/scope so no
/// in-memory change tracker state can mask a rollback that did not actually happen in SQL
/// Server.
/// </summary>
public sealed class SupportTicketStoreTransactionTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string MemberEmail = "kafen-test-store-member@doselect.local";

    private readonly WebApplicationFactory<Program> _factory;
    private string _memberId = string.Empty;

    public SupportTicketStoreTransactionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var existing = await dbContext.Users.SingleOrDefaultAsync(u => u.Email == MemberEmail);
        if (existing is not null)
        {
            _memberId = existing.Id;
            return;
        }

        var user = ApplicationUser.CreateMember(Guid.NewGuid(), MemberEmail, DateTime.UtcNow);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        _memberId = user.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateTicketWithArtifactsAsync_OnSuccess_PersistsAllFourRowsTogether()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISupportTicketStore>();
        var nowUtc = DateTime.UtcNow;
        var ticketNumber = NewTestTicketNumber();
        var ticket = NewTicket(ticketNumber, nowUtc);

        var result = await store.CreateTicketWithArtifactsAsync(
            ticket,
            ticketId => NewFirstMessage(ticketId, _memberId, nowUtc),
            ticketId => NewStatusHistory(ticketId, _memberId, nowUtc),
            ticketId => NewSlaEvent(ticketId, nowUtc),
            CancellationToken.None);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        Assert.Equal(1, await verifyDb.SupportTickets.CountAsync(t => t.Id == result.TicketId));
        Assert.Equal(1, await verifyDb.SupportMessages.CountAsync(m => m.SupportTicketId == result.TicketId));
        Assert.Equal(1, await verifyDb.SupportStatusHistories.CountAsync(h => h.SupportTicketId == result.TicketId));
        Assert.Equal(1, await verifyDb.SupportSlaEvents.CountAsync(e => e.SupportTicketId == result.TicketId));
    }

    [Fact]
    public async Task CreateTicketWithArtifactsAsync_WhenArtifactInsertFails_RollsBackTheTicketToo()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISupportTicketStore>();
        var nowUtc = DateTime.UtcNow;
        var ticketNumber = NewTestTicketNumber();
        var ticket = NewTicket(ticketNumber, nowUtc);
        const string nonExistentUserId = "does-not-exist-in-aspnetusers";

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => store.CreateTicketWithArtifactsAsync(
            ticket,
            ticketId => NewFirstMessage(ticketId, nonExistentUserId, nowUtc), // FK violation on SenderUserId
            ticketId => NewStatusHistory(ticketId, _memberId, nowUtc),
            ticketId => NewSlaEvent(ticketId, nowUtc),
            CancellationToken.None));

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        Assert.False(await verifyDb.SupportTickets.AnyAsync(t => t.TicketNumber == ticketNumber));
    }

    [Fact]
    public async Task CreateTicketWithArtifactsAsync_WhenCancelledBeforeCommit_RollsBackAndLeavesNoOrphan()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISupportTicketStore>();
        var nowUtc = DateTime.UtcNow;
        var ticketNumber = NewTestTicketNumber();
        var ticket = NewTicket(ticketNumber, nowUtc);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CreateTicketWithArtifactsAsync(
            ticket,
            ticketId => NewFirstMessage(ticketId, _memberId, nowUtc),
            ticketId => NewStatusHistory(ticketId, _memberId, nowUtc),
            ticketId => NewSlaEvent(ticketId, nowUtc),
            cancelled.Token));

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        Assert.False(await verifyDb.SupportTickets.AnyAsync(t => t.TicketNumber == ticketNumber));
    }

    [Fact]
    public async Task CreateTicketWithArtifactsAsync_WhenTicketNumberCollides_ThrowsCollisionExceptionNotRawSqlException()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISupportTicketStore>();
        var nowUtc = DateTime.UtcNow;
        var ticketNumber = NewTestTicketNumber();

        var firstTicket = NewTicket(ticketNumber, nowUtc);
        await store.CreateTicketWithArtifactsAsync(
            firstTicket,
            ticketId => NewFirstMessage(ticketId, _memberId, nowUtc),
            ticketId => NewStatusHistory(ticketId, _memberId, nowUtc),
            ticketId => NewSlaEvent(ticketId, nowUtc),
            CancellationToken.None);

        var secondTicket = NewTicket(ticketNumber, nowUtc);

        var exception = await Assert.ThrowsAsync<SupportTicketNumberCollisionException>(() => store.CreateTicketWithArtifactsAsync(
            secondTicket,
            ticketId => NewFirstMessage(ticketId, _memberId, nowUtc),
            ticketId => NewStatusHistory(ticketId, _memberId, nowUtc),
            ticketId => NewSlaEvent(ticketId, nowUtc),
            CancellationToken.None));

        Assert.Equal(ticketNumber, exception.TicketNumber);
    }

    // "CS-TEST-" (8 chars) + 16 hex chars stays within the TicketNumber column's 32-char cap
    // while remaining effectively unique per test run.
    private static string NewTestTicketNumber() => $"CS-TEST-{Guid.NewGuid():N}"[..24];

    private SupportTicket NewTicket(string ticketNumber, DateTime nowUtc) => new(
        Guid.CreateVersion7(),
        ticketNumber,
        _memberId,
        orderId: null,
        SupportTicketCategory.Other,
        "Store-level transaction test",
        CasePriority.Normal,
        nowUtc.AddHours(8),
        nowUtc.AddDays(3),
        nowUtc);

    private static SupportMessage NewFirstMessage(long ticketId, string senderUserId, DateTime nowUtc) => new(
        Guid.CreateVersion7(),
        ticketId,
        SupportSenderType.Member,
        senderUserId,
        "test message body",
        isInternal: false,
        aiGenerated: false,
        replyToMessageId: null,
        language: "zh-TW",
        sentAtUtc: nowUtc);

    private static SupportStatusHistory NewStatusHistory(long ticketId, string actorUserId, DateTime nowUtc) => new(
        ticketId,
        fromStatus: null,
        SupportTicketStatus.Open,
        reasonCode: null,
        note: "created by test",
        actorUserId,
        nowUtc);

    private static SupportSlaEvent NewSlaEvent(long ticketId, DateTime nowUtc) => new(
        ticketId,
        SupportSlaEventType.Started,
        SupportSlaTargetType.FirstResponse,
        nowUtc.AddHours(8),
        durationSeconds: null,
        nowUtc,
        metadataJson: null);
}
