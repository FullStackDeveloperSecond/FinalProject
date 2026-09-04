using System.Globalization;
using DoSelect.Application.Auditing;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using DoSelect.Application.Files;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

/// <summary>
/// M-03 商品圖片後台（檔案與圖片儲存設計.md「商品圖片上傳、後台預覽、發布及中繼資料寫入 Endpoint
/// 屬 M-03 商品功能垂直切片；須沿用既有儲存與清理能力，不自建第二套檔案服務」）。
/// 檔案的檢查、掃描、衍生圖與 Staging→正式目錄的原子發布全部在 <see cref="IImageStorage"/>；
/// 這裡只負責 ProductImages 那一列的生命週期：Processing→Ready→Published→Deleted。
///
/// 組長 PR #101 裁定 B：upload／update／publish／delete 都寫中央 Audit，且與資料庫異動同一次
/// SaveChanges——稽核先加進 ChangeTracker，再跟圖片那一列一起提交，寫不進去就整筆不成立。
/// </summary>
public sealed class EfProductImageAdminService : IProductImageAdminService
{
    private readonly DoSelectDbContext _dbContext;
    private readonly IImageStorage _imageStorage;
    private readonly IAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    public EfProductImageAdminService(
        DoSelectDbContext dbContext,
        IImageStorage imageStorage,
        IAuditWriter auditWriter,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _imageStorage = imageStorage;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
    }

    public async Task<AdminProductImageDto> UploadAsync(
        Guid productPublicId,
        ProductImageUpload upload,
        UploadProductImageMetadata metadata,
        string actorUserId,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(auditContext);

        var actor = await ResolveActorAsync(actorUserId, cancellationToken);
        var product = await _dbContext.Products.AsNoTracking()
            .Where(candidate => candidate.PublicId == productPublicId)
            .Select(candidate => new { candidate.Id, candidate.PublicId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ResourceNotFound, "The product was not found.");

        // 中繼資料先驗再存檔：長度或網址不對就不該花掃描與轉圖的成本，也不會留下要清的孤兒檔。
        var sourceUrl = RequireOptionalHttpUrl(metadata.SourceUrl, ProductImageMetadataLimits.SourceUrlMaxLength, "sourceUrl");
        var licenseName = RequireOptional(metadata.LicenseName, ProductImageMetadataLimits.LicenseNameMaxLength, "licenseName");
        var licenseUrl = RequireOptionalHttpUrl(metadata.LicenseUrl, ProductImageMetadataLimits.LicenseUrlMaxLength, "licenseUrl");
        var altText = RequireOptional(metadata.AltText, ProductImageMetadataLimits.AltTextMaxLength, "altText")
            ?? DefaultAltText(upload.OriginalFileName);

        var stored = await _imageStorage.StoreAsync(upload, cancellationToken);
        if (!stored.IsStored)
        {
            throw MapStorageFailure(stored.Status);
        }

        var file = stored.Image!;
        try
        {
            var now = UtcNow();
            var nextSortOrder = (await _dbContext.ProductImages
                .Where(image => image.ProductId == product.Id && image.Status != ProductImageStatus.Deleted)
                .MaxAsync(image => (int?)image.SortOrder, cancellationToken) ?? -1) + 1;

            var image = new ProductImage(
                Guid.CreateVersion7(),
                product.Id,
                skuId: null,
                file.StorageKey,
                file.OriginalFileName,
                file.ContentType,
                file.OriginalFileSizeBytes,
                file.Width,
                file.Height,
                file.Sha256,
                altText,
                now);
            image.RecordVariantHashes(
                VariantHash(file, ProductImageVariant.Small320),
                VariantHash(file, ProductImageVariant.Medium800),
                VariantHash(file, ProductImageVariant.Large1600),
                now);
            // 三種衍生圖都已經在正式目錄裡（IImageStorage 只在全部成功後才 Move），所以上傳流程
            // 在這一步就結束：Ready、可預覽、尚未發布。
            image.MarkReady(now);
            image.UpdateMetadata(altText, nextSortOrder, sourceUrl, licenseName, licenseUrl, now);

            _dbContext.ProductImages.Add(image);
            _auditWriter.Add(AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                actor,
                AuditActions.ProductImageUpload,
                AuditResourceTypes.ProductImage,
                image.PublicId,
                AuditResult.Success,
                errorCode: null,
                [
                    AuditFieldChange.Code(ProductImageAuditFields.ProductPublicId, null, product.PublicId.ToString("D")),
                    AuditFieldChange.Code(ProductImageAuditFields.Status, null, image.Status.ToString()),
                    AuditFieldChange.Code(ProductImageAuditFields.SortOrder, null, Number(image.SortOrder)),
                    AuditFieldChange.Code(ProductImageAuditFields.HasCompleteMetadata, null, Flag(image.HasCompleteMetadata)),
                ],
                reason: ProductImageAuditReasons.AdminUpload,
                auditContext.CorrelationId,
                auditContext.TraceId,
                jobPublicId: null,
                auditContext.RemoteIpAddress));
            await _dbContext.SaveChangesAsync(cancellationToken);

            return ProductImageProjection.ToAdminDto(image, product.PublicId, await IsPrimaryAsync(image, cancellationToken));
        }
        catch
        {
            // 「上傳完成但資料交易失敗時立即排程孤兒清理」——本機儲存直接刪比排程更快也更確定；
            // 刪不掉的，StorageMaintenanceJob 的 24 小時孤兒目錄清理會接手。
            await _imageStorage.DeleteAsync(file.StorageKey, cancellationToken);
            throw;
        }
    }

    public async Task<ProductImagePreview?> OpenPreviewAsync(
        Guid imagePublicId,
        string variant,
        CancellationToken cancellationToken)
    {
        if (!ProductImageVariantNames.TryParse(variant, out var imageVariant))
        {
            return null;
        }

        var image = await _dbContext.ProductImages.AsNoTracking()
            .Where(candidate => candidate.PublicId == imagePublicId)
            .Select(candidate => new { candidate.StorageKey, candidate.MediaType, candidate.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (image is null || !ProductImageProjection.IsVisibleToAdmin(image.Status))
        {
            return null;
        }

        var stream = await _imageStorage.OpenReadAsync(image.StorageKey, imageVariant, cancellationToken);
        if (stream is null)
        {
            return null;
        }

        return new ProductImagePreview(
            stream,
            imageVariant == ProductImageVariant.Original ? image.MediaType : "image/webp");
    }

    public async Task<AdminProductImageDto> UpdateAsync(
        Guid imagePublicId,
        UpdateProductImageCommand command,
        string actorUserId,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(auditContext);
        var altText = RequireOptional(command.AltText, ProductImageMetadataLimits.AltTextMaxLength, "altText")
            ?? throw new CatalogWriteException(CatalogWriteException.ErrorCodes.ValidationFailed, "altText is required.");
        if (command.SortOrder is < 0 or > ProductImageMetadataLimits.SortOrderMax)
        {
            throw new CatalogWriteException(CatalogWriteException.ErrorCodes.ValidationFailed, "sortOrder is out of range.");
        }

        var sourceUrl = RequireOptionalHttpUrl(command.SourceUrl, ProductImageMetadataLimits.SourceUrlMaxLength, "sourceUrl");
        var licenseName = RequireOptional(command.LicenseName, ProductImageMetadataLimits.LicenseNameMaxLength, "licenseName");
        var licenseUrl = RequireOptionalHttpUrl(command.LicenseUrl, ProductImageMetadataLimits.LicenseUrlMaxLength, "licenseUrl");

        var actor = await ResolveActorAsync(actorUserId, cancellationToken);
        var (image, productPublicId) = await LoadForWriteAsync(imagePublicId, command.RowVersion, cancellationToken);

        // 組長 PR #101 item 1：已發布的圖片不能被改成中繼資料不完整卻繼續公開。發布門檻與這裡
        // 是同一條規則，只是方向相反——公開中的圖片要維持門檻。
        if (image.Status == ProductImageStatus.Published &&
            (sourceUrl is null || licenseName is null || licenseUrl is null))
        {
            throw DomainProblemException.UnprocessableEntity(
                DomainErrorCodes.ImageMetadataIncomplete,
                "A published image must keep its source URL, license name and license URL.");
        }

        var sortOrderBefore = image.SortOrder;
        var completeBefore = image.HasCompleteMetadata;
        image.UpdateMetadata(altText, command.SortOrder, sourceUrl, licenseName, licenseUrl, UtcNow());

        var changes = new List<AuditFieldChange>
        {
            AuditFieldChange.Code(ProductImageAuditFields.ProductPublicId, null, productPublicId.ToString("D")),
            AuditFieldChange.Changed(ProductImageAuditFields.Metadata),
        };
        if (sortOrderBefore != image.SortOrder)
        {
            changes.Add(AuditFieldChange.Code(ProductImageAuditFields.SortOrder, Number(sortOrderBefore), Number(image.SortOrder)));
        }

        if (completeBefore != image.HasCompleteMetadata)
        {
            changes.Add(AuditFieldChange.Code(ProductImageAuditFields.HasCompleteMetadata, Flag(completeBefore), Flag(image.HasCompleteMetadata)));
        }

        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            actor,
            AuditActions.ProductImageUpdate,
            AuditResourceTypes.ProductImage,
            image.PublicId,
            AuditResult.Success,
            errorCode: null,
            changes,
            reason: ProductImageAuditReasons.AdminEdit,
            auditContext.CorrelationId,
            auditContext.TraceId,
            jobPublicId: null,
            auditContext.RemoteIpAddress));
        await SaveWithConcurrencyCheckAsync(cancellationToken);

        return ProductImageProjection.ToAdminDto(image, productPublicId, await IsPrimaryAsync(image, cancellationToken));
    }

    public async Task<AdminProductImageDto> PublishAsync(
        Guid imagePublicId,
        byte[] rowVersion,
        string actorUserId,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditContext);
        var actor = await ResolveActorAsync(actorUserId, cancellationToken);
        var (image, productPublicId) = await LoadForWriteAsync(imagePublicId, rowVersion, cancellationToken);
        if (image.Status != ProductImageStatus.Published)
        {
            if (!image.HasCompleteMetadata)
            {
                throw DomainProblemException.UnprocessableEntity(
                    DomainErrorCodes.ImageMetadataIncomplete,
                    "Alt text, source URL, license name and license URL are required before publishing.");
            }

            // 組長 PR #101 裁定 C：現階段只允許 Ready → Published；Domain 的 Publish 守著這條邊。
            var statusBefore = image.Status;
            image.Publish(UtcNow());
            _auditWriter.Add(AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                actor,
                AuditActions.ProductImagePublish,
                AuditResourceTypes.ProductImage,
                image.PublicId,
                AuditResult.Success,
                errorCode: null,
                [
                    AuditFieldChange.Code(ProductImageAuditFields.ProductPublicId, null, productPublicId.ToString("D")),
                    AuditFieldChange.Code(ProductImageAuditFields.Status, statusBefore.ToString(), image.Status.ToString()),
                    AuditFieldChange.Code(ProductImageAuditFields.SortOrder, null, Number(image.SortOrder)),
                    AuditFieldChange.Code(ProductImageAuditFields.HasCompleteMetadata, null, Flag(image.HasCompleteMetadata)),
                ],
                reason: ProductImageAuditReasons.AdminPublish,
                auditContext.CorrelationId,
                auditContext.TraceId,
                jobPublicId: null,
                auditContext.RemoteIpAddress));
            await SaveWithConcurrencyCheckAsync(cancellationToken);
        }

        return ProductImageProjection.ToAdminDto(image, productPublicId, await IsPrimaryAsync(image, cancellationToken));
    }

    public async Task DeleteAsync(
        Guid imagePublicId,
        byte[] rowVersion,
        string actorUserId,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditContext);
        var actor = await ResolveActorAsync(actorUserId, cancellationToken);
        var (image, productPublicId) = await LoadForWriteAsync(imagePublicId, rowVersion, cancellationToken);
        var statusBefore = image.Status;
        image.MarkDeleted(UtcNow());
        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            actor,
            AuditActions.ProductImageDelete,
            AuditResourceTypes.ProductImage,
            image.PublicId,
            AuditResult.Success,
            errorCode: null,
            [
                AuditFieldChange.Code(ProductImageAuditFields.ProductPublicId, null, productPublicId.ToString("D")),
                AuditFieldChange.Code(ProductImageAuditFields.Status, statusBefore.ToString(), image.Status.ToString()),
                AuditFieldChange.Code(ProductImageAuditFields.SortOrder, Number(image.SortOrder), null),
            ],
            reason: ProductImageAuditReasons.AdminDelete,
            auditContext.CorrelationId,
            auditContext.TraceId,
            jobPublicId: null,
            auditContext.RemoteIpAddress));
        await SaveWithConcurrencyCheckAsync(cancellationToken);
    }

    private async Task<(ProductImage Image, Guid ProductPublicId)> LoadForWriteAsync(
        Guid imagePublicId,
        byte[] rowVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);
        var image = await _dbContext.ProductImages
            .SingleOrDefaultAsync(candidate => candidate.PublicId == imagePublicId, cancellationToken);
        if (image is null || !ProductImageProjection.IsVisibleToAdmin(image.Status))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ResourceNotFound, "The product image was not found.");
        }

        // 先比對再交給資料庫：發布已發布的圖片不會發 UPDATE，光靠 SaveChanges 抓不到過期的 RowVersion。
        if (!image.RowVersion.AsSpan().SequenceEqual(rowVersion))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ConcurrencyConflict,
                "The product image was modified by someone else. Reload and try again.");
        }

        _dbContext.Entry(image).Property(candidate => candidate.RowVersion).OriginalValue = rowVersion;

        var productPublicId = await _dbContext.Products.AsNoTracking()
            .Where(product => product.Id == image.ProductId)
            .Select(product => product.PublicId)
            .SingleAsync(cancellationToken);
        return (image, productPublicId);
    }

    private async Task SaveWithConcurrencyCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ConcurrencyConflict,
                "The product image was modified by someone else. Reload and try again.");
        }
    }

    private async Task<bool> IsPrimaryAsync(ProductImage image, CancellationToken cancellationToken)
    {
        if (!ProductImageProjection.IsVisibleToAdmin(image.Status))
        {
            return false;
        }

        var firstId = await _dbContext.ProductImages.AsNoTracking()
            .Where(candidate => candidate.ProductId == image.ProductId &&
                candidate.Status != ProductImageStatus.Deleted &&
                candidate.Status != ProductImageStatus.PendingDelete)
            .OrderBy(candidate => candidate.SortOrder)
            .ThenBy(candidate => candidate.CreatedAtUtc)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return firstId == image.Id;
    }

    /// <summary>Same shape as EfProductAdminService.BulkActions.ResolveActorAsync：稽核的角色快照從真正的 UserRoles 讀。</summary>
    private async Task<AuditActor> ResolveActorAsync(string actorUserId, CancellationToken cancellationToken)
    {
        var admin = await _dbContext.Users.AsNoTracking()
            .Where(user => user.Id == actorUserId && user.AccountType == DoSelect.Domain.Members.AccountType.Admin)
            .Select(user => new { user.Id, user.PublicId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "The administrator identity is invalid.");

        var roles = await (
            from userRole in _dbContext.UserRoles.AsNoTracking()
            join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == admin.Id && role.Name != null
            select role.Name!)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (!roles.Contains(AuditRoleNames.CatalogManager, StringComparer.Ordinal) &&
            !roles.Contains(AuditRoleNames.SuperAdmin, StringComparer.Ordinal))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "The administrator is not allowed to manage product images.");
        }

        return AuditActor.Create(AuditActorType.Admin, admin.PublicId, roles);
    }

    private static byte[] VariantHash(StoredProductImage file, ProductImageVariant variant) =>
        file.Variants.Single(candidate => candidate.Variant == variant).Sha256;

    private static string DefaultAltText(string originalFileName)
    {
        var name = Path.GetFileNameWithoutExtension(originalFileName).Trim();
        if (name.Length == 0)
        {
            return "商品圖片";
        }

        return name.Length <= ProductImageMetadataLimits.AltTextMaxLength
            ? name
            : name[..ProductImageMetadataLimits.AltTextMaxLength];
    }

    private static string? RequireOptional(string? value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (value.Length > maximumLength)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                $"{field} cannot exceed {maximumLength} characters.");
        }

        return value;
    }

    /// <summary>組長 PR #101 裁定 D：來源與授權網址只接受 absolute HTTP／HTTPS URL。</summary>
    private static string? RequireOptionalHttpUrl(string? value, int maximumLength, string field)
    {
        var trimmed = RequireOptional(value, maximumLength, field);
        if (trimmed is null)
        {
            return null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                $"{field} must be an absolute http or https URL.");
        }

        return trimmed;
    }

    /// <summary>檔案與圖片儲存設計「API 與錯誤契約」：413／415／422／503 與對應錯誤碼。</summary>
    private static DomainProblemException MapStorageFailure(ProductImageStoreStatus status) => status switch
    {
        ProductImageStoreStatus.SizeExceeded => DomainProblemException.PayloadTooLarge(
            DomainErrorCodes.FileSizeExceeded, "The uploaded image exceeds 10 MB."),
        ProductImageStoreStatus.FormatInvalid => DomainProblemException.UnsupportedMediaType(
            DomainErrorCodes.FileFormatInvalid, "Only JPG, PNG and WebP product images are allowed."),
        ProductImageStoreStatus.MalwareDetected => DomainProblemException.UnprocessableEntity(
            DomainErrorCodes.FileMalwareDetected, "The uploaded image did not pass the security scan."),
        ProductImageStoreStatus.ScanUnavailable => DomainProblemException.ServiceUnavailable(
            DomainErrorCodes.FileScanUnavailable, "The file security scan is temporarily unavailable."),
        ProductImageStoreStatus.ProcessingFailed => DomainProblemException.UnprocessableEntity(
            DomainErrorCodes.ImageProcessingFailed, "The uploaded image could not be decoded or safely processed."),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A stored image has no failure response."),
    };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Flag(bool value) => value ? "true" : "false";

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
