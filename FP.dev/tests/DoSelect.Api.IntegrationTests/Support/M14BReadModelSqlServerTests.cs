using DoSelect.Application.Support.Admin;
using DoSelect.Application.Support.Admin.Dtos;
using DoSelect.Domain.Members;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Persistence.Support.Admin;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Support;

/// <summary>
/// SQL Server acceptance coverage for the M-14B read stores. These tests deliberately exercise
/// the real provider and vw_CaseWorkbench so EF translation failures cannot be hidden by an
/// in-memory LINQ provider.
/// </summary>
public sealed class M14BReadModelSqlServerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public M14BReadModelSqlServerTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task SlaQueue_ComputesPreResponseActivePauseCapWaitingInternalAndTerminalRules()
    {
        var now = new DateTime(2026, 8, 20, 4, 0, 0, DateTimeKind.Utc);
        var marker = $"M14S{Guid.NewGuid():N}"[..20];
        var member = await SeedMemberAsync(marker);
        var assignee = await SeedAdminAsync(marker);

        var preResponse = NewTicket(marker + "A", member.Id, now.AddDays(-2), now.AddHours(-6), now.AddDays(1));
        var cappedPause = NewTicket(marker + "B", member.Id, now.AddDays(-10), now.AddDays(-9), now.AddDays(-5));
        cappedPause.Assign(assignee.UserId, now.AddDays(-9).AddHours(1));
        cappedPause.Transition(SupportTicketStatus.InProgress, now.AddDays(-9).AddHours(2));
        cappedPause.RecordFirstHumanResponse(now.AddDays(-9).AddHours(3));
        cappedPause.AddPausedSeconds(71 * 60 * 60, now.AddDays(-9).AddHours(4));
        cappedPause.Transition(SupportTicketStatus.WaitingForCustomer, now.AddHours(-2));

        var waitingInternal = NewTicket(marker + "C", member.Id, now.AddDays(-4), now.AddDays(-3), now.AddDays(-1));
        waitingInternal.Assign(assignee.UserId, now.AddDays(-4).AddHours(1));
        waitingInternal.Transition(SupportTicketStatus.InProgress, now.AddDays(-4).AddHours(2));
        waitingInternal.RecordFirstHumanResponse(now.AddDays(-4).AddHours(3));
        waitingInternal.Transition(SupportTicketStatus.WaitingForInternal, now.AddDays(-2));

        var terminal = NewTicket(marker + "D", member.Id, now.AddDays(-3), now.AddDays(-2), now.AddDays(-1));
        terminal.Assign(assignee.UserId, now.AddDays(-3).AddHours(1));
        terminal.Transition(SupportTicketStatus.InProgress, now.AddDays(-3).AddHours(2));
        terminal.Transition(SupportTicketStatus.Resolved, now.AddDays(-2));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
            db.SupportTickets.AddRange(preResponse, cappedPause, waitingInternal, terminal);
            await db.SaveChangesAsync();
        }

        var page = await QuerySlaAsync(now);
        var pre = Assert.Single(page.Items, item => item.TicketPublicId == preResponse.PublicId);
        var capped = Assert.Single(page.Items, item => item.TicketPublicId == cappedPause.PublicId);
        var internalWait = Assert.Single(page.Items, item => item.TicketPublicId == waitingInternal.PublicId);

        Assert.Equal(preResponse.FirstResponseDueAtUtc, pre.EffectiveDueAtUtc);
        Assert.True(pre.IsOverdue);
        var expectedPreResponseUsage =
            (now - preResponse.CreatedAtUtc).TotalSeconds /
            (preResponse.FirstResponseDueAtUtc - preResponse.CreatedAtUtc).TotalSeconds;
        Assert.Equal(expectedPreResponseUsage, pre.UsageRatio, precision: 8);

        Assert.Equal(cappedPause.ResolutionDueAtUtc.AddHours(72), capped.EffectiveDueAtUtc);
        Assert.Equal(assignee.ProfilePublicId, capped.Assignee?.PublicId);
        Assert.Equal("M14 Agent", capped.Assignee?.DisplayName);
        Assert.True(capped.IsOverdue);

        Assert.Equal(waitingInternal.ResolutionDueAtUtc, internalWait.EffectiveDueAtUtc);
        Assert.True(internalWait.IsOverdue);
        Assert.DoesNotContain(page.Items, item => item.TicketPublicId == terminal.PublicId);
    }

    [Fact]
    public async Task Workbench_AppliesSqlFiltersAndStableDescendingKeysetWithoutDuplicates()
    {
        var now = DateTime.UtcNow;
        var marker = $"M14W{Guid.NewGuid():N}"[..20];
        var member = await SeedMemberAsync(marker);
        var runIdPrefix = Guid.NewGuid().ToString("N")[..20];
        var highId = Guid.ParseExact(runIdPrefix + "ffffffffffff", "N");
        var lowId = Guid.ParseExact(runIdPrefix + "000000000000", "N");
        var olderId = Guid.NewGuid();
        var tiedAt = now.AddMinutes(-10);
        var high = NewTicket(marker + "H", member.Id, now.AddDays(-1), now.AddHours(1), now.AddDays(2), highId);
        var low = NewTicket(marker + "L", member.Id, now.AddDays(-1), now.AddHours(1), now.AddDays(2), lowId);
        var older = NewTicket(marker + "O", member.Id, now.AddDays(-2), now.AddHours(1), now.AddDays(2), olderId);
        high.RecordActivity(tiedAt);
        low.RecordActivity(tiedAt);
        older.RecordActivity(tiedAt.AddMinutes(-1));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
            db.SupportTickets.AddRange(high, low, older);
            await db.SaveChangesAsync();
        }

        var first = await QueryWorkbenchAsync(
            [CaseWorkbenchCaseType.Support, CaseWorkbenchCaseType.Support], marker,
            pageSize: 1, after: null);
        var firstItem = Assert.Single(first.Items);
        Assert.Equal(highId, firstItem.CasePublicId);
        Assert.Equal("Support", firstItem.CaseType);
        Assert.True(first.HasMore);

        var second = await QueryWorkbenchAsync(
            [CaseWorkbenchCaseType.Support], marker, pageSize: 1,
            after: new CaseWorkbenchCursorPosition(firstItem.LastActivityAtUtc, firstItem.CasePublicId));
        var secondItem = Assert.Single(second.Items);
        Assert.Equal(lowId, secondItem.CasePublicId);
        Assert.True(second.HasMore);

        var third = await QueryWorkbenchAsync(
            [CaseWorkbenchCaseType.Support], marker, pageSize: 1,
            after: new CaseWorkbenchCursorPosition(secondItem.LastActivityAtUtc, secondItem.CasePublicId));
        var thirdItem = Assert.Single(third.Items);
        Assert.Equal(olderId, thirdItem.CasePublicId);
        Assert.False(third.HasMore);
        Assert.Equal(3, new[] { firstItem.CasePublicId, secondItem.CasePublicId, thirdItem.CasePublicId }.Distinct().Count());

        var unauthorized = await QueryWorkbenchAsync(
            [CaseWorkbenchCaseType.Return, CaseWorkbenchCaseType.Report], marker,
            pageSize: 100, after: null);
        Assert.Empty(unauthorized.Items);
    }

    private async Task<SupportSlaQueuePage> QuerySlaAsync(DateTime now)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        return await new SupportSlaQueueStore(db).QueryPageAsync(100, null, now, CancellationToken.None);
    }

    private async Task<CaseWorkbenchPage> QueryWorkbenchAsync(
        IReadOnlyCollection<CaseWorkbenchCaseType> scopeTypes,
        string marker,
        int pageSize,
        CaseWorkbenchCursorPosition? after)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        return await new CaseWorkbenchStore(db).QueryPageAsync(
            scopeTypes,
            statuses: [SupportTicketStatus.Open.ToString()],
            priorities: null,
            assigneePublicId: null,
            overdueOnly: null,
            keyword: marker,
            pageSize,
            after,
            CancellationToken.None);
    }

    private async Task<ApplicationUser> SeedMemberAsync(string marker)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var user = ApplicationUser.CreateMember(Guid.NewGuid(), $"{marker}@example.test", DateTime.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<AdminFixture> SeedAdminAsync(string marker)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var now = DateTime.UtcNow;
        var user = ApplicationUser.CreateAdmin(Guid.NewGuid(), $"{marker}-admin@example.test", now);
        user.ConfirmEmail(now.AddMilliseconds(1));
        var profileId = Guid.NewGuid();
        db.Users.Add(user);
        db.AdminProfiles.Add(new AdminProfile(user.Id, profileId, $"EMP-{marker}", "M14 Agent", now));
        await db.SaveChangesAsync();
        return new AdminFixture(user.Id, profileId);
    }

    private static SupportTicket NewTicket(
        string number,
        string memberId,
        DateTime created,
        DateTime firstDue,
        DateTime resolutionDue,
        Guid? publicId = null) =>
        new(publicId ?? Guid.NewGuid(), number, memberId, null, SupportTicketCategory.Other,
            "M14 acceptance", CasePriority.Normal, firstDue, resolutionDue, created);

    private sealed record AdminFixture(string UserId, Guid ProfilePublicId);
}
