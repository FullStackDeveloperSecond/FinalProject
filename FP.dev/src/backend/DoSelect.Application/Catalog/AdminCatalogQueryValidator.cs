namespace DoSelect.Application.Catalog;

public static class AdminCatalogQueryValidator
{
    public static string NormalizeSort(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return AdminProductSortOptions.UpdatedDesc;
        }

        var trimmed = sort.Trim();
        foreach (var candidate in AdminProductSortOptions.All)
        {
            if (string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new CatalogWriteException(
            CatalogWriteException.ErrorCodes.ValidationFailed,
            $"The sort value '{sort}' is not supported.");
    }

    public static string NormalizeStockState(string? stockState)
    {
        if (string.IsNullOrWhiteSpace(stockState))
        {
            return AdminStockStates.Any;
        }

        var trimmed = stockState.Trim();
        foreach (var candidate in AdminStockStates.All)
        {
            if (string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new CatalogWriteException(
            CatalogWriteException.ErrorCodes.ValidationFailed,
            $"The stockState value '{stockState}' is not supported.");
    }

    /// <summary>
    /// Replaces a bare Enum.Parse call that threw an unhandled ArgumentException (500) for
    /// any invalid status token — collects every invalid value instead of stopping at the
    /// first one, so the caller can fix its whole request in one round trip.
    /// </summary>
    public static IReadOnlyList<TStatus> NormalizeStatuses<TStatus>(IReadOnlyList<string>? statuses)
        where TStatus : struct, Enum
    {
        if (statuses is not { Count: > 0 })
        {
            return [];
        }

        var invalid = new List<string>();
        var parsed = new List<TStatus>(statuses.Count);
        foreach (var status in statuses)
        {
            if (Enum.TryParse<TStatus>(status, ignoreCase: true, out var value))
            {
                parsed.Add(value);
            }
            else
            {
                invalid.Add(status);
            }
        }

        if (invalid.Count > 0)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                $"The status value(s) '{string.Join(", ", invalid)}' are not supported.");
        }

        return parsed;
    }
}
