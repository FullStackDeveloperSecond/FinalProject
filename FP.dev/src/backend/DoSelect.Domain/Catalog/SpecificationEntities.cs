using DoSelect.Domain.Common;

namespace DoSelect.Domain.Catalog;

public sealed class ProductImage : MutablePublicEntity
{
    private ProductImage() { }

    public ProductImage(
        Guid publicId,
        long productId,
        long? skuId,
        string storageKey,
        string originalFileName,
        string mediaType,
        long fileSizeBytes,
        int width,
        int height,
        byte[] sha256,
        string altTextZhTw,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (fileSizeBytes is < 1 or > 10 * 1024 * 1024 || width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSizeBytes));
        }

        ProductId = CatalogGuard.Id(productId, nameof(productId));
        SkuId = CatalogGuard.OptionalId(skuId, nameof(skuId));
        StorageKey = RequireText(storageKey, nameof(storageKey));
        OriginalFileName = RequireText(originalFileName, nameof(originalFileName));
        MediaType = RequireText(mediaType, nameof(mediaType));
        FileSizeBytes = fileSizeBytes;
        Width = width;
        Height = height;
        Sha256 = CatalogHash.Copy(sha256, nameof(sha256));
        AltTextZhTw = RequireText(altTextZhTw, nameof(altTextZhTw));
        Status = ProductImageStatus.Processing;
    }

    public long ProductId { get; private set; }
    public long? SkuId { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string MediaType { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public byte[] Sha256 { get; private set; } = [];
    public string AltTextZhTw { get; private set; } = string.Empty;
    public string? SourceUrl { get; private set; }
    public string? LicenseUrl { get; private set; }
    public string? AuthorName { get; private set; }
    public string? LicenseName { get; private set; }
    public DateTime? DownloadedAtUtc { get; private set; }
    public ProductImageStatus Status { get; private set; }
    public int SortOrder { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    public void SetExternalAttribution(
        string sourceUrl,
        string licenseUrl,
        string authorName,
        string licenseName,
        DateTime downloadedAtUtc,
        DateTime updatedAtUtc)
    {
        SourceUrl = RequireText(sourceUrl, nameof(sourceUrl));
        LicenseUrl = RequireText(licenseUrl, nameof(licenseUrl));
        AuthorName = RequireText(authorName, nameof(authorName));
        LicenseName = RequireText(licenseName, nameof(licenseName));
        DownloadedAtUtc = RequireUtc(downloadedAtUtc, nameof(downloadedAtUtc));
        MarkUpdated(updatedAtUtc);
    }

    public void Publish(DateTime publishedAtUtc)
    {
        publishedAtUtc = RequireUtc(publishedAtUtc, nameof(publishedAtUtc));
        Status = ProductImageStatus.Published;
        PublishedAtUtc = publishedAtUtc;
        MarkUpdated(publishedAtUtc);
    }

    public void MarkDeleted(DateTime deletedAtUtc)
    {
        deletedAtUtc = RequireUtc(deletedAtUtc, nameof(deletedAtUtc));
        Status = ProductImageStatus.Deleted;
        DeletedAtUtc = deletedAtUtc;
        MarkUpdated(deletedAtUtc);
    }
}

public sealed class MeasurementUnit : MutablePublicEntity
{
    private MeasurementUnit() { }

    public MeasurementUnit(
        Guid publicId,
        string code,
        string nameZhTw,
        string symbol,
        string dimension,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        Code = CatalogCode.Normalize(code);
        NameZhTw = RequireText(nameZhTw, nameof(nameZhTw));
        Symbol = RequireText(symbol, nameof(symbol));
        Dimension = RequireText(dimension, nameof(dimension));
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string NameZhTw { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }
    public string Symbol { get; private set; } = string.Empty;
    public string Dimension { get; private set; } = string.Empty;
}

public sealed class SpecificationDefinition : MutablePublicEntity
{
    private SpecificationDefinition() { }

    public SpecificationDefinition(
        Guid publicId,
        long categoryId,
        string semanticKey,
        string displayNameZhTw,
        SpecificationValueType valueType,
        long? measurementUnitId,
        bool isRequired,
        bool isProtected,
        int sortOrder,
        DateTime createdAtUtc,
        bool allowsMultiple = false)
        : base(publicId, createdAtUtc)
    {
        if (valueType != SpecificationValueType.Decimal && measurementUnitId.HasValue)
        {
            throw new ArgumentException(
                "Only decimal definitions may use a measurement unit.",
                nameof(measurementUnitId));
        }

        if (allowsMultiple && valueType != SpecificationValueType.Option)
        {
            throw new ArgumentException(
                "Only option definitions may allow multiple selections.",
                nameof(allowsMultiple));
        }

        CategoryId = CatalogGuard.Id(categoryId, nameof(categoryId));
        SemanticKey = CatalogCode.Normalize(semanticKey);
        DisplayNameZhTw = RequireText(displayNameZhTw, nameof(displayNameZhTw));
        ValueType = valueType;
        MeasurementUnitId = CatalogGuard.OptionalId(
            measurementUnitId,
            nameof(measurementUnitId));
        IsRequired = isRequired;
        IsProtected = isProtected;
        AllowsMultiple = allowsMultiple;
        IsActive = true;
        SortOrder = sortOrder;
    }

    public long CategoryId { get; private set; }
    public string SemanticKey { get; private set; } = string.Empty;
    public string DisplayNameZhTw { get; private set; } = string.Empty;
    public SpecificationValueType ValueType { get; private set; }
    public long? MeasurementUnitId { get; private set; }
    public bool IsRequired { get; private set; }
    public bool IsProtected { get; private set; }
    public bool AllowsMultiple { get; private set; }
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }
}

public sealed class SpecificationOption : MutablePublicEntity
{
    private SpecificationOption() { }

    public SpecificationOption(
        Guid publicId,
        long specificationDefinitionId,
        string code,
        string displayNameZhTw,
        int sortOrder,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        SpecificationDefinitionId = CatalogGuard.Id(
            specificationDefinitionId,
            nameof(specificationDefinitionId));
        Code = CatalogCode.Normalize(code);
        DisplayNameZhTw = RequireText(displayNameZhTw, nameof(displayNameZhTw));
        IsActive = true;
        SortOrder = sortOrder;
    }

    public long SpecificationDefinitionId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string DisplayNameZhTw { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }
}

public sealed class SpecificationSource : MutablePublicEntity
{
    private SpecificationSource() { }

    public SpecificationSource(
        Guid publicId,
        SpecificationSourceType sourceType,
        string providerName,
        string sourceUrl,
        string? originalFieldName,
        DateTime retrievedAtUtc,
        DateTime reviewedAtUtc,
        string reviewedByAdminUserId,
        string sourceVersion,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        SourceType = sourceType;
        ProviderName = RequireText(providerName, nameof(providerName));
        SourceUrl = RequireText(sourceUrl, nameof(sourceUrl));
        OriginalFieldName = CatalogText.Optional(originalFieldName);
        RetrievedAtUtc = RequireUtc(retrievedAtUtc, nameof(retrievedAtUtc));
        ReviewedAtUtc = RequireUtc(reviewedAtUtc, nameof(reviewedAtUtc));
        ReviewedByAdminUserId = RequireText(
            reviewedByAdminUserId,
            nameof(reviewedByAdminUserId));
        SourceVersion = RequireText(sourceVersion, nameof(sourceVersion));
    }

    public SpecificationSourceType SourceType { get; private set; }
    public string ProviderName { get; private set; } = string.Empty;
    public string SourceUrl { get; private set; } = string.Empty;
    public string? OriginalFieldName { get; private set; }
    public DateTime RetrievedAtUtc { get; private set; }
    public DateTime ReviewedAtUtc { get; private set; }
    public string ReviewedByAdminUserId { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public string SourceVersion { get; private set; } = string.Empty;
}

public sealed class SkuSpecificationValue : MutableEntity
{
    private SkuSpecificationValue() { }

    public SkuSpecificationValue(
        long skuId,
        long specificationDefinitionId,
        string? stringValue,
        decimal? decimalValue,
        bool? booleanValue,
        long? optionId,
        long? specificationSourceId,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        var normalizedStringValue = CatalogText.Optional(stringValue);
        var populated = new[]
        {
            normalizedStringValue is not null,
            decimalValue.HasValue,
            booleanValue.HasValue,
            optionId.HasValue,
        }.Count(value => value);
        if (populated != 1)
        {
            throw new ArgumentException("Exactly one specification value is required.");
        }

        SkuId = CatalogGuard.Id(skuId, nameof(skuId));
        SpecificationDefinitionId = CatalogGuard.Id(
            specificationDefinitionId,
            nameof(specificationDefinitionId));
        StringValue = normalizedStringValue;
        DecimalValue = decimalValue;
        BooleanValue = booleanValue;
        OptionId = CatalogGuard.OptionalId(optionId, nameof(optionId));
        SpecificationSourceId = CatalogGuard.OptionalId(
            specificationSourceId,
            nameof(specificationSourceId));
    }

    public long SkuId { get; private set; }
    public long SpecificationDefinitionId { get; private set; }
    public string? StringValue { get; private set; }
    public decimal? DecimalValue { get; private set; }
    public bool? BooleanValue { get; private set; }
    public long? OptionId { get; private set; }
    public long? SpecificationSourceId { get; private set; }
}

public sealed class SkuSpecificationOptionSelection : MutableEntity
{
    private SkuSpecificationOptionSelection() { }

    public SkuSpecificationOptionSelection(
        long skuId,
        long specificationOptionId,
        DateTime createdAtUtc,
        long? specificationSourceId = null)
        : base(createdAtUtc)
    {
        SkuId = CatalogGuard.Id(skuId, nameof(skuId));
        SpecificationOptionId = CatalogGuard.Id(
            specificationOptionId,
            nameof(specificationOptionId));
        SpecificationSourceId = CatalogGuard.OptionalId(
            specificationSourceId,
            nameof(specificationSourceId));
    }

    public long SkuId { get; private set; }
    public long SpecificationOptionId { get; private set; }
    public long? SpecificationSourceId { get; private set; }
}

public sealed class SalePrice : MutablePublicEntity
{
    private SalePrice() { }

    public SalePrice(
        Guid publicId,
        long skuId,
        decimal price,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        string createdByAdminUserId,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        startsAtUtc = RequireUtc(startsAtUtc, nameof(startsAtUtc));
        endsAtUtc = RequireUtc(endsAtUtc, nameof(endsAtUtc));
        if (endsAtUtc <= startsAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(endsAtUtc));
        }

        SkuId = CatalogGuard.Id(skuId, nameof(skuId));
        Price = CatalogGuard.NonNegative(price, nameof(price));
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        Status = SalePriceStatus.Draft;
        CreatedByAdminUserId = RequireText(
            createdByAdminUserId,
            nameof(createdByAdminUserId));
    }

    public long SkuId { get; private set; }
    public decimal Price { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public SalePriceStatus Status { get; private set; }
    public string CreatedByAdminUserId { get; private set; } = string.Empty;

    public void ChangeStatus(SalePriceStatus status, DateTime updatedAtUtc)
    {
        Status = status;
        MarkUpdated(updatedAtUtc);
    }
}

internal static class CatalogHash
{
    public static byte[] Copy(byte[] value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length != 32)
        {
            throw new ArgumentException("The hash must contain 32 bytes.", parameterName);
        }

        return value.ToArray();
    }
}
