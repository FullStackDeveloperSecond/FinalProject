using DoSelect.Domain.Support;

namespace DoSelect.Domain.Tests;

public sealed class SupportTicketClaimTests
{
    private static readonly DateTime CreatedAtUtc = new(2026, 8, 19, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Assign_WhenOpenAndUnassigned_ChangesAssigneeStatusAndActivity()
    {
        var ticket = NewTicket();
        var claimedAtUtc = CreatedAtUtc.AddMinutes(5);

        ticket.Assign("admin-1", claimedAtUtc);

        Assert.Equal("admin-1", ticket.AssigneeAdminUserId);
        Assert.Equal(SupportTicketStatus.Assigned, ticket.Status);
        Assert.Equal(claimedAtUtc, ticket.LastActivityAtUtc);
        Assert.Equal(claimedAtUtc, ticket.UpdatedAtUtc);
    }

    [Fact]
    public void Assign_WhenAlreadyAssigned_ThrowsAndPreservesFirstAssignee()
    {
        var ticket = NewTicket();
        ticket.Assign("admin-1", CreatedAtUtc.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => ticket.Assign("admin-2", CreatedAtUtc.AddMinutes(2)));

        Assert.Equal("admin-1", ticket.AssigneeAdminUserId);
        Assert.Equal(SupportTicketStatus.Assigned, ticket.Status);
    }

    [Fact]
    public void Assign_WhenTicketIsNotOpen_ThrowsWithoutAssigning()
    {
        var ticket = NewTicket();
        ticket.Transition(SupportTicketStatus.Cancelled, CreatedAtUtc.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => ticket.Assign("admin-1", CreatedAtUtc.AddMinutes(2)));

        Assert.Null(ticket.AssigneeAdminUserId);
        Assert.Equal(SupportTicketStatus.Cancelled, ticket.Status);
    }

    private static SupportTicket NewTicket() => new(
        Guid.NewGuid(),
        "CS-CLAIM-DOMAIN",
        "member-1",
        orderId: null,
        SupportTicketCategory.Other,
        "Claim domain test",
        CasePriority.Normal,
        CreatedAtUtc.AddHours(8),
        CreatedAtUtc.AddDays(3),
        CreatedAtUtc);
}
