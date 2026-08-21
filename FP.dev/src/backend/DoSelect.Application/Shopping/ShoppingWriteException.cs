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
        public const string CartItemRequiresAttention = "cart_item_requires_attention";
        public const string CartMergeConflict = "cart_merge_conflict";
        public const string ConcurrencyConflict = "concurrency_conflict";
        public const string ResourceNotFound = "resource_not_found";
        public const string ValidationFailed = "validation_failed";
        public const string IdempotencyPayloadConflict = "idempotency_payload_conflict";
    }
}
