namespace DoSelect.Application.Shopping;

public sealed class ShoppingWriteException : Exception
{
    public ShoppingWriteException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }

    public static class ErrorCodes
    {
        public const string SkuUnavailable = "sku_unavailable";
        public const string CartQuantityExceeded = "cart_quantity_exceeded";

        /// <summary>
        /// Not in API錯誤碼目錄.md — CartDto.items is documented as [0..100] (API DTO與Schema契約.md)
        /// but no error code exists for exceeding it. Added to close the gap 組長 flagged on PR
        /// #28 review; flagged for 組長 to confirm the name/status code.
        /// </summary>
        public const string CartItemLimitExceeded = "cart_item_limit_exceeded";

        public const string CartItemRequiresAttention = "cart_item_requires_attention";
        public const string CartMergeConflict = "cart_merge_conflict";
        public const string ConcurrencyConflict = "concurrency_conflict";
        public const string ResourceNotFound = "resource_not_found";
        public const string ValidationFailed = "validation_failed";

        /// <summary>
        /// 組長 PR #29 round-6 review, P1: an item that belongs to an assembly group
        /// (AssemblyGroupKey non-null) represents one SKU of one physical build — 商品、組裝與相容性.md's
        /// "組裝電腦...底層保留每一個 SKU" is describing display, not independence. Changing one
        /// group member's quantity or removing it alone would leave the rest of the group (still
        /// under the same AssemblyGroupKey, still billed one NT$300 assembly fee) referring to a
        /// build that no longer matches what was actually configured. The frontend already refuses
        /// to offer per-item controls for a grouped item; this is the same rule enforced server-side
        /// so no other client can bypass it.
        /// </summary>
        public const string CartAssemblyItemImmutable = "cart_assembly_item_immutable";
    }
}
