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
    public void Transition_ResolvedToInProgressEdge_StillExistsInTheStateMachineUnchanged()
    {
        // DES-23 must not expand the state machine: Resolved -> InProgress ("reopen") is proven
        // here to already be a legal Transition edge — Reopen(...) below builds on this exact
        // edge rather than adding a new one. The dedicated Reopen(...) method (not this raw
        // Transition call) is what the store actually uses for the reopen action, since only
        // Reopen enforces the 3-day window and recomputes ResolutionDueAtUtc.
        var ticket = NewTicket();
        ticket.AssignTo("admin-1", CreatedAtUtc.AddMinutes(1));
        ticket.Transition(SupportTicketStatus.InProgress, CreatedAtUtc.AddMinutes(2));
        ticket.Transition(SupportTicketStatus.Resolved, CreatedAtUtc.AddHours(1));

        ticket.Transition(SupportTicketStatus.InProgress, CreatedAtUtc.AddHours(2));

        Assert.Equal(SupportTicketStatus.InProgress, ticket.Status);
        Assert.Equal(1, ticket.ReopenCount);
    }

    [Theory]
    [InlineData(CasePriority.Low, 5 * 24)]
    [InlineData(CasePriority.Normal, 3 * 24)]
    [InlineData(CasePriority.High, 24)]
    [InlineData(CasePriority.Urgent, 8)]
    public void Reopen_WithinThreeDaysOfResolution_RecomputesResolutionDueDateFromCurrentPriority(
        CasePriority priority, int resolutionTargetHours)
    {
        var ticket = ResolvedTicket(priority, out var resolvedAtUtc);
        var reopenedAtUtc = resolvedAtUtc.AddDays(2);
        var resolutionTarget = TimeSpan.FromHours(resolutionTargetHours);

        ticket.Reopen(reopenedAtUtc, resolutionTarget);

        Assert.Equal(SupportTicketStatus.InProgress, ticket.Status);
        Assert.Equal(reopenedAtUtc.Add(resolutionTarget), ticket.ResolutionDueAtUtc);
    }

    [Fact]
    public void Reopen_PreservesFirstHumanResponseAtUtcAndIncrementsReopenCount()
    {
        var ticket = ResolvedTicket(CasePriority.Normal, out var resolvedAtUtc);
        var firstHumanResponseBefore = ticket.FirstHumanResponseAtUtc;
        Assert.NotNull(firstHumanResponseBefore);

        ticket.Reopen(resolvedAtUtc.AddHours(1), TimeSpan.FromDays(3));

        Assert.Equal(firstHumanResponseBefore, ticket.FirstHumanResponseAtUtc);
        Assert.Equal(1, ticket.ReopenCount);

        // A second resolve-then-reopen cycle should increment again, proving this isn't a
        // one-time flag but the same ReopenCount++ Transition already performs.
        ticket.Transition(SupportTicketStatus.Resolved, resolvedAtUtc.AddHours(2));
        ticket.Reopen(resolvedAtUtc.AddHours(3), TimeSpan.FromDays(3));
        Assert.Equal(2, ticket.ReopenCount);
    }

    [Fact]
    public void Reopen_ExactlyAtThreeDayBoundary_Succeeds()
    {
        var ticket = ResolvedTicket(CasePriority.Normal, out var resolvedAtUtc);

        ticket.Reopen(resolvedAtUtc.AddDays(3), TimeSpan.FromDays(3));

        Assert.Equal(SupportTicketStatus.InProgress, ticket.Status);
    }

    [Fact]
    public void Reopen_AfterThreeDayWindow_ThrowsWithoutChangingStatusOrDueDate()
    {
        var ticket = ResolvedTicket(CasePriority.Normal, out var resolvedAtUtc);
        var dueDateBefore = ticket.ResolutionDueAtUtc;

        Assert.Throws<InvalidOperationException>(
            () => ticket.Reopen(resolvedAtUtc.AddDays(3).AddSeconds(1), TimeSpan.FromDays(3)));

        Assert.Equal(SupportTicketStatus.Resolved, ticket.Status);
        Assert.Equal(dueDateBefore, ticket.ResolutionDueAtUtc);
        Assert.Equal(0, ticket.ReopenCount);
    }

    [Theory]
    [InlineData(SupportTicketStatus.Closed)]
    [InlineData(SupportTicketStatus.Cancelled)]
    public void Reopen_WhenClosedOrCancelled_ThrowsWithoutChangingStatus(SupportTicketStatus terminalStatus)
    {
        var ticket = NewTicket();
        MoveToTerminal(ticket, terminalStatus);

        Assert.Throws<InvalidOperationException>(() => ticket.Reopen(CreatedAtUtc.AddHours(3), TimeSpan.FromDays(3)));

        Assert.Equal(terminalStatus, ticket.Status);
    }

    /// <summary>A Resolved ticket with a recorded first human response, for Reopen tests.</summary>
    private static SupportTicket ResolvedTicket(CasePriority priority, out DateTime resolvedAtUtc)
    {
        var ticket = NewTicket();
        ticket.ChangePriority(priority, CreatedAtUtc.AddMinutes(1));
        ticket.AssignTo("admin-1", CreatedAtUtc.AddMinutes(2));
        ticket.Transition(SupportTicketStatus.InProgress, CreatedAtUtc.AddMinutes(3));
        ticket.RecordFirstHumanResponse(CreatedAtUtc.AddMinutes(4));
        resolvedAtUtc = CreatedAtUtc.AddHours(1);
        ticket.Transition(SupportTicketStatus.Resolved, resolvedAtUtc);
        return ticket;
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
