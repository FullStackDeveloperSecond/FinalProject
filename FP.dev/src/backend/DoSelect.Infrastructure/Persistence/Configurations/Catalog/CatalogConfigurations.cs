using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Catalog;

public sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ConfigureMutablePublicEntity("Brands");
        builder.Property(entity => entity.Code).HasMaxLength(64).IsRequired();
        builder.HasIndex(entity => entity.Code).IsUnique().HasDatabaseName("UX_Brands_Code");
        builder.Property(entity => entity.NameZhTw).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(entity => entity.SortOrder).HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.Description).HasMaxLength(1000);
        builder.Property(entity => entity.WebsiteUrl).HasMaxLength(2048);
    }
}

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ConfigureMutablePublicEntity("Categories");
        builder.Property(entity => entity.Code).HasMaxLength(64).IsRequired();
        builder.HasIndex(entity => entity.Code)
            .IsUnique()
            .HasDatabaseName("UX_Categories_Code");
        builder.Property(entity => entity.NameZhTw).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(entity => entity.SortOrder).HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.Slug).HasMaxLength(120).IsRequired();
        builder.HasIndex(entity => entity.Slug)
            .IsUnique()
            .HasDatabaseName("UX_Categories_Slug");
        builder.Property(entity => entity.Description).HasMaxLength(1000);
        builder.HasIndex(entity => entity.ParentCategoryId)
            .HasDatabaseName("IX_Categories_ParentCategoryId");
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(entity => entity.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("Categories", table =>
            table.HasCheckConstraint(
                "CK_Categories_NotSelfParent",
                "[ParentCategoryId] IS NULL OR [ParentCategoryId] <> [Id]"));
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ConfigureMutablePublicEntity("Products");
        builder.Property(entity => entity.ProductCode).HasMaxLength(64).IsRequired();
        builder.HasIndex(entity => entity.ProductCode)
            .IsUnique()
            .HasDatabaseName("UX_Products_ProductCode");
        builder.Property(entity => entity.NameZhTw).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.DescriptionZhTw).HasMaxLength(4000);
        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.IsFeatured).HasDefaultValue(false).IsRequired();
        builder.HasIndex(entity => new { entity.CategoryId, entity.Status })
            .HasDatabaseName("IX_Products_CategoryId_Status");
        builder.HasIndex(entity => new { entity.BrandId, entity.Status })
            .HasDatabaseName("IX_Products_BrandId_Status");
        builder.HasOne<Brand>()
            .WithMany()
            .HasForeignKey(entity => entity.BrandId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(entity => entity.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("Products", table =>
            table.HasCheckConstraint(
                "CK_Products_WarrantyMonths",
                "[WarrantyMonths] IS NULL OR ([WarrantyMonths] >= 0 AND [WarrantyMonths] <= 120)"));
    }
}

public sealed class SkuConfiguration : IEntityTypeConfiguration<Sku>
{
    public void Configure(EntityTypeBuilder<Sku> builder)
    {
        builder.ConfigureMutablePublicEntity("Skus");
        builder.Property(entity => entity.SkuCode).HasMaxLength(64).IsRequired();
        builder.HasIndex(entity => entity.SkuCode)
            .IsUnique()
            .HasDatabaseName("UX_Skus_SkuCode");
        builder.Property(entity => entity.NameZhTw).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.ListPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.UnitCost).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.WeightKg).HasPrecision(10, 3);
        builder.Property(entity => entity.LengthCm).HasPrecision(10, 2);
        builder.Property(entity => entity.WidthCm).HasPrecision(10, 2);
        builder.Property(entity => entity.HeightCm).HasPrecision(10, 2);
        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.IsDefault).HasDefaultValue(false).IsRequired();
        builder.Property(entity => entity.RequiresPrepayment).HasDefaultValue(false).IsRequired();
        builder.HasIndex(entity => new { entity.ProductId, entity.Status })
            .HasDatabaseName("IX_Skus_ProductId_Status");
        builder.HasIndex(entity => new { entity.ProductId, entity.IsDefault })
            .IsUnique()
            .HasFilter("[IsDefault] = 1")
            .HasDatabaseName("UX_Skus_ProductId_IsDefault");
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("Skus", table =>
        {
            table.HasCheckConstraint(
                "CK_Skus_Prices",
                "[ListPrice] >= 0 AND [UnitCost] >= 0");
            table.HasCheckConstraint(
                "CK_Skus_Dimensions",
                "([WeightKg] IS NULL OR [WeightKg] > 0) AND ([LengthCm] IS NULL OR [LengthCm] > 0) AND ([WidthCm] IS NULL OR [WidthCm] > 0) AND ([HeightCm] IS NULL OR [HeightCm] > 0)");
        });
    }
}

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ConfigureMutablePublicEntity("Tags");
        builder.Property(entity => entity.Code).HasMaxLength(64).IsRequired();
        builder.HasIndex(entity => entity.Code).IsUnique().HasDatabaseName("UX_Tags_Code");
        builder.Property(entity => entity.NameZhTw).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(entity => entity.SortOrder).HasDefaultValue(0).IsRequired();
    }
}

public sealed class ProductTagConfiguration : IEntityTypeConfiguration<ProductTag>
{
    public void Configure(EntityTypeBuilder<ProductTag> builder)
    {
        builder.ToTable("ProductTags");
        builder.HasKey(entity => new { entity.ProductId, entity.TagId });
        builder.Property(entity => entity.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.HasIndex(entity => new { entity.TagId, entity.ProductId })
            .HasDatabaseName("IX_ProductTags_TagId_ProductId");
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tag>()
            .WithMany()
            .HasForeignKey(entity => entity.TagId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
