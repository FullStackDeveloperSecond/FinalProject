using DoSelect.Application.Common;
using DoSelect.Application.Support;
using DoSelect.Domain.Members;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Persistence.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Support;

public sealed class SupportAttachmentUploadStoreSqlServerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public SupportAttachmentUploadStoreSqlServerTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task SerializableInsert_RechecksCurrentOwnerInsideTransaction()
    {
        var fixture = await SeedAsync(activeCount: 0);
        await ChangeOwnerAsync(fixture.TicketId, fixture.OtherMemberId);

        using var scope = _factory.Services.CreateScope();
        var store = new SupportAttachmentUploadStore(scope.ServiceProvider.GetRequiredService<DoSelectDbContext>());
        var error = await Assert.ThrowsAsync<DomainProblemException>(
            () => store.InsertCleanAttachmentAsync(NewAttachment(fixture, fixture.OwnerId), fixture.OwnerId, default));

        Assert.Equal((404, DomainErrorCodes.ResourceNotFound), (error.StatusCode, error.Code));
        Assert.Equal(0, await CountActiveAsync(fixture.TicketId));
    }

    [Theory]
    [InlineData(true, 409, DomainErrorCodes.SupportTicketStateConflict)]
    [InlineData(false, 400, DomainErrorCodes.FileCountExceeded)]
    public async Task SerializableInsert_RechecksTerminalStatusAndActiveCount(
        bool makeTerminal, int expectedStatus, string expectedCode)
    {
        var fixture = await SeedAsync(activeCount: makeTerminal ? 0 : 3);
        if (makeTerminal)
        {
            await CancelTicketAsync(fixture.TicketId);
        }

        using var scope = _factory.Services.CreateScope();
        var store = new SupportAttachmentUploadStore(scope.ServiceProvider.GetRequiredService<DoSelectDbContext>());
        var error = await Assert.ThrowsAsync<DomainProblemException>(
            () => store.InsertCleanAttachmentAsync(NewAttachment(fixture, fixture.OwnerId), fixture.OwnerId, default));

        Assert.Equal((expectedStatus, expectedCode), (error.StatusCode, error.Code));
        Assert.Equal(makeTerminal ? 0 : 3, await CountActiveAsync(fixture.TicketId));
    }

    [Fact]
    public async Task ConcurrentThirdAndFourthInsert_NeverRetainMoreThanThreeActiveRows()
    {
        var fixture = await SeedAsync(activeCount: 2);
        using var gate = new Barrier(2);

        async Task<Exception?> AttemptAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var store = new SupportAttachmentUploadStore(scope.ServiceProvider.GetRequiredService<DoSelectDbContext>());
            gate.SignalAndWait(TimeSpan.FromSeconds(10));
            try
            {
                await store.InsertCleanAttachmentAsync(NewAttachment(fixture, fixture.OwnerId), fixture.OwnerId, default);
                return null;
            }
            catch (Exception error)
            {
                return error;
            }
        }

        var outcomes = await Task.WhenAll(Task.Run(AttemptAsync), Task.Run(AttemptAsync));

        Assert.Equal(3, await CountActiveAsync(fixture.TicketId));
        Assert.Single(outcomes, error => error is null);
        Assert.Single(outcomes, error => error is not null);
    }

    private async Task<FixtureIds> SeedAsync(int activeCount)
    {
        var run = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var owner = ApplicationUser.CreateMember(Guid.NewGuid(), $"upload-owner-{run}@example.test", now);
        var other = ApplicationUser.CreateMember(Guid.NewGuid(), $"upload-other-{run}@example.test", now);
        db.Users.AddRange(owner, other);
        await db.SaveChangesAsync();
        var ticket = new SupportTicket(Guid.NewGuid(), $"UP-{run[..20]}", owner.Id, null,
            SupportTicketCategory.Other, $"upload-{run}", CasePriority.Normal,
            now.AddHours(1), now.AddHours(8), now);
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();
        for (var i = 0; i < activeCount; i++)
        {
            db.SupportAttachments.Add(NewAttachment(new(ticket.Id, ticket.PublicId, owner.Id, other.Id, run), owner.Id));
        }
        await db.SaveChangesAsync();
        return new(ticket.Id, ticket.PublicId, owner.Id, other.Id, run);
    }

    private async Task ChangeOwnerAsync(long ticketId, string memberId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SupportTickets SET MemberUserId = {memberId} WHERE Id = {ticketId}");
    }

    private async Task CancelTicketAsync(long ticketId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var ticket = await db.SupportTickets.SingleAsync(value => value.Id == ticketId);
        ticket.Transition(SupportTicketStatus.Cancelled, DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    private async Task<int> CountActiveAsync(long ticketId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        return await db.SupportAttachments.CountAsync(value => value.SupportTicketId == ticketId && value.DeletedAtUtc == null);
    }

    private static SupportAttachment NewAttachment(FixtureIds fixture, string uploader)
    {
        var now = DateTime.UtcNow;
        var attachment = new SupportAttachment(Guid.NewGuid(), fixture.TicketId, null, uploader,
            $"{Guid.NewGuid():N}.png", Guid.NewGuid().ToString("N"), ".png", "image/png", 1, new byte[32], now);
        attachment.RecordScan(PrivateAttachmentScanStatus.Clean, now);
        return attachment;
    }

    private sealed record FixtureIds(long TicketId, Guid TicketPublicId, string OwnerId, string OtherMemberId, string RunId);
}
