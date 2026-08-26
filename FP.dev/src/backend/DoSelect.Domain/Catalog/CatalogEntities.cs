using System.Globalization;
using System.Text;
using DoSelect.Domain.Common;

namespace DoSelect.Domain.Catalog;

public sealed class Brand : MutablePublicEntity
{
    private Brand()
    {
    }

    public Brand(Guid publicId, string code, string nameZhTw, DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        Code = CatalogCode.Normalize(code);
        NameZhTw = RequireText(nameZhTw, nameof(nameZhTw));
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string NameZhTw { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }
    public string? Description { get; private set; }
    public string? WebsiteUrl { get; private set; }

    public void UpdateDetails(
        string nameZhTw,
        string? description,
        string? websiteUrl,
        int sortOrder,
        DateTime updatedAtUtc)
    {
        NameZhTw = RequireText(nameZhTw, nameof(nameZhTw));
        Description = CatalogText.Optional(description);
        WebsiteUrl = CatalogText.Optional(websiteUrl);
        SortOrder = sortOrder;
        MarkUpdated(updatedAtUtc);
    }

    public void SetActive(bool isActive, DateTime updatedAtUtc)
    {
        IsActive = isActive;
        MarkUpdated(updatedAtUtc);
    }
}

public sealed class Category : MutablePublicEntity
{
    private Category()
    {
    }

    public Category(
        Guid publicId,
        string code,
        string slug,
        string nameZhTw,
        long? parentCategoryId,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        Code = CatalogCode.Normalize(code);
        Slug = RequireText(slug, nameof(slug));
        NameZhTw = RequireText(nameZhTw, nameof(nameZhTw));
        ParentCategoryId = CatalogGuard.OptionalId(parentCategoryId, nameof(parentCategoryId));
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string NameZhTw { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }
    public long? ParentCategoryId { get; private set; }
    public string Slug { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    public void UpdateDetails(
        string nameZhTw,
        string slug,
        string? description,
        int sortOrder,
        DateTime updatedAtUtc)
    {
        NameZhTw = RequireText(nameZhTw, nameof(nameZhTw));
        Slug = RequireText(slug, nameof(slug));
        Description = CatalogText.Optional(description);
        SortOrder = sortOrder;
        MarkUpdated(updatedAtUtc);
    }

    public void MoveTo(long? parentCategoryId, DateTime updatedAtUtc)
    {
        ParentCategoryId = CatalogGuard.OptionalId(parentCategoryId, nameof(parentCategoryId));
        MarkUpdated(updatedAtUtc);
    }

    public void SetActive(bool isActive, DateTime updatedAtUtc)
    {
        IsActive = isActive;
        MarkUpdated(updatedAtUtc);
    }
}

public sealed class Product : MutablePublicEntity
{
    private Product()
    {
    }

    public Product(
        Guid publicId,
        string productCode,
        long brandId,
        long categoryId,
        string nameZhTw,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        ProductCode = CatalogCode.Normalize(productCode);
        BrandId = CatalogGuard.Id(brandId, nameof(brandId));
        CategoryId = CatalogGuard.Id(categoryId, nameof(categoryId));
        NameZhTw = RequireText(nameZhTw, nameof(nameZhTw));
        Status = ProductStatus.Draft;
    }

    public string ProductCode { get; private set; } = string.Empty;
    public long BrandId { get; private set; }
    public long CategoryId { get; private set; }
    public string NameZhTw { get; private set; } = string.Empty;
    public string? DescriptionZhTw { get; private set; }
    public int? WarrantyMonths { get; private set; }
    public ProductStatus Status { get; private set; }
    public bool IsFeatured { get; private set; }

    public void UpdateDetails(
        long brandId,
        long categoryId,
        string nameZhTw,
        string? descriptionZhTw,
        int? warrantyMonths,
        bool isFeatured,
        DateTime updatedAtUtc)
    {
        if (warrantyMonths is < 0 or > 120)
        {
            throw new ArgumentOutOfRangeException(nameof(warrantyMonths));
        }

        BrandId = CatalogGuard.Id(brandId, nameof(brandId));
        CategoryId = CatalogGuard.Id(categoryId, nameof(categoryId));
        NameZhTw = RequireText(nameZhTw, nameof(nameZhTw));
        DescriptionZhTw = CatalogText.Optional(descriptionZhTw);
        WarrantyMonths = warrantyMonths;
        IsFeatured = isFeatured;
        MarkUpdated(updatedAtUtc);
    }

    public void ChangeStatus(ProductStatus status, DateTime updatedAtUtc)
    {
        Status = status;
        MarkUpdated(updatedAtUtc);
    }

    /// <summary>
    /// Advances this product's RowVersion when a child Sku's specification values change, even
    /// though nothing on Product itself changed — a category switch (EfProductAdminService.
    /// UpdateAsync) validates against the *current* set of SKU specification values before
    /// committing, and without this, two admins racing a spec edit against a category switch
    /// could both read a consistent-looking state and both succeed, leaving the switch's
    /// validation checked against data that's already stale by the time it commits (組長 PR #24
    /// round 5 review, item 2). Mirrors Cart.Touch's rationale for the same shape of problem.
    /// </summary>
    public void Touch(DateTime updatedAtUtc) => MarkUpdated(updatedAtUtc);
}

public sealed class Sku : MutablePublicEntity
{
    private Sku()
    {
    }

    public Sku(
        Guid publicId,
        string skuCode,
        long productId,
        string nameZhTw,
        decimal listPrice,
        decimal unitCost,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        SkuCode = CatalogCode.Normalize(skuCode);
        ProductId = CatalogGuard.Id(productId, nameof(productId));
        NameZhTw = RequireText(nameZhTw, nameof(nameZhTw));
        ListPrice = CatalogGuard.NonNegative(listPrice, nameof(listPrice));
        UnitCost = CatalogGuard.NonNegative(unitCost, nameof(unitCost));
        Status = SkuStatus.Draft;
    }

    public string SkuCode { get; private set; } = string.Empty;
    public long ProductId { get; private set; }
    public string NameZhTw { get; private set; } = string.Empty;
    public decimal ListPrice { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal? WeightKg { get; private set; }
    public decimal? LengthCm { get; private set; }
    public decimal? WidthCm { get; private set; }
    public decimal? HeightCm { get; private set; }
    public SkuStatus Status { get; private set; }
    public bool IsDefault { get; private set; }
    public bool RequiresPrepayment { get; private set; }

    public void UpdateCommercialDetails(
        string nameZhTw,
        decimal listPrice,
        decimal unitCost,
        bool isDefault,
        bool requiresPrepayment,
        DateTime updatedAtUtc)
    {
        NameZhTw = RequireText(nameZhTw, nameof(nameZhTw));
        ListPrice = CatalogGuard.NonNegative(listPrice, nameof(listPrice));
        UnitCost = CatalogGuard.NonNegative(unitCost, nameof(unitCost));
        IsDefault = isDefault;
        RequiresPrepayment = requiresPrepayment;
        MarkUpdated(updatedAtUtc);
    }

    public void UpdatePackageDimensions(
        decimal? weightKg,
        decimal? lengthCm,
        decimal? widthCm,
        decimal? heightCm,
        DateTime updatedAtUtc)
    {
        WeightKg = CatalogGuard.OptionalPositive(weightKg, nameof(weightKg));
        LengthCm = CatalogGuard.OptionalPositive(lengthCm, nameof(lengthCm));
        WidthCm = CatalogGuard.OptionalPositive(widthCm, nameof(widthCm));
        HeightCm = CatalogGuard.OptionalPositive(heightCm, nameof(heightCm));
        MarkUpdated(updatedAtUtc);
    }

    public void ChangeStatus(SkuStatus status, DateTime updatedAtUtc)
    {
        Status = status;
        MarkUpdated(updatedAtUtc);
    }
}

public sealed class Tag : MutablePublicEntity
{
    private Tag()
    {
    }

    public Tag(Guid publicId, string code, string nameZhTw, DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        Code = CatalogCode.Normalize(code);
        NameZhTw = RequireText(nameZhTw, nameof(nameZhTw));
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string NameZhTw { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }

    public void Update(string nameZhTw, bool isActive, int sortOrder, DateTime updatedAtUtc)
    {
        NameZhTw = RequireText(nameZhTw, nameof(nameZhTw));
        IsActive = isActive;
        SortOrder = sortOrder;
        MarkUpdated(updatedAtUtc);
    }
}

public sealed class ProductTag
{
    private ProductTag()
    {
    }

    public ProductTag(long productId, long tagId, DateTime createdAtUtc)
    {
        ProductId = CatalogGuard.Id(productId, nameof(productId));
        TagId = CatalogGuard.Id(tagId, nameof(tagId));
        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The value must use UTC.", nameof(createdAtUtc));
        }

        CreatedAtUtc = createdAtUtc;
    }

    public long ProductId { get; private set; }
    public long TagId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}

internal static class CatalogCode
{
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The code is required.", nameof(value));
        }

        return value.Trim().Normalize(NormalizationForm.FormKC).ToUpper(CultureInfo.InvariantCulture);
    }
}

internal static class CatalogText
{
    public static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class CatalogGuard
{
    public static long Id(long value, string parameterName) =>
        value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName);

    public static long? OptionalId(long? value, string parameterName) =>
        value.HasValue ? Id(value.Value, parameterName) : null;

    public static decimal NonNegative(decimal value, string parameterName) =>
        value >= 0 ? value : throw new ArgumentOutOfRangeException(parameterName);

    public static decimal? OptionalPositive(decimal? value, string parameterName) =>
        value is null or > 0 ? value : throw new ArgumentOutOfRangeException(parameterName);
}
