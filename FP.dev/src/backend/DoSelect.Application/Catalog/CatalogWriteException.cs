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

        /// <summary>
        /// 組長 PR #24 round 4 review: unsetting or deleting the SKU that's currently a
        /// product's default must not be allowed to leave the product with zero default SKUs —
        /// only ever thrown when the *current* default SKU is the target and the request would
        /// leave it (or the product, on delete) without one. Designate a different SKU as
        /// default first (that path already atomically clears the old one via
        /// EfSkuAdminService.ClearExistingDefaultAsync); this rejects trying to go the other way.
        /// </summary>
        public const string SkuDefaultRequired = "sku_default_required";

        /// <summary>組長 PR #24 round 4 review: a Published SKU must have a value for every
        /// IsRequired specification its category defines.</summary>
        public const string SkuMissingRequiredSpecification = "sku_missing_required_specification";
        public const string SpecificationInvalid = "specification_invalid";

        /// <summary>API錯誤碼目錄：同一分類下的規格語意鍵重複（409）。</summary>
        public const string SpecificationSemanticKeyDuplicate = "specification_semantic_key_duplicate";

        /// <summary>
        /// API錯誤碼目錄：「規格定義已被商品、搜尋、匯入或相容性規則引用，只能停用」（409）。
        /// 這個 API 沒有刪除端點，實際會觸發的情境是「停用固定相容性引擎依賴的受保護定義」——
        /// 停掉它會讓該分類的硬性相容規則失去必要欄位。
        /// </summary>
        public const string SpecificationDefinitionReferenced = "specification_definition_referenced";
        public const string ConcurrencyConflict = "concurrency_conflict";
        public const string ResourceNotFound = "resource_not_found";
        public const string CategoryParentInvalid = "category_parent_invalid";
        public const string ReferenceNotFound = "reference_not_found";
        public const string ValidationFailed = "validation_failed";
    }
}
