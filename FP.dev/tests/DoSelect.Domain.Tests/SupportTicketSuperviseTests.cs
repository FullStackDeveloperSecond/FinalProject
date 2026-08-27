using DoSelect.Domain.Support;

namespace DoSelect.Domain.Tests;

/// <summary>
/// DES-23 domain coverage for SupportTicket.AssignTo/ChangePriority: the supervisor-driven
/// handoff and priority mutation added alongside the existing self-claim Assign, without
/// expanding the AllowedTransitions state machine.
/// </summary>
public sealed class SupportTicketSuperviseTests
{
    private static readonly DateTime CreatedAtUtc = new(2026, 8, 19, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AssignTo_WhenOpenAndUnassigned_TransitionsToAssigned()
    {
        var ticket = NewTicket();
        var occurredAtUtc = CreatedAtUtc.AddMinutes(5);

        ticket.AssignTo("admin-1", occurredAtUtc);

        Assert.Equal("admin-1", ticket.AssigneeAdminUserId);
        Assert.Equal(SupportTicketStatus.Assigned, ticket.Status);
        Assert.Equal(occurredAtUtc, ticket.LastActivityAtUtc);
    }

    [Fact]
    public void AssignTo_WhenAlreadyAssignedToSomeoneElse_OverwritesAssigneeWithoutChangingStatus()
    {
        var ticket = NewTicket();
        ticket.AssignTo("admin-1", CreatedAtUtc.AddMinutes(1));
        ticket.Transition(SupportTicketStatus.InProgress, CreatedAtUtc.AddMinutes(2));
        var transferredAtUtc = CreatedAtUtc.AddMinutes(3);

        ticket.AssignTo("admin-2", transferredAtUtc);

        Assert.Equal("admin-2", ticket.AssigneeAdminUserId);
        Assert.Equal(SupportTicketStatus.InProgress, ticket.Status);
        Assert.Equal(transferredAtUtc, ticket.LastActivityAtUtc);
    }

    [Theory]
    [InlineData(SupportTicketStatus.Closed)]
    [InlineData(SupportTicketStatus.Cancelled)]
    public void AssignTo_WhenTerminal_ThrowsWithoutChangingAssignee(SupportTicketStatus terminalStatus)
    {
        var ticket = NewTicket();
        MoveToTerminal(ticket, terminalStatus);
        var assigneeBeforeAttempt = ticket.AssigneeAdminUserId;

        Assert.Throws<InvalidOperationException>(() => ticket.AssignTo("admin-2", CreatedAtUtc.AddHours(1)));

        Assert.Equal(assigneeBeforeAttempt, ticket.AssigneeAdminUserId);
    }

    [Fact]
    public void ChangePriority_WhenNotTerminal_UpdatesPriorityAndActivity()
    {
        var ticket = NewTicket();
        var occurredAtUtc = CreatedAtUtc.AddMinutes(10);

        ticket.ChangePriority(CasePriority.Urgent, occurredAtUtc);

        Assert.Equal(CasePriority.Urgent, ticket.Priority);
        Assert.Equal(occurredAtUtc, ticket.LastActivityAtUtc);
        Assert.Equal(occurredAtUtc, ticket.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(SupportTicketStatus.Closed)]
    [InlineData(SupportTicketStatus.Cancelled)]
    public void ChangePriority_WhenTerminal_ThrowsWithoutChangingPriority(SupportTicketStatus terminalStatus)
    {
        var ticket = NewTicket();
        MoveToTerminal(ticket, terminalStatus);

        Assert.Throws<InvalidOperationException>(() => ticket.ChangePriority(CasePriority.Urgent, CreatedAtUtc.AddHours(1)));

        Assert.Equal(CasePriority.Normal, ticket.Priority);
    }

    [Fact]
    public void ChangeStatus_ReopenEdge_StillWorksThroughExistingTransitionUnchanged()
    {
        // DES-23 must not expand the state machine: Resolved -> InProgress ("reopen") is proven
        // here to already be a legal Transition edge with no new domain method required for it.
        var ticket = NewTicket();
        ticket.AssignTo("admin-1", CreatedAtUtc.AddMinutes(1));
        ticket.Transition(SupportTicketStatus.InProgress, CreatedAtUtc.AddMinutes(2));
        ticket.Transition(SupportTicketStatus.Resolved, CreatedAtUtc.AddHours(1));

        ticket.Transition(SupportTicketStatus.InProgress, CreatedAtUtc.AddHours(2));

        Assert.Equal(SupportTicketStatus.InProgress, ticket.Status);
        Assert.Equal(1, ticket.ReopenCount);
    }

    private static void MoveToTerminal(SupportTicket ticket, SupportTicketStatus terminalStatus)
    {
        if (terminalStatus == SupportTicketStatus.Cancelled)
        {
            ticket.Transition(SupportTicketStatus.Cancelled, CreatedAtUtc.AddMinutes(1));
            return;
        }

        ticket.AssignTo("admin-1", CreatedAtUtc.AddMinutes(1));
        ticket.Transition(SupportTicketStatus.InProgress, CreatedAtUtc.AddMinutes(2));
        ticket.Transition(SupportTicketStatus.Resolved, CreatedAtUtc.AddHours(1));
        ticket.Transition(SupportTicketStatus.Closed, CreatedAtUtc.AddHours(2));
    }

    private static SupportTicket NewTicket() => new(
        Guid.NewGuid(),
        "CS-SUPERVISE-DOMAIN",
        "member-1",
        orderId: null,
        SupportTicketCategory.Other,
        "Supervise domain test",
        CasePriority.Normal,
        CreatedAtUtc.AddHours(8),
        CreatedAtUtc.AddDays(3),
        CreatedAtUtc);
}
