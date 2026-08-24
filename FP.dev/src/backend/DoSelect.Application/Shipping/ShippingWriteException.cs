namespace DoSelect.Application.Shipping;

public sealed class ShippingWriteException : Exception
{
    public ShippingWriteException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }

    public static class ErrorCodes
    {
        public const string StoreCodeDuplicate = "store_code_duplicate";
        public const string PackageLimitPeriodOverlap = "package_limit_period_overlap";
        public const string ShippingBatchLimitExceeded = "shipping_batch_limit_exceeded";
        public const string ShippingOrderNotReady = "shipping_order_not_ready";
        public const string ShippingTrackingDuplicate = "shipping_tracking_duplicate";
        public const string ShippingMethodNotAllowed = "shipping_method_not_allowed";
        public const string ConcurrencyConflict = "concurrency_conflict";
        public const string ResourceNotFound = "resource_not_found";
        public const string ValidationFailed = "validation_failed";
    }
}
