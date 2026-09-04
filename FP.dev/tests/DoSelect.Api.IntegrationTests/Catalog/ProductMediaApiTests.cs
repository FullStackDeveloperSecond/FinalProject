using System.Net;
using System.Security.Cryptography;
using DoSelect.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Catalog;

[Collection(nameof(ProductsApiCollection))]
public sealed class ProductMediaApiTests(ProductsApiFixture fixture)
{
    [Fact]
    public async Task PublishedVariant_WithMatchingHash_IsPublicAndImmutable()
    {
        var bytes = "synthetic-webp-variant"u8.ToArray();
        var hash = SHA256.HashData(bytes);
        var imagePublicId = Guid.CreateVersion7();
        var storageKey = $"product-images/aa/{Guid.NewGuid():N}";
        await using (var context = fixture.CreateScopedContext())
        {
            var (product, _, _) = await ProductsApiSeeding.CreatePublishedProductAsync(context);
            var image = CreateImage(imagePublicId, product.Id, storageKey);
            image.RecordVariantHashes(hash, new byte[32], new byte[32], DateTime.UtcNow);
            // 組長 PR #101 裁定 C：只有 Ready → Published，上傳流程結束時 MarkReady。
            image.MarkReady(DateTime.UtcNow);
            image.Publish(DateTime.UtcNow);
            context.ProductImages.Add(image);
            await context.SaveChangesAsync();
        }

        await fixture.WriteImageVariantAsync(storageKey, "320.webp", bytes);

        using var response = await fixture.Client.GetAsync(
            $"/media/products/{imagePublicId:D}/320/{Convert.ToHexString(hash).ToLowerInvariant()}.webp");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.True(response.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromDays(365), response.Headers.CacheControl?.MaxAge);
        Assert.Contains("immutable", response.Headers.GetValues("Cache-Control").Single());
        Assert.Equal($"\"{Convert.ToHexString(hash).ToLowerInvariant()}\"", response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task HashMismatchOrUnpublishedImage_ReturnsNotFound()
    {
        var imagePublicId = Guid.CreateVersion7();
        await using (var context = fixture.CreateScopedContext())
        {
            var (product, _, _) = await ProductsApiSeeding.CreatePublishedProductAsync(context);
            var image = CreateImage(
                imagePublicId,
                product.Id,
                $"product-images/bb/{Guid.NewGuid():N}");
            image.RecordVariantHashes(new byte[32], new byte[32], new byte[32], DateTime.UtcNow);
            context.ProductImages.Add(image);
            await context.SaveChangesAsync();
        }

        using var unpublished = await fixture.Client.GetAsync(
            $"/media/products/{imagePublicId:D}/320/{new string('0', 64)}.webp");
        using var malformed = await fixture.Client.GetAsync(
            $"/media/products/{imagePublicId:D}/320/not-a-hash.webp");

        Assert.Equal(HttpStatusCode.NotFound, unpublished.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
    }

    private static ProductImage CreateImage(Guid publicId, long productId, string storageKey) => new(
        publicId,
        productId,
        null,
        storageKey,
        "product.png",
        "image/png",
        1_024,
        800,
        600,
        new byte[32],
        "測試圖片",
        DateTime.UtcNow);
}
