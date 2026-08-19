using System.Linq.Expressions;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Common;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence.Configurations;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DoSelect.Infrastructure.Persistence.Configurations.Catalog;

public sealed class BrandTranslationConfiguration
    : IEntityTypeConfiguration<BrandTranslation>
{
    public void Configure(EntityTypeBuilder<BrandTranslation> builder)
    {
        TranslationConfiguration.ConfigureBase(
            builder,
            "BrandTranslations",
            entity => entity.Locale,
            entity => entity.TranslationStatus,
            entity => entity.ReviewedByAdminUserId,
            entity => entity.ReviewedAtUtc);
        builder.Property(entity => entity.Name).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.Description).HasMaxLength(1000);
        builder.HasIndex(entity => new { entity.BrandId, entity.Locale })
            .IsUnique()
            .HasDatabaseName("UX_BrandTranslations_BrandId_Locale");
        builder.HasOne<Brand>()
            .WithMany()
            .HasForeignKey(entity => entity.BrandId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CategoryTranslationConfiguration
    : IEntityTypeConfiguration<CategoryTranslation>
{
    public void Configure(EntityTypeBuilder<CategoryTranslation> builder)
    {
        TranslationConfiguration.ConfigureBase(
            builder,
            "CategoryTranslations",
            entity => entity.Locale,
            entity => entity.TranslationStatus,
            entity => entity.ReviewedByAdminUserId,
            entity => entity.ReviewedAtUtc);
        builder.Property(entity => entity.Name).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.Description).HasMaxLength(1000);
        builder.HasIndex(entity => new { entity.CategoryId, entity.Locale })
            .IsUnique()
            .HasDatabaseName("UX_CategoryTranslations_CategoryId_Locale");
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(entity => entity.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductTranslationConfiguration
    : IEntityTypeConfiguration<ProductTranslation>
{
    public void Configure(EntityTypeBuilder<ProductTranslation> builder)
    {
        TranslationConfiguration.ConfigureBase(
            builder,
            "ProductTranslations",
            entity => entity.Locale,
            entity => entity.TranslationStatus,
            entity => entity.ReviewedByAdminUserId,
            entity => entity.ReviewedAtUtc);
        builder.Property(entity => entity.Name).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.Description).HasMaxLength(4000);
        builder.HasIndex(entity => new { entity.ProductId, entity.Locale })
            .IsUnique()
            .HasDatabaseName("UX_ProductTranslations_ProductId_Locale");
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SkuTranslationConfiguration
    : IEntityTypeConfiguration<SkuTranslation>
{
    public void Configure(EntityTypeBuilder<SkuTranslation> builder)
    {
        TranslationConfiguration.ConfigureBase(
            builder,
            "SkuTranslations",
            entity => entity.Locale,
            entity => entity.TranslationStatus,
            entity => entity.ReviewedByAdminUserId,
            entity => entity.ReviewedAtUtc);
        builder.Property(entity => entity.Name).HasMaxLength(160).IsRequired();
        builder.HasIndex(entity => new { entity.SkuId, entity.Locale })
            .IsUnique()
            .HasDatabaseName("UX_SkuTranslations_SkuId_Locale");
        builder.HasOne<Sku>()
            .WithMany()
            .HasForeignKey(entity => entity.SkuId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SpecificationDefinitionTranslationConfiguration
    : IEntityTypeConfiguration<SpecificationDefinitionTranslation>
{
    public void Configure(EntityTypeBuilder<SpecificationDefinitionTranslation> builder)
    {
        TranslationConfiguration.ConfigureBase(
            builder,
            "SpecificationDefinitionTranslations",
            entity => entity.Locale,
            entity => entity.TranslationStatus,
            entity => entity.ReviewedByAdminUserId,
            entity => entity.ReviewedAtUtc);
        builder.Property(entity => entity.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.HelpText).HasMaxLength(500);
        builder.HasIndex(entity => new
        {
            entity.SpecificationDefinitionId,
            entity.Locale,
        })
            .IsUnique()
            .HasDatabaseName("UX_SpecDefTranslations_DefId_Locale");
        builder.HasOne<SpecificationDefinition>()
            .WithMany()
            .HasForeignKey(entity => entity.SpecificationDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SpecificationOptionTranslationConfiguration
    : IEntityTypeConfiguration<SpecificationOptionTranslation>
{
    public void Configure(EntityTypeBuilder<SpecificationOptionTranslation> builder)
    {
        TranslationConfiguration.ConfigureBase(
            builder,
            "SpecificationOptionTranslations",
            entity => entity.Locale,
            entity => entity.TranslationStatus,
            entity => entity.ReviewedByAdminUserId,
            entity => entity.ReviewedAtUtc);
        builder.Property(entity => entity.DisplayName).HasMaxLength(160).IsRequired();
        builder.HasIndex(entity => new { entity.SpecificationOptionId, entity.Locale })
            .IsUnique()
            .HasDatabaseName("UX_SpecOptTranslations_OptId_Locale");
        builder.HasOne<SpecificationOption>()
            .WithMany()
            .HasForeignKey(entity => entity.SpecificationOptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal static class TranslationConfiguration
{
    private static readonly ValueConverter<SupportedLocale, string> LocaleConverter =
        new(
            locale => locale == SupportedLocale.ZhTw
                ? "zh-TW"
                : locale == SupportedLocale.JaJp
                    ? "ja-JP"
                    : "ko-KR",
            code => code == "zh-TW"
                ? SupportedLocale.ZhTw
                : code == "ja-JP"
                    ? SupportedLocale.JaJp
                    : SupportedLocale.KoKr);

    public static void ConfigureBase<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        string tableName,
        Expression<Func<TEntity, SupportedLocale>> locale,
        Expression<Func<TEntity, TranslationStatus>> status,
        Expression<Func<TEntity, string?>> reviewer,
        Expression<Func<TEntity, DateTime?>> reviewedAt)
        where TEntity : MutableEntity
    {
        builder.ConfigureMutableEntity(tableName);
        builder.Property(locale)
            .HasConversion(LocaleConverter)
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(reviewer).HasMaxLength(450);
        builder.Property(reviewedAt).HasPrecision(3);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(GetPropertyName(reviewer))
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(tableName, table =>
            table.HasCheckConstraint(
                $"CK_{tableName}_Locale",
                "[Locale] IN ('zh-TW','ja-JP','ko-KR')"));
    }

    private static string GetPropertyName<TEntity, TProperty>(
        Expression<Func<TEntity, TProperty>> expression) =>
        expression.Body is MemberExpression member
            ? member.Member.Name
            : throw new ArgumentException("A property expression is required.", nameof(expression));
}
