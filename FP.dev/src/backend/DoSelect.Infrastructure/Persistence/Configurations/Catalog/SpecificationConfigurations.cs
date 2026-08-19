using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence.Configurations;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Catalog;

public sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ConfigureMutablePublicEntity("ProductImages");
        builder.Property(entity => entity.StorageKey).HasMaxLength(500).IsRequired();
        builder.HasIndex(entity => entity.StorageKey)
            .IsUnique()
            .HasDatabaseName("UX_ProductImages_StorageKey");
        builder.Property(entity => entity.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(entity => entity.MediaType).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.Sha256).HasColumnType("binary(32)").IsRequired();
        builder.Property(entity => entity.AltTextZhTw).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.SourceUrl).HasMaxLength(2048);
        builder.Property(entity => entity.LicenseUrl).HasMaxLength(2048);
        builder.Property(entity => entity.AuthorName).HasMaxLength(160);
        builder.Property(entity => entity.LicenseName).HasMaxLength(160);
        builder.Property(entity => entity.DownloadedAtUtc).HasPrecision(3);
        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.SortOrder).HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.PublishedAtUtc).HasPrecision(3);
        builder.Property(entity => entity.DeletedAtUtc).HasPrecision(3);
        builder.HasIndex(entity => new { entity.ProductId, entity.Status, entity.SortOrder })
            .HasDatabaseName("IX_ProductImages_ProductId_Status_SortOrder");
        builder.HasIndex(entity => new { entity.Status, entity.DeletedAtUtc })
            .HasDatabaseName("IX_ProductImages_Status_DeletedAtUtc");
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Sku>()
            .WithMany()
            .HasForeignKey(entity => entity.SkuId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("ProductImages", table =>
        {
            table.HasCheckConstraint(
                "CK_ProductImages_FileSize",
                "[FileSizeBytes] >= 1 AND [FileSizeBytes] <= 10485760");
            table.HasCheckConstraint(
                "CK_ProductImages_Dimensions",
                "[Width] > 0 AND [Height] > 0");
        });
    }
}

public sealed class MeasurementUnitConfiguration : IEntityTypeConfiguration<MeasurementUnit>
{
    public void Configure(EntityTypeBuilder<MeasurementUnit> builder)
    {
        builder.ConfigureMutablePublicEntity("MeasurementUnits");
        builder.Property(entity => entity.Code).HasMaxLength(64).IsRequired();
        builder.HasIndex(entity => entity.Code)
            .IsUnique()
            .HasDatabaseName("UX_MeasurementUnits_Code");
        builder.Property(entity => entity.NameZhTw).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(entity => entity.SortOrder).HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.Symbol).HasMaxLength(24).IsRequired();
        builder.Property(entity => entity.Dimension).HasMaxLength(32).IsUnicode(false).IsRequired();
    }
}

public sealed class SpecificationDefinitionConfiguration
    : IEntityTypeConfiguration<SpecificationDefinition>
{
    public void Configure(EntityTypeBuilder<SpecificationDefinition> builder)
    {
        builder.ConfigureMutablePublicEntity("SpecificationDefinitions");
        builder.Property(entity => entity.SemanticKey).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.DisplayNameZhTw).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.ValueType)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsUnicode(false)
            .IsRequired();
        builder.HasIndex(entity => new { entity.CategoryId, entity.SemanticKey })
            .IsUnique()
            .HasDatabaseName("UX_SpecificationDefinitions_CategoryId_SemanticKey");
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(entity => entity.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MeasurementUnit>()
            .WithMany()
            .HasForeignKey(entity => entity.MeasurementUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("SpecificationDefinitions", table =>
            table.HasCheckConstraint(
                "CK_SpecificationDefinitions_MeasurementUnit",
                "[MeasurementUnitId] IS NULL OR [ValueType] = 'Decimal'"));
    }
}

public sealed class SpecificationOptionConfiguration
    : IEntityTypeConfiguration<SpecificationOption>
{
    public void Configure(EntityTypeBuilder<SpecificationOption> builder)
    {
        builder.ConfigureMutablePublicEntity("SpecificationOptions");
        builder.Property(entity => entity.Code).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.DisplayNameZhTw).HasMaxLength(160).IsRequired();
        builder.HasIndex(entity => new { entity.SpecificationDefinitionId, entity.Code })
            .IsUnique()
            .HasDatabaseName("UX_SpecificationOptions_DefinitionId_Code");
        builder.HasOne<SpecificationDefinition>()
            .WithMany()
            .HasForeignKey(entity => entity.SpecificationDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SpecificationSourceConfiguration
    : IEntityTypeConfiguration<SpecificationSource>
{
    public void Configure(EntityTypeBuilder<SpecificationSource> builder)
    {
        builder.ConfigureMutablePublicEntity("SpecificationSources");
        builder.Property(entity => entity.SourceType)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.ProviderName).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.SourceUrl).HasMaxLength(2048).IsRequired();
        builder.Property(entity => entity.OriginalFieldName).HasMaxLength(160);
        builder.Property(entity => entity.RetrievedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.ReviewedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.ReviewedByAdminUserId).HasMaxLength(450).IsRequired();
        builder.Property(entity => entity.Note).HasMaxLength(1000);
        builder.Property(entity => entity.SourceVersion).HasMaxLength(64).IsRequired();
        builder.HasIndex(entity => new
        {
            entity.SourceUrl,
            entity.ProviderName,
            entity.SourceVersion,
        })
            .HasDatabaseName("IX_SpecificationSources_Url_Provider_Version");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.ReviewedByAdminUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SkuSpecificationValueConfiguration
    : IEntityTypeConfiguration<SkuSpecificationValue>
{
    public void Configure(EntityTypeBuilder<SkuSpecificationValue> builder)
    {
        builder.ConfigureMutableEntity("SkuSpecificationValues");
        builder.Property(entity => entity.StringValue).HasMaxLength(500);
        builder.Property(entity => entity.DecimalValue).HasPrecision(18, 4);
        builder.HasIndex(entity => new { entity.SkuId, entity.SpecificationDefinitionId })
            .IsUnique()
            .HasDatabaseName("UX_SkuSpecificationValues_SkuId_SpecificationDefinitionId");
        builder.HasIndex(entity => new
        {
            entity.SpecificationDefinitionId,
            entity.DecimalValue,
        })
            .HasDatabaseName("IX_SkuSpecificationValues_DefinitionId_DecimalValue");
        builder.HasIndex(entity => new
        {
            entity.SpecificationDefinitionId,
            entity.OptionId,
        })
            .HasDatabaseName("IX_SkuSpecificationValues_DefinitionId_OptionId");
        builder.HasOne<Sku>()
            .WithMany()
            .HasForeignKey(entity => entity.SkuId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SpecificationDefinition>()
            .WithMany()
            .HasForeignKey(entity => entity.SpecificationDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SpecificationOption>()
            .WithMany()
            .HasForeignKey(entity => entity.OptionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SpecificationSource>()
            .WithMany()
            .HasForeignKey(entity => entity.SpecificationSourceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("SkuSpecificationValues", table =>
            table.HasCheckConstraint(
                "CK_SkuSpecificationValues_ExactlyOneValue",
                "(CASE WHEN [StringValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1"));
    }
}

public sealed class SalePriceConfiguration : IEntityTypeConfiguration<SalePrice>
{
    public void Configure(EntityTypeBuilder<SalePrice> builder)
    {
        builder.ConfigureMutablePublicEntity("SalePrices");
        builder.Property(entity => entity.Price).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.StartsAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.EndsAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.CreatedByAdminUserId).HasMaxLength(450).IsRequired();
        builder.HasIndex(entity => new
        {
            entity.SkuId,
            entity.StartsAtUtc,
            entity.EndsAtUtc,
        })
            .HasDatabaseName("IX_SalePrices_SkuId_StartsAtUtc_EndsAtUtc");
        builder.HasOne<Sku>()
            .WithMany()
            .HasForeignKey(entity => entity.SkuId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.CreatedByAdminUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("SalePrices", table =>
        {
            table.HasCheckConstraint("CK_SalePrices_Price", "[Price] >= 0");
            table.HasCheckConstraint("CK_SalePrices_Period", "[EndsAtUtc] > [StartsAtUtc]");
        });
    }
}
