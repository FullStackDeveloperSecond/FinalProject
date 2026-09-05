using DoSelect.Application.Auditing;
using DoSelect.Application.Outbox;
using DoSelect.Application.Support;
using DoSelect.Application.Support.Admin;
using DoSelect.Domain.Members;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Support;

/// <summary>
/// SQL Server coverage for the complete admin-public-reply write: actor scope, member
/// visibility, first-human SLA timestamp, Audit, notifications, and optimistic concurrency.
/// </summary>
public sealed class AdminSupportTicketReplyStoreTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AdminSupportTicketReplyStoreTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task AddPublicReplyAsync_CommitsMemberVisibleReplyFirstResponseAuditAndNotifications()
    {
        var fixture = await SeedAssignedTicketAsync();
        var occurredAtUtc = fixture.CreatedAtUtc.AddMinutes(5);

        SupportTicketMutationResult result;
        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAdminSupportTicketStore>();
            result = await store.AddPublicReplyAsync(
                NewCommand(fixture, fixture.AdminUserId, fixture.RowVersion, occurredAtUtc, "member-visible sentinel"),
                CancellationToken.None);
        }

        Assert.Equal(SupportTicketMutationOutcome.Success, result.Outcome);
        Assert.Equal(SupportTicketStatus.InProgress, result.Ticket!.Status);
        Assert.Equal(occurredAtUtc, result.Ticket.FirstHumanResponseAtUtc);
        var publicReply = Assert.Single(result.Ticket.Messages, message => message.Body == "member-visible sentinel");
        Assert.False(publicReply.IsInternal);
        Assert.Equal(SupportSenderType.Admin, publicReply.SenderType);

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var ticket = await db.SupportTickets.AsNoTracking()
            .SingleAsync(candidate => candidate.PublicId == fixture.TicketPublicId);
        Assert.Equal(SupportTicketStatus.InProgress, ticket.Status);
        Assert.Equal(occurredAtUtc, ticket.FirstHumanResponseAtUtc);
        Assert.Contains(await db.SupportStatusHistories.AsNoTracking()
            .Where(history => history.SupportTicketId == ticket.Id)
            .ToListAsync(), history => history.FromStatus == SupportTicketStatus.Assigned &&
                history.ToStatus == SupportTicketStatus.InProgress);

        var memberMessages = await verifyScope.ServiceProvider.GetRequiredService<ISupportTicketStore>()
            .ListPublicMessagesAsync(ticket.Id, CancellationToken.None);
        Assert.Contains(memberMessages, message => message.Body == "member-visible sentinel" && !message.IsInternal);
        Assert.DoesNotContain(memberMessages, message => message.Body == "internal-only sentinel");

        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(log => log.ResourcePublicId == fixture.TicketPublicId &&
                log.Action == AuditActions.SupportTicketReply);
        Assert.DoesNotContain("member-visible sentinel", audit.ChangedFieldsJson, StringComparison.Ordinal);

        var outbox = await db.OutboxMessages.AsNoTracking()
            .Where(message => message.AggregatePublicId == fixture.TicketPublicId)
            .ToListAsync();
        Assert.Contains(outbox, message => message.Type == OutboxEventTypes.EmailNotificationRequestedV1);
        Assert.Contains(outbox, message => message.Type == OutboxEventTypes.InAppNotificationRequestedV1);
        Assert.All(outbox, message => Assert.DoesNotContain("member-visible sentinel", message.PayloadJson, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddPublicReplyAsync_SecondReplyPreservesFirstHumanResponseTimestamp()
    {
        var fixture = await SeedAssignedTicketAsync();
        var firstAtUtc = fixture.CreatedAtUtc.AddMinutes(5);
        var secondAtUtc = fixture.CreatedAtUtc.AddMinutes(10);

        SupportTicketMutationResult first;
        using (var firstScope = _factory.Services.CreateScope())
        {
            first = await firstScope.ServiceProvider.GetRequiredService<IAdminSupportTicketStore>()
                .AddPublicReplyAsync(
                    NewCommand(fixture, fixture.AdminUserId, fixture.RowVersion, firstAtUtc, "first reply"),
                    CancellationToken.None);
        }
        Assert.Equal(SupportTicketMutationOutcome.Success, first.Outcome);

        using (var secondScope = _factory.Services.CreateScope())
        {
            var second = await secondScope.ServiceProvider.GetRequiredService<IAdminSupportTicketStore>()
                .AddPublicReplyAsync(
                    NewCommand(fixture, fixture.AdminUserId, first.Ticket!.RowVersion, secondAtUtc, "second reply"),
                    CancellationToken.None);
            Assert.Equal(SupportTicketMutationOutcome.Success, second.Outcome);
            Assert.Equal(firstAtUtc, second.Ticket!.FirstHumanResponseAtUtc);
        }

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var ticket = await db.SupportTickets.AsNoTracking()
            .SingleAsync(candidate => candidate.PublicId == fixture.TicketPublicId);
        Assert.Equal(firstAtUtc, ticket.FirstHumanResponseAtUtc);
    }

    [Fact]
    public async Task AddPublicReplyAsync_OtherHandlerOrStaleRowVersionCannotCreateSideEffects()
    {
        var fixture = await SeedAssignedTicketAsync();
        var occurredAtUtc = fixture.CreatedAtUtc.AddMinutes(5);

        using (var otherScope = _factory.Services.CreateScope())
        {
            var otherResult = await otherScope.ServiceProvider.GetRequiredService<IAdminSupportTicketStore>()
                .AddPublicReplyAsync(
                    NewCommand(fixture, fixture.OtherAdminUserId, fixture.RowVersion, occurredAtUtc, "forbidden reply"),
                    CancellationToken.None);
            Assert.Equal(SupportTicketMutationOutcome.NotFound, otherResult.Outcome);
        }

        var stale = fixture.RowVersion.ToArray();
        stale[0] ^= 0xff;
        using (var staleScope = _factory.Services.CreateScope())
        {
            var staleResult = await staleScope.ServiceProvider.GetRequiredService<IAdminSupportTicketStore>()
                .AddPublicReplyAsync(
                    NewCommand(fixture, fixture.AdminUserId, stale, occurredAtUtc, "stale reply"),
                    CancellationToken.None);
            Assert.Equal(SupportTicketMutationOutcome.ConcurrencyConflict, staleResult.Outcome);
        }

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var ticket = await db.SupportTickets.AsNoTracking()
            .SingleAsync(candidate => candidate.PublicId == fixture.TicketPublicId);
        Assert.Null(ticket.FirstHumanResponseAtUtc);
        Assert.DoesNotContain(await db.SupportMessages.AsNoTracking()
            .Where(message => message.SupportTicketId == ticket.Id)
            .ToListAsync(), message => message.Body is "forbidden reply" or "stale reply");
        Assert.DoesNotContain(await db.AuditLogs.AsNoTracking()
            .Where(log => log.ResourcePublicId == fixture.TicketPublicId)
            .ToListAsync(), log => log.Action == AuditActions.SupportTicketReply);
        Assert.Empty(await db.OutboxMessages.AsNoTracking()
            .Where(message => message.AggregatePublicId == fixture.TicketPublicId)
            .ToListAsync());
    }

    private static SupportTicketAddPublicReplyCommand NewCommand(
        Fixture fixture,
        string actorUserId,
        byte[] rowVersion,
        DateTime occurredAtUtc,
        string body) => new(
        fixture.TicketPublicId,
        actorUserId,
        [AuditRoleNames.CustomerService],
        CanSupervise: false,
        rowVersion,
        occurredAtUtc,
        $"corr-{Guid.NewGuid():N}"[..32],
        Guid.NewGuid().ToString("N"),
        RemoteIpAddress: null,
        body);

    private async Task<Fixture> SeedAssignedTicketAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var currentUtc = DateTime.UtcNow.AddMinutes(-15);
        var nowUtc = new DateTime(
            currentUtc.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond,
            DateTimeKind.Utc);
        var member = ApplicationUser.CreateMember(
            Guid.NewGuid(),
            $"reply-member-{Guid.NewGuid():N}@example.test",
            nowUtc);
        var admin = ApplicationUser.CreateAdmin(
            Guid.NewGuid(),
            $"reply-admin-{Guid.NewGuid():N}@example.test",
            nowUtc);
        var otherAdmin = ApplicationUser.CreateAdmin(
            Guid.NewGuid(),
            $"reply-other-admin-{Guid.NewGuid():N}@example.test",
            nowUtc);
        member.ConfirmEmail(nowUtc.AddMilliseconds(1));
        admin.ConfirmEmail(nowUtc.AddMilliseconds(1));
        otherAdmin.ConfirmEmail(nowUtc.AddMilliseconds(1));
        db.Users.AddRange(member, admin, otherAdmin);
        await db.SaveChangesAsync();

        db.MemberProfiles.Add(new MemberProfile(member.Id, Guid.NewGuid(), "Reply Member", null, nowUtc));
        db.AdminProfiles.Add(new AdminProfile(
            admin.Id,
            Guid.NewGuid(),
            $"EMP-{Guid.NewGuid():N}"[..20],
            "Reply Agent",
            nowUtc));
        db.AdminProfiles.Add(new AdminProfile(
            otherAdmin.Id,
            Guid.NewGuid(),
            $"EMP-{Guid.NewGuid():N}"[..20],
            "Other Reply Agent",
            nowUtc));
        var ticket = new SupportTicket(
            Guid.NewGuid(),
            $"RPL-{Guid.NewGuid():N}"[..24],
            member.Id,
            orderId: null,
            SupportTicketCategory.Other,
            "Reply integration",
            CasePriority.Normal,
            nowUtc.AddHours(8),
            nowUtc.AddDays(3),
            nowUtc);
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();
        ticket.Assign(admin.Id, nowUtc.AddMinutes(1));
        db.SupportMessages.AddRange(
            new SupportMessage(
                Guid.NewGuid(), ticket.Id, SupportSenderType.Member, member.Id,
                "member question", false, false, null, "zh-TW", nowUtc.AddMinutes(2)),
            new SupportMessage(
                Guid.NewGuid(), ticket.Id, SupportSenderType.Admin, admin.Id,
                "internal-only sentinel", true, false, null, "zh-TW", nowUtc.AddMinutes(3)));
        await db.SaveChangesAsync();

        return new Fixture(ticket.PublicId, admin.Id, otherAdmin.Id, ticket.RowVersion.ToArray(), nowUtc);
    }

    private sealed record Fixture(
        Guid TicketPublicId,
        string AdminUserId,
        string OtherAdminUserId,
        byte[] RowVersion,
        DateTime CreatedAtUtc);
}
