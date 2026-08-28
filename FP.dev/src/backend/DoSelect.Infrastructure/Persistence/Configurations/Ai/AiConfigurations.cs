using DoSelect.Domain.Ai;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DoSelect.Infrastructure.Persistence.Configurations.Ai;

public sealed class AiConsentRecordConfiguration : IEntityTypeConfiguration<AiConsentRecord>
{
    public void Configure(EntityTypeBuilder<AiConsentRecord> builder)
    {
        builder.ToTable("AiConsentRecords", table =>
        {
            table.HasCheckConstraint(
                "CK_AiConsentRecords_PolicyVersion",
                "[PolicyVersion] > 0");
            table.HasCheckConstraint(
                "CK_AiConsentRecords_Purpose",
                "[Purpose] IN ('Support')");
            table.HasCheckConstraint(
                "CK_AiConsentRecords_Status",
                "([Status] = 'Granted' AND [WithdrawnAtUtc] IS NULL) OR " +
                "([Status] = 'Withdrawn' AND [WithdrawnAtUtc] IS NOT NULL AND " +
                "[WithdrawnAtUtc] >= [GrantedAtUtc])");
            table.HasCheckConstraint(
                "CK_AiConsentRecords_Locale",
                "[Locale] IN ('zh-TW','ja-JP','ko-KR')");
        });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).UseIdentityColumn();
        builder.Property(entity => entity.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.MemberUserId).HasMaxLength(450).IsRequired();
        builder.Property(entity => entity.PolicyVersion).IsRequired();
        builder.Property(entity => entity.Purpose)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.Locale)
            .HasConversion(new ValueConverter<SupportedLocale, string>(
                locale => ToLocaleCode(locale),
                code => FromLocaleCode(code)))
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.GrantedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.WithdrawnAtUtc).HasPrecision(3);
        builder.Property(entity => entity.Source)
            .HasMaxLength(32)
            .IsUnicode(false)
            .IsRequired();
        builder.HasIndex(entity => new
        {
            entity.MemberUserId,
            entity.Purpose,
            entity.PolicyVersion,
            entity.CreatedAtUtc,
        }).HasDatabaseName(
            "IX_AiConsentRecords_MemberUserId_Purpose_PolicyVersion_CreatedAtUtc");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.MemberUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string ToLocaleCode(SupportedLocale locale) => locale switch
    {
        SupportedLocale.ZhTw => "zh-TW",
        SupportedLocale.JaJp => "ja-JP",
        SupportedLocale.KoKr => "ko-KR",
        _ => throw new ArgumentOutOfRangeException(nameof(locale)),
    };

    private static SupportedLocale FromLocaleCode(string code) => code switch
    {
        "zh-TW" => SupportedLocale.ZhTw,
        "ja-JP" => SupportedLocale.JaJp,
        "ko-KR" => SupportedLocale.KoKr,
        _ => throw new InvalidOperationException("Unsupported locale code."),
    };
}

public sealed class AiUsageLedgerEntryConfiguration
    : IEntityTypeConfiguration<AiUsageLedgerEntry>
{
    public void Configure(EntityTypeBuilder<AiUsageLedgerEntry> builder)
    {
        builder.ToTable("AiUsageLedger", table =>
        {
            table.HasCheckConstraint(
                "CK_AiUsageLedger_Owner",
                "([MemberUserId] IS NOT NULL AND [AnonymousSessionKeyHash] IS NULL) OR " +
                "([MemberUserId] IS NULL AND [AnonymousSessionKeyHash] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_AiUsageLedger_Usage",
                "[InputTokens] >= 0 AND [OutputTokens] >= 0 AND [EstimatedCostUsd] >= 0");
        });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).UseIdentityColumn();
        builder.Property(entity => entity.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.MemberUserId).HasMaxLength(450);
        builder.Property(entity => entity.AnonymousSessionKeyHash).HasColumnType("binary(32)");
        builder.Property(entity => entity.Feature)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.RequestPublicId).IsRequired();
        builder.Property(entity => entity.InputTokens).IsRequired();
        builder.Property(entity => entity.OutputTokens).IsRequired();
        builder.Property(entity => entity.EstimatedCostUsd).HasPrecision(12, 6).IsRequired();
        builder.Property(entity => entity.Succeeded).IsRequired();
        builder.Property(entity => entity.OccurredAtUtc).HasPrecision(3).IsRequired();
        builder.HasIndex(entity => entity.RequestPublicId)
            .IsUnique()
            .HasDatabaseName("UX_AiUsageLedger_RequestPublicId");
        builder.HasIndex(entity => new
        {
            entity.MemberUserId,
            entity.Feature,
            entity.OccurredAtUtc,
        }).HasDatabaseName("IX_AiUsageLedger_MemberUserId_Feature_OccurredAtUtc");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.MemberUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


public sealed class AiConversationConfiguration : IEntityTypeConfiguration<AiConversation>
{
    public void Configure(EntityTypeBuilder<AiConversation> builder)
    {
        builder.ToTable("AiConversations", table =>
        {
            table.HasCheckConstraint(
                "CK_AiConversations_Purpose",
                "[Purpose] IN ('Support')");
            table.HasCheckConstraint(
                "CK_AiConversations_Status",
                "[Status] IN ('Active','Closed')");
            table.HasCheckConstraint(
                "CK_AiConversations_ConsentPolicyVersion",
                "[ConsentPolicyVersion] > 0");
        });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).UseIdentityColumn();
        builder.Property(entity => entity.PublicId).IsRequired();
        builder.Property(entity => entity.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.UpdatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.RowVersion).IsRowVersion();
        builder.Property(entity => entity.MemberUserId).HasMaxLength(450).IsRequired();
        builder.Property(entity => entity.Purpose)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.Locale)
            .HasConversion(new ValueConverter<SupportedLocale, string>(
                locale => ToLocaleCode(locale),
                code => FromLocaleCode(code)))
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.ConsentPolicyVersion).IsRequired();
        builder.Property(entity => entity.LastActivityAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.ExpiresAtUtc).HasPrecision(3).IsRequired();
        builder.HasIndex(entity => entity.PublicId)
            .IsUnique()
            .HasDatabaseName("UX_AiConversations_PublicId");
        builder.HasIndex(entity => new { entity.MemberUserId, entity.LastActivityAtUtc })
            .HasDatabaseName("IX_AiConversations_MemberUserId_LastActivityAtUtc");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.MemberUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Domain.Support.SupportTicket>()
            .WithMany()
            .HasForeignKey(entity => entity.SupportTicketId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string ToLocaleCode(SupportedLocale locale) => locale switch
    {
        SupportedLocale.ZhTw => "zh-TW",
        SupportedLocale.JaJp => "ja-JP",
        SupportedLocale.KoKr => "ko-KR",
        _ => throw new ArgumentOutOfRangeException(nameof(locale)),
    };

    private static SupportedLocale FromLocaleCode(string code) => code switch
    {
        "zh-TW" => SupportedLocale.ZhTw,
        "ja-JP" => SupportedLocale.JaJp,
        "ko-KR" => SupportedLocale.KoKr,
        _ => throw new InvalidOperationException("Unsupported locale code."),
    };
}

public sealed class AiInteractionConfiguration : IEntityTypeConfiguration<AiInteraction>
{
    public void Configure(EntityTypeBuilder<AiInteraction> builder)
    {
        builder.ToTable("AiInteractions", table =>
        {
            table.HasCheckConstraint(
                "CK_AiInteractions_Owner",
                "([AiConversationId] IS NOT NULL AND [SearchPublicId] IS NULL) OR " +
                "([AiConversationId] IS NULL AND [SearchPublicId] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_AiInteractions_Usage",
                "[Sequence] > 0 AND [InputTokens] >= 0 AND [OutputTokens] >= 0 AND " +
                "[EstimatedCostUsd] >= 0 AND [LatencyMs] >= 0");
        });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).UseIdentityColumn();
        builder.Property(entity => entity.PublicId).IsRequired();
        builder.Property(entity => entity.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.UserContentProtected).HasMaxLength(4_000).IsRequired();
        builder.Property(entity => entity.AssistantContent).HasMaxLength(4_000);
        builder.Property(entity => entity.IntentJson).HasMaxLength(8_000);
        builder.Property(entity => entity.Model).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.PromptVersion).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.SchemaVersion).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.EstimatedCostUsd).HasPrecision(12, 6).IsRequired();
        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.FallbackReason).HasMaxLength(64).IsUnicode(false);
        builder.HasIndex(entity => entity.PublicId)
            .IsUnique()
            .HasDatabaseName("UX_AiInteractions_PublicId");
        builder.HasIndex(entity => new { entity.AiConversationId, entity.Sequence })
            .IsUnique()
            .HasFilter("[AiConversationId] IS NOT NULL")
            .HasDatabaseName("UX_AiInteractions_AiConversationId_Sequence");
        builder.HasOne<AiConversation>()
            .WithMany()
            .HasForeignKey(entity => entity.AiConversationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AiCitationConfiguration : IEntityTypeConfiguration<AiCitation>
{
    public void Configure(EntityTypeBuilder<AiCitation> builder)
    {
        builder.ToTable("AiCitations");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).UseIdentityColumn();
        builder.Property(entity => entity.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.SourceType).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.SourceVersion).HasMaxLength(64);
        builder.Property(entity => entity.Label).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Url).HasMaxLength(2_048);
        builder.HasIndex(entity => new { entity.AiInteractionId, entity.SortOrder })
            .IsUnique()
            .HasDatabaseName("UX_AiCitations_AiInteractionId_SortOrder");
        builder.HasOne<AiInteraction>()
            .WithMany()
            .HasForeignKey(entity => entity.AiInteractionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
