using DoSelect.Application.Support.Admin;
using DoSelect.Domain.Members;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Persistence.Support.Admin;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Support;

/// <summary>
/// SQL Server acceptance coverage for the claim-only persistence slice. Every assertion is
/// verified through a fresh context so tracked state cannot conceal transaction or rowversion
/// defects.
/// </summary>
public sealed class AdminSupportTicketClaimStoreTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AdminSupportTicketClaimStoreTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task ClaimAsync_WithActiveAdmin_CommitsAssignmentStatusHistoryAndNewRowVersionAtomically()
    {
        var fixture = await SeedClaimAsync(activeAdmin: true, includeAdminProfile: true);

        var result = await ClaimInNewScopeAsync(fixture.TicketPublicId, fixture.AdminUserId, fixture.RowVersion);

        Assert.Equal(SupportTicketClaimOutcome.Claimed, result.Outcome);
        Assert.NotNull(result.Ticket);
        Assert.Equal(SupportTicketStatus.Assigned, result.Ticket.Status);
        Assert.Equal(fixture.AdminProfilePublicId, result.Ticket.AssigneeAdminPublicId);
        Assert.NotEqual(fixture.RowVersion, result.Ticket.RowVersion);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var ticket = await verifyDb.SupportTickets.AsNoTracking().SingleAsync(t => t.PublicId == fixture.TicketPublicId);
        var history = await verifyDb.SupportAssignmentHistories.AsNoTracking()
            .SingleAsync(h => h.SupportTicketId == ticket.Id);
        Assert.Equal(fixture.AdminUserId, ticket.AssigneeAdminUserId);
        Assert.Equal(SupportTicketStatus.Assigned, ticket.Status);
        Assert.Equal(AssignmentAction.Claim, history.Action);
        Assert.Null(history.FromAdminUserId);
        Assert.Equal(fixture.AdminUserId, history.ToAdminUserId);
        Assert.Equal(fixture.AdminUserId, history.ActorUserId);
    }

    [Fact]
    public async Task ClaimAsync_TwoAdminsRace_ExactlyOneClaimsAndLoserIsAssignmentConflict()
    {
        var first = await SeedClaimAsync(activeAdmin: true, includeAdminProfile: true);
        var secondAdmin = await SeedAdminAsync(active: true, includeProfile: true);
        using var gate = new ManualResetEventSlim(false);

        var firstTask = Task.Run(async () =>
        {
            gate.Wait();
            return await ClaimInNewScopeAsync(first.TicketPublicId, first.AdminUserId, first.RowVersion);
        });
        var secondTask = Task.Run(async () =>
        {
            gate.Wait();
            return await ClaimInNewScopeAsync(first.TicketPublicId, secondAdmin.UserId, first.RowVersion);
        });
        gate.Set();

        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Single(results, r => r.Outcome == SupportTicketClaimOutcome.Claimed);
        Assert.Single(results, r => r.Outcome == SupportTicketClaimOutcome.AssignmentConflict);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var ticketId = await verifyDb.SupportTickets.Where(t => t.PublicId == first.TicketPublicId).Select(t => t.Id).SingleAsync();
        Assert.Equal(1, await verifyDb.SupportAssignmentHistories.CountAsync(h => h.SupportTicketId == ticketId));
    }

    [Fact]
    public async Task ClaimAsync_WithStaleRowVersionOnStillOpenTicket_ReturnsConcurrencyConflictWithoutHistory()
    {
        var fixture = await SeedClaimAsync(activeAdmin: true, includeAdminProfile: true);
        var stale = fixture.RowVersion.ToArray();
        stale[0] ^= 0xff;

        var result = await ClaimInNewScopeAsync(fixture.TicketPublicId, fixture.AdminUserId, stale);

        Assert.Equal(SupportTicketClaimOutcome.ConcurrencyConflict, result.Outcome);
        await AssertTicketRemainsUnclaimedWithoutHistoryAsync(fixture.TicketPublicId);
    }

    [Fact]
    public async Task ClaimAsync_WhenTicketDoesNotExist_ReturnsNotFound()
    {
        var admin = await SeedAdminAsync(active: true, includeProfile: true);

        var result = await ClaimInNewScopeAsync(Guid.NewGuid(), admin.UserId, new byte[8]);

        Assert.Equal(SupportTicketClaimOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task ClaimAsync_WhenTicketIsCancelled_ReturnsAssignmentConflictWithoutHistory()
    {
        var fixture = await SeedClaimAsync(activeAdmin: true, includeAdminProfile: true, cancelled: true);

        var result = await ClaimInNewScopeAsync(fixture.TicketPublicId, fixture.AdminUserId, fixture.RowVersion);

        Assert.Equal(SupportTicketClaimOutcome.AssignmentConflict, result.Outcome);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var ticketId = await db.SupportTickets.Where(t => t.PublicId == fixture.TicketPublicId).Select(t => t.Id).SingleAsync();
        Assert.Empty(await db.SupportAssignmentHistories.Where(h => h.SupportTicketId == ticketId).ToListAsync());
    }

    [Fact]
    public async Task ClaimAsync_WhenHistorySaveFails_RollsBackConditionalTicketUpdate()
    {
        var fixture = await SeedClaimAsync(activeAdmin: true, includeAdminProfile: true);
        string connectionString;
        using (var scope = _factory.Services.CreateScope())
        {
            connectionString = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>()
                .Database.GetConnectionString()!;
        }
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(new RejectAssignmentHistorySaveInterceptor())
            .Options;
        await using var db = new DoSelectDbContext(options);
        var store = new AdminSupportTicketStore(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ClaimAsync(
            fixture.TicketPublicId,
            fixture.AdminUserId,
            fixture.RowVersion,
            DateTime.UtcNow,
            CancellationToken.None));

        await AssertTicketRemainsUnclaimedWithoutHistoryAsync(fixture.TicketPublicId);
    }

    [Fact]
    public async Task ClaimAsync_WhenAdminProfileIsMissing_DoesNotMutateTicket()
    {
        var fixture = await SeedClaimAsync(activeAdmin: true, includeAdminProfile: false);

        var result = await ClaimInNewScopeAsync(fixture.TicketPublicId, fixture.AdminUserId, fixture.RowVersion);

        Assert.Equal(SupportTicketClaimOutcome.AdminNotEligible, result.Outcome);
        Assert.Null(result.Ticket);
        await AssertTicketRemainsUnclaimedWithoutHistoryAsync(fixture.TicketPublicId);
    }

    [Fact]
    public async Task ClaimAsync_WhenAdminProfileIsInactive_DoesNotMutateTicket()
    {
        var fixture = await SeedClaimAsync(activeAdmin: false, includeAdminProfile: true);

        var result = await ClaimInNewScopeAsync(fixture.TicketPublicId, fixture.AdminUserId, fixture.RowVersion);

        Assert.Equal(SupportTicketClaimOutcome.AdminNotEligible, result.Outcome);
        Assert.Null(result.Ticket);
        await AssertTicketRemainsUnclaimedWithoutHistoryAsync(fixture.TicketPublicId);
    }

    private async Task<SupportTicketClaimResult> ClaimInNewScopeAsync(Guid ticketId, string adminId, byte[] rowVersion)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAdminSupportTicketStore>();
        return await store.ClaimAsync(ticketId, adminId, rowVersion, DateTime.UtcNow, CancellationToken.None);
    }

    private async Task<ClaimFixture> SeedClaimAsync(bool activeAdmin, bool includeAdminProfile, bool cancelled = false)
    {
        var admin = await SeedAdminAsync(activeAdmin, includeAdminProfile);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var now = DateTime.UtcNow;
        var member = ApplicationUser.CreateMember(Guid.NewGuid(), $"claim-member-{Guid.NewGuid():N}@example.test", now);
        db.Users.Add(member);
        await db.SaveChangesAsync();
        var ticket = new SupportTicket(
            Guid.NewGuid(),
            $"CLM-{Guid.NewGuid():N}"[..24],
            member.Id,
            orderId: null,
            SupportTicketCategory.Other,
            "SQL claim integration test",
            CasePriority.Normal,
            now.AddHours(8),
            now.AddDays(3),
            now);
        if (cancelled)
        {
            ticket.Transition(SupportTicketStatus.Cancelled, now.AddSeconds(1));
        }
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();
        return new ClaimFixture(ticket.PublicId, ticket.RowVersion.ToArray(), admin.UserId, admin.ProfilePublicId);
    }

    private async Task<AdminFixture> SeedAdminAsync(bool active, bool includeProfile)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var now = DateTime.UtcNow;
        var user = ApplicationUser.CreateAdmin(Guid.NewGuid(), $"claim-admin-{Guid.NewGuid():N}@example.test", now);
        user.ConfirmEmail(now.AddMilliseconds(1));
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var profilePublicId = Guid.NewGuid();
        if (includeProfile)
        {
            var profile = new AdminProfile(user.Id, profilePublicId, $"EMP-{Guid.NewGuid():N}", "Claim Agent", now);
            if (!active)
            {
                profile.SetActive(false, now.AddMilliseconds(2));
            }
            db.AdminProfiles.Add(profile);
            await db.SaveChangesAsync();
        }
        return new AdminFixture(user.Id, profilePublicId);
    }

    private async Task AssertTicketRemainsUnclaimedWithoutHistoryAsync(Guid ticketPublicId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var ticket = await db.SupportTickets.AsNoTracking().SingleAsync(t => t.PublicId == ticketPublicId);
        Assert.Null(ticket.AssigneeAdminUserId);
        Assert.Equal(SupportTicketStatus.Open, ticket.Status);
        Assert.Empty(await db.SupportAssignmentHistories.Where(h => h.SupportTicketId == ticket.Id).ToListAsync());
    }

    private sealed record ClaimFixture(Guid TicketPublicId, byte[] RowVersion, string AdminUserId, Guid AdminProfilePublicId);
    private sealed record AdminFixture(string UserId, Guid ProfilePublicId);

    private sealed class RejectAssignmentHistorySaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<SupportAssignmentHistory>()
                .Any(entry => entry.State == EntityState.Added))
            {
                throw new InvalidOperationException("Injected history persistence failure.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
