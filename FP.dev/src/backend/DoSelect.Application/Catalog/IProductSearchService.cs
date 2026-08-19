using DoSelect.Application.Common;

namespace DoSelect.Application.Catalog;

public interface IProductSearchService
{
    Task<PageResult<ProductCardDto>> SearchAsync(
        ProductSearchQuery query,
        CancellationToken cancellationToken);
}
