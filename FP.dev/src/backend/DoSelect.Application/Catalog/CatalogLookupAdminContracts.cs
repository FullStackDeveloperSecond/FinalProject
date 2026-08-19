using DoSelect.Application.Common;

namespace DoSelect.Application.Catalog;

public sealed record CatalogLookupQuery(string? Q, bool? IsActive, int PageNumber, int PageSize);

public sealed record CatalogLookupDto(
    Guid PublicId,
    string Code,
    string NameZhTw,
    bool IsActive,
    int SortOrder,
    byte[] RowVersion);

public sealed record BrandDto(
    Guid PublicId,
    string Code,
    string NameZhTw,
    string? Description,
    string? WebsiteUrl,
    bool IsActive,
    int SortOrder,
    byte[] RowVersion);

public sealed record CreateBrandRequest(
    string Code,
    string NameZhTw,
    string? Description,
    string? WebsiteUrl,
    int SortOrder,
    bool IsActive);

public sealed record UpdateBrandRequest(
    string NameZhTw,
    string? Description,
    string? WebsiteUrl,
    int SortOrder,
    bool IsActive,
    byte[] RowVersion);

public interface IBrandAdminService
{
    Task<PageResult<BrandDto>> ListAsync(CatalogLookupQuery query, CancellationToken cancellationToken);

    Task<BrandDto> CreateAsync(CreateBrandRequest request, CancellationToken cancellationToken);

    Task<BrandDto> UpdateAsync(Guid publicId, UpdateBrandRequest request, CancellationToken cancellationToken);
}

public sealed record CategoryDto(
    Guid PublicId,
    string Code,
    string NameZhTw,
    string Slug,
    string? Description,
    Guid? ParentCategoryPublicId,
    bool IsActive,
    int SortOrder,
    byte[] RowVersion);

public sealed record CreateCategoryRequest(
    string Code,
    string NameZhTw,
    string Slug,
    string? Description,
    Guid? ParentCategoryPublicId,
    int SortOrder,
    bool IsActive);

public sealed record UpdateCategoryRequest(
    string NameZhTw,
    string Slug,
    string? Description,
    Guid? ParentCategoryPublicId,
    int SortOrder,
    bool IsActive,
    byte[] RowVersion);

public interface ICategoryAdminService
{
    Task<PageResult<CategoryDto>> ListAsync(CatalogLookupQuery query, CancellationToken cancellationToken);

    Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken);

    Task<CategoryDto> UpdateAsync(Guid publicId, UpdateCategoryRequest request, CancellationToken cancellationToken);
}

public sealed record CreateTagRequest(string Code, string NameZhTw, int SortOrder, bool IsActive);

public sealed record UpdateTagRequest(string NameZhTw, int SortOrder, bool IsActive, byte[] RowVersion);

public interface ITagAdminService
{
    Task<PageResult<CatalogLookupDto>> ListAsync(CatalogLookupQuery query, CancellationToken cancellationToken);

    Task<CatalogLookupDto> CreateAsync(CreateTagRequest request, CancellationToken cancellationToken);

    Task<CatalogLookupDto> UpdateAsync(Guid publicId, UpdateTagRequest request, CancellationToken cancellationToken);
}
