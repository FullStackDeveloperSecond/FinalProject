using DoSelect.Domain.Builds;
using DoSelect.Infrastructure.Persistence.Configurations;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Builds;

public sealed class BuildListConfiguration : IEntityTypeConfiguration<BuildList>
{
    public void Configure(EntityTypeBuilder<BuildList> builder)
    {
        builder.ConfigureMutablePublicEntity("BuildLists");
        builder.Property(entity => entity.OwnerUserId).HasMaxLength(450).IsRequired();
        builder.Property(entity => entity.Name).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(16).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.LastCheckedAtUtc).HasPrecision(3);
        builder.Property(entity => entity.CompatibilityStatus)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false);
        builder.HasIndex(entity => new { entity.OwnerUserId, entity.UpdatedAtUtc })
            .HasDatabaseName("IX_BuildLists_OwnerUserId_UpdatedAtUtc");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class BuildListItemConfiguration : IEntityTypeConfiguration<BuildListItem>
{
    public void Configure(EntityTypeBuilder<BuildListItem> builder)
    {
        builder.ConfigurePublicEntity("BuildListItems");
        builder.HasIndex(entity => new { entity.BuildListId, entity.SkuId })
            .IsUnique()
            .HasDatabaseName("UX_BuildListItems_BuildListId_SkuId");
        builder.HasOne<BuildList>()
            .WithMany()
            .HasForeignKey(entity => entity.BuildListId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DoSelect.Domain.Catalog.Sku>()
            .WithMany()
            .HasForeignKey(entity => entity.SkuId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("BuildListItems", table =>
            table.HasCheckConstraint(
                "CK_BuildListItems_Quantity",
                "[Quantity] >= 1 AND [Quantity] <= 8"));
    }
}

public sealed class BuildShareTokenConfiguration : IEntityTypeConfiguration<BuildShareToken>
{
    public void Configure(EntityTypeBuilder<BuildShareToken> builder)
    {
        builder.ConfigurePublicEntity("BuildShareTokens");
        builder.Property(entity => entity.TokenHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(entity => entity.ExpiresAtUtc).HasPrecision(3);
        builder.Property(entity => entity.RevokedAtUtc).HasPrecision(3);
        builder.Property(entity => entity.LastAccessedAtUtc).HasPrecision(3);
        builder.HasIndex(entity => entity.TokenHash)
            .IsUnique()
            .HasDatabaseName("UX_BuildShareTokens_TokenHash");
        builder.HasIndex(entity => entity.ExpiresAtUtc)
            .HasFilter("[ExpiresAtUtc] IS NOT NULL")
            .HasDatabaseName("IX_BuildShareTokens_ExpiresAtUtc");
        builder.HasOne<BuildList>()
            .WithMany()
            .HasForeignKey(entity => entity.BuildListId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CompatibilityRuleSettingConfiguration
    : IEntityTypeConfiguration<CompatibilityRuleSetting>
{
    public void Configure(EntityTypeBuilder<CompatibilityRuleSetting> builder)
    {
        builder.ConfigureMutablePublicEntity("CompatibilityRuleSettings");
        builder.Property(entity => entity.RuleCode).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.SettingCode).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.DecimalValue).HasPrecision(18, 4);
        builder.Property(entity => entity.Reason).HasMaxLength(500).IsRequired();
        builder.Property(entity => entity.ChangedByAdminUserId).HasMaxLength(450).IsRequired();
        builder.HasIndex(entity => new
        {
            entity.RuleCode,
            entity.SettingCode,
            entity.SettingsVersion,
        })
            .IsUnique()
            .HasDatabaseName(
                "UX_CompatibilityRuleSettings_RuleCode_SettingCode_SettingsVersion");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.ChangedByAdminUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("CompatibilityRuleSettings", table =>
        {
            table.HasCheckConstraint(
                "CK_CompatibilityRuleSettings_ExactlyOneValue",
                "([DecimalValue] IS NOT NULL AND [BooleanValue] IS NULL) OR ([DecimalValue] IS NULL AND [BooleanValue] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_CompatibilityRuleSettings_SettingsVersion",
                "[SettingsVersion] > 0");
        });
    }
}

public sealed class CompatibilityCheckRunConfiguration
    : IEntityTypeConfiguration<CompatibilityCheckRun>
{
    public void Configure(EntityTypeBuilder<CompatibilityCheckRun> builder)
    {
        builder.ConfigurePublicEntity("CompatibilityCheckRuns");
        builder.Property(entity => entity.Overall)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.InputHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(entity => entity.EvaluatedAtUtc).HasPrecision(3).IsRequired();
        builder.HasIndex(entity => new { entity.BuildListId, entity.EvaluatedAtUtc })
            .HasDatabaseName("IX_CompatibilityCheckRuns_BuildListId_EvaluatedAtUtc");
        // 組長 PR #34 round-5 review, item 3: BuildListId leads the index above, so
        // EfCompatibilityCheckService.PurgeExpiredRunsAsync's BuildListId-less date filter can't
        // seek on it — it was scanning/sorting the whole table every batch. Public compatibility
        // checks are anonymous and unrate-limited beyond 30/min/IP, so this table can grow to tens
        // of thousands of rows/day; a date-leading index keeps a 500-row retention batch to an
        // index seek. Id (the identity PK) trails EvaluatedAtUtc so ties between rows sharing a
        // millisecond timestamp still sort deterministically, matching the query's ThenBy(Id).
        builder.HasIndex(entity => new { entity.EvaluatedAtUtc, entity.Id })
            .HasDatabaseName("IX_CompatibilityCheckRuns_EvaluatedAtUtc_Id");
        builder.HasOne<BuildList>()
            .WithMany()
            .HasForeignKey(entity => entity.BuildListId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("CompatibilityCheckRuns", table =>
        {
            table.HasCheckConstraint(
                "CK_CompatibilityCheckRuns_Versions",
                "[RuleSetVersion] > 0 AND [SettingsVersion] > 0");
            table.HasCheckConstraint(
                "CK_CompatibilityCheckRuns_Overall",
                "[Overall] IN ('Compatible','Warning','Blocked','InsufficientData')");
        });
    }
}

public sealed class SkuCompatibilityAttributeConfiguration
    : IEntityTypeConfiguration<SkuCompatibilityAttribute>
{
    public void Configure(EntityTypeBuilder<SkuCompatibilityAttribute> builder)
    {
        builder.ToTable("SkuCompatibilityAttributes");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).UseIdentityColumn();
        builder.Property(entity => entity.AttributeKey).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.AttributeValue).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.HasIndex(entity => new { entity.SkuId, entity.AttributeKey, entity.AttributeValue })
            .IsUnique()
            .HasDatabaseName("UX_SkuCompatibilityAttributes_SkuId_AttributeKey_AttributeValue");
        builder.HasOne<DoSelect.Domain.Catalog.Sku>()
            .WithMany()
            .HasForeignKey(entity => entity.SkuId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SkuStorageInterfacePortConfiguration
    : IEntityTypeConfiguration<SkuStorageInterfacePort>
{
    public void Configure(EntityTypeBuilder<SkuStorageInterfacePort> builder)
    {
        builder.ToTable("SkuStorageInterfacePorts", table => table.HasCheckConstraint(
            "CK_SkuStorageInterfacePorts_PortCount",
            $"[PortCount] > 0 AND [PortCount] <= {CompatibilityAttributeLimits.MaxStorageInterfacePortCount}"));
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).UseIdentityColumn();
        builder.Property(entity => entity.InterfaceCode).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.HasIndex(entity => new { entity.SkuId, entity.InterfaceCode })
            .IsUnique()
            .HasDatabaseName("UX_SkuStorageInterfacePorts_SkuId_InterfaceCode");
        builder.HasOne<DoSelect.Domain.Catalog.Sku>()
            .WithMany()
            .HasForeignKey(entity => entity.SkuId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CompatibilityCheckResultConfiguration
    : IEntityTypeConfiguration<CompatibilityCheckResult>
{
    public void Configure(EntityTypeBuilder<CompatibilityCheckResult> builder)
    {
        builder.ToTable("CompatibilityCheckResults");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).UseIdentityColumn();
        builder.Property(entity => entity.RuleCode).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Severity).HasMaxLength(24).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.MessageKey).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.FactsJson).HasMaxLength(4000);
        builder.HasIndex(entity => new { entity.CompatibilityCheckRunId, entity.Severity })
            .HasDatabaseName("IX_CompatibilityCheckResults_RunId_Severity");
        builder.HasOne<CompatibilityCheckRun>()
            .WithMany()
            .HasForeignKey(entity => entity.CompatibilityCheckRunId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
