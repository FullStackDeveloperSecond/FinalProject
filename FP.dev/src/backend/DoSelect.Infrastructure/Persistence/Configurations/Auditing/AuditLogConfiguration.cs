using DoSelect.Domain.Auditing;
using DoSelect.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Auditing;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ConfigurePublicEntity("AuditLogs");
        builder.Property(entity => entity.ActorType)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.ActorRolesJson).HasMaxLength(1_000).IsRequired();
        builder.Property(entity => entity.Action)
            .HasMaxLength(128)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.ResourceType)
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.Result)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.ErrorCode).HasMaxLength(128).IsUnicode(false);
        builder.Property(entity => entity.ChangedFieldsJson).HasMaxLength(4_000).IsRequired();
        builder.Property(entity => entity.Reason)
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.CorrelationId)
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.TraceId)
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.MaskedIpAddress)
            .HasMaxLength(64)
            .IsUnicode(false);
        builder.Property(entity => entity.OccurredAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.RetentionUntilUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.IsLegalHold).IsRequired();
        builder.Property(entity => entity.HoldReason).HasMaxLength(500);
        builder.HasIndex(entity => entity.OccurredAtUtc)
            .HasDatabaseName("IX_AuditLogs_OccurredAtUtc");
        builder.HasIndex(entity => new { entity.Action, entity.OccurredAtUtc })
            .HasDatabaseName("IX_AuditLogs_Action_OccurredAtUtc");
        builder.HasIndex(entity => new
        {
            entity.ResourceType,
            entity.ResourcePublicId,
            entity.OccurredAtUtc,
        }).HasDatabaseName("IX_AuditLogs_Resource_OccurredAtUtc");
        builder.HasIndex(entity => new { entity.ActorPublicId, entity.OccurredAtUtc })
            .HasDatabaseName("IX_AuditLogs_ActorPublicId_OccurredAtUtc");
        builder.HasIndex(entity => new { entity.IsLegalHold, entity.RetentionUntilUtc })
            .HasDatabaseName("IX_AuditLogs_Retention");
        builder.ToTable("AuditLogs", table =>
        {
            table.HasCheckConstraint(
                "CK_AuditLogs_Actor",
                "(([ActorType] = 'System' AND [ActorPublicId] IS NULL) OR ([ActorType] IN ('Member', 'Admin', 'Guest') AND [ActorPublicId] IS NOT NULL)) AND (([ActorType] = 'Admin' AND [ActorRolesJson] <> '[]') OR ([ActorType] <> 'Admin' AND [ActorRolesJson] = '[]'))");
            table.HasCheckConstraint(
                "CK_AuditLogs_Result",
                "([Result] = 'Success' AND [ErrorCode] IS NULL) OR ([Result] IN ('Rejected', 'Conflict', 'Failed') AND [ErrorCode] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_AuditLogs_Json",
                "ISJSON([ActorRolesJson]) = 1 AND ISJSON([ChangedFieldsJson]) = 1");
            table.HasCheckConstraint(
                "CK_AuditLogs_SchemaVersion",
                "[ChangedFieldsSchemaVersion] > 0");
            table.HasCheckConstraint(
                "CK_AuditLogs_Retention",
                "[RetentionUntilUtc] >= [OccurredAtUtc]");
            table.HasCheckConstraint(
                "CK_AuditLogs_LegalHold",
                "([IsLegalHold] = 0 AND [HoldReason] IS NULL) OR ([IsLegalHold] = 1 AND [HoldReason] IS NOT NULL)");
        });
    }
}
