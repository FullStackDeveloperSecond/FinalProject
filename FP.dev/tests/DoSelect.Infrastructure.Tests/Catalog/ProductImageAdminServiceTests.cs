using System.Security.Cryptography;
using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using DoSelect.Application.Files;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Catalog;
using DoSelect.Infrastructure.Files;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace DoSelect.Infrastructure.Tests.Catalog;

/// <summary>
/// M-03 商品圖片後台。用真的 <see cref="LocalImageStorage"/>（假掃描器、暫存 DataRoot）與真的
/// SQL Server：要證明的是「資料列與磁碟上的三種 WebP 是一致的」——雜湊、尺寸、發布後的公開 URL
/// 都對得上檔案，而不是只對得上記憶體裡的假儲存。
/// </summary>
[Collection(nameof(CatalogAdminCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ProductImageAdminServiceTests : IAsyncLifetime
{
    private static readonly AuditRequestContext TestAuditContext = CatalogAdminFixture.TestAuditContext;

    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "DoSelect.Tests", "product-images", Guid.NewGuid().ToString("N"));
    private FakeFileScanner _scanner = new(FileScanOutcome.Clean);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task UploadAsync_StoresThreeVariantsRecordsTheirHashesLeavesTheImageReadyAndWritesTheAudit()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (product, admin) = await SeedAsync(context);
        var service = CreateService(context);

        await using var png = await CreatePngAsync(2000, 1000);
        var dto = await service.UploadAsync(
            product.PublicId,
            new ProductImageUpload(png, "front.png", "image/png"),
            new UploadProductImageMetadata("顯示卡正面", null, null, null),
            admin,
            TestAuditContext,
            CancellationToken.None);

        Assert.Equal(ProductImageStatus.Ready.ToString(), dto.Status);
        Assert.Equal("顯示卡正面", dto.AltText);
        Assert.Equal(0, dto.SortOrder);
        Assert.True(dto.IsPrimary);
        Assert.False(dto.HasCompleteMetadata);
        Assert.Equal(2000, dto.Width);
        Assert.Equal(1000, dto.Height);
        Assert.Equal($"/api/v1/admin/product-images/{dto.PublicId:D}/preview", dto.PreviewPathBase);
        // 未發布：三種衍生圖都有尺寸、都沒有公開 URL。
        Assert.Equal(["320", "800", "1600"], dto.Variants.Select(variant => variant.Variant).ToArray());
        Assert.All(dto.Variants, variant => Assert.Null(variant.PublicUrl));
        Assert.Equal((320, 160), (dto.Variants[0].Width, dto.Variants[0].Height));
        Assert.Equal((800, 400), (dto.Variants[1].Width, dto.Variants[1].Height));
        Assert.Equal((1600, 800), (dto.Variants[2].Width, dto.Variants[2].Height));

        await using var verify = CatalogAdminFixture.CreateContext();
        var image = await verify.ProductImages.AsNoTracking().SingleAsync(candidate => candidate.PublicId == dto.PublicId);
        Assert.Equal(ProductImageStatus.Ready, image.Status);
        Assert.Equal("front.png", image.OriginalFileName);
        Assert.Equal("image/png", image.MediaType);
        // 資料列上的雜湊就是磁碟上那個 WebP 的雜湊。
        var storage = CreateStorage();
        foreach (var (variant, expected) in new[]
                 {
                     (ProductImageVariant.Small320, image.SmallSha256!),
                     (ProductImageVariant.Medium800, image.MediumSha256!),
                     (ProductImageVariant.Large1600, image.LargeSha256!),
                 })
        {
            await using var stream = await storage.OpenReadAsync(image.StorageKey, variant);
            Assert.NotNull(stream);
            Assert.Equal(expected, await SHA256.HashDataAsync(stream!));
        }

        // 裁定 B：上傳寫 product_image.upload，與那一列同一次 SaveChanges。
        var audit = await AuditAsync(verify, dto.PublicId, AuditActions.ProductImageUpload);
        Assert.Equal(AuditResourceTypes.ProductImage, audit.ResourceType);
        Assert.Contains(AuditRoleNames.CatalogManager, audit.ActorRolesJson);
        var changes = Changes(audit);
        Assert.Equal((null, product.PublicId.ToString("D")), changes["productPublicId"]);
        Assert.Equal((null, "Ready"), changes["status"]);
        Assert.Equal((null, "0"), changes["sortOrder"]);
        Assert.Equal((null, "false"), changes["hasCompleteMetadata"]);
        Assert.DoesNotContain("front.png", audit.ChangedFieldsJson);
    }

    [Fact]
    public async Task UploadAsync_AssignsTheNextSortOrderAndOnlyTheFirstImageIsPrimary()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (product, admin) = await SeedAsync(context);
        var service = CreateService(context);

        var first = await UploadAsync(service, product.PublicId, admin, "a.png");
        var second = await UploadAsync(service, product.PublicId, admin, "b.png");

        Assert.Equal(0, first.SortOrder);
        Assert.Equal(1, second.SortOrder);
        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);

        var detail = await CatalogAdminFixture.CreateProductService(context)
            .GetByPublicIdAsync(product.PublicId, CancellationToken.None);
        Assert.Equal([first.PublicId, second.PublicId], detail!.Images.Select(image => image.PublicId).ToArray());
        Assert.True(detail.Images[0].IsPrimary);
    }

    [Fact]
    public async Task UploadAsync_WhenTheScannerIsUnavailable_FailsClosedWithoutARowOrFiles()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (product, admin) = await SeedAsync(context);
        _scanner = new FakeFileScanner(FileScanOutcome.Unavailable);
        var service = CreateService(context);

        await using var png = await CreatePngAsync(64, 64);
        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.UploadAsync(
            product.PublicId,
            new ProductImageUpload(png, "x.png", "image/png"),
            new UploadProductImageMetadata(null, null, null, null),
            admin,
            TestAuditContext,
            CancellationToken.None));

        Assert.Equal(503, exception.StatusCode);
        Assert.Equal(DomainErrorCodes.FileScanUnavailable, exception.Code);
        Assert.False(await context.ProductImages.AsNoTracking().AnyAsync(image => image.ProductId == product.Id));
        Assert.Empty(PermanentFiles());
    }

    [Fact]
    public async Task UploadAsync_WhenTheFileIsNotAnImage_Returns415WithoutARow()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (product, admin) = await SeedAsync(context);
        var service = CreateService(context);

        await using var text = new MemoryStream("not an image"u8.ToArray());
        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.UploadAsync(
            product.PublicId,
            new ProductImageUpload(text, "notes.txt", "text/plain"),
            new UploadProductImageMetadata(null, null, null, null),
            admin,
            TestAuditContext,
            CancellationToken.None));

        Assert.Equal(415, exception.StatusCode);
        Assert.Equal(DomainErrorCodes.FileFormatInvalid, exception.Code);
        Assert.False(await context.ProductImages.AsNoTracking().AnyAsync(image => image.ProductId == product.Id));
    }

    /// <summary>中繼資料在存檔前驗：Alt 超長或網址不合法都不該留下任何檔案。</summary>
    [Theory]
    [InlineData("alt-too-long", null)]
    [InlineData(null, "ftp://example.com/source")]
    [InlineData(null, "example.com/no-scheme")]
    [InlineData(null, "javascript:alert(1)")]
    public async Task UploadAsync_WhenTheMetadataIsInvalid_RejectsBeforeStoringAnything(string? altMode, string? sourceUrl)
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (product, admin) = await SeedAsync(context);
        var service = CreateService(context);

        await using var png = await CreatePngAsync(64, 64);
        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.UploadAsync(
            product.PublicId,
            new ProductImageUpload(png, "x.png", "image/png"),
            new UploadProductImageMetadata(altMode is null ? null : new string('a', 161), sourceUrl, null, null),
            admin,
            TestAuditContext,
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
        Assert.Empty(PermanentFiles());
        Assert.False(await context.ProductImages.AsNoTracking().AnyAsync(image => image.ProductId == product.Id));
    }

    [Fact]
    public async Task PublishAsync_RequiresCompleteMetadataThenExposesPublicUrlsMatchingTheFilesOnDisk()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (product, admin) = await SeedAsync(context);
        var service = CreateService(context);
        var uploaded = await UploadAsync(service, product.PublicId, admin, "front.png");

        var incomplete = await Assert.ThrowsAsync<DomainProblemException>(() =>
            service.PublishAsync(uploaded.PublicId, uploaded.RowVersion, admin, TestAuditContext, CancellationToken.None));
        Assert.Equal(422, incomplete.StatusCode);
        Assert.Equal(DomainErrorCodes.ImageMetadataIncomplete, incomplete.Code);

        var updated = await service.UpdateAsync(
            uploaded.PublicId,
            new UpdateProductImageCommand(
                "顯示卡正面", 0, "https://example.com/source", "CC BY 4.0",
                "https://creativecommons.org/licenses/by/4.0/", uploaded.RowVersion),
            admin,
            TestAuditContext,
            CancellationToken.None);
        Assert.True(updated.HasCompleteMetadata);
        Assert.NotEqual(uploaded.RowVersion, updated.RowVersion);

        var published = await service.PublishAsync(updated.PublicId, updated.RowVersion, admin, TestAuditContext, CancellationToken.None);

        Assert.Equal(ProductImageStatus.Published.ToString(), published.Status);
        Assert.NotNull(published.PublishedAtUtc);
        await using var verify = CatalogAdminFixture.CreateContext();
        var image = await verify.ProductImages.AsNoTracking().SingleAsync(candidate => candidate.PublicId == published.PublicId);
        foreach (var variant in published.Variants)
        {
            var hash = variant.Variant switch
            {
                "320" => image.SmallSha256!,
                "800" => image.MediumSha256!,
                _ => image.LargeSha256!,
            };
            Assert.Equal(
                $"/media/products/{image.PublicId:D}/{variant.Variant}/{Convert.ToHexString(hash).ToLowerInvariant()}.webp",
                variant.PublicUrl);
        }

        // 稽核：update 記「改過」與完整性 false→true，publish 記 Ready→Published。
        var updateAudit = await AuditAsync(verify, published.PublicId, AuditActions.ProductImageUpdate);
        Assert.Equal(("false", "true"), Changes(updateAudit)["hasCompleteMetadata"]);
        Assert.DoesNotContain("example.com", updateAudit.ChangedFieldsJson);
        var publishAudit = await AuditAsync(verify, published.PublicId, AuditActions.ProductImagePublish);
        Assert.Equal(("Ready", "Published"), Changes(publishAudit)["status"]);

        // 再發布一次是 no-op（不寫第二筆稽核），但仍要拿目前的 RowVersion。
        var again = await service.PublishAsync(published.PublicId, published.RowVersion, admin, TestAuditContext, CancellationToken.None);
        Assert.Equal(published.RowVersion, again.RowVersion);
        Assert.Equal(1, await verify.AuditLogs.AsNoTracking().CountAsync(a => a.ResourcePublicId == published.PublicId && a.Action == AuditActions.ProductImagePublish));
        var stale = await Assert.ThrowsAsync<CatalogWriteException>(() =>
            service.PublishAsync(published.PublicId, uploaded.RowVersion, admin, TestAuditContext, CancellationToken.None));
        Assert.Equal(CatalogWriteException.ErrorCodes.ConcurrencyConflict, stale.ErrorCode);
    }

    /// <summary>組長 PR #101 item 1：Published 的圖片不能被 PATCH 成來源／授權不完整卻繼續公開。</summary>
    [Fact]
    public async Task UpdateAsync_OnAPublishedImage_RejectsClearingTheAttributionAndLeavesItUntouched()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (product, admin) = await SeedAsync(context);
        var service = CreateService(context);
        var published = await UploadAndPublishAsync(service, product.PublicId, admin, "front.png");
        var updateAuditsBefore = await context.AuditLogs.AsNoTracking()
            .CountAsync(a => a.ResourcePublicId == published.PublicId && a.Action == AuditActions.ProductImageUpdate);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.UpdateAsync(
            published.PublicId,
            new UpdateProductImageCommand("正面", 0, "https://example.com/s", null, "https://example.com/l", published.RowVersion),
            admin,
            TestAuditContext,
            CancellationToken.None));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal(DomainErrorCodes.ImageMetadataIncomplete, exception.Code);
        await using var verify = CatalogAdminFixture.CreateContext();
        var image = await verify.ProductImages.AsNoTracking().SingleAsync(candidate => candidate.PublicId == published.PublicId);
        Assert.Equal(ProductImageStatus.Published, image.Status);
        Assert.Equal("CC0", image.LicenseName);
        Assert.True(image.HasCompleteMetadata);
        // 被拒的更新不留稽核（發布前那一次完整更新的稽核仍在）。
        Assert.Equal(updateAuditsBefore, await verify.AuditLogs.AsNoTracking()
            .CountAsync(a => a.ResourcePublicId == published.PublicId && a.Action == AuditActions.ProductImageUpdate));

        // 完整的更新（換 Alt、保留來源／授權）仍可以。
        var renamed = await service.UpdateAsync(
            published.PublicId,
            new UpdateProductImageCommand("新的 Alt", 0, "https://example.com/s", "CC0", "https://example.com/l", published.RowVersion),
            admin,
            TestAuditContext,
            CancellationToken.None);
        Assert.Equal("新的 Alt", renamed.AltText);
        Assert.Equal(ProductImageStatus.Published.ToString(), renamed.Status);
    }

    [Fact]
    public async Task PublishedImagesAppearOnTheStorefrontAndUnpublishedOnesDoNot()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (product, admin) = await SeedAsync(context, publish: true);
        var service = CreateService(context);
        var visible = await UploadAndPublishAsync(service, product.PublicId, admin, "front.png");
        var draft = await UploadAsync(service, product.PublicId, admin, "back.png");

        var detail = await new EfProductDetailService(context)
            .GetByPublicIdAsync(product.PublicId, DoSelect.Domain.Members.SupportedLocale.ZhTw, CancellationToken.None);

        Assert.NotNull(detail);
        var image = Assert.Single(detail!.Images);
        Assert.StartsWith($"/media/products/{visible.PublicId:D}/800/", image.Url);
        Assert.Equal("正面", image.Alt);
        Assert.Equal((800, 400), (image.Width, image.Height));
        Assert.True(image.IsPrimary);
        Assert.NotNull(detail.PrimaryImage);
        Assert.StartsWith($"/media/products/{visible.PublicId:D}/800/", detail.PrimaryImage!.Url);
        Assert.DoesNotContain(draft.PublicId.ToString("D"), detail.Images.Select(candidate => candidate.Url).Concat([detail.PrimaryImage.Url]).Aggregate((a, b) => a + b));

        // 商品卡（搜尋）主圖是 320 的公開 URL。
        var cards = await new EfProductSearchService(context).SearchAsync(
            new ProductSearchQuery(product.ProductCode, null, null, null, null, null, [], null, 1, 20, DoSelect.Domain.Members.SupportedLocale.ZhTw),
            CancellationToken.None);
        var card = Assert.Single(cards.Items, item => item.ProductPublicId == product.PublicId);
        Assert.StartsWith($"/media/products/{visible.PublicId:D}/320/", card.PrimaryImage!.Url);
        Assert.Equal((320, 160), (card.PrimaryImage.Width, card.PrimaryImage.Height));

        // 後台列表的主圖走授權預覽路由，未發布也看得到。
        var list = await CatalogAdminFixture.CreateProductService(context).ListAsync(
            new AdminProductQuery(product.ProductCode, null, null, null, null, null, 1, 20), CancellationToken.None);
        var summary = Assert.Single(list.Items, item => item.PublicId == product.PublicId);
        Assert.Equal($"/api/v1/admin/product-images/{visible.PublicId:D}/preview/320", summary.PrimaryImage!.Url);
    }

    /// <summary>
    /// 組長 PR #101 item 2：Migration 之前的 Published 列可能缺衍生圖雜湊，媒體端點對它一定 404——
    /// 前台不得為它產生一個假的公開 URL，也不得讓它當主圖。
    /// </summary>
    [Fact]
    public async Task LegacyPublishedImagesWithoutVariantHashesAreNotProjectedToTheStorefront()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (product, admin) = await SeedAsync(context, publish: true);
        var service = CreateService(context);
        var legacy = await UploadAndPublishAsync(service, product.PublicId, admin, "legacy.png");
        var healthy = await UploadAndPublishAsync(service, product.PublicId, admin, "healthy.png");
        // 直接把舊列的一個雜湊清掉，模擬 AddProductImageVariantHashes 之前就存在的資料。
        await context.ProductImages
            .Where(image => image.PublicId == legacy.PublicId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(image => image.SmallSha256, (byte[]?)null));

        await using var fresh = CatalogAdminFixture.CreateContext();
        var detail = await new EfProductDetailService(fresh)
            .GetByPublicIdAsync(product.PublicId, DoSelect.Domain.Members.SupportedLocale.ZhTw, CancellationToken.None);
        var cards = await new EfProductSearchService(fresh).SearchAsync(
            new ProductSearchQuery(product.ProductCode, null, null, null, null, null, [], null, 1, 20, DoSelect.Domain.Members.SupportedLocale.ZhTw),
            CancellationToken.None);

        var only = Assert.Single(detail!.Images);
        Assert.StartsWith($"/media/products/{healthy.PublicId:D}/800/", only.Url);
        Assert.StartsWith($"/media/products/{healthy.PublicId:D}/800/", detail.PrimaryImage!.Url);
        Assert.StartsWith($"/media/products/{healthy.PublicId:D}/320/", Assert.Single(cards.Items).PrimaryImage!.Url);
        Assert.DoesNotContain(new string('0', 64), only.Url);

        // 後台仍列出它（管理員要看得到才修得了），只是 320 沒有公開 URL。
        var admin_detail = await CatalogAdminFixture.CreateProductService(fresh).GetByPublicIdAsync(product.PublicId, CancellationToken.None);
        var legacyRow = Assert.Single(admin_detail!.Images, image => image.PublicId == legacy.PublicId);
        Assert.Null(legacyRow.Variants.Single(variant => variant.Variant == "320").PublicUrl);
        Assert.NotNull(legacyRow.Variants.Single(variant => variant.Variant == "800").PublicUrl);
    }

    [Fact]
    public async Task DeleteAsync_HidesTheImageEverywhereKeepsTheFilesForTheRetentionJobAndWritesTheAudit()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (product, admin) = await SeedAsync(context);
        var service = CreateService(context);
        var uploaded = await UploadAsync(service, product.PublicId, admin, "front.png");

        await service.DeleteAsync(uploaded.PublicId, uploaded.RowVersion, admin, TestAuditContext, CancellationToken.None);

        await using var verify = CatalogAdminFixture.CreateContext();
        var image = await verify.ProductImages.AsNoTracking().SingleAsync(candidate => candidate.PublicId == uploaded.PublicId);
        Assert.Equal(ProductImageStatus.Deleted, image.Status);
        Assert.NotNull(image.DeletedAtUtc);
        // 「替換圖片…新檔完成、資料交易成功後才排程清理舊檔」：檔案留給 StorageMaintenanceJob 的 30 天規則。
        Assert.NotEmpty(PermanentFiles());
        var audit = await AuditAsync(verify, uploaded.PublicId, AuditActions.ProductImageDelete);
        Assert.Equal(("Ready", "Deleted"), Changes(audit)["status"]);

        Assert.Null(await service.OpenPreviewAsync(uploaded.PublicId, "320", CancellationToken.None));
        var detail = await CatalogAdminFixture.CreateProductService(context).GetByPublicIdAsync(product.PublicId, CancellationToken.None);
        Assert.Empty(detail!.Images);
        var gone = await Assert.ThrowsAsync<CatalogWriteException>(() =>
            service.UpdateAsync(uploaded.PublicId, new UpdateProductImageCommand("x", 0, null, null, null, image.RowVersion), admin, TestAuditContext, CancellationToken.None));
        Assert.Equal(CatalogWriteException.ErrorCodes.ResourceNotFound, gone.ErrorCode);
    }

    /// <summary>稽核與圖片那一列同一次 SaveChanges：AuditLogs 的 INSERT 壞掉，刪除就不成立。</summary>
    [Fact]
    public async Task DeleteAsync_WhenTheAuditInsertFails_DoesNotDeleteTheImage()
    {
        await using var seed = CatalogAdminFixture.CreateContext();
        var (product, admin) = await SeedAsync(seed);
        var uploaded = await UploadAsync(CreateService(seed), product.PublicId, admin, "front.png");

        var interceptor = new ThrowOnAuditInsertInterceptor();
        await using var context = CatalogAdminFixture.CreateContext(interceptor);
        var service = CreateService(context);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.DeleteAsync(uploaded.PublicId, uploaded.RowVersion, admin, TestAuditContext, CancellationToken.None));
        Assert.True(interceptor.Engaged);

        await using var verify = CatalogAdminFixture.CreateContext();
        var image = await verify.ProductImages.AsNoTracking().SingleAsync(candidate => candidate.PublicId == uploaded.PublicId);
        Assert.Equal(ProductImageStatus.Ready, image.Status);
    }

    [Fact]
    public async Task OpenPreviewAsync_StreamsEveryVariantAndReturnsNullForUnknownOnes()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (product, admin) = await SeedAsync(context);
        var service = CreateService(context);
        var uploaded = await UploadAsync(service, product.PublicId, admin, "front.png");

        foreach (var (variant, contentType) in new[] { ("original", "image/png"), ("320", "image/webp"), ("800", "image/webp"), ("1600", "image/webp") })
        {
            var preview = await service.OpenPreviewAsync(uploaded.PublicId, variant, CancellationToken.None);
            Assert.NotNull(preview);
            Assert.Equal(contentType, preview!.ContentType);
            await using var content = preview.Content;
            Assert.True(content.Length > 0);
        }

        Assert.Null(await service.OpenPreviewAsync(uploaded.PublicId, "640", CancellationToken.None));
        Assert.Null(await service.OpenPreviewAsync(Guid.NewGuid(), "320", CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_WithAStaleRowVersion_ThrowsConcurrencyConflict()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (product, admin) = await SeedAsync(context);
        var service = CreateService(context);
        var uploaded = await UploadAsync(service, product.PublicId, admin, "front.png");
        await service.UpdateAsync(
            uploaded.PublicId, new UpdateProductImageCommand("第一次", 0, null, null, null, uploaded.RowVersion), admin, TestAuditContext, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.UpdateAsync(
            uploaded.PublicId, new UpdateProductImageCommand("第二次", 0, null, null, null, uploaded.RowVersion), admin, TestAuditContext, CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
        var image = await context.ProductImages.AsNoTracking().SingleAsync(candidate => candidate.PublicId == uploaded.PublicId);
        Assert.Equal("第一次", image.AltTextZhTw);
    }

    private EfProductImageAdminService CreateService(DoSelectDbContext context) =>
        new(context, CreateStorage(), new EfAuditWriter(context, TimeProvider.System), TimeProvider.System);

    private LocalImageStorage CreateStorage() => new(_dataRoot, _scanner);

    private string[] PermanentFiles()
    {
        var directory = Path.Combine(_dataRoot, "product-images");
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories) : [];
    }

    private static async Task<AdminProductImageDto> UploadAsync(EfProductImageAdminService service, Guid productPublicId, string admin, string fileName)
    {
        await using var png = await CreatePngAsync(2000, 1000);
        return await service.UploadAsync(
            productPublicId,
            new ProductImageUpload(png, fileName, "image/png"),
            new UploadProductImageMetadata(null, null, null, null),
            admin,
            TestAuditContext,
            CancellationToken.None);
    }

    private static async Task<AdminProductImageDto> UploadAndPublishAsync(EfProductImageAdminService service, Guid productPublicId, string admin, string fileName)
    {
        var uploaded = await UploadAsync(service, productPublicId, admin, fileName);
        var complete = await service.UpdateAsync(
            uploaded.PublicId,
            new UpdateProductImageCommand("正面", uploaded.SortOrder, "https://example.com/s", "CC0", "https://example.com/l", uploaded.RowVersion),
            admin,
            TestAuditContext,
            CancellationToken.None);
        return await service.PublishAsync(complete.PublicId, complete.RowVersion, admin, TestAuditContext, CancellationToken.None);
    }

    private static async Task<DoSelect.Domain.Auditing.AuditLog> AuditAsync(DoSelectDbContext context, Guid imagePublicId, string action) =>
        await context.AuditLogs.AsNoTracking().SingleAsync(a => a.ResourcePublicId == imagePublicId && a.Action == action);

    private static Dictionary<string, (string? Before, string? After)> Changes(DoSelect.Domain.Auditing.AuditLog audit)
    {
        using var envelope = JsonDocument.Parse(audit.ChangedFieldsJson);
        return envelope.RootElement.GetProperty("changes").EnumerateArray()
            .ToDictionary(
                change => change.GetProperty("field").GetString()!,
                change => (change.GetProperty("beforeCode").GetString(), change.GetProperty("afterCode").GetString()));
    }

    private static async Task<(Product Product, string AdminUserId)> SeedAsync(DoSelectDbContext context, bool publish = false)
    {
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var admin = await CatalogAdminFixture.SeedCatalogAdminAsync(context);
        if (publish)
        {
            // 前台詳情要有已上架的商品與已上架的預設 SKU 才會回東西（EfProductDetailService）。
            var now = DateTime.UtcNow;
            product.ChangeStatus(ProductStatus.Published, now);
            var sku = new Sku(Guid.CreateVersion7(), CatalogAdminFixture.UniqueCode("SKU"), product.Id, "預設規格", 1000m, 600m, now);
            sku.ChangeStatus(SkuStatus.Published, now);
            sku.UpdateCommercialDetails(sku.NameZhTw, sku.ListPrice, sku.UnitCost, isDefault: true, requiresPrepayment: false, now);
            context.Skus.Add(sku);
            await context.SaveChangesAsync();
            context.InventoryBalances.Add(new DoSelect.Domain.Inventory.InventoryBalance(Guid.CreateVersion7(), sku.Id, 5, reorderLevel: 1, now));
            await context.SaveChangesAsync();
        }

        return (product, admin);
    }

    private static async Task<MemoryStream> CreatePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();
        await image.SaveAsync(stream, new PngEncoder());
        stream.Position = 0;
        return stream;
    }

    private sealed class FakeFileScanner(FileScanOutcome outcome) : IFileScanner
    {
        public Task<FileScanResult> ScanAsync(string quarantinedFilePath, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new FileScanResult(outcome, "Synthetic scanner", now, now));
        }
    }

    private sealed class ThrowOnAuditInsertInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public bool Engaged { get; private set; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Throw(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Throw(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Throw(System.Data.Common.DbCommand command)
        {
            if (command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("[AuditLogs]", StringComparison.OrdinalIgnoreCase))
            {
                Engaged = true;
                throw new InvalidOperationException("Injected AuditLogs insert failure.");
            }
        }
    }
}
