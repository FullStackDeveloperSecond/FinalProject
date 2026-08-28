using DoSelect.Domain.Catalog;

namespace DoSelect.Domain.Tests.Catalog;

public sealed class ProductImageTests
{
    private static readonly DateTime Now =
        new(2026, 8, 28, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Publish_RequiresAllVariantHashesAndPreservesCopies()
    {
        var image = CreateImage();

        Assert.Throws<InvalidOperationException>(() => image.Publish(Now.AddMinutes(1)));

        var small = Enumerable.Repeat((byte)1, 32).ToArray();
        var medium = Enumerable.Repeat((byte)2, 32).ToArray();
        var large = Enumerable.Repeat((byte)3, 32).ToArray();
        image.RecordVariantHashes(small, medium, large, Now.AddSeconds(30));
        small[0] = 99;

        image.Publish(Now.AddMinutes(1));

        Assert.Equal(ProductImageStatus.Published, image.Status);
        Assert.Equal(1, image.SmallSha256![0]);
        Assert.Equal(medium, image.MediumSha256);
        Assert.Equal(large, image.LargeSha256);
        Assert.Throws<InvalidOperationException>(() =>
            image.RecordVariantHashes(small, medium, large, Now.AddMinutes(2)));
    }

    private static ProductImage CreateImage() => new(
        Guid.CreateVersion7(),
        1,
        null,
        "product-images/example",
        "example.png",
        "image/png",
        1_024,
        800,
        600,
        new byte[32],
        "範例圖片",
        Now);
}
