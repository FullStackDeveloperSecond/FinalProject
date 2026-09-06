using System.Data.Common;
using DoSelect.Application.Support.Admin;
using DoSelect.Domain.Members;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Persistence.Support.Admin;
using DoSelect.Infrastructure.Outbox;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Support;

public sealed class AdminSupportTicketDetailStoreTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AdminSupportTicketDetailStoreTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task GetDetailAsync_ProjectsMessagesAndAttachmentsUsingThreeQueries()
    {
        var fixture = await SeedAsync(activeAssignee: true);
        var counter = new ReaderCommandCounter();
        await using var db = CreateCountingContext(counter);

        var detail = await new AdminSupportTicketStore(
            db,
            new EfAuditWriter(db, TimeProvider.System),
            new EfOutboxWriter(db, TimeProvider.System))
            .GetDetailAsync(fixture.TicketPublicId, "supervisor", true, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(3, counter.ReaderCommands);
        Assert.Equal(fixture.AssigneePublicId, detail.AssigneeAdminPublicId);
        Assert.Equal("Public Agent", detail.AssigneeAdminDisplayName);
        Assert.Equal([fixture.EarlierPublicId, fixture.LaterInternalLowPublicId, fixture.LaterInternalHighPublicId],
            detail.Messages.Select(message => message.PublicId));
        Assert.Equal([false, true, true], detail.Messages.Select(message => message.IsInternal));
        Assert.DoesNotContain(detail.Messages, message => message.Body.Contains("identity-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetDetailAsync_WithInactiveAssignee_ReturnsNullPublicAssignee()
    {
        var fixture = await SeedAsync(activeAssignee: false);
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAdminSupportTicketStore>();

        var detail = await store.GetDetailAsync(fixture.TicketPublicId, "supervisor", true, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Null(detail.AssigneeAdminPublicId);
        Assert.Null(detail.AssigneeAdminDisplayName);
    }

    [Fact]
    public async Task GetDetailAsync_EnforcesAssigneeScopeForRegularHandlerAndAllowsSupervisor()
    {
        var fixture = await SeedAsync(activeAssignee: true);
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAdminSupportTicketStore>();

        var ownerDetail = await store.GetDetailAsync(
            fixture.TicketPublicId,
            fixture.AssigneeUserId,
            canSupervise: false,
            CancellationToken.None);
        var otherHandlerDetail = await store.GetDetailAsync(
            fixture.TicketPublicId,
            $"other-handler-{Guid.NewGuid():N}",
            canSupervise: false,
            CancellationToken.None);
        var supervisorDetail = await store.GetDetailAsync(
            fixture.TicketPublicId,
            $"supervisor-{Guid.NewGuid():N}",
            canSupervise: true,
            CancellationToken.None);

        Assert.NotNull(ownerDetail);
        Assert.Null(otherHandlerDetail);
        Assert.NotNull(supervisorDetail);
    }
    [Fact]
    public async Task GetDetailAsync_WhenMissing_StopsAfterTicketQuery()
    {
        var counter = new ReaderCommandCounter();
        await using var db = CreateCountingContext(counter);

        var detail = await new AdminSupportTicketStore(
            db,
            new EfAuditWriter(db, TimeProvider.System),
            new EfOutboxWriter(db, TimeProvider.System)).GetDetailAsync(
                Guid.NewGuid(), "supervisor", true, CancellationToken.None);

        Assert.Null(detail);
        Assert.Equal(1, counter.ReaderCommands);
    }

    private DoSelectDbContext CreateCountingContext(DbCommandInterceptor interceptor)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>().Database.GetConnectionString()!;
        return new DoSelectDbContext(new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(interceptor)
            .Options);
    }

    private async Task<Fixture> SeedAsync(bool activeAssignee)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var now = DateTime.UtcNow;
        var member = ApplicationUser.CreateMember(Guid.NewGuid(), $"identity-member-sentinel-{Guid.NewGuid():N}@example.test", now);
        var admin = ApplicationUser.CreateAdmin(Guid.NewGuid(), $"identity-admin-sentinel-{Guid.NewGuid():N}@example.test", now);
        admin.ConfirmEmail(now.AddMilliseconds(1));
        db.Users.AddRange(member, admin);
        await db.SaveChangesAsync();

        var assigneePublicId = Guid.NewGuid();
        var profile = new AdminProfile(admin.Id, assigneePublicId, $"EMP-{Guid.NewGuid():N}", "Public Agent", now);
        if (!activeAssignee) profile.SetActive(false, now.AddMilliseconds(2));
        db.AdminProfiles.Add(profile);

        var ticket = new SupportTicket(Guid.NewGuid(), $"DET-{Guid.NewGuid():N}"[..24], member.Id, null,
            SupportTicketCategory.Other, "SQL detail", CasePriority.High, now.AddHours(1), now.AddHours(8), now);
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();
        ticket.Assign(admin.Id, now.AddSeconds(1));

        var messagePublicIdBytes = Guid.NewGuid().ToByteArray();
        messagePublicIdBytes[^1] = 0x10;
        var earlier = new Guid(messagePublicIdBytes);
        messagePublicIdBytes[^1] = 0x20;
        var laterLow = new Guid(messagePublicIdBytes);
        messagePublicIdBytes[^1] = 0x30;
        var laterHigh = new Guid(messagePublicIdBytes);
        db.SupportMessages.AddRange(
            new SupportMessage(laterHigh, ticket.Id, SupportSenderType.Admin, admin.Id, "internal high", true, false, null, "zh-TW", now.AddMinutes(2)),
            new SupportMessage(earlier, ticket.Id, SupportSenderType.Member, member.Id, "public earlier", false, false, null, "zh-TW", now.AddMinutes(1)),
            new SupportMessage(laterLow, ticket.Id, SupportSenderType.Admin, admin.Id, "internal low", true, false, null, "zh-TW", now.AddMinutes(2)));
        await db.SaveChangesAsync();
        return new Fixture(ticket.PublicId, admin.Id, assigneePublicId, earlier, laterLow, laterHigh);
    }

    private sealed record Fixture(Guid TicketPublicId, string AssigneeUserId, Guid AssigneePublicId, Guid EarlierPublicId,
        Guid LaterInternalLowPublicId, Guid LaterInternalHighPublicId);

    private sealed class ReaderCommandCounter : DbCommandInterceptor
    {
        public int ReaderCommands { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            ReaderCommands++;
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCommands++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
