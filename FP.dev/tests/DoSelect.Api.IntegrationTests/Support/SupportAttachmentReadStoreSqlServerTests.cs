using DoSelect.Application.Support;
using DoSelect.Domain.Members;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Persistence.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Support;

public sealed class SupportAttachmentReadStoreSqlServerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public SupportAttachmentReadStoreSqlServerTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task SqlPredicate_AllowsOwnerAndHandlerButNotMemberBDeletedOrNonClean()
    {
        var run = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        Guid cleanId, deletedId, pendingId, rejectedId;
        string ownerId, memberBId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
            var owner = ApplicationUser.CreateMember(Guid.NewGuid(), $"att-owner-{run}@example.test", now);
            var memberB = ApplicationUser.CreateMember(Guid.NewGuid(), $"att-other-{run}@example.test", now);
            db.Users.AddRange(owner, memberB);
            await db.SaveChangesAsync();
            ownerId = owner.Id;
            memberBId = memberB.Id;
            var ticket = new SupportTicket(Guid.NewGuid(), $"AT-{run[..20]}", owner.Id, null,
                SupportTicketCategory.Other, $"attachment-{run}", CasePriority.Normal,
                now.AddHours(1), now.AddHours(8), now);
            db.SupportTickets.Add(ticket);
            await db.SaveChangesAsync();

            var clean = NewAttachment(ticket.Id, owner.Id, run + "-clean", now);
            clean.RecordScan(PrivateAttachmentScanStatus.Clean, now.AddSeconds(1));
            var deleted = NewAttachment(ticket.Id, owner.Id, run + "-deleted", now);
            deleted.RecordScan(PrivateAttachmentScanStatus.Clean, now.AddSeconds(1));
            deleted.SoftDelete(now.AddSeconds(2));
            var pending = NewAttachment(ticket.Id, owner.Id, run + "-pending", now);
            var rejected = NewAttachment(ticket.Id, owner.Id, run + "-rejected", now);
            rejected.RecordScan(PrivateAttachmentScanStatus.Rejected, now.AddSeconds(1));
            db.SupportAttachments.AddRange(clean, deleted, pending, rejected);
            await db.SaveChangesAsync();
            (cleanId, deletedId, pendingId, rejectedId) = (clean.PublicId, deleted.PublicId, pending.PublicId, rejected.PublicId);
        }

        Assert.NotNull(await FindAsync(cleanId, new(SupportAttachmentActorType.Member, ownerId)));
        Assert.Null(await FindAsync(cleanId, new(SupportAttachmentActorType.Member, memberBId)));
        Assert.NotNull(await FindAsync(cleanId, new(SupportAttachmentActorType.SupportHandler, $"admin-{Guid.NewGuid():N}")));
        Assert.Null(await FindAsync(deletedId, new(SupportAttachmentActorType.Member, ownerId)));
        Assert.Null(await FindAsync(pendingId, new(SupportAttachmentActorType.Member, ownerId)));
        Assert.Null(await FindAsync(rejectedId, new(SupportAttachmentActorType.SupportHandler, $"admin-{Guid.NewGuid():N}")));
        Assert.Null(await FindAsync(Guid.NewGuid(), new(SupportAttachmentActorType.SupportHandler, $"admin-{Guid.NewGuid():N}")));
    }

    private async Task<SupportAttachmentReadRecord?> FindAsync(Guid id, SupportAttachmentActor actor)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        return await new SupportAttachmentReadStore(db).FindReadableAsync(id, actor, CancellationToken.None);
    }

    private static SupportAttachment NewAttachment(long ticketId, string uploader, string unique, DateTime now) =>
        new(Guid.NewGuid(), ticketId, null, uploader, $"{unique}.txt", $"{unique}/payload.txt", ".txt",
            "text/plain", 1, new byte[32], now);
}
