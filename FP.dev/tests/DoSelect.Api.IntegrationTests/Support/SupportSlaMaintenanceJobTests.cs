using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Application.Notifications;
using DoSelect.Application.Outbox;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Outbox;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Persistence.Support.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DoSelect.Api.IntegrationTests.Support;

[Trait("Category", "RequiresSqlServer")]
public sealed class SupportSlaMaintenanceJobTests : IClassFixture<M14BReadModelSqlServerFixture>
{
    private static readonly DateTime Now = new(2026, 9, 5, 6, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
    private readonly WebApplicationFactory<Program> _factory;

    public SupportSlaMaintenanceJobTests(M14BReadModelSqlServerFixture fixture) =>
        _factory = fixture.Factory;

    [Fact]
    public async Task RunAsync_AtEightyPercent_NotifiesAssigneeOnce()
    {
        var marker = Guid.NewGuid().ToString("N")[..12];
        var member = await SeedMemberAsync(marker);
        var assignee = await SeedAdminAsync(marker + "A", AuditRoleNames.CustomerService);
        var created = Now.AddHours(-7);
        var ticket = NewTicket(marker, member.Id, created, created.AddHours(8), created.AddDays(3));
        ticket.Assign(assignee.UserId, created.AddMinutes(5));
        await SaveTicketAsync(ticket);

        await RunJobAsync();
        await RunJobAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var events = await db.SupportSlaEvents.AsNoTracking()
            .Where(item => item.SupportTicketId == ticket.Id)
            .ToListAsync();
        var warning = Assert.Single(events);
        Assert.Equal(SupportSlaEventType.Warning80, warning.EventType);
        Assert.Equal(SupportSlaTargetType.FirstResponse, warning.TargetType);

        var outbox = await db.OutboxMessages.AsNoTracking()
            .Where(item => item.AggregatePublicId == ticket.PublicId)
            .ToListAsync();
        Assert.Equal(2, outbox.Count);
        var email = Assert.Single(outbox, item => item.Type == OutboxEventTypes.EmailNotificationRequestedV1);
        var emailPayload = JsonSerializer.Deserialize<EmailNotificationRequestedV1>(
            email.PayloadJson,
            JsonOptions);
        var emailContent = await scope.ServiceProvider
            .GetRequiredService<IEmailNotificationContentResolver>()
            .ResolveAsync(emailPayload!);
        Assert.Equal(assignee.UserId, emailContent?.RecipientUserId);
        var inApp = Assert.Single(outbox, item => item.Type == OutboxEventTypes.InAppNotificationRequestedV1);
        var payload = JsonSerializer.Deserialize<InAppNotificationRequestedV1>(
            inApp.PayloadJson,
            JsonOptions);
        Assert.Equal(assignee.PublicId, payload?.MemberPublicId);
        Assert.Equal(ticket.PublicId, payload?.ResourcePublicId);
    }

    [Fact]
    public async Task RunAsync_WhenOverdue_NotifiesAssigneeAndActiveSupervisorsOnce()
    {
        var marker = Guid.NewGuid().ToString("N")[..12];
        var member = await SeedMemberAsync(marker);
        var assignee = await SeedAdminAsync(marker + "A", AuditRoleNames.CustomerService);
        var supervisor = await SeedAdminAsync(marker + "S", AuditRoleNames.CustomerServiceSupervisor);
        var inactiveSupervisor = await SeedAdminAsync(
            marker + "I",
            AuditRoleNames.CustomerServiceSupervisor,
            active: false);
        var created = Now.AddHours(-10);
        var ticket = NewTicket(marker, member.Id, created, created.AddHours(8), created.AddDays(3));
        ticket.Assign(assignee.UserId, created.AddMinutes(5));
        await SaveTicketAsync(ticket);

        await RunJobAsync();
        await RunJobAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var events = await db.SupportSlaEvents.AsNoTracking()
            .Where(item => item.SupportTicketId == ticket.Id)
            .ToListAsync();
        var overdue = Assert.Single(events);
        Assert.Equal(SupportSlaEventType.Overdue100, overdue.EventType);
        Assert.Equal(SupportSlaTargetType.FirstResponse, overdue.TargetType);

        var inAppPayloads = (await db.OutboxMessages.AsNoTracking()
                .Where(item => item.AggregatePublicId == ticket.PublicId &&
                    item.Type == OutboxEventTypes.InAppNotificationRequestedV1)
                .Select(item => item.PayloadJson)
                .ToListAsync())
            .Select(json => JsonSerializer.Deserialize<InAppNotificationRequestedV1>(
                json,
                JsonOptions)!)
            .ToArray();
        Assert.Equal(2, inAppPayloads.Length);
        Assert.Contains(inAppPayloads, payload => payload.MemberPublicId == assignee.PublicId);
        Assert.Contains(inAppPayloads, payload => payload.MemberPublicId == supervisor.PublicId);
        Assert.DoesNotContain(inAppPayloads, payload => payload.MemberPublicId == inactiveSupervisor.PublicId);

        var totalOutbox = await db.OutboxMessages.CountAsync(
            item => item.AggregatePublicId == ticket.PublicId);
        Assert.Equal(4, totalOutbox);
    }

    [Fact]
    public async Task RunAsync_AfterResolvedForThreeDays_ClosesWithHistorySlaEventAndSystemAuditOnce()
    {
        var marker = Guid.NewGuid().ToString("N")[..12];
        var member = await SeedMemberAsync(marker);
        var assignee = await SeedAdminAsync(marker + "A", AuditRoleNames.CustomerService);
        var created = Now.AddDays(-5);
        var ticket = NewTicket(marker, member.Id, created, created.AddHours(8), created.AddDays(3));
        ticket.Assign(assignee.UserId, created.AddMinutes(5));
        ticket.Transition(SupportTicketStatus.InProgress, created.AddMinutes(10));
        ticket.RecordFirstHumanResponse(created.AddMinutes(20));
        ticket.Transition(SupportTicketStatus.Resolved, Now.AddDays(-3).AddMinutes(-1));
        await SaveTicketAsync(ticket);

        await RunJobAsync();
        await RunJobAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var reloaded = await db.SupportTickets.AsNoTracking()
            .SingleAsync(item => item.PublicId == ticket.PublicId);
        Assert.Equal(SupportTicketStatus.Closed, reloaded.Status);
        Assert.Equal(Now, reloaded.ClosedAtUtc);

        var history = Assert.Single(await db.SupportStatusHistories.AsNoTracking()
            .Where(item => item.SupportTicketId == ticket.Id &&
                item.ToStatus == SupportTicketStatus.Closed)
            .ToListAsync());
        Assert.Equal("sla-auto-close", history.ReasonCode);
        Assert.Null(history.ActorUserId);

        var closedEvent = Assert.Single(await db.SupportSlaEvents.AsNoTracking()
            .Where(item => item.SupportTicketId == ticket.Id &&
                item.EventType == SupportSlaEventType.Closed)
            .ToListAsync());
        Assert.Equal(SupportSlaTargetType.Resolution, closedEvent.TargetType);

        var audit = Assert.Single(await db.AuditLogs.AsNoTracking()
            .Where(item => item.ResourcePublicId == ticket.PublicId &&
                item.Action == AuditActions.SupportTicketChangeStatus)
            .ToListAsync());
        Assert.Equal(AuditActorType.System, audit.ActorType);
    }

    [Fact]
    public async Task RunAsync_WhenProcessedTicketsFillFirstPage_ContinuesToLaterOverdueTicket()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var member = await SeedMemberAsync(marker);
        var blockerCreated = Now.AddDays(-301);
        var blockers = Enumerable.Range(0, SupportSlaMaintenanceJob.BatchSize)
            .Select(index => NewTicket(
                $"{marker}-B{index:D3}",
                member.Id,
                blockerCreated,
                blockerCreated.AddHours(8),
                blockerCreated.AddDays(3)))
            .ToArray();
        var targetCreated = Now.AddDays(-201);
        var target = NewTicket(
            $"{marker}-TARGET",
            member.Id,
            targetCreated,
            targetCreated.AddHours(8),
            targetCreated.AddDays(3));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
            db.SupportTickets.AddRange(blockers);
            db.SupportTickets.Add(target);
            await db.SaveChangesAsync();
            db.SupportSlaEvents.AddRange(blockers.Select(ticket => new SupportSlaEvent(
                ticket.Id,
                SupportSlaEventType.Overdue100,
                SupportSlaTargetType.FirstResponse,
                ticket.FirstResponseDueAtUtc,
                checked((int)(ticket.FirstResponseDueAtUtc - ticket.CreatedAtUtc).TotalSeconds),
                blockerCreated.AddDays(1),
                metadataJson: null)));
            await db.SaveChangesAsync();
        }

        await RunJobAsync();

        using var assertionScope = _factory.Services.CreateScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var targetEvent = Assert.Single(await assertionDb.SupportSlaEvents.AsNoTracking()
            .Where(item => item.SupportTicketId == target.Id)
            .ToListAsync());
        Assert.Equal(SupportSlaEventType.Overdue100, targetEvent.EventType);
        Assert.Equal(SupportSlaTargetType.FirstResponse, targetEvent.TargetType);
    }

    [Fact]
    public async Task RunAsync_WhenResolutionCycleReopens_IgnoresPriorCyclePauseAndStagesNewOverdueOnce()
    {
        var marker = Guid.NewGuid().ToString("N")[..12];
        var member = await SeedMemberAsync(marker);
        var assignee = await SeedAdminAsync(marker + "A", AuditRoleNames.CustomerService);
        var created = Now.AddDays(-2);
        var reopenedAt = Now.AddHours(-2);
        var ticket = NewTicket(
            marker,
            member.Id,
            created,
            created.AddHours(8),
            created.AddHours(12));
        ticket.Assign(assignee.UserId, created.AddMinutes(5));
        ticket.Transition(SupportTicketStatus.InProgress, created.AddMinutes(10));
        ticket.RecordFirstHumanResponse(created.AddMinutes(20));
        ticket.Transition(SupportTicketStatus.WaitingForCustomer, created.AddMinutes(30));
        ticket.ResumeFromCustomerWait(created.AddHours(2).AddMinutes(30));
        Assert.Equal((int)TimeSpan.FromHours(2).TotalSeconds, ticket.PausedSeconds);
        ticket.Transition(SupportTicketStatus.Resolved, reopenedAt.AddHours(-1));
        ticket.Reopen(reopenedAt, TimeSpan.FromHours(1));
        Assert.Equal(0, ticket.PausedSeconds);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
            db.SupportTickets.Add(ticket);
            await db.SaveChangesAsync();
            db.SupportSlaEvents.Add(new SupportSlaEvent(
                ticket.Id,
                SupportSlaEventType.Overdue100,
                SupportSlaTargetType.Resolution,
                created.AddHours(12),
                (int)TimeSpan.FromHours(12).TotalSeconds,
                reopenedAt.AddHours(-2),
                metadataJson: null));
            db.SupportStatusHistories.Add(new SupportStatusHistory(
                ticket.Id,
                SupportTicketStatus.Resolved,
                SupportTicketStatus.InProgress,
                reasonCode: "reopened",
                note: null,
                actorUserId: assignee.UserId,
                occurredAtUtc: reopenedAt));
            await db.SaveChangesAsync();
        }

        await RunJobAsync();
        await RunJobAsync();

        using var assertionScope = _factory.Services.CreateScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var overdueEvents = await assertionDb.SupportSlaEvents.AsNoTracking()
            .Where(item => item.SupportTicketId == ticket.Id &&
                item.EventType == SupportSlaEventType.Overdue100 &&
                item.TargetType == SupportSlaTargetType.Resolution)
            .OrderBy(item => item.OccurredAtUtc)
            .ToListAsync();
        Assert.Equal(2, overdueEvents.Count);
        Assert.Equal(Now, overdueEvents[^1].OccurredAtUtc);
    }

    private async Task RunJobAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var job = new SupportSlaMaintenanceJob(
            services.GetRequiredService<DoSelectDbContext>(),
            services.GetRequiredService<IOutboxWriter>(),
            services.GetRequiredService<IAuditWriter>(),
            new FixedTimeProvider(Now),
            NullLogger<SupportSlaMaintenanceJob>.Instance);
        await job.RunAsync(CancellationToken.None);
    }

    private async Task SaveTicketAsync(SupportTicket ticket)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();
    }

    private async Task<ApplicationUser> SeedMemberAsync(string marker)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var user = ApplicationUser.CreateMember(Guid.NewGuid(), $"{marker}@example.test", Now.AddDays(-10));
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<AdminFixture> SeedAdminAsync(string marker, string roleName, bool active = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var role = await db.Roles.SingleOrDefaultAsync(item => item.Name == roleName);
        if (role is null)
        {
            role = new IdentityRole(roleName)
            {
                NormalizedName = roleName.ToUpperInvariant(),
            };
            db.Roles.Add(role);
        }

        var user = ApplicationUser.CreateAdmin(Guid.NewGuid(), $"{marker}-admin@example.test", Now.AddDays(-10));
        user.ConfirmEmail(Now.AddDays(-10).AddMinutes(1));
        var profile = new AdminProfile(
            user.Id,
            user.PublicId,
            $"EMP-{marker}",
            $"SLA {marker}",
            Now.AddDays(-10));
        if (!active)
        {
            profile.SetActive(false, Now.AddDays(-1));
        }

        db.Users.Add(user);
        db.AdminProfiles.Add(profile);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id });
        await db.SaveChangesAsync();
        return new AdminFixture(user.Id, user.PublicId);
    }

    private static SupportTicket NewTicket(
        string marker,
        string memberId,
        DateTime created,
        DateTime firstDue,
        DateTime resolutionDue) =>
        new(
            Guid.NewGuid(),
            $"SLA-{marker}",
            memberId,
            null,
            SupportTicketCategory.Other,
            "SLA maintenance acceptance",
            CasePriority.Normal,
            firstDue,
            resolutionDue,
            created);

    private sealed record AdminFixture(string UserId, Guid PublicId);

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
}
