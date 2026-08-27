using DoSelect.Application.Auditing;
using DoSelect.Application.Support.Admin;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Support;

/// <summary>
/// DES-23 SQL Server acceptance coverage for assign/transfer/change-priority/change-status/
/// cancel/reopen: every assertion re-reads through a fresh scope so tracked state cannot conceal
/// a transaction, RowVersion, or zero-side-effect defect. Races use the same
/// ManualResetEventSlim + Task.WhenAll pattern as AdminSupportTicketClaimStoreTests.
/// </summary>
public sealed class AdminSupportTicketSuperviseStoreTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Reason = "sql acceptance reason";
    private readonly WebApplicationFactory<Program> _factory;

    public AdminSupportTicketSuperviseStoreTests(WebApplicationFactory<Program> factory) => _factory = factory;

    // ---- assign ---------------------------------------------------------

    [Fact]
    public async Task AssignAsync_WithEligibleTarget_CommitsTicketHistoryAndAuditAtomically()
    {
        var ticket = await SeedOpenTicketAsync();
        var target = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerService);
        var actor = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerServiceSupervisor);

        var result = await AssignInNewScopeAsync(ticket.PublicId, target.PublicId, actor.UserId, ticket.RowVersion);

        Assert.Equal(SupportTicketAssignOutcome.Success, result.Outcome);
        Assert.Equal(target.PublicId, result.Ticket!.AssigneeAdminPublicId);
        Assert.NotEqual(ticket.RowVersion, result.Ticket.RowVersion);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var row = await db.SupportTickets.AsNoTracking().SingleAsync(t => t.PublicId == ticket.PublicId);
        Assert.Equal(target.UserId, row.AssigneeAdminUserId);
        Assert.Equal(SupportTicketStatus.Assigned, row.Status);
        var history = await db.SupportAssignmentHistories.AsNoTracking().SingleAsync(h => h.SupportTicketId == row.Id);
        Assert.Equal(AssignmentAction.Assign, history.Action);
        Assert.Null(history.FromAdminUserId);
        Assert.Equal(target.UserId, history.ToAdminUserId);
        Assert.Equal(Reason, history.Reason);
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == AuditActions.SupportTicketAssign && a.ResourcePublicId == ticket.PublicId);
        Assert.Equal(AuditResult.Success, audit.Result);
        Assert.Equal(ticket.PublicId, audit.ResourcePublicId);
    }

    [Fact]
    public async Task AssignAsync_TwoSupervisorsRaceOnSameUnassignedTicket_ExactlyOneWinsAndLoserHasZeroSideEffects()
    {
        var ticket = await SeedOpenTicketAsync();
        var targetA = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerService);
        var targetB = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerService);
        var actorA = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerServiceSupervisor);
        var actorB = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerServiceSupervisor);
        using var gate = new ManualResetEventSlim(false);

        var taskA = Task.Run(async () => { gate.Wait(); return await AssignInNewScopeAsync(ticket.PublicId, targetA.PublicId, actorA.UserId, ticket.RowVersion); });
        var taskB = Task.Run(async () => { gate.Wait(); return await AssignInNewScopeAsync(ticket.PublicId, targetB.PublicId, actorB.UserId, ticket.RowVersion); });
        gate.Set();
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Single(results, r => r.Outcome == SupportTicketAssignOutcome.Success);
        Assert.Single(results, r => r.Outcome == SupportTicketAssignOutcome.AssignmentConflict);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var row = await db.SupportTickets.AsNoTracking().SingleAsync(t => t.PublicId == ticket.PublicId);
        Assert.Contains(row.AssigneeAdminUserId, new[] { targetA.UserId, targetB.UserId });
        Assert.Equal(1, await db.SupportAssignmentHistories.CountAsync(h => h.SupportTicketId == row.Id));
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == AuditActions.SupportTicketAssign && a.ResourcePublicId == ticket.PublicId));
    }

    [Fact]
    public async Task AssignAsync_RacesAgainstSelfClaimOnSameTicket_ExactlyOneWinsAndLoserHasZeroSideEffects()
    {
        var ticket = await SeedOpenTicketAsync();
        var claimant = await SeedAdminAsync(active: true, role: null);
        var target = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerService);
        var supervisorActor = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerServiceSupervisor);
        using var gate = new ManualResetEventSlim(false);

        var claimTask = Task.Run(async () =>
        {
            gate.Wait();
            using var scope = _factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IAdminSupportTicketStore>();
            return await store.ClaimAsync(ticket.PublicId, claimant.UserId, ticket.RowVersion, DateTime.UtcNow, CancellationToken.None);
        });
        var assignTask = Task.Run(async () => { gate.Wait(); return await AssignInNewScopeAsync(ticket.PublicId, target.PublicId, supervisorActor.UserId, ticket.RowVersion); });
        gate.Set();
        var claimResult = await claimTask;
        var assignResult = await assignTask;

        var outcomes = new[] { claimResult.Outcome == SupportTicketClaimOutcome.Claimed, assignResult.Outcome == SupportTicketAssignOutcome.Success };
        Assert.Single(outcomes, succeeded => succeeded);
        using var scope2 = _factory.Services.CreateScope();
        var db = scope2.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var row = await db.SupportTickets.AsNoTracking().SingleAsync(t => t.PublicId == ticket.PublicId);
        Assert.Equal(1, await db.SupportAssignmentHistories.CountAsync(h => h.SupportTicketId == row.Id));
    }

    [Fact]
    public async Task AssignAsync_WhenTargetIsInactiveOrUnqualified_ReturnsTargetNotEligibleWithoutMutatingTicket()
    {
        var ticket = await SeedOpenTicketAsync();
        var inactiveTarget = await SeedAdminAsync(active: false, role: AuditRoleNames.CustomerService);
        var unqualifiedTarget = await SeedAdminAsync(active: true, role: null);
        var actor = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerServiceSupervisor);

        var inactiveResult = await AssignInNewScopeAsync(ticket.PublicId, inactiveTarget.PublicId, actor.UserId, ticket.RowVersion);
        var unqualifiedResult = await AssignInNewScopeAsync(ticket.PublicId, unqualifiedTarget.PublicId, actor.UserId, ticket.RowVersion);

        Assert.Equal(SupportTicketAssignOutcome.TargetNotEligible, inactiveResult.Outcome);
        Assert.Equal(SupportTicketAssignOutcome.TargetNotEligible, unqualifiedResult.Outcome);
        await AssertTicketUnassignedWithNoHistoryOrAuditAsync(ticket.PublicId);
    }

    // ---- transfer ---------------------------------------------------------

    [Fact]
    public async Task TransferAsync_FromCurrentAssigneeToQualifiedTarget_CommitsHistoryWithFromAndTo()
    {
        var (ticket, currentAssignee) = await SeedAssignedTicketAsync();
        var target = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerServiceSupervisor);
        var actor = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerServiceSupervisor);

        var result = await TransferInNewScopeAsync(ticket.PublicId, target.PublicId, actor.UserId, ticket.RowVersion);

        Assert.Equal(SupportTicketAssignOutcome.Success, result.Outcome);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var row = await db.SupportTickets.AsNoTracking().SingleAsync(t => t.PublicId == ticket.PublicId);
        Assert.Equal(target.UserId, row.AssigneeAdminUserId);
        var history = await db.SupportAssignmentHistories.AsNoTracking().SingleAsync(h => h.SupportTicketId == row.Id);
        Assert.Equal(AssignmentAction.Reassign, history.Action);
        Assert.Equal(currentAssignee.UserId, history.FromAdminUserId);
        Assert.Equal(target.UserId, history.ToAdminUserId);
    }

    [Fact]
    public async Task TransferAsync_ToCurrentAssignee_ReturnsAssignmentConflictWithoutMutating()
    {
        var (ticket, currentAssignee) = await SeedAssignedTicketAsync();
        var actor = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerServiceSupervisor);

        var result = await TransferInNewScopeAsync(ticket.PublicId, currentAssignee.PublicId, actor.UserId, ticket.RowVersion);

        Assert.Equal(SupportTicketAssignOutcome.AssignmentConflict, result.Outcome);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var row = await db.SupportTickets.AsNoTracking().SingleAsync(t => t.PublicId == ticket.PublicId);
        Assert.Equal(currentAssignee.UserId, row.AssigneeAdminUserId);
        Assert.Empty(await db.SupportAssignmentHistories.Where(h => h.SupportTicketId == row.Id).ToListAsync());
    }

    [Fact]
    public async Task TransferAsync_TwoSupervisorsRaceToDifferentTargets_ExactlyOneWinsAndLoserHasZeroSideEffects()
    {
        var (ticket, _) = await SeedAssignedTicketAsync();
        var targetA = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerService);
        var targetB = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerService);
        var actorA = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerServiceSupervisor);
        var actorB = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerServiceSupervisor);
        using var gate = new ManualResetEventSlim(false);

        var taskA = Task.Run(async () => { gate.Wait(); return await TransferInNewScopeAsync(ticket.PublicId, targetA.PublicId, actorA.UserId, ticket.RowVersion); });
        var taskB = Task.Run(async () => { gate.Wait(); return await TransferInNewScopeAsync(ticket.PublicId, targetB.PublicId, actorB.UserId, ticket.RowVersion); });
        gate.Set();
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Single(results, r => r.Outcome == SupportTicketAssignOutcome.Success);
        Assert.Single(results, r => r.Outcome is SupportTicketAssignOutcome.AssignmentConflict or SupportTicketAssignOutcome.ConcurrencyConflict);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var row = await db.SupportTickets.AsNoTracking().SingleAsync(t => t.PublicId == ticket.PublicId);
        Assert.Equal(1, await db.SupportAssignmentHistories.CountAsync(h => h.SupportTicketId == row.Id));
    }

    // ---- change-priority ---------------------------------------------------------

    [Fact]
    public async Task ChangePriorityAsync_ByAssignee_UpdatesPriorityAndWritesBeforeAfterAudit()
    {
        var (ticket, assignee) = await SeedAssignedTicketAsync();

        var result = await ChangePriorityInNewScopeAsync(ticket.PublicId, assignee.UserId, canSupervise: false, CasePriority.Urgent, ticket.RowVersion);

        Assert.Equal(SupportTicketMutationOutcome.Success, result.Outcome);
        Assert.Equal(CasePriority.Urgent, result.Ticket!.Priority);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == AuditActions.SupportTicketChangePriority && a.ResourcePublicId == ticket.PublicId);
        Assert.Contains("Normal", audit.ChangedFieldsJson);
        Assert.Contains("Urgent", audit.ChangedFieldsJson);
    }

    [Fact]
    public async Task ChangePriorityAsync_ByNonAssigneeWithoutSupervise_IsOutOfActorScopeAndReturnsNotFound()
    {
        var (ticket, _) = await SeedAssignedTicketAsync();
        var otherHandler = await SeedAdminAsync(active: true, role: null);

        var result = await ChangePriorityInNewScopeAsync(ticket.PublicId, otherHandler.UserId, canSupervise: false, CasePriority.High, ticket.RowVersion);

        Assert.Equal(SupportTicketMutationOutcome.NotFound, result.Outcome);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var row = await db.SupportTickets.AsNoTracking().SingleAsync(t => t.PublicId == ticket.PublicId);
        Assert.Equal(CasePriority.Normal, row.Priority);
    }

    [Fact]
    public async Task ChangePriorityAsync_BySupervisorOnUnrelatedTicket_Succeeds()
    {
        var (ticket, _) = await SeedAssignedTicketAsync();
        var supervisor = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerServiceSupervisor);

        var result = await ChangePriorityInNewScopeAsync(ticket.PublicId, supervisor.UserId, canSupervise: true, CasePriority.Low, ticket.RowVersion);

        Assert.Equal(SupportTicketMutationOutcome.Success, result.Outcome);
        Assert.Equal(CasePriority.Low, result.Ticket!.Priority);
    }

    [Fact]
    public async Task ChangePriorityAsync_WithStaleRowVersion_ReturnsConcurrencyConflictWithoutMutatingOrAuditing()
    {
        var (ticket, assignee) = await SeedAssignedTicketAsync();
        var stale = ticket.RowVersion.ToArray();
        stale[0] ^= 0xff;

        var result = await ChangePriorityInNewScopeAsync(ticket.PublicId, assignee.UserId, canSupervise: false, CasePriority.Urgent, stale);

        Assert.Equal(SupportTicketMutationOutcome.ConcurrencyConflict, result.Outcome);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var row = await db.SupportTickets.AsNoTracking().SingleAsync(t => t.PublicId == ticket.PublicId);
        Assert.Equal(CasePriority.Normal, row.Priority);
        Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.SupportTicketChangePriority && a.ResourcePublicId == ticket.PublicId));
    }

    // ---- change-status / cancel / reopen ---------------------------------------------------------

    [Fact]
    public async Task ChangeStatusAsync_LegalTransition_CommitsStatusHistoryAndAudit()
    {
        var (ticket, assignee) = await SeedAssignedTicketAsync();

        var result = await ChangeStatusInNewScopeAsync(ticket.PublicId, assignee.UserId, false, SupportTicketStatus.InProgress, ticket.RowVersion);

        Assert.Equal(SupportTicketMutationOutcome.Success, result.Outcome);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var row = await db.SupportTickets.AsNoTracking().SingleAsync(t => t.PublicId == ticket.PublicId);
        Assert.Equal(SupportTicketStatus.InProgress, row.Status);
        Assert.True(await db.SupportStatusHistories.AnyAsync(h => h.SupportTicketId == row.Id && h.ToStatus == SupportTicketStatus.InProgress));
    }

    [Fact]
    public async Task ChangeStatusAsync_IllegalTransition_ReturnsStateConflictWithoutMutating()
    {
        var ticket = await SeedOpenTicketAsync();
        var actor = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerServiceSupervisor);

        var result = await ChangeStatusInNewScopeAsync(ticket.PublicId, actor.UserId, true, SupportTicketStatus.Resolved, ticket.RowVersion);

        Assert.Equal(SupportTicketMutationOutcome.StateConflict, result.Outcome);
        await AssertStatusUnchangedAsync(ticket.PublicId, SupportTicketStatus.Open);
    }

    [Theory]
    [InlineData(SupportTicketStatus.Cancelled)]
    public async Task ChangeStatusAsync_RejectsDedicatedActionEdges(SupportTicketStatus targetStatus)
    {
        var ticket = await SeedOpenTicketAsync();
        var actor = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerServiceSupervisor);

        var result = await ChangeStatusInNewScopeAsync(ticket.PublicId, actor.UserId, true, targetStatus, ticket.RowVersion);

        Assert.Equal(SupportTicketMutationOutcome.StateConflict, result.Outcome);
    }

    [Fact]
    public async Task CancelAsync_WhileOpenAndNoHumanReply_TransitionsToCancelled()
    {
        var ticket = await SeedOpenTicketAsync();
        var actor = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerServiceSupervisor);

        var result = await CancelInNewScopeAsync(ticket.PublicId, actor.UserId, true, ticket.RowVersion);

        Assert.Equal(SupportTicketMutationOutcome.Success, result.Outcome);
        await AssertStatusUnchangedAsync(ticket.PublicId, SupportTicketStatus.Cancelled);
    }

    [Fact]
    public async Task CancelAsync_AfterFirstHumanResponse_ReturnsStateConflictWithoutMutating()
    {
        var (ticket, assignee) = await SeedAssignedTicketAsync(firstHumanResponse: true);

        var result = await CancelInNewScopeAsync(ticket.PublicId, assignee.UserId, false, ticket.RowVersion);

        Assert.Equal(SupportTicketMutationOutcome.StateConflict, result.Outcome);
        await AssertStatusUnchangedAsync(ticket.PublicId, SupportTicketStatus.Assigned);
    }

    [Fact]
    public async Task ReopenAsync_FromResolved_TransitionsToInProgressAndIncrementsReopenCount()
    {
        var ticket = await SeedResolvedTicketAsync();
        var actor = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerServiceSupervisor);

        var result = await ReopenInNewScopeAsync(ticket.PublicId, actor.UserId, true, ticket.RowVersion);

        Assert.Equal(SupportTicketMutationOutcome.Success, result.Outcome);
        Assert.Equal(1, result.Ticket!.ReopenCount);
        await AssertStatusUnchangedAsync(ticket.PublicId, SupportTicketStatus.InProgress);
    }

    [Fact]
    public async Task ReopenAsync_FromClosed_ReturnsStateConflictWithoutMutating()
    {
        var ticket = await SeedClosedTicketAsync();
        var actor = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerServiceSupervisor);

        var result = await ReopenInNewScopeAsync(ticket.PublicId, actor.UserId, true, ticket.RowVersion);

        Assert.Equal(SupportTicketMutationOutcome.StateConflict, result.Outcome);
        await AssertStatusUnchangedAsync(ticket.PublicId, SupportTicketStatus.Closed);
    }

    // ---- helpers ---------------------------------------------------------

    private async Task<SupportTicketAssignResult> AssignInNewScopeAsync(Guid ticketId, Guid targetPublicId, string actorUserId, byte[] rowVersion)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAdminSupportTicketStore>();
        return await store.AssignAsync(NewAssignCommand(ticketId, targetPublicId, actorUserId, rowVersion), CancellationToken.None);
    }

    private async Task<SupportTicketAssignResult> TransferInNewScopeAsync(Guid ticketId, Guid targetPublicId, string actorUserId, byte[] rowVersion)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAdminSupportTicketStore>();
        return await store.TransferAsync(NewAssignCommand(ticketId, targetPublicId, actorUserId, rowVersion), CancellationToken.None);
    }

    private async Task<SupportTicketMutationResult> ChangePriorityInNewScopeAsync(Guid ticketId, string actorUserId, bool canSupervise, CasePriority priority, byte[] rowVersion)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAdminSupportTicketStore>();
        return await store.ChangePriorityAsync(
            new SupportTicketChangePriorityCommand(
                ticketId, actorUserId, ["CustomerService"], canSupervise, rowVersion, DateTime.UtcNow,
                "corr", "0123456789abcdef0123456789abcdef", null, priority, Reason),
            CancellationToken.None);
    }

    private async Task<SupportTicketMutationResult> ChangeStatusInNewScopeAsync(Guid ticketId, string actorUserId, bool canSupervise, SupportTicketStatus targetStatus, byte[] rowVersion)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAdminSupportTicketStore>();
        return await store.ChangeStatusAsync(
            new SupportTicketChangeStatusCommand(
                ticketId, actorUserId, ["CustomerService"], canSupervise, rowVersion, DateTime.UtcNow,
                "corr", "0123456789abcdef0123456789abcdef", null, targetStatus, Reason),
            CancellationToken.None);
    }

    private async Task<SupportTicketMutationResult> CancelInNewScopeAsync(Guid ticketId, string actorUserId, bool canSupervise, byte[] rowVersion)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAdminSupportTicketStore>();
        return await store.CancelAsync(
            new SupportTicketReasonCommand(
                ticketId, actorUserId, ["CustomerService"], canSupervise, rowVersion, DateTime.UtcNow,
                "corr", "0123456789abcdef0123456789abcdef", null, Reason),
            CancellationToken.None);
    }

    private async Task<SupportTicketMutationResult> ReopenInNewScopeAsync(Guid ticketId, string actorUserId, bool canSupervise, byte[] rowVersion)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAdminSupportTicketStore>();
        return await store.ReopenAsync(
            new SupportTicketReasonCommand(
                ticketId, actorUserId, ["CustomerService"], canSupervise, rowVersion, DateTime.UtcNow,
                "corr", "0123456789abcdef0123456789abcdef", null, Reason),
            CancellationToken.None);
    }

    private static SupportTicketAssignCommand NewAssignCommand(Guid ticketId, Guid targetPublicId, string actorUserId, byte[] rowVersion) => new(
        ticketId, actorUserId, ["CustomerServiceSupervisor"], true, rowVersion, DateTime.UtcNow,
        "corr", "0123456789abcdef0123456789abcdef", null, targetPublicId, Reason);

    private async Task<TicketFixture> SeedOpenTicketAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var now = DateTime.UtcNow;
        var member = ApplicationUser.CreateMember(Guid.NewGuid(), $"supervise-member-{Guid.NewGuid():N}@example.test", now);
        db.Users.Add(member);
        await db.SaveChangesAsync();
        var ticket = new SupportTicket(
            Guid.NewGuid(), $"SUP-{Guid.NewGuid():N}"[..24], member.Id, orderId: null,
            SupportTicketCategory.Other, "SQL supervise integration test", CasePriority.Normal,
            now.AddHours(8), now.AddDays(3), now);
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();
        return new TicketFixture(ticket.PublicId, ticket.RowVersion.ToArray());
    }

    private Task<(TicketFixture Ticket, AdminFixture Assignee)> SeedAssignedTicketAsync(bool firstHumanResponse = false) =>
        SeedAssignedTicketAsyncCore(firstHumanResponse);

    private async Task<(TicketFixture Ticket, AdminFixture Assignee)> SeedAssignedTicketAsyncCore(bool firstHumanResponse)
    {
        var ticket = await SeedOpenTicketAsync();
        var admin = await SeedAdminAsync(active: true, role: AuditRoleNames.CustomerService);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var row = await db.SupportTickets.SingleAsync(t => t.PublicId == ticket.PublicId);
        var now = DateTime.UtcNow;
        row.AssignTo(admin.UserId, now);
        if (firstHumanResponse)
        {
            row.RecordFirstHumanResponse(now.AddSeconds(1));
        }
        await db.SaveChangesAsync();
        return (new TicketFixture(row.PublicId, row.RowVersion.ToArray()), admin);
    }

    private async Task<TicketFixture> SeedResolvedTicketAsync()
    {
        var ticket = await SeedOpenTicketAsync();
        var resolver = await SeedAdminAsync(active: true, role: null);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var row = await db.SupportTickets.SingleAsync(t => t.PublicId == ticket.PublicId);
        var now = DateTime.UtcNow;
        row.AssignTo(resolver.UserId, now);
        row.Transition(SupportTicketStatus.InProgress, now.AddMinutes(1));
        row.Transition(SupportTicketStatus.Resolved, now.AddMinutes(2));
        await db.SaveChangesAsync();
        return new TicketFixture(row.PublicId, row.RowVersion.ToArray());
    }

    private async Task<TicketFixture> SeedClosedTicketAsync()
    {
        var ticket = await SeedResolvedTicketAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var row = await db.SupportTickets.SingleAsync(t => t.PublicId == ticket.PublicId);
        row.Transition(SupportTicketStatus.Closed, DateTime.UtcNow.AddMinutes(3));
        await db.SaveChangesAsync();
        return new TicketFixture(row.PublicId, row.RowVersion.ToArray());
    }

    private async Task<AdminFixture> SeedAdminAsync(bool active, string? role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var now = DateTime.UtcNow;
        var user = ApplicationUser.CreateAdmin(Guid.NewGuid(), $"supervise-admin-{Guid.NewGuid():N}@example.test", now);
        user.ConfirmEmail(now.AddMilliseconds(1));
        db.Users.Add(user);
        var profilePublicId = Guid.NewGuid();
        var profile = new AdminProfile(user.Id, profilePublicId, $"EMP-{Guid.NewGuid():N}", "Supervise Agent", now);
        if (!active)
        {
            profile.SetActive(false, now.AddMilliseconds(2));
        }
        db.AdminProfiles.Add(profile);
        await db.SaveChangesAsync();
        if (role is not null)
        {
            var normalizedName = role.ToUpperInvariant();
            var roleId = await db.Roles.AsNoTracking()
                .Where(r => r.NormalizedName == normalizedName)
                .Select(r => r.Id)
                .SingleOrDefaultAsync();
            if (roleId is null)
            {
                var identityRole = new IdentityRole(role) { NormalizedName = normalizedName };
                db.Roles.Add(identityRole);
                await db.SaveChangesAsync();
                roleId = identityRole.Id;
            }
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roleId! });
            await db.SaveChangesAsync();
        }
        return new AdminFixture(user.Id, profilePublicId);
    }

    private async Task AssertTicketUnassignedWithNoHistoryOrAuditAsync(Guid ticketPublicId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var row = await db.SupportTickets.AsNoTracking().SingleAsync(t => t.PublicId == ticketPublicId);
        Assert.Null(row.AssigneeAdminUserId);
        Assert.Equal(SupportTicketStatus.Open, row.Status);
        Assert.Empty(await db.SupportAssignmentHistories.Where(h => h.SupportTicketId == row.Id).ToListAsync());
        Assert.False(await db.AuditLogs.AnyAsync(a => a.ResourcePublicId == ticketPublicId));
    }

    private async Task AssertStatusUnchangedAsync(Guid ticketPublicId, SupportTicketStatus expected)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var row = await db.SupportTickets.AsNoTracking().SingleAsync(t => t.PublicId == ticketPublicId);
        Assert.Equal(expected, row.Status);
    }

    private sealed record TicketFixture(Guid PublicId, byte[] RowVersion);
    private sealed record AdminFixture(string UserId, Guid PublicId);
}
