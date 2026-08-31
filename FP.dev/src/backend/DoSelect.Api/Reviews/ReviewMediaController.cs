using DoSelect.Application.Files;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Reviews;
using DoSelect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.Reviews;

[ApiExplorerSettings(IgnoreApi = true)]
[Route("media/reviews")]
public sealed class ReviewMediaController(
    DoSelectDbContext dbContext,
    IImageStorage imageStorage) : ControllerBase
{
    [HttpGet("{reviewPublicId:guid}/{sortOrder:int}/{variant}")]
    [Produces("image/webp")]
    public async Task<IActionResult> Get(
        Guid reviewPublicId,
        int sortOrder,
        string variant,
        CancellationToken cancellationToken)
    {
        var imageVariant = variant switch
        {
            "320" => ProductImageVariant.Small320,
            "800" => ProductImageVariant.Medium800,
            "1600" => ProductImageVariant.Large1600,
            _ => ProductImageVariant.Original,
        };
        if (imageVariant == ProductImageVariant.Original)
        {
            return NotFound();
        }

        var storageKey = await (
            from image in dbContext.ReviewImages.AsNoTracking()
            join review in dbContext.ProductReviews.AsNoTracking()
                on image.ProductReviewId equals review.Id
            join product in dbContext.Products.AsNoTracking()
                on review.ProductId equals product.Id
            where review.PublicId == reviewPublicId &&
                review.Status == ProductReviewStatus.Approved &&
                product.Status == ProductStatus.Published &&
                image.SortOrder == sortOrder &&
                image.DeletedAtUtc == null &&
                image.ScanStatus == ReviewImageScanStatus.Clean
            select image.StorageKey)
            .SingleOrDefaultAsync(cancellationToken);
        if (storageKey is null)
        {
            return NotFound();
        }

        var stream = await imageStorage.OpenReadAsync(storageKey, imageVariant, cancellationToken);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public,max-age=300";
        return File(stream, "image/webp");
    }
}
