using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Catalog;

/// <summary>
/// M-03 商品圖片後台五條端點的 HTTP 層（API Endpoint 目錄「M 商品圖片」列）：multipart 繫結、
/// 檔案錯誤碼的 HTTP 狀態、預覽路由的 404 語意、RowVersion 與發布後的公開路由。業務規則在
/// DoSelect.Infrastructure.Tests.Catalog.ProductImageAdminServiceTests。
/// </summary>
[Collection(nameof(CatalogAdminApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ProductImagesAdminApiTests
{
    private readonly CatalogAdminApiFixture _fixture;

    public ProductImagesAdminApiTests(CatalogAdminApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Upload_ThenPreview_ThenPublish_ExposesThePublicMediaRoute()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientWithIdentityAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);

        using var uploadResponse = await UploadAsync(client, productId, altText: "正面");
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var imageId = uploaded.GetProperty("publicId").GetGuid();
        Assert.Equal("Ready", uploaded.GetProperty("status").GetString());
        Assert.Equal("正面", uploaded.GetProperty("altText").GetString());
        Assert.Equal(productId, uploaded.GetProperty("productPublicId").GetGuid());
        Assert.True(uploaded.GetProperty("isPrimary").GetBoolean());
        Assert.False(uploaded.GetProperty("hasCompleteMetadata").GetBoolean());

        // 後台預覽：原圖用原本的 MIME，衍生圖是 WebP，且不快取。
        using var original = await client.GetAsync($"/api/v1/admin/product-images/{imageId}/preview/original");
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        Assert.Equal("image/png", original.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", original.Headers.CacheControl?.ToString() ?? string.Empty);
        using var small = await client.GetAsync($"/api/v1/admin/product-images/{imageId}/preview/320");
        Assert.Equal(HttpStatusCode.OK, small.StatusCode);
        Assert.Equal("image/webp", small.Content.Headers.ContentType?.MediaType);

        // 商品詳情帶著後台形狀的圖片清單。
        using var detail = await client.GetAsync($"/api/v1/admin/products/{productId}");
        var detailBody = await detail.Content.ReadFromJsonAsync<JsonElement>();
        var listed = Assert.Single(detailBody.GetProperty("images").EnumerateArray());
        Assert.Equal(imageId, listed.GetProperty("publicId").GetGuid());

        // 來源／授權不齊不能發布。
        var rowVersion = uploaded.GetProperty("rowVersion").GetString();
        using var incomplete = await PublishAsync(client, imageId, rowVersion!);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, incomplete.StatusCode);
        var (_, code, _) = await CatalogAdminApiFixture.ReadProblemAsync(incomplete);
        Assert.Equal("image_metadata_incomplete", code);

        using var patch = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client,
            new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/admin/product-images/{imageId}")
            {
                Content = JsonContent.Create(new
                {
                    altText = "顯示卡正面",
                    sortOrder = 0,
                    sourceUrl = "https://example.com/source",
                    licenseName = "CC BY 4.0",
                    licenseUrl = "https://creativecommons.org/licenses/by/4.0/",
                    rowVersion,
                }),
            });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var patched = await patch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(patched.GetProperty("hasCompleteMetadata").GetBoolean());

        using var publish = await PublishAsync(client, imageId, patched.GetProperty("rowVersion").GetString()!);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        var published = await publish.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Published", published.GetProperty("status").GetString());
        var publicUrl = published.GetProperty("variants").EnumerateArray()
            .Single(variant => variant.GetProperty("variant").GetString() == "320")
            .GetProperty("publicUrl").GetString();
        Assert.StartsWith($"/media/products/{imageId:D}/320/", publicUrl);

        // 公開路由（SH-06）現在讀得到，而且是同一個檔案。
        using var anonymous = _fixture.CreateClient();
        using var media = await anonymous.GetAsync(publicUrl);
        Assert.Equal(HttpStatusCode.OK, media.StatusCode);
        Assert.Equal("image/webp", media.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await small.Content.ReadAsByteArrayAsync(), await media.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_WithoutAFile_ReturnsValidationFailed()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientWithIdentityAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);

        using var content = new MultipartFormDataContent { { new StringContent("正面"), "altText" } };
        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client,
            new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/products/{productId}/images") { Content = content });

        var (status, code, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);
        Assert.Equal(400, status);
        Assert.Equal("validation_failed", code);
    }

    [Fact]
    public async Task Upload_WhenTheFileIsNotAnImage_Returns415FileFormatInvalid()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientWithIdentityAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);

        using var response = await UploadAsync(client, productId, bytes: "definitely not an image"u8.ToArray(), fileName: "notes.txt", contentType: "text/plain");

        var (status, code, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);
        Assert.Equal(415, status);
        Assert.Equal("file_format_invalid", code);
        await using var context = _fixture.CreateScopedContext();
        var productInternalId = await context.Products.AsNoTracking()
            .Where(product => product.PublicId == productId).Select(product => product.Id).SingleAsync();
        Assert.False(await context.ProductImages.AsNoTracking().AnyAsync(image => image.ProductId == productInternalId));
    }

    [Fact]
    public async Task Upload_WhenTheProductDoesNotExist_Returns404()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientWithIdentityAsync();

        using var response = await UploadAsync(client, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>檔案與圖片儲存設計：「未登入、無 Catalog 權限…或檔案不存在時均回 404，不揭露檔案是否存在」。</summary>
    [Fact]
    public async Task Preview_WithoutTheViewDraftPolicy_Returns404NotAuthorizationErrors()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientWithIdentityAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);
        using var uploadResponse = await UploadAsync(client, productId);
        var imageId = (await uploadResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("publicId").GetGuid();

        using var anonymous = _fixture.CreateClient();
        using var anonymousResponse = await anonymous.GetAsync($"/api/v1/admin/product-images/{imageId}/preview/320");
        Assert.Equal(HttpStatusCode.NotFound, anonymousResponse.StatusCode);

        using var wrongRole = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.OrderManager);
        using var wrongRoleResponse = await wrongRole.GetAsync($"/api/v1/admin/product-images/{imageId}/preview/320");
        Assert.Equal(HttpStatusCode.NotFound, wrongRoleResponse.StatusCode);

        using var unknownVariant = await client.GetAsync($"/api/v1/admin/product-images/{imageId}/preview/640");
        Assert.Equal(HttpStatusCode.NotFound, unknownVariant.StatusCode);
    }

    [Fact]
    public async Task Delete_HidesThePreviewAndTheImageFromTheProduct()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientWithIdentityAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);
        using var uploadResponse = await UploadAsync(client, productId);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var imageId = uploaded.GetProperty("publicId").GetGuid();
        var rowVersion = uploaded.GetProperty("rowVersion").GetString();

        using var stale = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client,
            new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/product-images/{imageId}")
            {
                Content = JsonContent.Create(new { rowVersion = Convert.ToBase64String(new byte[8]) }),
            });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var delete = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client,
            new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/product-images/{imageId}")
            {
                Content = JsonContent.Create(new { rowVersion }),
            });
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using var preview = await client.GetAsync($"/api/v1/admin/product-images/{imageId}/preview/320");
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        using var detail = await client.GetAsync($"/api/v1/admin/products/{productId}");
        var detailBody = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(detailBody.GetProperty("images").EnumerateArray());

        await using var context = _fixture.CreateScopedContext();
        var image = await context.ProductImages.AsNoTracking().SingleAsync(candidate => candidate.PublicId == imageId);
        Assert.Equal(ProductImageStatus.Deleted, image.Status);
    }

    /// <summary>組長 PR #101 item 1：Published 的圖片不能被 PATCH 成來源／授權不完整。</summary>
    [Fact]
    public async Task Patch_OnAPublishedImage_ClearingTheLicense_Returns422AndKeepsItPublished()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientWithIdentityAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);
        var published = await UploadAndPublishAsync(client, productId);
        var imageId = published.GetProperty("publicId").GetGuid();

        using var response = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client,
            new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/admin/product-images/{imageId}")
            {
                Content = JsonContent.Create(new
                {
                    altText = "正面",
                    sortOrder = 0,
                    sourceUrl = "https://example.com/source",
                    licenseName = (string?)null,
                    licenseUrl = "https://creativecommons.org/licenses/by/4.0/",
                    rowVersion = published.GetProperty("rowVersion").GetString(),
                }),
            });

        var (status, code, _) = await CatalogAdminApiFixture.ReadProblemAsync(response);
        Assert.Equal(422, status);
        Assert.Equal("image_metadata_incomplete", code);
        await using var context = _fixture.CreateScopedContext();
        var image = await context.ProductImages.AsNoTracking().SingleAsync(candidate => candidate.PublicId == imageId);
        Assert.Equal(ProductImageStatus.Published, image.Status);
        Assert.Equal("CC BY 4.0", image.LicenseName);
    }

    /// <summary>組長 PR #101 裁定 D：來源／授權網址只接受 absolute HTTP／HTTPS。</summary>
    [Theory]
    [InlineData("ftp://example.com/source", "https://example.com/license")]
    [InlineData("https://example.com/source", "example.com/license")]
    public async Task UploadAndPatch_WithANonHttpUrl_Return400ValidationFailed(string sourceUrl, string licenseUrl)
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientWithIdentityAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);

        using var upload = await UploadAsync(client, productId, sourceUrl: sourceUrl, licenseUrl: licenseUrl);
        var (uploadStatus, uploadCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(upload);
        Assert.Equal(400, uploadStatus);
        Assert.Equal("validation_failed", uploadCode);

        using var created = await UploadAsync(client, productId);
        var uploaded = await created.Content.ReadFromJsonAsync<JsonElement>();
        using var patch = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client,
            new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/admin/product-images/{uploaded.GetProperty("publicId").GetGuid()}")
            {
                Content = JsonContent.Create(new
                {
                    altText = "正面",
                    sortOrder = 0,
                    sourceUrl,
                    licenseName = "CC0",
                    licenseUrl,
                    rowVersion = uploaded.GetProperty("rowVersion").GetString(),
                }),
            });
        var (patchStatus, patchCode, _) = await CatalogAdminApiFixture.ReadProblemAsync(patch);
        Assert.Equal(400, patchStatus);
        Assert.Equal("validation_failed", patchCode);
    }

    /// <summary>組長 PR #101 裁定 B：四個動作都寫中央 Audit，帶本次請求的 Correlation。</summary>
    [Fact]
    public async Task EveryImageActionWritesACentralAuditEntry()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientWithIdentityAsync();
        var productId = await CatalogAdminApiSeeding.CreateProductWithCatalogAsync(client);
        var published = await UploadAndPublishAsync(client, productId);
        var imageId = published.GetProperty("publicId").GetGuid();
        using var delete = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client,
            new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/product-images/{imageId}")
            {
                Content = JsonContent.Create(new { rowVersion = published.GetProperty("rowVersion").GetString() }),
            });
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var deleteCorrelation = Assert.Single(delete.Headers.GetValues("X-Correlation-ID"));

        await using var context = _fixture.CreateScopedContext();
        var audits = await context.AuditLogs.AsNoTracking()
            .Where(a => a.ResourcePublicId == imageId)
            .OrderBy(a => a.OccurredAtUtc)
            .Select(a => new { a.Action, a.ResourceType, a.CorrelationId, a.ChangedFieldsJson, a.ActorRolesJson })
            .ToListAsync();
        Assert.Equal(
            ["product_image.upload", "product_image.update", "product_image.publish", "product_image.delete"],
            audits.Select(a => a.Action).ToArray());
        Assert.All(audits, a => Assert.Equal("ProductImage", a.ResourceType));
        Assert.All(audits, a => Assert.Contains(DoSelectRoles.CatalogManager, a.ActorRolesJson));
        Assert.All(audits, a => Assert.DoesNotContain("example.com", a.ChangedFieldsJson));
        Assert.Equal(deleteCorrelation, audits[^1].CorrelationId);
        Assert.Contains(productId.ToString("D"), audits[0].ChangedFieldsJson);
    }

    private async Task<JsonElement> UploadAndPublishAsync(HttpClient client, Guid productId)
    {
        using var uploadResponse = await UploadAsync(client, productId, altText: "正面");
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var imageId = uploaded.GetProperty("publicId").GetGuid();
        using var patch = await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client,
            new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/admin/product-images/{imageId}")
            {
                Content = JsonContent.Create(new
                {
                    altText = "正面",
                    sortOrder = 0,
                    sourceUrl = "https://example.com/source",
                    licenseName = "CC BY 4.0",
                    licenseUrl = "https://creativecommons.org/licenses/by/4.0/",
                    rowVersion = uploaded.GetProperty("rowVersion").GetString(),
                }),
            });
        patch.EnsureSuccessStatusCode();
        var patched = await patch.Content.ReadFromJsonAsync<JsonElement>();
        using var publish = await PublishAsync(client, imageId, patched.GetProperty("rowVersion").GetString()!);
        publish.EnsureSuccessStatusCode();
        return await publish.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> PublishAsync(HttpClient client, Guid imageId, string rowVersion) =>
        CatalogAdminApiFixture.SendWithAntiforgeryAsync(client,
            new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/product-images/{imageId}/actions/publish")
            {
                Content = JsonContent.Create(new { rowVersion }),
            });

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        Guid productId,
        string? altText = null,
        byte[]? bytes = null,
        string fileName = "front.png",
        string contentType = "image/png",
        string? sourceUrl = null,
        string? licenseUrl = null)
    {
        var file = new ByteArrayContent(bytes ?? OnePixelPng);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var content = new MultipartFormDataContent { { file, "file", fileName } };
        if (altText is not null)
        {
            content.Add(new StringContent(altText), "altText");
        }

        if (sourceUrl is not null)
        {
            content.Add(new StringContent(sourceUrl), "sourceUrl");
        }

        if (licenseUrl is not null)
        {
            content.Add(new StringContent(licenseUrl), "licenseUrl");
        }

        return await CatalogAdminApiFixture.SendWithAntiforgeryAsync(client,
            new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/products/{productId}/images") { Content = content });
    }

    /// <summary>一張 1×1 的合法 PNG——簽章、IHDR、IDAT、IEND 齊全，ImageSharp 解得開。</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
}
