using System.Security.Cryptography;
using DoSelect.Application.Files;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.Catalog;

[ApiExplorerSettings(IgnoreApi = true)]
[Route("media/products")]
public sealed class ProductMediaController : ControllerBase
{
    private readonly DoSelectDbContext _dbContext;
    private readonly IImageStorage _imageStorage;

    public ProductMediaController(DoSelectDbContext dbContext, IImageStorage imageStorage)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(imageStorage);
        _dbContext = dbContext;
        _imageStorage = imageStorage;
    }

    [HttpGet("{publicId:guid}/{variant}/{contentHash}.webp")]
    [Produces("image/webp")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        Guid publicId,
        string variant,
        string contentHash,
        CancellationToken cancellationToken)
    {
        if (!TryResolveVariant(variant, out var imageVariant) ||
            !TryDecodeHash(contentHash, out var requestedHash))
        {
            return NotFound();
        }

        var image = await _dbContext.ProductImages
            .AsNoTracking()
            .Where(candidate =>
                candidate.PublicId == publicId &&
                candidate.Status == ProductImageStatus.Published)
            .Select(candidate => new
            {
                candidate.StorageKey,
                candidate.SmallSha256,
                candidate.MediumSha256,
                candidate.LargeSha256,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (image is null)
        {
            return NotFound();
        }

        var storedHash = imageVariant switch
        {
            ProductImageVariant.Small320 => image.SmallSha256,
            ProductImageVariant.Medium800 => image.MediumSha256,
            ProductImageVariant.Large1600 => image.LargeSha256,
            _ => null,
        };
        if (storedHash is not { Length: 32 } ||
            !CryptographicOperations.FixedTimeEquals(storedHash, requestedHash))
        {
            return NotFound();
        }

        var stream = await _imageStorage.OpenReadAsync(
            image.StorageKey,
            imageVariant,
            cancellationToken);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        Response.Headers.ETag = $"\"{contentHash.ToLowerInvariant()}\"";
        return File(stream, "image/webp");
    }

    private static bool TryResolveVariant(string value, out ProductImageVariant variant)
    {
        variant = value switch
        {
            "320" => ProductImageVariant.Small320,
            "800" => ProductImageVariant.Medium800,
            "1600" => ProductImageVariant.Large1600,
            _ => ProductImageVariant.Original,
        };
        return variant != ProductImageVariant.Original;
    }

    private static bool TryDecodeHash(string value, out byte[] hash)
    {
        hash = [];
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            return false;
        }

        hash = Convert.FromHexString(value);
        return true;
    }
}
