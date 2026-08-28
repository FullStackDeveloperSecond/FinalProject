namespace DoSelect.Application.Orders;

public sealed class OrderWriteException : Exception
{
    public OrderWriteException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }

    /// <summary>Matches API錯誤碼目錄.md's order/return sections.</summary>
    public static class ErrorCodes
    {
        public const string ResourceNotFound = "resource_not_found";
        public const string ValidationFailed = "validation_failed";
        public const string OrderStateConflict = "order_state_conflict";
        public const string OrderCancellationNotAllowed = "order_cancellation_not_allowed";
        public const string ConcurrencyConflict = "concurrency_conflict";
    }
}
