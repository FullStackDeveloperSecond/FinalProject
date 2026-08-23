namespace DoSelect.Application.Catalog;

public sealed class CatalogWriteException : Exception
{
    public CatalogWriteException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }

    public static class ErrorCodes
    {
        public const string ProductCodeDuplicate = "product_code_duplicate";
        public const string ProductUnavailable = "product_unavailable";
        public const string BrandCodeDuplicate = "brand_code_duplicate";
        public const string CategoryCodeDuplicate = "category_code_duplicate";
        public const string TagCodeDuplicate = "tag_code_duplicate";
        public const string SkuCodeDuplicate = "sku_code_duplicate";
        public const string SkuCodeImmutable = "sku_code_immutable";
        public const string SkuDeleteReferenced = "sku_delete_referenced";
        public const string SpecificationInvalid = "specification_invalid";
        public const string ConcurrencyConflict = "concurrency_conflict";
        public const string ResourceNotFound = "resource_not_found";
        public const string CategoryParentInvalid = "category_parent_invalid";
        public const string ReferenceNotFound = "reference_not_found";
        public const string ValidationFailed = "validation_failed";
    }
}
