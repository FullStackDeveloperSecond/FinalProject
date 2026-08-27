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
        Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("note").ValueKind);
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

    [Fact]
    public void Request_AllowsABoundedNoteOnlyForAnAllowListedAction()
    {
        using var context = CreateContext();
        var writer = new EfAuditWriter(context, new FixedTimeProvider(Now));
        var request = Request(note: "人工確認後重新執行退款。", reason: "refund.manual_retry");

        var audit = writer.Add(request);

        using var document = JsonDocument.Parse(audit.ChangedFieldsJson);
        Assert.Equal("人工確認後重新執行退款。", document.RootElement.GetProperty("note").GetString());
        Assert.Equal("refund.manual_retry", audit.Reason);
    }

    [Theory]
    [InlineData(AuditActions.MemberRestore, AuditResourceTypes.Member, "status")]
    [InlineData(AuditActions.AuditQuery, AuditResourceTypes.AuditLog, "filter")]
    public void Request_RejectsNoteForActionsThatDoNotAllowIt(
        string action,
        string resourceType,
        string field)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Request(
            action: action,
            resourceType: resourceType,
            changes: [AuditFieldChange.Changed(field)],
            note: "這個動作不接受自由文字備註"));
    }

    [Theory]
    [InlineData("admin@example.com")]
    [InlineData("token-reset-value")]
    public void Request_RejectsSensitiveAuditNote(string note)
    {
        Assert.Throws<ArgumentException>(() => Request(note: note));
    }

    [Fact]
    public void Request_RejectsAnOversizedAuditNote()
    {
        Assert.Throws<ArgumentException>(() => Request(note: new string('字', 1001)));
    }

    [Fact]
    public void Writer_PersistsAThousandCharacterUnicodeNoteWithinTheJsonColumnLimit()
    {
        using var context = CreateContext();
        var writer = new EfAuditWriter(context, new FixedTimeProvider(Now));
        var note = new string('字', 1000);

        var audit = writer.Add(Request(note: note));

        Assert.True(audit.ChangedFieldsJson.Length <= 4_000);
        using var document = JsonDocument.Parse(audit.ChangedFieldsJson);
        Assert.Equal(note, document.RootElement.GetProperty("note").GetString());
    }

    [Theory]
    [InlineData(AuditActions.CouponCreate, false)]
    [InlineData(AuditActions.CouponUpdate, false)]
    [InlineData(AuditActions.CouponActivate, true)]
    [InlineData(AuditActions.CouponPause, true)]
    [InlineData(AuditActions.CouponDisable, true)]
    public void Request_AcceptsCouponAuditDefinitions(string action, bool allowsNote)
    {
        var request = Request(
            action: action,
            resourceType: AuditResourceTypes.Coupon,
            changes:
            [
                AuditFieldChange.Code("status", "Draft", "Active"),
                AuditFieldChange.Changed("ruleVersion"),
                AuditFieldChange.Changed("changedFields"),
            ],
            reason: "coupon.admin_action",
            note: allowsNote ? "人工覆核完成" : null);

        Assert.Equal(action, request.Action);
        Assert.Equal(AuditResourceTypes.Coupon, request.ResourceType);
        Assert.Equal(allowsNote ? "人工覆核完成" : null, request.Note);
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
        string resourceType = AuditResourceTypes.Refund,
        IReadOnlyCollection<string>? roles = null,
        IReadOnlyCollection<AuditFieldChange>? changes = null,
        string reason = "refund.approved",
        string? note = null) =>
        AuditWriteRequest.Create(
            Guid.NewGuid(),
            AuditActor.Create(
                AuditActorType.Admin,
                Guid.NewGuid(),
                roles ?? ["SuperAdmin", "FinanceManager"]),
            action,
            resourceType,
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
            IPAddress.Parse("203.0.113.42"),
            note);

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
