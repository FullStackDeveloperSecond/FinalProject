using DoSelect.Domain.Common;
using DoSelect.Domain.Members;

namespace DoSelect.Domain.Catalog;

public sealed class BrandTranslation : MutableEntity
{
    private BrandTranslation() { }

    public BrandTranslation(
        long brandId,
        SupportedLocale locale,
        string name,
        string? description,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        BrandId = CatalogGuard.Id(brandId, nameof(brandId));
        Locale = locale;
        Name = RequireText(name, nameof(name));
        Description = CatalogText.Optional(description);
        TranslationStatus = TranslationStatus.MachineDraft;
    }

    public long BrandId { get; private set; }
    public SupportedLocale Locale { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public TranslationStatus TranslationStatus { get; private set; }
    public string? ReviewedByAdminUserId { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }

    public void ReplaceDraft(string name, string? description, DateTime updatedAtUtc)
    {
        Name = RequireText(name, nameof(name));
        Description = CatalogText.Optional(description);
        TranslationStatus = TranslationStatus.MachineDraft;
        ReviewedByAdminUserId = null;
        ReviewedAtUtc = null;
        MarkUpdated(updatedAtUtc);
    }

    public void Review(string adminUserId, bool publish, DateTime reviewedAtUtc)
    {
        ReviewedByAdminUserId = RequireText(adminUserId, nameof(adminUserId));
        ReviewedAtUtc = reviewedAtUtc.Kind == DateTimeKind.Utc
            ? reviewedAtUtc
            : throw new ArgumentException("The value must use UTC.", nameof(reviewedAtUtc));
        TranslationStatus = publish ? TranslationStatus.Published : TranslationStatus.Reviewed;
        MarkUpdated(reviewedAtUtc);
    }
}

public sealed class CategoryTranslation : MutableEntity
{
    private CategoryTranslation() { }

    public CategoryTranslation(
        long categoryId,
        SupportedLocale locale,
        string name,
        string? description,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        CategoryId = CatalogGuard.Id(categoryId, nameof(categoryId));
        Locale = locale;
        Name = RequireText(name, nameof(name));
        Description = CatalogText.Optional(description);
        TranslationStatus = TranslationStatus.MachineDraft;
    }

    public long CategoryId { get; private set; }
    public SupportedLocale Locale { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public TranslationStatus TranslationStatus { get; private set; }
    public string? ReviewedByAdminUserId { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }

    public void Review(string adminUserId, bool publish, DateTime reviewedAtUtc)
    {
        ReviewedByAdminUserId = RequireText(adminUserId, nameof(adminUserId));
        ReviewedAtUtc = RequireUtc(reviewedAtUtc, nameof(reviewedAtUtc));
        TranslationStatus = publish ? TranslationStatus.Published : TranslationStatus.Reviewed;
        MarkUpdated(reviewedAtUtc);
    }
}

public sealed class ProductTranslation : MutableEntity
{
    private ProductTranslation() { }

    public ProductTranslation(
        long productId,
        SupportedLocale locale,
        string name,
        string? description,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        ProductId = CatalogGuard.Id(productId, nameof(productId));
        Locale = locale;
        Name = RequireText(name, nameof(name));
        Description = CatalogText.Optional(description);
        TranslationStatus = TranslationStatus.MachineDraft;
    }

    public long ProductId { get; private set; }
    public SupportedLocale Locale { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public TranslationStatus TranslationStatus { get; private set; }
    public string? ReviewedByAdminUserId { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }

    public void Review(string adminUserId, bool publish, DateTime reviewedAtUtc)
    {
        ReviewedByAdminUserId = RequireText(adminUserId, nameof(adminUserId));
        ReviewedAtUtc = RequireUtc(reviewedAtUtc, nameof(reviewedAtUtc));
        TranslationStatus = publish ? TranslationStatus.Published : TranslationStatus.Reviewed;
        MarkUpdated(reviewedAtUtc);
    }
}

public sealed class SkuTranslation : MutableEntity
{
    private SkuTranslation() { }

    public SkuTranslation(
        long skuId,
        SupportedLocale locale,
        string name,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        SkuId = CatalogGuard.Id(skuId, nameof(skuId));
        Locale = locale;
        Name = RequireText(name, nameof(name));
        TranslationStatus = TranslationStatus.MachineDraft;
    }

    public long SkuId { get; private set; }
    public SupportedLocale Locale { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public TranslationStatus TranslationStatus { get; private set; }
    public string? ReviewedByAdminUserId { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }

    public void Review(string adminUserId, bool publish, DateTime reviewedAtUtc)
    {
        ReviewedByAdminUserId = RequireText(adminUserId, nameof(adminUserId));
        ReviewedAtUtc = RequireUtc(reviewedAtUtc, nameof(reviewedAtUtc));
        TranslationStatus = publish ? TranslationStatus.Published : TranslationStatus.Reviewed;
        MarkUpdated(reviewedAtUtc);
    }
}

public sealed class SpecificationDefinitionTranslation : MutableEntity
{
    private SpecificationDefinitionTranslation() { }

    public SpecificationDefinitionTranslation(
        long specificationDefinitionId,
        SupportedLocale locale,
        string displayName,
        string? helpText,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        SpecificationDefinitionId = CatalogGuard.Id(
            specificationDefinitionId,
            nameof(specificationDefinitionId));
        Locale = locale;
        DisplayName = RequireText(displayName, nameof(displayName));
        HelpText = CatalogText.Optional(helpText);
        TranslationStatus = TranslationStatus.MachineDraft;
    }

    public long SpecificationDefinitionId { get; private set; }
    public SupportedLocale Locale { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string? HelpText { get; private set; }
    public TranslationStatus TranslationStatus { get; private set; }
    public string? ReviewedByAdminUserId { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }

    public void Review(string adminUserId, bool publish, DateTime reviewedAtUtc)
    {
        ReviewedByAdminUserId = RequireText(adminUserId, nameof(adminUserId));
        ReviewedAtUtc = RequireUtc(reviewedAtUtc, nameof(reviewedAtUtc));
        TranslationStatus = publish ? TranslationStatus.Published : TranslationStatus.Reviewed;
        MarkUpdated(reviewedAtUtc);
    }
}

public sealed class SpecificationOptionTranslation : MutableEntity
{
    private SpecificationOptionTranslation() { }

    public SpecificationOptionTranslation(
        long specificationOptionId,
        SupportedLocale locale,
        string displayName,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        SpecificationOptionId = CatalogGuard.Id(
            specificationOptionId,
            nameof(specificationOptionId));
        Locale = locale;
        DisplayName = RequireText(displayName, nameof(displayName));
        TranslationStatus = TranslationStatus.MachineDraft;
    }

    public long SpecificationOptionId { get; private set; }
    public SupportedLocale Locale { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public TranslationStatus TranslationStatus { get; private set; }
    public string? ReviewedByAdminUserId { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }

    public void Review(string adminUserId, bool publish, DateTime reviewedAtUtc)
    {
        ReviewedByAdminUserId = RequireText(adminUserId, nameof(adminUserId));
        ReviewedAtUtc = RequireUtc(reviewedAtUtc, nameof(reviewedAtUtc));
        TranslationStatus = publish ? TranslationStatus.Published : TranslationStatus.Reviewed;
        MarkUpdated(reviewedAtUtc);
    }
}
