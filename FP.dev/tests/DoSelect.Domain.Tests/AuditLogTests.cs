using DoSelect.Domain.Auditing;

namespace DoSelect.Domain.Tests;

public sealed class AuditLogTests
{
    private static readonly DateTime OccurredAtUtc =
        new(2026, 8, 26, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AuditLogStoresImmutableWhitelistedSnapshots()
    {
        var actorPublicId = Guid.NewGuid();
        var resourcePublicId = Guid.NewGuid();
        var audit = Create(actorPublicId: actorPublicId, resourcePublicId: resourcePublicId);

        Assert.Equal(AuditActorType.Admin, audit.ActorType);
        Assert.Equal(actorPublicId, audit.ActorPublicId);
        Assert.Equal("[\"FinanceManager\"]", audit.ActorRolesJson);
        Assert.Equal("refund.execute", audit.Action);
        Assert.Equal("Refund", audit.ResourceType);
        Assert.Equal(resourcePublicId, audit.ResourcePublicId);
        Assert.Equal(AuditResult.Success, audit.Result);
        Assert.Null(audit.ErrorCode);
        Assert.Equal(1, audit.ChangedFieldsSchemaVersion);
        Assert.Equal("refund.approved", audit.Reason);
        Assert.Equal("203.0.113.0/24", audit.MaskedIpAddress);
        Assert.Equal(OccurredAtUtc.AddDays(365), audit.RetentionUntilUtc);
        Assert.False(audit.IsLegalHold);
        Assert.Null(audit.HoldReason);
    }

    [Fact]
    public void NonSystemActorRequiresPublicIdAndAdminRequiresRoleSnapshot()
    {
        Assert.Throws<ArgumentException>(() => Create(hasActorPublicId: false));
        Assert.Throws<ArgumentException>(() => Create(actorRolesJson: "[]"));
    }

    [Theory]
    [InlineData(AuditActorType.Member)]
    [InlineData(AuditActorType.Guest)]
    [InlineData(AuditActorType.System)]
    public void OnlyAdminActorCanStoreRoleSnapshot(AuditActorType actorType)
    {
        Assert.Throws<ArgumentException>(() => Create(
            actorType: actorType,
            actorPublicId: actorType == AuditActorType.System ? null : Guid.NewGuid(),
            hasActorPublicId: actorType != AuditActorType.System));
    }

    [Fact]
    public void ActorRoleSnapshotMustBeAJsonArrayOfNonEmptyNames()
    {
        Assert.Throws<ArgumentException>(() => Create(actorRolesJson: "not-json"));
        Assert.Throws<ArgumentException>(() => Create(actorRolesJson: "[\"\"]"));
    }

    [Fact]
    public void FailureRequiresSafeErrorCodeAndSuccessRejectsOne()
    {
        Assert.Throws<ArgumentException>(() => Create(
            result: AuditResult.Failed,
            errorCode: null));
        Assert.Throws<ArgumentException>(() => Create(
            result: AuditResult.Success,
            errorCode: "refund_failed"));
    }

    [Fact]
    public void RetentionAndHoldMetadataMustBeConsistent()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(
            retentionUntilUtc: OccurredAtUtc.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => Create(
            isLegalHold: true,
            holdReason: null));
        Assert.Throws<ArgumentException>(() => Create(
            isLegalHold: false,
            holdReason: "investigation"));
    }

    private static AuditLog Create(
        AuditActorType actorType = AuditActorType.Admin,
        Guid? actorPublicId = null,
        bool hasActorPublicId = true,
        Guid? resourcePublicId = null,
        string actorRolesJson = "[\"FinanceManager\"]",
        AuditResult result = AuditResult.Success,
        string? errorCode = null,
        DateTime? retentionUntilUtc = null,
        bool isLegalHold = false,
        string? holdReason = null) =>
        new(
            Guid.NewGuid(),
            actorType,
            hasActorPublicId ? actorPublicId ?? Guid.NewGuid() : null,
            actorRolesJson,
            "refund.execute",
            "Refund",
            resourcePublicId ?? Guid.NewGuid(),
            result,
            errorCode,
            "{\"schemaVersion\":1,\"changes\":[]}",
            1,
            "refund.approved",
            "correlation-1",
            "0123456789abcdef0123456789abcdef",
            null,
            "203.0.113.0/24",
            OccurredAtUtc,
            retentionUntilUtc ?? OccurredAtUtc.AddDays(365),
            isLegalHold,
            holdReason);
}
