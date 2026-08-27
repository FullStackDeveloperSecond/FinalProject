using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Common;
using DoSelect.Application.Support.Admin;
using DoSelect.Application.Support.Admin.Dtos;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Tests.Support.Admin;

public sealed class AdminSupportTicketServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClaimAsync_WhenStoreClaims_MapsPublicIdsAndForwardsCurrentTime()
    {
        var ticketId = Guid.NewGuid();
        var adminPublicId = Guid.NewGuid();
        var rowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var store = new StubAdminSupportTicketStore
        {
            Result = SupportTicketClaimResult.Claimed(NewClaimedTicket(ticketId, adminPublicId, rowVersion)),
        };
        var service = new AdminSupportTicketService(store, new FixedTimeProvider(Now));

        var result = await service.ClaimAsync(
            "identity-admin-id",
            ticketId,
            new ClaimSupportTicketRequest { RowVersion = rowVersion },
            CancellationToken.None);

        Assert.Equal(ticketId, result.PublicId);
        Assert.Equal(adminPublicId, result.Assignee.PublicId);
        Assert.Equal("Claim Agent", result.Assignee.DisplayName);
        Assert.Equal(SupportTicketStatus.Assigned, result.Status);
        Assert.Equal(rowVersion, result.RowVersion);
        Assert.Equal("identity-admin-id", store.AdminUserId);
        Assert.Equal(Now.UtcDateTime, store.OccurredAtUtc);
        Assert.Same(rowVersion, store.ExpectedRowVersion);
    }

    [Theory]
    [InlineData(SupportTicketClaimOutcome.NotFound, 404, DomainErrorCodes.ResourceNotFound)]
    [InlineData(SupportTicketClaimOutcome.AssignmentConflict, 409, DomainErrorCodes.SupportTicketAssignmentConflict)]
    [InlineData(SupportTicketClaimOutcome.ConcurrencyConflict, 409, DomainErrorCodes.ConcurrencyConflict)]
    [InlineData(SupportTicketClaimOutcome.AdminNotEligible, 403, DomainErrorCodes.AuthorizationForbidden)]
    public async Task ClaimAsync_MapsStoreOutcomeToStableProblemCode(
        SupportTicketClaimOutcome outcome,
        int status,
        string code)
    {
        var store = new StubAdminSupportTicketStore
        {
            Result = new SupportTicketClaimResult(outcome, null),
        };
        var service = new AdminSupportTicketService(store, new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.ClaimAsync(
            "admin",
            Guid.NewGuid(),
            new ClaimSupportTicketRequest { RowVersion = new byte[8] },
            CancellationToken.None));

        Assert.Equal(status, exception.StatusCode);
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public void ClaimRequest_MissingOrWrongLengthRowVersion_FailsDataAnnotationsValidation()
    {
        Assert.False(IsValid(new ClaimSupportTicketRequest()));
        Assert.False(IsValid(new ClaimSupportTicketRequest { RowVersion = new byte[7] }));
        Assert.False(IsValid(new ClaimSupportTicketRequest { RowVersion = new byte[9] }));
        Assert.True(IsValid(new ClaimSupportTicketRequest { RowVersion = new byte[8] }));
    }

    [Fact]
    public void AdminAssigneeSummaryContract_DoesNotExposeEmailAndIncludesDisplayName()
    {
        var properties = typeof(AdminAssigneeSummaryDto).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain("Email", properties);
        Assert.Contains("DisplayName", properties);
        Assert.Contains("PublicId", properties);
    }

    [Fact]
    public async Task GetDetailAsync_MapsInternalMessagesAndUsesFixedServerTime()
    {
        var store = new StubAdminSupportTicketStore
        {
            Detail = NewDetail(firstResponseDueAtUtc: Now.UtcDateTime.AddMinutes(-1), messages:
            [
                new(Guid.NewGuid(), SupportSenderType.Member, false, false, "public sentinel", "zh-TW", Now.UtcDateTime.AddMinutes(-3)),
                new(Guid.NewGuid(), SupportSenderType.Admin, false, true, "internal sentinel", "en", Now.UtcDateTime.AddMinutes(-2)),
            ]),
        };

        var result = await new AdminSupportTicketService(store, new FixedTimeProvider(Now))
            .GetDetailAsync("admin-a", false, store.Detail!.PublicId, CancellationToken.None);

        Assert.True(result.IsOverdue);
        Assert.Equal(store.Detail.PublicId, store.DetailTicketPublicId);
        Assert.Equal("Visible Agent", result.Assignee?.DisplayName);
        // NewDetail()'s defaults are Status=Open + assigned=true + a Handle-only (non-Supervise)
        // caller: claim is excluded (already assigned), assign/transfer are excluded
        // (canSupervise is false), and cancel is included (Open, no human reply yet).
        Assert.Equal(["change-priority", "change-status", "internal-note", "cancel"], result.AvailableActions);
        Assert.Collection(result.Messages,
            message => Assert.False(message.IsInternal),
            message => Assert.True(message.IsInternal));
    }

    [Theory]
    [InlineData(SupportTicketStatus.Open, false, new[] { "claim", "change-priority", "change-status", "internal-note", "cancel" })]
    [InlineData(SupportTicketStatus.Open, true, new[] { "change-priority", "change-status", "internal-note", "cancel" })]
    [InlineData(SupportTicketStatus.Assigned, false, new[] { "change-priority", "change-status", "internal-note", "cancel" })]
    [InlineData(SupportTicketStatus.InProgress, false, new[] { "change-priority", "change-status", "internal-note" })]
    [InlineData(SupportTicketStatus.Resolved, false, new[] { "change-priority", "change-status", "internal-note", "reopen" })]
    [InlineData(SupportTicketStatus.Closed, false, new string[0])]
    public async Task GetDetailAsync_ByHandleOnlyCaller_ExposesStateGatedActionsWithoutAssignOrTransfer(
        SupportTicketStatus status,
        bool assigned,
        string[] expectedActions)
    {
        var store = new StubAdminSupportTicketStore
        {
            Detail = NewDetail(status, assigned: assigned),
        };

        var result = await new AdminSupportTicketService(store, new FixedTimeProvider(Now))
            .GetDetailAsync("admin-a", canSupervise: false, store.Detail.PublicId, CancellationToken.None);

        Assert.Equal(expectedActions, result.AvailableActions);
    }

    [Theory]
    [InlineData(SupportTicketStatus.Open, false, new[] { "claim", "assign", "change-priority", "change-status", "internal-note", "cancel" })]
    [InlineData(SupportTicketStatus.Open, true, new[] { "transfer", "change-priority", "change-status", "internal-note", "cancel" })]
    [InlineData(SupportTicketStatus.InProgress, true, new[] { "transfer", "change-priority", "change-status", "internal-note" })]
    [InlineData(SupportTicketStatus.Closed, true, new string[0])]
    public async Task GetDetailAsync_BySupervisor_AlsoExposesAssignOrTransferWhenEligible(
        SupportTicketStatus status,
        bool assigned,
        string[] expectedActions)
    {
        var store = new StubAdminSupportTicketStore
        {
            Detail = NewDetail(status, assigned: assigned),
        };

        var result = await new AdminSupportTicketService(store, new FixedTimeProvider(Now))
            .GetDetailAsync("supervisor-a", canSupervise: true, store.Detail.PublicId, CancellationToken.None);

        Assert.Equal(expectedActions, result.AvailableActions);
    }

    [Theory]
    [InlineData(false, SupportTicketStatus.Open, true)]
    [InlineData(true, SupportTicketStatus.Open, true)]
    [InlineData(true, SupportTicketStatus.InProgress, true)]
    [InlineData(true, SupportTicketStatus.Resolved, false)]
    [InlineData(true, SupportTicketStatus.Closed, false)]
    [InlineData(true, SupportTicketStatus.Cancelled, false)]
    public async Task GetDetailAsync_ComputesActiveSlaTargetAndTerminalState(bool humanReply, SupportTicketStatus status, bool expected)
    {
        var store = new StubAdminSupportTicketStore
        {
            Detail = NewDetail(status, firstHumanResponseAtUtc: humanReply ? Now.UtcDateTime.AddHours(-1) : null,
                firstResponseDueAtUtc: humanReply ? Now.UtcDateTime.AddHours(-2) : Now.UtcDateTime.AddMinutes(-1),
                resolutionDueAtUtc: humanReply ? Now.UtcDateTime.AddMinutes(-1) : Now.UtcDateTime.AddHours(2)),
        };

        var result = await new AdminSupportTicketService(store, new FixedTimeProvider(Now))
            .GetDetailAsync("admin-a", false, store.Detail.PublicId, CancellationToken.None);

        Assert.Equal(expected, result.IsOverdue);
    }

    [Fact]
    public async Task GetDetailAsync_WhenMissing_ThrowsStandardNotFound()
    {
        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            new AdminSupportTicketService(new StubAdminSupportTicketStore(), new FixedTimeProvider(Now))
                .GetDetailAsync("admin-a", false, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(404, exception.StatusCode);
        Assert.Equal(DomainErrorCodes.ResourceNotFound, exception.Code);
    }

    private static SupportTicketActionContext NewContext(bool canSupervise = false) => new(
        "identity-admin-id",
        ["CustomerService"],
        canSupervise,
        "correlation-1",
        "0123456789abcdef0123456789abcdef",
        null);

    [Fact]
    public async Task AssignAsync_WhenStoreSucceeds_MapsPublicIdsAndForwardsCommandFields()
    {
        var ticketId = Guid.NewGuid();
        var targetPublicId = Guid.NewGuid();
        var adminPublicId = Guid.NewGuid();
        var rowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var store = new StubAdminSupportTicketStore
        {
            AssignResult = SupportTicketAssignResult.Success(NewClaimedTicket(ticketId, adminPublicId, rowVersion)),
        };
        var service = new AdminSupportTicketService(store, new FixedTimeProvider(Now));
        var context = NewContext(canSupervise: true);

        var result = await service.AssignAsync(
            context,
            ticketId,
            new AssignSupportTicketRequest { TargetAdminPublicId = targetPublicId, Reason = "supervisor assign", RowVersion = rowVersion },
            CancellationToken.None);

        Assert.Equal(ticketId, result.PublicId);
        Assert.Equal(adminPublicId, result.Assignee.PublicId);
        var command = Assert.IsType<SupportTicketAssignCommand>(store.LastCommand);
        Assert.Equal(targetPublicId, command.TargetAdminPublicId);
        Assert.Equal("supervisor assign", command.Reason);
        Assert.Equal(context.AdminUserId, command.ActorUserId);
        Assert.True(command.CanSupervise);
        Assert.Equal(context.CorrelationId, command.CorrelationId);
        Assert.Equal(context.TraceId, command.TraceId);
        Assert.Equal(Now.UtcDateTime, command.OccurredAtUtc);
    }

    [Theory]
    [InlineData(SupportTicketAssignOutcome.NotFound, 404, DomainErrorCodes.ResourceNotFound)]
    [InlineData(SupportTicketAssignOutcome.TargetNotEligible, 400, DomainErrorCodes.ValidationFailed)]
    [InlineData(SupportTicketAssignOutcome.AssignmentConflict, 409, DomainErrorCodes.SupportTicketAssignmentConflict)]
    [InlineData(SupportTicketAssignOutcome.ConcurrencyConflict, 409, DomainErrorCodes.ConcurrencyConflict)]
    [InlineData(SupportTicketAssignOutcome.AdminNotEligible, 403, DomainErrorCodes.AuthorizationForbidden)]
    public async Task AssignAsync_MapsStoreOutcomeToStableProblemCode(SupportTicketAssignOutcome outcome, int status, string code)
    {
        var store = new StubAdminSupportTicketStore { AssignResult = new SupportTicketAssignResult(outcome, null) };
        var service = new AdminSupportTicketService(store, new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.AssignAsync(
            NewContext(canSupervise: true),
            Guid.NewGuid(),
            new AssignSupportTicketRequest { TargetAdminPublicId = Guid.NewGuid(), Reason = "r", RowVersion = new byte[8] },
            CancellationToken.None));

        Assert.Equal(status, exception.StatusCode);
        Assert.Equal(code, exception.Code);
    }

    [Theory]
    [InlineData(SupportTicketAssignOutcome.NotFound, 404, DomainErrorCodes.ResourceNotFound)]
    [InlineData(SupportTicketAssignOutcome.TargetNotEligible, 400, DomainErrorCodes.ValidationFailed)]
    [InlineData(SupportTicketAssignOutcome.AssignmentConflict, 409, DomainErrorCodes.SupportTicketAssignmentConflict)]
    [InlineData(SupportTicketAssignOutcome.ConcurrencyConflict, 409, DomainErrorCodes.ConcurrencyConflict)]
    public async Task TransferAsync_MapsStoreOutcomeToStableProblemCode(SupportTicketAssignOutcome outcome, int status, string code)
    {
        var store = new StubAdminSupportTicketStore { TransferResult = new SupportTicketAssignResult(outcome, null) };
        var service = new AdminSupportTicketService(store, new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.TransferAsync(
            NewContext(canSupervise: true),
            Guid.NewGuid(),
            new TransferSupportTicketRequest { TargetAdminPublicId = Guid.NewGuid(), Reason = "r", RowVersion = new byte[8] },
            CancellationToken.None));

        Assert.Equal(status, exception.StatusCode);
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public async Task ChangePriorityAsync_WhenStoreSucceeds_ReturnsDetailAndForwardsPriorityAndReason()
    {
        var store = new StubAdminSupportTicketStore { ChangePriorityResult = SupportTicketMutationResult.Success(NewDetail()) };
        var service = new AdminSupportTicketService(store, new FixedTimeProvider(Now));

        var result = await service.ChangePriorityAsync(
            NewContext(),
            store.ChangePriorityResult.Ticket!.PublicId,
            new ChangeSupportTicketPriorityRequest { Priority = CasePriority.Urgent, Reason = "escalate", RowVersion = new byte[8] },
            CancellationToken.None);

        Assert.NotNull(result);
        var command = Assert.IsType<SupportTicketChangePriorityCommand>(store.LastCommand);
        Assert.Equal(CasePriority.Urgent, command.Priority);
        Assert.Equal("escalate", command.Reason);
    }

    [Theory]
    [InlineData(SupportTicketMutationOutcome.NotFound, 404, DomainErrorCodes.ResourceNotFound)]
    [InlineData(SupportTicketMutationOutcome.StateConflict, 409, DomainErrorCodes.SupportTicketStateConflict)]
    [InlineData(SupportTicketMutationOutcome.ConcurrencyConflict, 409, DomainErrorCodes.ConcurrencyConflict)]
    [InlineData(SupportTicketMutationOutcome.AdminNotEligible, 403, DomainErrorCodes.AuthorizationForbidden)]
    public async Task ChangePriorityAsync_MapsStoreOutcomeToStableProblemCode(SupportTicketMutationOutcome outcome, int status, string code)
    {
        var store = new StubAdminSupportTicketStore { ChangePriorityResult = new SupportTicketMutationResult(outcome, null) };
        var service = new AdminSupportTicketService(store, new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.ChangePriorityAsync(
            NewContext(),
            Guid.NewGuid(),
            new ChangeSupportTicketPriorityRequest { Priority = CasePriority.High, Reason = "r", RowVersion = new byte[8] },
            CancellationToken.None));

        Assert.Equal(status, exception.StatusCode);
        Assert.Equal(code, exception.Code);
    }

    [Theory]
    [InlineData(SupportTicketMutationOutcome.NotFound, 404, DomainErrorCodes.ResourceNotFound)]
    [InlineData(SupportTicketMutationOutcome.StateConflict, 409, DomainErrorCodes.SupportTicketStateConflict)]
    [InlineData(SupportTicketMutationOutcome.ConcurrencyConflict, 409, DomainErrorCodes.ConcurrencyConflict)]
    public async Task ChangeStatusAsync_MapsStoreOutcomeToStableProblemCode(SupportTicketMutationOutcome outcome, int status, string code)
    {
        var store = new StubAdminSupportTicketStore { ChangeStatusResult = new SupportTicketMutationResult(outcome, null) };
        var service = new AdminSupportTicketService(store, new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.ChangeStatusAsync(
            NewContext(),
            Guid.NewGuid(),
            new ChangeSupportTicketStatusRequest { Status = SupportTicketStatus.InProgress, RowVersion = new byte[8] },
            CancellationToken.None));

        Assert.Equal(status, exception.StatusCode);
        Assert.Equal(code, exception.Code);
    }

    [Theory]
    [InlineData(SupportTicketMutationOutcome.NotFound, 404, DomainErrorCodes.ResourceNotFound)]
    [InlineData(SupportTicketMutationOutcome.StateConflict, 409, DomainErrorCodes.SupportTicketCancelNotAllowed)]
    [InlineData(SupportTicketMutationOutcome.ConcurrencyConflict, 409, DomainErrorCodes.ConcurrencyConflict)]
    public async Task CancelAsync_MapsStoreOutcomeToStableProblemCode(SupportTicketMutationOutcome outcome, int status, string code)
    {
        var store = new StubAdminSupportTicketStore { CancelResult = new SupportTicketMutationResult(outcome, null) };
        var service = new AdminSupportTicketService(store, new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.CancelAsync(
            NewContext(),
            Guid.NewGuid(),
            new CancelSupportTicketByAdminRequest { Reason = "customer requested", RowVersion = new byte[8] },
            CancellationToken.None));

        Assert.Equal(status, exception.StatusCode);
        Assert.Equal(code, exception.Code);
    }

    [Theory]
    [InlineData(SupportTicketMutationOutcome.NotFound, 404, DomainErrorCodes.ResourceNotFound)]
    [InlineData(SupportTicketMutationOutcome.StateConflict, 409, DomainErrorCodes.SupportTicketStateConflict)]
    [InlineData(SupportTicketMutationOutcome.ConcurrencyConflict, 409, DomainErrorCodes.ConcurrencyConflict)]
    public async Task ReopenAsync_MapsStoreOutcomeToStableProblemCode(SupportTicketMutationOutcome outcome, int status, string code)
    {
        var store = new StubAdminSupportTicketStore { ReopenResult = new SupportTicketMutationResult(outcome, null) };
        var service = new AdminSupportTicketService(store, new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.ReopenAsync(
            NewContext(),
            Guid.NewGuid(),
            new ReopenSupportTicketRequest { Reason = "customer replied again", RowVersion = new byte[8] },
            CancellationToken.None));

        Assert.Equal(status, exception.StatusCode);
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public async Task AddInternalNoteAsync_WhenStoreSucceeds_ReturnsDetailAndForwardsBody()
    {
        var store = new StubAdminSupportTicketStore { AddInternalNoteResult = SupportTicketMutationResult.Success(NewDetail()) };
        var service = new AdminSupportTicketService(store, new FixedTimeProvider(Now));

        var result = await service.AddInternalNoteAsync(
            NewContext(),
            store.AddInternalNoteResult.Ticket!.PublicId,
            new CreateInternalNoteRequest { Body = "internal note body", RowVersion = new byte[8] },
            CancellationToken.None);

        Assert.NotNull(result);
        var command = Assert.IsType<SupportTicketAddInternalNoteCommand>(store.LastCommand);
        Assert.Equal("internal note body", command.Body);
    }

    [Theory]
    [InlineData(SupportTicketMutationOutcome.NotFound, 404, DomainErrorCodes.ResourceNotFound)]
    [InlineData(SupportTicketMutationOutcome.StateConflict, 409, DomainErrorCodes.SupportTicketStateConflict)]
    [InlineData(SupportTicketMutationOutcome.ConcurrencyConflict, 409, DomainErrorCodes.ConcurrencyConflict)]
    [InlineData(SupportTicketMutationOutcome.AdminNotEligible, 403, DomainErrorCodes.AuthorizationForbidden)]
    public async Task AddInternalNoteAsync_MapsStoreOutcomeToStableProblemCode(SupportTicketMutationOutcome outcome, int status, string code)
    {
        var store = new StubAdminSupportTicketStore { AddInternalNoteResult = new SupportTicketMutationResult(outcome, null) };
        var service = new AdminSupportTicketService(store, new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.AddInternalNoteAsync(
            NewContext(),
            Guid.NewGuid(),
            new CreateInternalNoteRequest { Body = "internal note body", RowVersion = new byte[8] },
            CancellationToken.None));

        Assert.Equal(status, exception.StatusCode);
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public void CreateInternalNoteRequest_MissingOrOversizedBody_FailsDataAnnotationsValidation()
    {
        Assert.False(IsValid(new CreateInternalNoteRequest { Body = "", RowVersion = new byte[8] }));
        Assert.False(IsValid(new CreateInternalNoteRequest { Body = "   ", RowVersion = new byte[8] }));
        Assert.False(IsValid(new CreateInternalNoteRequest { Body = new string('x', 4001), RowVersion = new byte[8] }));
        Assert.True(IsValid(new CreateInternalNoteRequest { Body = new string('x', 4000), RowVersion = new byte[8] }));
        Assert.False(IsValid(new CreateInternalNoteRequest { Body = "ok", RowVersion = new byte[7] }));
    }

    [Fact]
    public void ActionRequests_MissingRequiredFields_FailDataAnnotationsValidation()
    {
        Assert.False(IsValid(new AssignSupportTicketRequest { TargetAdminPublicId = Guid.NewGuid(), Reason = "", RowVersion = new byte[8] }));
        Assert.False(IsValid(new AssignSupportTicketRequest { TargetAdminPublicId = Guid.NewGuid(), Reason = "ok", RowVersion = new byte[7] }));
        Assert.True(IsValid(new AssignSupportTicketRequest { TargetAdminPublicId = Guid.NewGuid(), Reason = "ok", RowVersion = new byte[8] }));

        Assert.False(IsValid(new TransferSupportTicketRequest { TargetAdminPublicId = Guid.NewGuid(), Reason = " ", RowVersion = new byte[8] }));
        Assert.True(IsValid(new TransferSupportTicketRequest { TargetAdminPublicId = Guid.NewGuid(), Reason = "ok", RowVersion = new byte[8] }));

        Assert.False(IsValid(new ChangeSupportTicketPriorityRequest { Priority = CasePriority.High, Reason = "", RowVersion = new byte[8] }));
        Assert.True(IsValid(new ChangeSupportTicketPriorityRequest { Priority = CasePriority.High, Reason = "ok", RowVersion = new byte[8] }));

        Assert.False(IsValid(new CancelSupportTicketByAdminRequest { Reason = "", RowVersion = new byte[8] }));
        Assert.True(IsValid(new CancelSupportTicketByAdminRequest { Reason = "ok", RowVersion = new byte[8] }));

        Assert.False(IsValid(new ReopenSupportTicketRequest { Reason = "", RowVersion = new byte[8] }));
        Assert.True(IsValid(new ReopenSupportTicketRequest { Reason = "ok", RowVersion = new byte[8] }));

        Assert.True(IsValid(new ChangeSupportTicketStatusRequest { Status = SupportTicketStatus.InProgress, Reason = null, RowVersion = new byte[8] }));
    }

    [Fact]
    public void AdminDetailContracts_ExposeOnlyExplicitPublicSafePropertyNames()
    {
        Assert.Equal(["PublicId", "SenderType", "AiGenerated", "IsInternal", "Body", "Language", "SentAtUtc"],
            typeof(AdminSupportMessageDto).GetProperties().Select(p => p.Name));
        var names = typeof(AdminSupportTicketDetailDto).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain(names, name => name is "Id" or "MemberUserId" or "AssigneeAdminUserId" or "Email" or "StorageKey" or "SenderUserId");
    }

    private static bool IsValid(object model)
    {
        var results = new List<ValidationResult>();
        return Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
    }

    private static ClaimedSupportTicket NewClaimedTicket(Guid ticketId, Guid adminPublicId, byte[] rowVersion) => new(
        ticketId,
        "CS-20260819-TEST",
        SupportTicketCategory.Other,
        "Claim test",
        SupportTicketStatus.Assigned,
        CasePriority.Normal,
        OrderPublicId: null,
        adminPublicId,
        "Claim Agent",
        Now.UtcDateTime.AddHours(-1),
        Now.UtcDateTime,
        Now.UtcDateTime.AddHours(7),
        Now.UtcDateTime.AddDays(3),
        FirstHumanResponseAtUtc: null,
        ResolvedAtUtc: null,
        ClosedAtUtc: null,
        ReopenCount: 0,
        rowVersion);

    private static AdminSupportTicketDetail NewDetail(
        SupportTicketStatus status = SupportTicketStatus.Open,
        DateTime? firstHumanResponseAtUtc = null,
        DateTime? firstResponseDueAtUtc = null,
        DateTime? resolutionDueAtUtc = null,
        IReadOnlyList<AdminSupportMessageProjection>? messages = null,
        bool assigned = true,
        DateTime? resolvedAtUtc = null) => new(
        Guid.NewGuid(), "CS-DETAIL", SupportTicketCategory.Other, "Detail", status, CasePriority.High,
        Guid.NewGuid(), assigned ? Guid.NewGuid() : null, assigned ? "Visible Agent" : null, Now.UtcDateTime.AddDays(-1), Now.UtcDateTime.AddMinutes(-2),
        firstResponseDueAtUtc ?? Now.UtcDateTime.AddHours(1), resolutionDueAtUtc ?? Now.UtcDateTime.AddHours(8),
        firstHumanResponseAtUtc,
        resolvedAtUtc ?? (status == SupportTicketStatus.Resolved ? Now.UtcDateTime.AddHours(-1) : null),
        null, 2, new byte[8], messages ?? [], []);

    private sealed class StubAdminSupportTicketStore : IAdminSupportTicketStore
    {
        public SupportTicketClaimResult Result { get; init; } = SupportTicketClaimResult.NotFound;
        public string? AdminUserId { get; private set; }
        public byte[]? ExpectedRowVersion { get; private set; }
        public DateTime OccurredAtUtc { get; private set; }
        public AdminSupportTicketDetail? Detail { get; init; }
        public Guid DetailTicketPublicId { get; private set; }

        public Task<SupportTicketClaimResult> ClaimAsync(
            Guid ticketPublicId,
            string adminUserId,
            byte[] expectedRowVersion,
            DateTime occurredAtUtc,
            CancellationToken cancellationToken)
        {
            AdminUserId = adminUserId;
            ExpectedRowVersion = expectedRowVersion;
            OccurredAtUtc = occurredAtUtc;
            return Task.FromResult(Result);
        }

        public Task<AdminSupportTicketDetail?> GetDetailAsync(
            Guid ticketPublicId,
            string adminUserId,
            bool canSupervise,
            CancellationToken cancellationToken)
        {
            DetailTicketPublicId = ticketPublicId;
            return Task.FromResult(Detail);
        }

        public SupportTicketAssignResult AssignResult { get; init; } = SupportTicketAssignResult.NotFound;
        public SupportTicketAssignResult TransferResult { get; init; } = SupportTicketAssignResult.NotFound;
        public SupportTicketMutationResult ChangePriorityResult { get; init; } = SupportTicketMutationResult.NotFound;
        public SupportTicketMutationResult ChangeStatusResult { get; init; } = SupportTicketMutationResult.NotFound;
        public SupportTicketMutationResult CancelResult { get; init; } = SupportTicketMutationResult.NotFound;
        public SupportTicketMutationResult ReopenResult { get; init; } = SupportTicketMutationResult.NotFound;
        public SupportTicketActionCommand? LastCommand { get; private set; }

        public Task<SupportTicketAssignResult> AssignAsync(SupportTicketAssignCommand command, CancellationToken cancellationToken)
        {
            LastCommand = command;
            return Task.FromResult(AssignResult);
        }

        public Task<SupportTicketAssignResult> TransferAsync(SupportTicketAssignCommand command, CancellationToken cancellationToken)
        {
            LastCommand = command;
            return Task.FromResult(TransferResult);
        }

        public Task<SupportTicketMutationResult> ChangePriorityAsync(SupportTicketChangePriorityCommand command, CancellationToken cancellationToken)
        {
            LastCommand = command;
            return Task.FromResult(ChangePriorityResult);
        }

        public Task<SupportTicketMutationResult> ChangeStatusAsync(SupportTicketChangeStatusCommand command, CancellationToken cancellationToken)
        {
            LastCommand = command;
            return Task.FromResult(ChangeStatusResult);
        }

        public Task<SupportTicketMutationResult> CancelAsync(SupportTicketReasonCommand command, CancellationToken cancellationToken)
        {
            LastCommand = command;
            return Task.FromResult(CancelResult);
        }

        public Task<SupportTicketMutationResult> ReopenAsync(SupportTicketReasonCommand command, CancellationToken cancellationToken)
        {
            LastCommand = command;
            return Task.FromResult(ReopenResult);
        }

        public SupportTicketMutationResult AddInternalNoteResult { get; init; } = SupportTicketMutationResult.NotFound;

        public Task<SupportTicketMutationResult> AddInternalNoteAsync(SupportTicketAddInternalNoteCommand command, CancellationToken cancellationToken)
        {
            LastCommand = command;
            return Task.FromResult(AddInternalNoteResult);
        }
    }
}
