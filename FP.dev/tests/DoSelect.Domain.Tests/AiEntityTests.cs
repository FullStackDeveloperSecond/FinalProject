using DoSelect.Domain.Ai;
using DoSelect.Domain.Members;
using System.Reflection;

namespace DoSelect.Domain.Tests;

public sealed class AiEntityTests
{
    private static readonly DateTime GrantedAtUtc =
        new(2026, 8, 28, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ConsentWithdrawal_IsAppendOnlyEvidenceOfTheOriginalGrant()
    {
        var withdrawnAtUtc = GrantedAtUtc.AddHours(1);

        var record = AiConsentRecord.Withdraw(
            Guid.NewGuid().ToString("D"),
            policyVersion: 1,
            AiConsentPurpose.Support,
            SupportedLocale.ZhTw,
            source: "MemberWeb",
            GrantedAtUtc,
            withdrawnAtUtc);

        Assert.Equal(AiConsentRecordStatus.Withdrawn, record.Status);
        Assert.Equal(AiConsentPurpose.Support, record.Purpose);
        Assert.Equal(GrantedAtUtc, record.GrantedAtUtc);
        Assert.Equal(withdrawnAtUtc, record.WithdrawnAtUtc);
        Assert.Equal(withdrawnAtUtc, record.CreatedAtUtc);
    }

    [Fact]
    public void UsageReservation_RequiresExactlyOneOwnerAndARequestId()
    {
        Assert.Throws<ArgumentException>(() => AiUsageLedgerEntry.ReserveSupport(
            memberUserId: " ",
            Guid.NewGuid(),
            GrantedAtUtc));
        Assert.Throws<ArgumentException>(() => AiUsageLedgerEntry.ReserveSupport(
            Guid.NewGuid().ToString("D"),
            Guid.Empty,
            GrantedAtUtc));
    }

    [Theory]
    [InlineData(typeof(AiConsentRecord))]
    [InlineData(typeof(AiUsageLedgerEntry))]
    public void AiEntities_DoNotExposePublicPropertySetters(Type entityType)
    {
        Assert.DoesNotContain(
            entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => property.SetMethod?.IsPublic == true);
    }
}
