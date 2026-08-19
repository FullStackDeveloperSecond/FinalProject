namespace DoSelect.Application.Catalog;

public static class ProductSearchQueryValidator
{
    public static string NormalizeSort(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return ProductSortOptions.Relevance;
        }

        var trimmed = sort.Trim();
        foreach (var candidate in ProductSortOptions.All)
        {
            if (string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new CatalogSearchException(
            CatalogSearchException.ErrorCodes.SortUnsupported,
            $"The sort value '{sort}' is not supported.");
    }

    public static SpecFilterOperator ParseOperator(string? operatorToken)
    {
        if (string.IsNullOrWhiteSpace(operatorToken))
        {
            throw new CatalogSearchException(
                CatalogSearchException.ErrorCodes.FilterUnsupported,
                "A spec filter operator is required.");
        }

        return operatorToken.Trim().ToLowerInvariant() switch
        {
            SpecFilterOperatorCodes.Eq => SpecFilterOperator.Eq,
            SpecFilterOperatorCodes.Gte => SpecFilterOperator.Gte,
            SpecFilterOperatorCodes.Lte => SpecFilterOperator.Lte,
            SpecFilterOperatorCodes.In => SpecFilterOperator.In,
            _ => throw new CatalogSearchException(
                CatalogSearchException.ErrorCodes.FilterUnsupported,
                $"The spec filter operator '{operatorToken}' is not supported."),
        };
    }
}
