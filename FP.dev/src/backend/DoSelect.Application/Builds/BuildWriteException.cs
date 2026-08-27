namespace DoSelect.Application.Builds;

public sealed class BuildWriteException : Exception
{
    public BuildWriteException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }

    public static class ErrorCodes
    {
        public const string ValidationFailed = "validation_failed";
        public const string ResourceNotFound = "resource_not_found";
        public const string ConcurrencyConflict = "concurrency_conflict";
        public const string BuildIncomplete = "build_incomplete";
        public const string BuildIncompatible = "build_incompatible";
        public const string BuildUnavailableItem = "build_unavailable_item";
        public const string InventoryInsufficient = "inventory_insufficient";
        public const string CompatibilityThresholdOutOfRange = "compatibility_threshold_out_of_range";
    }
}
