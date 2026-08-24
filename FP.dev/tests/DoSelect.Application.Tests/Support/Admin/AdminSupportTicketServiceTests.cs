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

    private sealed class StubAdminSupportTicketStore : IAdminSupportTicketStore
    {
        public SupportTicketClaimResult Result { get; init; } = SupportTicketClaimResult.NotFound;
        public string? AdminUserId { get; private set; }
        public byte[]? ExpectedRowVersion { get; private set; }
        public DateTime OccurredAtUtc { get; private set; }

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
    }
}
