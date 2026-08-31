namespace DoSelect.Application.Inventory;

public sealed class InventoryWriteException : Exception
{
    public InventoryWriteException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }

    public static class ErrorCodes
    {
        public const string InsufficientStock = "inventory_insufficient";
        public const string ReservationNotActive = "inventory_reservation_not_active";
        public const string ReservationAlreadyProcessed = "inventory_reservation_already_processed";
        public const string ReconciliationCaseNotOpen = "inventory_reconciliation_case_not_open";
        public const string ConcurrencyConflict = "concurrency_conflict";
        public const string ResourceNotFound = "resource_not_found";
        public const string ValidationFailed = "validation_failed";
    }
}
