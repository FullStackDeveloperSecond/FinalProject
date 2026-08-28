using DoSelect.Domain.Ai;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DoSelect.Infrastructure.Tests.Ai;

public sealed class AiPersistenceModelTests
{
    private const string SyntheticConnectionString =
        "Server=localhost;Database=DoSelectAiSynthetic;Trusted_Connection=True;" +
        "TrustServerCertificate=True;";

    [Fact]
    public void Model_MapsAiSafetyTablesWithRequiredIntegrityConstraints()
    {
        using var context = CreateContext();
        var consent = Assert.IsAssignableFrom<IReadOnlyEntityType>(
            context.Model.FindEntityType(typeof(AiConsentRecord)));
        var usage = Assert.IsAssignableFrom<IReadOnlyEntityType>(
            context.Model.FindEntityType(typeof(AiUsageLedgerEntry)));

        Assert.Equal("AiConsentRecords", consent.GetTableName());
        Assert.Contains(consent.GetIndexes(), index =>
            index.GetDatabaseName() == "IX_AiConsentRecords_MemberUserId_CreatedAtUtc");
        Assert.Contains(consent.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Single().Name == nameof(AiConsentRecord.MemberUserId) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict);

        Assert.Equal("AiUsageLedger", usage.GetTableName());
        Assert.Contains(usage.GetIndexes(), index =>
            index.IsUnique &&
            index.GetDatabaseName() == "UX_AiUsageLedger_RequestPublicId");
        Assert.Contains(usage.GetIndexes(), index =>
            index.GetDatabaseName() ==
            "IX_AiUsageLedger_MemberUserId_Feature_OccurredAtUtc");
        Assert.Equal(
            "binary(32)",
            usage.FindProperty(nameof(AiUsageLedgerEntry.AnonymousSessionKeyHash))?.GetColumnType());
    }

    private static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(SyntheticConnectionString)
            .Options);
}
