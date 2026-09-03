using DoSelect.Domain.Catalog;

namespace DoSelect.Domain.Tests.Catalog;

/// <summary>M-03 商品圖片後台：Processing → Ready → Published → Deleted 的邊與中繼資料規則。</summary>
public sealed class ProductImageLifecycleTests
{
    private static readonly DateTime Now = new(2026, 9, 4, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void MarkReady_RequiresProcessingWithAllVariantHashes()
    {
        var image = CreateImage();

        // 沒有衍生圖雜湊不能 Ready：Ready 的意思是「三種 WebP 都在正式目錄了」。
        Assert.Throws<InvalidOperationException>(() => image.MarkReady(Now));

        RecordHashes(image);
        image.MarkReady(Now.AddSeconds(1));

        Assert.Equal(ProductImageStatus.Ready, image.Status);
        Assert.Throws<InvalidOperationException>(() => image.MarkReady(Now.AddSeconds(2)));
    }

    [Fact]
    public void UpdateMetadata_SetsAltSortAndAttributionAndNormalizesBlanksToNull()
    {
        var image = CreateImage();

        image.UpdateMetadata("顯示卡正面", 3, " https://example.com/src ", "  ", null, Now);

        Assert.Equal("顯示卡正面", image.AltTextZhTw);
        Assert.Equal(3, image.SortOrder);
        Assert.Equal("https://example.com/src", image.SourceUrl);
        Assert.Null(image.LicenseName);
        Assert.Null(image.LicenseUrl);
        Assert.False(image.HasCompleteMetadata);
        Assert.Throws<ArgumentException>(() => image.UpdateMetadata(" ", 0, null, null, null, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.UpdateMetadata("x", -1, null, null, null, Now));
    }

    [Fact]
    public void HasCompleteMetadata_RequiresAltSourceLicenseNameAndLicenseUrl()
    {
        var image = CreateImage();
        Assert.False(image.HasCompleteMetadata);

        image.UpdateMetadata("Alt", 0, "https://example.com/src", "CC BY 4.0", "https://creativecommons.org/licenses/by/4.0/", Now);

        Assert.True(image.HasCompleteMetadata);
    }

    [Fact]
    public void Publish_AndUpdate_AreRejectedOnceDeleted()
    {
        var image = CreateImage();
        RecordHashes(image);
        image.MarkReady(Now);
        image.MarkDeleted(Now.AddMinutes(1));

        Assert.Equal(ProductImageStatus.Deleted, image.Status);
        Assert.Throws<InvalidOperationException>(() => image.Publish(Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => image.UpdateMetadata("Alt", 0, null, null, null, Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => image.MarkDeleted(Now.AddMinutes(2)));
    }

    [Fact]
    public void Publish_FromReady_RecordsPublishedAt()
    {
        var image = CreateImage();
        RecordHashes(image);
        image.MarkReady(Now);

        image.Publish(Now.AddMinutes(1));

        Assert.Equal(ProductImageStatus.Published, image.Status);
        Assert.Equal(Now.AddMinutes(1), image.PublishedAtUtc);
    }

    private static void RecordHashes(ProductImage image) => image.RecordVariantHashes(
        Enumerable.Repeat((byte)1, 32).ToArray(),
        Enumerable.Repeat((byte)2, 32).ToArray(),
        Enumerable.Repeat((byte)3, 32).ToArray(),
        Now);

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
