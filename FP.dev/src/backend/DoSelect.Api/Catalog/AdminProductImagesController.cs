using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Application.Catalog;
using DoSelect.Application.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Catalog;

/// <summary>
/// M-03 商品圖片後台五條端點（API Endpoint 目錄「M 商品圖片」列；契約依檔案與圖片儲存設計.md）。
/// Policy 分三個：`CatalogImage.Manage`（上傳／改中繼資料／刪除）、`CatalogImage.ViewDraft`
/// （未發布預覽）、`CatalogImage.Publish`（核准）——角色與權限.md 三者都給 CatalogManager／SuperAdmin，
/// 但名稱分開是為了讓「能改不能發布」的角色以後只改一行。
/// </summary>
[ApiController]
[Route("api/v1/admin")]
public sealed class AdminProductImagesController : ControllerBase
{
    // 圖片最大 10 MB；多出來的是 multipart 邊界與四個文字欄位（與匯入、客服附件同一個做法）。
    private const long MultipartBodyLengthLimit = ProductImageConstraints.MaximumFileSizeBytes + 65_536;

    private readonly IProductImageAdminService _service;
    private readonly IAuthorizationPolicyProvider _policyProvider;
    private readonly IPolicyEvaluator _policyEvaluator;

    public AdminProductImagesController(
        IProductImageAdminService service,
        IAuthorizationPolicyProvider policyProvider,
        IPolicyEvaluator policyEvaluator)
    {
        _service = service;
        _policyProvider = policyProvider;
        _policyEvaluator = policyEvaluator;
    }

    /// <summary>上傳一張商品原圖並建立三種衍生圖；成功回 201 與圖片 DTO（狀態 Ready，尚未發布）。</summary>
    [HttpPost("products/{productId:guid}/images")]
    [Authorize(Policy = DoSelectPolicies.CatalogImageManage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MultipartBodyLengthLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = MultipartBodyLengthLimit, ValueCountLimit = 5)]
    [ProducesResponseType<AdminProductImageDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<AdminProductImageDto>> Upload(
        Guid productId,
        IFormFile? file,
        [FromForm] UploadProductImageForm form,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            var problem = ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationFailed,
                detail: "A multipart field named 'file' with the image is required.");
            return BadRequest(problem);
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var upload = new ProductImageUpload(stream, file.FileName, file.ContentType);
            var created = await _service.UploadAsync(
                productId,
                upload,
                new UploadProductImageMetadata(form.AltText, form.SourceUrl, form.LicenseName, form.LicenseUrl),
                ActorUserId(),
                BuildAuditContext(),
                cancellationToken);
            return StatusCode(StatusCodes.Status201Created, created);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    /// <summary>
    /// 後台預覽 original／320／800／1600。檔案與圖片儲存設計：「未登入、無 Catalog 權限、資源不屬於
    /// 允許範圍或檔案不存在時均回 404，不揭露檔案是否存在」——所以這裡不掛 [Authorize]（那會回
    /// 401／403），而是自己跑同一個 Policy，失敗一律 404。
    /// </summary>
    [HttpGet("product-images/{imageId:guid}/preview/{variant}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview(Guid imageId, string variant, CancellationToken cancellationToken)
    {
        var policy = await _policyProvider.GetPolicyAsync(DoSelectPolicies.CatalogImageViewDraft)
            ?? throw new InvalidOperationException($"Policy '{DoSelectPolicies.CatalogImageViewDraft}' is not registered.");
        var authentication = await _policyEvaluator.AuthenticateAsync(policy, HttpContext);
        var authorization = await _policyEvaluator.AuthorizeAsync(policy, authentication, HttpContext, resource: null);
        if (!authorization.Succeeded)
        {
            return NotFound();
        }

        var preview = await _service.OpenPreviewAsync(imageId, variant, cancellationToken);
        if (preview is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private,no-store";
        return File(preview.Content, preview.ContentType);
    }

    /// <summary>更新 Alt、排序與來源／授權中繼資料（RowVersion）。</summary>
    [HttpPatch("product-images/{imageId:guid}")]
    [Authorize(Policy = DoSelectPolicies.CatalogImageManage)]
    [ProducesResponseType<AdminProductImageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<AdminProductImageDto>> Update(
        Guid imageId,
        [FromBody] UpdateProductImageRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _service.UpdateAsync(
                imageId,
                new UpdateProductImageCommand(
                    request.AltText,
                    request.SortOrder,
                    request.SourceUrl,
                    request.LicenseName,
                    request.LicenseUrl,
                    request.RowVersion),
                ActorUserId(),
                BuildAuditContext(),
                cancellationToken);
            return Ok(updated);
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    /// <summary>核准並產生公開內容雜湊 URL；Alt／來源／授權不齊回 422 image_metadata_incomplete。</summary>
    [HttpPost("product-images/{imageId:guid}/actions/publish")]
    [Authorize(Policy = DoSelectPolicies.CatalogImagePublish)]
    [ProducesResponseType<AdminProductImageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<AdminProductImageDto>> Publish(
        Guid imageId,
        [FromBody] ProductImageActionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.PublishAsync(imageId, request.RowVersion, ActorUserId(), BuildAuditContext(), cancellationToken));
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    /// <summary>解除引用並依生命週期排程清理（RowVersion）。</summary>
    [HttpDelete("product-images/{imageId:guid}")]
    [Authorize(Policy = DoSelectPolicies.CatalogImageManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Delete(
        Guid imageId,
        [FromBody] ProductImageActionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _service.DeleteAsync(imageId, request.RowVersion, ActorUserId(), BuildAuditContext(), cancellationToken);
            return NoContent();
        }
        catch (CatalogWriteException exception)
        {
            return exception.ToActionResult(HttpContext);
        }
    }

    private string ActorUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private AuditRequestContext BuildAuditContext()
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString();
        return new AuditRequestContext(
            CorrelationIdMiddleware.GetCorrelationId(HttpContext),
            traceId,
            HttpContext.Connection.RemoteIpAddress);
    }
}

/// <summary>Multipart 文字欄位（檔案與圖片儲存設計：altText 160、sourceUrl 1000、licenseName 100、licenseUrl 1000）。</summary>
public sealed class UploadProductImageForm
{
    // 欄位名稱明寫成 camelCase：設計文件與前端 FormData 用的是 altText／sourceUrl…，OpenAPI 的
    // multipart schema 才會跟著顯示同一組名字（表單繫結本來就不分大小寫，這是給契約看的）。
    [FromForm(Name = "altText")]
    [StringLength(ProductImageMetadataLimits.AltTextMaxLength)]
    public string? AltText { get; init; }

    [FromForm(Name = "sourceUrl")]
    [StringLength(ProductImageMetadataLimits.SourceUrlMaxLength)]
    public string? SourceUrl { get; init; }

    [FromForm(Name = "licenseName")]
    [StringLength(ProductImageMetadataLimits.LicenseNameMaxLength)]
    public string? LicenseName { get; init; }

    [FromForm(Name = "licenseUrl")]
    [StringLength(ProductImageMetadataLimits.LicenseUrlMaxLength)]
    public string? LicenseUrl { get; init; }
}

/// <summary>`UpdateProductImageRequest`（API DTO與Schema契約）。</summary>
public sealed class UpdateProductImageRequest
{
    [Required]
    [StringLength(ProductImageMetadataLimits.AltTextMaxLength, MinimumLength = 1)]
    public string AltText { get; init; } = string.Empty;

    [Range(0, ProductImageMetadataLimits.SortOrderMax)]
    public int SortOrder { get; init; }

    [StringLength(ProductImageMetadataLimits.SourceUrlMaxLength)]
    public string? SourceUrl { get; init; }

    [StringLength(ProductImageMetadataLimits.LicenseNameMaxLength)]
    public string? LicenseName { get; init; }

    [StringLength(ProductImageMetadataLimits.LicenseUrlMaxLength)]
    public string? LicenseUrl { get; init; }

    [Required]
    [MinLength(1)]
    public byte[] RowVersion { get; init; } = [];
}

/// <summary>publish／delete 的請求：只帶 RowVersion。</summary>
public sealed class ProductImageActionRequest
{
    [Required]
    [MinLength(1)]
    public byte[] RowVersion { get; init; } = [];
}
