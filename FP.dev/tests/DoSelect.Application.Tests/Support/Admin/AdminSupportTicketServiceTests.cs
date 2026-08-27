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
        Assert.Empty(result.AvailableActions);
        Assert.Collection(result.Messages,
            message => Assert.False(message.IsInternal),
            message => Assert.True(message.IsInternal));
    }

    [Theory]
    [InlineData(SupportTicketStatus.Open, false, true)]
    [InlineData(SupportTicketStatus.Open, true, false)]
    [InlineData(SupportTicketStatus.Assigned, false, false)]
    [InlineData(SupportTicketStatus.InProgress, false, false)]
    [InlineData(SupportTicketStatus.Resolved, false, false)]
    [InlineData(SupportTicketStatus.Closed, false, false)]
    public async Task GetDetailAsync_ExposesClaimOnlyWhenOpenAndUnassigned(
        SupportTicketStatus status,
        bool assigned,
        bool expectClaim)
    {
        var store = new StubAdminSupportTicketStore
        {
            Detail = NewDetail(status, assigned: assigned),
        };

        var result = await new AdminSupportTicketService(store, new FixedTimeProvider(Now))
            .GetDetailAsync("admin-a", false, store.Detail.PublicId, CancellationToken.None);

        Assert.Equal(expectClaim, result.AvailableActions.Contains("claim"));
        Assert.Equal(expectClaim ? 1 : 0, result.AvailableActions.Count);
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
        bool assigned = true) => new(
        Guid.NewGuid(), "CS-DETAIL", SupportTicketCategory.Other, "Detail", status, CasePriority.High,
        Guid.NewGuid(), assigned ? Guid.NewGuid() : null, assigned ? "Visible Agent" : null, Now.UtcDateTime.AddDays(-1), Now.UtcDateTime.AddMinutes(-2),
        firstResponseDueAtUtc ?? Now.UtcDateTime.AddHours(1), resolutionDueAtUtc ?? Now.UtcDateTime.AddHours(8),
        firstHumanResponseAtUtc, null, null, 2, new byte[8], messages ?? [], []);

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
    }
}
