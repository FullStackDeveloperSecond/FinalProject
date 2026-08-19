using DoSelect.Domain.Imports;
using DoSelect.Infrastructure.Persistence.Configurations;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Imports;

public sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ConfigureMutablePublicEntity("ImportBatches");
        ConfigureEnum(builder.Property(entity => entity.ImportType), 24);
        ConfigureEnum(builder.Property(entity => entity.Status), 24);
        builder.Property(entity => entity.CreatedByAdminUserId).HasMaxLength(450).IsRequired();
        builder.Property(entity => entity.ExpiresAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.SourceFileHash1).HasColumnType("binary(32)");
        builder.Property(entity => entity.SourceFileHash2).HasColumnType("binary(32)");
        builder.Property(entity => entity.SourceFileHash3).HasColumnType("binary(32)");
        builder.Property(entity => entity.SourceFileNameDisplay1).HasMaxLength(255);
        builder.Property(entity => entity.SourceFileNameDisplay2).HasMaxLength(255);
        builder.Property(entity => entity.SourceFileNameDisplay3).HasMaxLength(255);
        builder.Property(entity => entity.RowCount).HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.NewCount).HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.UpdatedCount).HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.UnchangedCount).HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.ErrorCount).HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.NormalizedContentVersion).HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.ConfirmedAtUtc).HasPrecision(3);
        builder.Property(entity => entity.ResultSummaryJson).HasMaxLength(4000);
        builder.Property(entity => entity.CorrelationId).IsRequired();
        builder.HasIndex(entity => new { entity.CreatedByAdminUserId, entity.ImportType })
            .IsUnique()
            .HasFilter("[Status] IN ('Uploaded','Validating','Ready','Committing')")
            .HasDatabaseName("UX_ImportBatches_CreatedByAdminUserId_ImportType");
        builder.HasIndex(entity => new { entity.Status, entity.ExpiresAtUtc })
            .HasDatabaseName("IX_ImportBatches_Status_ExpiresAtUtc");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.CreatedByAdminUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("ImportBatches", table =>
        {
            table.HasCheckConstraint(
                "CK_ImportBatches_RowCount",
                "[RowCount] >= 0 AND [RowCount] <= 5000");
            table.HasCheckConstraint(
                "CK_ImportBatches_Counts",
                "[NewCount] >= 0 AND [UpdatedCount] >= 0 AND [UnchangedCount] >= 0 AND [ErrorCount] >= 0 AND [NewCount] + [UpdatedCount] + [UnchangedCount] + [ErrorCount] = [RowCount]");
        });
    }

    private static void ConfigureEnum<TEnum>(PropertyBuilder<TEnum> property, int maxLength)
        where TEnum : struct, Enum =>
        property.HasConversion<string>().HasMaxLength(maxLength).IsUnicode(false).IsRequired();
}

public sealed class ImportRowConfiguration : IEntityTypeConfiguration<ImportRow>
{
    public void Configure(EntityTypeBuilder<ImportRow> builder)
    {
        builder.ToTable("ImportRows");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).UseIdentityColumn();
        builder.Property(entity => entity.Dataset)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.ImportKey).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Action)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.NormalizedPayloadJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        builder.Property(entity => entity.ErrorCodes).HasMaxLength(2000);
        builder.Property(entity => entity.RowHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(entity => entity.RawJson).HasColumnType("nvarchar(max)");
        builder.Property(entity => entity.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.HasIndex(entity => new
        {
            entity.ImportBatchId,
            entity.Dataset,
            entity.SourceRowNumber,
        })
            .IsUnique()
            .HasDatabaseName("UX_ImportRows_ImportBatchId_Dataset_SourceRowNumber");
        builder.HasIndex(entity => new
        {
            entity.ImportBatchId,
            entity.Dataset,
            entity.ImportKey,
        })
            .IsUnique()
            .HasDatabaseName("UX_ImportRows_ImportBatchId_Dataset_ImportKey");
        builder.HasOne<ImportBatch>()
            .WithMany()
            .HasForeignKey(entity => entity.ImportBatchId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.ToTable("ImportRows", table =>
            table.HasCheckConstraint(
                "CK_ImportRows_SourceRowNumber",
                "[SourceRowNumber] > 0"));
    }
}
