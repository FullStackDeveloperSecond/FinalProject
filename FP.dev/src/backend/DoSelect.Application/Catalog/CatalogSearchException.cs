namespace DoSelect.Application.Catalog;

public sealed class CatalogSearchException : Exception
{
    public CatalogSearchException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }

    public static class ErrorCodes
    {
        public const string SortUnsupported = "search_sort_unsupported";
        public const string FilterUnsupported = "search_filter_unsupported";
    }
}
