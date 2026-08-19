using System.Text;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

public sealed class EfTagAdminService : ITagAdminService
{
    private readonly DoSelectDbContext _dbContext;

    public EfTagAdminService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PageResult<CatalogLookupDto>> ListAsync(
        CatalogLookupQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tags = _dbContext.Tags.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var keyword = query.Q.Trim();
            tags = tags.Where(tag => tag.Code.Contains(keyword) || tag.NameZhTw.Contains(keyword));
        }

        if (query.IsActive.HasValue)
        {
            tags = tags.Where(tag => tag.IsActive == query.IsActive.Value);
        }

        var totalCount = await tags.CountAsync(cancellationToken);

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var entities = await tags
            .OrderBy(tag => tag.SortOrder)
            .ThenBy(tag => tag.Code)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PageResult<CatalogLookupDto>(entities.Select(ToDto).ToList(), pageNumber, pageSize, totalCount);
    }

    public async Task<CatalogLookupDto> CreateAsync(
        CreateTagRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedCode = NormalizeCode(request.Code);
        var exists = await _dbContext.Tags.AsNoTracking()
            .AnyAsync(tag => tag.Code == normalizedCode, cancellationToken);
        if (exists)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.TagCodeDuplicate,
                $"Tag code '{request.Code}' already exists.");
        }

        var now = DateTime.UtcNow;
        var tag = new Tag(Guid.CreateVersion7(), request.Code, request.NameZhTw, now);
        tag.Update(request.NameZhTw, request.IsActive, request.SortOrder, now);

        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(tag);
    }

    public async Task<CatalogLookupDto> UpdateAsync(
        Guid publicId,
        UpdateTagRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tag = await _dbContext.Tags
            .FirstOrDefaultAsync(candidate => candidate.PublicId == publicId, cancellationToken);
        if (tag is null)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ResourceNotFound,
                $"Tag '{publicId}' was not found.");
        }

        _dbContext.Entry(tag).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;

        var now = DateTime.UtcNow;
        tag.Update(request.NameZhTw, request.IsActive, request.SortOrder, now);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ConcurrencyConflict,
                "The tag was updated by someone else. Reload and try again.");
        }

        return ToDto(tag);
    }

    private static string NormalizeCode(string value) =>
        value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();

    private static CatalogLookupDto ToDto(Tag tag) => new(
        tag.PublicId,
        tag.Code,
        tag.NameZhTw,
        tag.IsActive,
        tag.SortOrder,
        tag.RowVersion);
}
