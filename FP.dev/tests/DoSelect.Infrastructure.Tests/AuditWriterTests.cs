using System.Net;
using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Domain.Auditing;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests;

public sealed class AuditWriterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 26, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Add_TracksAWhitelistedMaskedAuditWithoutSavingIt()
    {
        using var context = CreateContext();
        var writer = new EfAuditWriter(context, new FixedTimeProvider(Now));
        var request = Request();

        var audit = writer.Add(request);

        Assert.Equal(EntityState.Added, context.Entry(audit).State);
        Assert.Equal("[\"FinanceManager\",\"SuperAdmin\"]", audit.ActorRolesJson);
        Assert.Equal("203.0.113.0/24", audit.MaskedIpAddress);
        Assert.Equal(Now.UtcDateTime, audit.OccurredAtUtc);
        Assert.Equal(Now.UtcDateTime.AddDays(365), audit.RetentionUntilUtc);
        using var document = JsonDocument.Parse(audit.ChangedFieldsJson);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.DoesNotContain("@", audit.ChangedFieldsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("token", audit.ChangedFieldsJson,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("member@example.com")]
    [InlineData("customer full name")]
    [InlineData("payment-card-4111111111111111")]
    [InlineData("token-reset-value")]
    public void Request_RejectsFreeFormOrSensitiveReason(string reason)
    {
        Assert.Throws<ArgumentException>(() => Request(reason: reason));
    }

    [Fact]
    public void Request_RejectsUnknownActionRoleFieldAndUnsafeChangedValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Request(action: "refund.force"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Request(roles: ["UnknownRole"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Request(
            changes: [AuditFieldChange.Changed("email")]));
        Assert.Throws<ArgumentException>(() => Request(
            changes: [AuditFieldChange.Code("status", "Approved", "admin@example.com")]));
    }

    [Theory]
    [InlineData(AuditActorType.Member)]
    [InlineData(AuditActorType.Guest)]
    public void Actor_RejectsAdminRoleSnapshotForNonAdminActor(AuditActorType actorType)
    {
        Assert.Throws<ArgumentException>(() => AuditActor.Create(
            actorType,
            Guid.NewGuid(),
            [AuditRoleNames.FinanceManager]));
    }

    [Fact]
    public void ActorAndRequestSnapshotsCannotBeMutatedAfterValidation()
    {
        var actor = AuditActor.Create(
            AuditActorType.Admin,
            Guid.NewGuid(),
            [AuditRoleNames.FinanceManager]);
        var request = Request();

        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)actor.Roles)[0] = AuditRoleNames.SuperAdmin);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<AuditFieldChange>)request.Changes)[0] =
                AuditFieldChange.Changed("allocationCount"));
    }

    private static AuditWriteRequest Request(
        string action = AuditActions.RefundExecute,
        IReadOnlyCollection<string>? roles = null,
        IReadOnlyCollection<AuditFieldChange>? changes = null,
        string reason = "refund.approved") =>
        AuditWriteRequest.Create(
            Guid.NewGuid(),
            AuditActor.Create(
                AuditActorType.Admin,
                Guid.NewGuid(),
                roles ?? ["SuperAdmin", "FinanceManager"]),
            action,
            AuditResourceTypes.Refund,
            Guid.NewGuid(),
            AuditResult.Success,
            errorCode: null,
            changes ??
            [
                AuditFieldChange.Code("status", "Approved", "Succeeded"),
                AuditFieldChange.Changed("succeededAmount"),
            ],
            reason,
            "correlation-1",
            "0123456789abcdef0123456789abcdef",
            jobPublicId: null,
            IPAddress.Parse("203.0.113.42"));

    private static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
