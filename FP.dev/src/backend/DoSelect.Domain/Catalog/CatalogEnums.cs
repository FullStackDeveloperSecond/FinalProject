namespace DoSelect.Domain.Catalog;

public enum ProductStatus
{
    Draft,
    Published,
    Unpublished,
    Discontinued,
}

public enum SkuStatus
{
    Draft,
    Published,
    Unpublished,
}

public enum TranslationStatus
{
    MachineDraft,
    Reviewed,
    Published,
}

public enum SpecificationValueType
{
    String,
    Decimal,
    Boolean,
    Option,
}

public enum SpecificationSourceType
{
    Manufacturer,
    CuratedReference,
    SystemEstimate,
}

public enum ProductImageStatus
{
    Processing,
    Ready,
    Published,
    Rejected,
    PendingDelete,
    Deleted,
}

public enum SalePriceStatus
{
    Draft,
    Scheduled,
    Active,
    Cancelled,
    Expired,
}
