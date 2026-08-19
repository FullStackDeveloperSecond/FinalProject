using System.Text;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

public sealed class EfCategoryAdminService : ICategoryAdminService
{
    private readonly DoSelectDbContext _dbContext;

    public EfCategoryAdminService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PageResult<CategoryDto>> ListAsync(
        CatalogLookupQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var categories = _dbContext.Categories.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var keyword = query.Q.Trim();
            categories = categories.Where(category =>
                category.Code.Contains(keyword) || category.NameZhTw.Contains(keyword));
        }

        if (query.IsActive.HasValue)
        {
            categories = categories.Where(category => category.IsActive == query.IsActive.Value);
        }

        var totalCount = await categories.CountAsync(cancellationToken);

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var entities = await categories
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Code)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var parentIds = entities
            .Where(category => category.ParentCategoryId.HasValue)
            .Select(category => category.ParentCategoryId!.Value)
            .Distinct()
            .ToArray();
        var parentPublicIds = await _dbContext.Categories.AsNoTracking()
            .Where(category => parentIds.Contains(category.Id))
            .ToDictionaryAsync(category => category.Id, category => category.PublicId, cancellationToken);

        return new PageResult<CategoryDto>(
            entities.Select(category => ToDto(category, parentPublicIds)).ToList(),
            pageNumber,
            pageSize,
            totalCount);
    }

    public async Task<CategoryDto> CreateAsync(
        CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedCode = NormalizeCode(request.Code);
        var codeExists = await _dbContext.Categories.AsNoTracking()
            .AnyAsync(category => category.Code == normalizedCode, cancellationToken);
        if (codeExists)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.CategoryCodeDuplicate,
                $"Category code '{request.Code}' already exists.");
        }

        var parentId = await ResolveParentIdAsync(request.ParentCategoryPublicId, null, cancellationToken);

        var now = DateTime.UtcNow;
        var category = new Category(
            Guid.CreateVersion7(),
            request.Code,
            request.Slug,
            request.NameZhTw,
            parentId,
            now);
        category.UpdateDetails(request.NameZhTw, request.Slug, request.Description, request.SortOrder, now);
        if (!request.IsActive)
        {
            category.SetActive(false, now);
        }

        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var parentPublicId = request.ParentCategoryPublicId;
        return ToDto(category, parentPublicId);
    }

    public async Task<CategoryDto> UpdateAsync(
        Guid publicId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var category = await _dbContext.Categories
            .FirstOrDefaultAsync(candidate => candidate.PublicId == publicId, cancellationToken);
        if (category is null)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ResourceNotFound,
                $"Category '{publicId}' was not found.");
        }

        var parentId = await ResolveParentIdAsync(request.ParentCategoryPublicId, category.Id, cancellationToken);

        _dbContext.Entry(category).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;

        var now = DateTime.UtcNow;
        category.UpdateDetails(request.NameZhTw, request.Slug, request.Description, request.SortOrder, now);
        category.MoveTo(parentId, now);
        category.SetActive(request.IsActive, now);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ConcurrencyConflict,
                "The category was updated by someone else. Reload and try again.");
        }

        return ToDto(category, request.ParentCategoryPublicId);
    }

    private async Task<long?> ResolveParentIdAsync(
        Guid? parentPublicId,
        long? selfId,
        CancellationToken cancellationToken)
    {
        if (parentPublicId is null)
        {
            return null;
        }

        var parent = await _dbContext.Categories.AsNoTracking()
            .Where(category => category.PublicId == parentPublicId.Value)
            .Select(category => new { category.Id })
            .FirstOrDefaultAsync(cancellationToken);
        if (parent is null)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.CategoryParentInvalid,
                $"Parent category '{parentPublicId}' was not found.");
        }

        if (selfId.HasValue && parent.Id == selfId.Value)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.CategoryParentInvalid,
                "A category cannot be its own parent.");
        }

        return parent.Id;
    }

    private static string NormalizeCode(string value) =>
        value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();

    private static CategoryDto ToDto(Category category, IReadOnlyDictionary<long, Guid> parentPublicIds)
    {
        Guid? parentPublicId = category.ParentCategoryId.HasValue &&
            parentPublicIds.TryGetValue(category.ParentCategoryId.Value, out var value)
                ? value
                : null;
        return ToDto(category, parentPublicId);
    }

    private static CategoryDto ToDto(Category category, Guid? parentPublicId) => new(
        category.PublicId,
        category.Code,
        category.NameZhTw,
        category.Slug,
        category.Description,
        parentPublicId,
        category.IsActive,
        category.SortOrder,
        category.RowVersion);
}
