using System.Diagnostics;
using DoSelect.Application.Auditing;
using DoSelect.Application.Files;
using DoSelect.Application.Reviews;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Reviews;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Reviews;

public sealed class EfReviewService(
    DoSelectDbContext dbContext,
    IImageStorage imageStorage,
    IAuditWriter auditWriter,
    TimeProvider timeProvider) : IReviewService
{
    public async Task<IReadOnlyList<EligibleReviewOrderItemDto>> ListEligibleOrderItemsAsync(
        string memberUserId,
        CancellationToken cancellationToken)
    {
        memberUserId = RequireActor(memberUserId);
        return await (
            from item in dbContext.OrderItems.AsNoTracking()
            join order in dbContext.Orders.AsNoTracking() on item.OrderId equals order.Id
            join sku in dbContext.Skus.AsNoTracking() on item.SkuId equals sku.Id
            join product in dbContext.Products.AsNoTracking() on sku.ProductId equals product.Id
            join review in dbContext.ProductReviews.AsNoTracking() on item.Id equals review.OrderItemId into reviewRows
            from review in reviewRows.DefaultIfEmpty()
            where order.MemberUserId == memberUserId && order.OrderStatus == OrderStatus.Completed
            orderby order.CompletedAtUtc descending, item.Id descending
            select new EligibleReviewOrderItemDto(
                item.PublicId,
                product.PublicId,
                item.SkuCodeSnapshot,
                item.ProductNameSnapshot,
                item.SkuNameSnapshot,
                order.CompletedAtUtc!.Value,
                review == null ? null : review.PublicId,
                review == null ? null : ToApiStatus(review.Status)))
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MemberReviewDto>> ListMineAsync(
        string memberUserId,
        CancellationToken cancellationToken)
    {
        memberUserId = RequireActor(memberUserId);
        var rows = await (
            from review in dbContext.ProductReviews.AsNoTracking()
            join item in dbContext.OrderItems.AsNoTracking() on review.OrderItemId equals item.Id
            join product in dbContext.Products.AsNoTracking() on review.ProductId equals product.Id
            where review.MemberUserId == memberUserId
            orderby review.CreatedAtUtc descending
            select new MemberReviewListRow(
                review.Id,
                review.PublicId,
                item.PublicId,
                product.PublicId,
                item.ProductNameSnapshot,
                item.SkuNameSnapshot,
                review.Rating,
                review.Title,
                review.Content,
                review.Status,
                review.RejectionReason,
                review.CreatedAtUtc,
                review.UpdatedAtUtc,
                review.RowVersion))
            .Take(100)
            .ToListAsync(cancellationToken);
        var images = await LoadImagesByReviewAsync(
            rows.ToDictionary(row => row.ReviewId, row => row.PublicId),
            cancellationToken);
        return rows.Select(row => new MemberReviewDto(
            row.PublicId,
            row.OrderItemPublicId,
            row.ProductPublicId,
            row.ProductName,
            row.SkuName,
            row.Rating,
            row.Title,
            row.Content,
            ToApiStatus(row.Status),
            row.RejectionReason,
            AsUtc(row.CreatedAtUtc),
            AsUtc(row.UpdatedAtUtc),
            Convert.ToBase64String(row.RowVersion),
            ImagesFor(images, row.ReviewId))).ToArray();
    }

    public async Task<MemberReviewDto> CreateAsync(
        string memberUserId,
        CreateReviewRequest request,
        CancellationToken cancellationToken)
    {
        memberUserId = RequireActor(memberUserId);
        ValidateContent(request.Rating, request.Title, request.Content);
        var eligible = await (
            from item in dbContext.OrderItems
            join order in dbContext.Orders on item.OrderId equals order.Id
            join sku in dbContext.Skus on item.SkuId equals sku.Id
            where item.PublicId == request.OrderItemPublicId &&
                order.MemberUserId == memberUserId &&
                order.OrderStatus == OrderStatus.Completed
            select new { Item = item, ProductId = sku.ProductId })
            .SingleOrDefaultAsync(cancellationToken);
        if (eligible is null)
        {
            throw Error(ReviewWriteException.ErrorCodes.NotEligible,
                "Only the member who owns a completed order item can review it.");
        }

        if (await dbContext.ProductReviews.AnyAsync(
                review => review.OrderItemId == eligible.Item.Id,
                cancellationToken))
        {
            throw Error(ReviewWriteException.ErrorCodes.Conflict,
                "This order item already has a review.");
        }

        var now = UtcNow();
        var review = new ProductReview(
            Guid.CreateVersion7(), memberUserId, eligible.Item.Id, eligible.ProductId,
            request.Rating, request.Title, request.Content, now);
        if (request.Submit)
        {
            review.Submit(now);
        }

        dbContext.ProductReviews.Add(review);
        await SaveAsync(cancellationToken);
        return await MapMemberAsync(review, cancellationToken);
    }

    public async Task<MemberReviewDto> UpdateAsync(
        string memberUserId,
        Guid reviewPublicId,
        UpdateReviewRequest request,
        CancellationToken cancellationToken)
    {
        ValidateContent(request.Rating, request.Title, request.Content);
        var review = await LoadOwnedAsync(memberUserId, reviewPublicId, cancellationToken);
        CheckRowVersion(review.RowVersion, request.RowVersion);
        if (review.Status == ProductReviewStatus.Hidden)
        {
            throw Error(ReviewWriteException.ErrorCodes.Conflict,
                "A hidden review cannot be edited by the member.");
        }

        var now = UtcNow();
        PreservePublishedRevision(review, ReviewSupersededReason.MemberEdited, now);
        review.Edit(request.Rating, request.Title, request.Content, now);
        await SaveAsync(cancellationToken);
        return await MapMemberAsync(review, cancellationToken);
    }

    public async Task<MemberReviewDto> SubmitAsync(
        string memberUserId,
        Guid reviewPublicId,
        ReviewRowVersionRequest request,
        CancellationToken cancellationToken)
    {
        var review = await LoadOwnedAsync(memberUserId, reviewPublicId, cancellationToken);
        CheckRowVersion(review.RowVersion, request.RowVersion);
        try
        {
            review.Submit(UtcNow());
        }
        catch (InvalidOperationException exception)
        {
            throw Error(ReviewWriteException.ErrorCodes.Conflict, exception.Message);
        }

        await SaveAsync(cancellationToken);
        return await MapMemberAsync(review, cancellationToken);
    }

    public async Task WithdrawAsync(
        string memberUserId,
        Guid reviewPublicId,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var review = await LoadOwnedAsync(memberUserId, reviewPublicId, cancellationToken);
        CheckRowVersion(review.RowVersion, rowVersion);
        if (review.Status is ProductReviewStatus.Approved or ProductReviewStatus.Hidden)
        {
            throw Error(ReviewWriteException.ErrorCodes.Conflict,
                "A published or hidden review cannot be withdrawn by the member.");
        }

        var images = await dbContext.ReviewImages
            .Where(image => image.ProductReviewId == review.Id)
            .ToListAsync(cancellationToken);
        dbContext.ReviewImages.RemoveRange(images);
        dbContext.ProductReviews.Remove(review);
        await SaveAsync(cancellationToken);
        foreach (var image in images)
        {
            await imageStorage.DeleteAsync(image.StorageKey, cancellationToken);
        }
    }

    public async Task<MemberReviewDto> UploadImageAsync(
        string memberUserId,
        Guid reviewPublicId,
        ProductImageUpload upload,
        long? declaredLength,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        if (declaredLength is < 1 or > ReviewLimits.MaximumImageSizeBytes)
        {
            throw Error(ReviewWriteException.ErrorCodes.FileTooLarge,
                "A review image must be between 1 byte and 5 MB.");
        }

        if (upload.ContentType is not ("image/jpeg" or "image/png"))
        {
            throw Error(ReviewWriteException.ErrorCodes.FileTypeNotAllowed,
                "Only JPG and PNG review images are allowed.");
        }

        var review = await LoadOwnedAsync(memberUserId, reviewPublicId, cancellationToken);
        CheckRowVersion(review.RowVersion, rowVersion);
        if (review.Status == ProductReviewStatus.Hidden)
        {
            throw Error(ReviewWriteException.ErrorCodes.Conflict,
                "A hidden review cannot be edited by the member.");
        }

        var activeImages = await dbContext.ReviewImages
            .Where(image => image.ProductReviewId == review.Id && image.DeletedAtUtc == null)
            .OrderBy(image => image.SortOrder)
            .ToListAsync(cancellationToken);
        if (activeImages.Count >= ReviewLimits.MaximumImages)
        {
            throw Error(ReviewWriteException.ErrorCodes.ImageLimitExceeded,
                "A review can contain at most three images.");
        }

        var stored = await imageStorage.StoreAsync(upload, cancellationToken);
        if (!stored.IsStored)
        {
            throw MapStorageError(stored.Status);
        }

        var file = stored.Image!;
        try
        {
            var now = UtcNow();
            PreservePublishedRevision(review, ReviewSupersededReason.MemberEdited, now);
            TouchReviewForImageMutation(review, now);

            var nextSortOrder = activeImages.Count == 0
                ? 0
                : activeImages.Max(image => image.SortOrder) + 1;
            var image = new ReviewImage(
                review.Id, file.StorageKey, file.OriginalFileName, file.ContentType,
                file.OriginalFileSizeBytes, file.Sha256, nextSortOrder, now);
            image.RecordScan(ReviewImageScanStatus.Clean, now);
            dbContext.ReviewImages.Add(image);
            await SaveAsync(cancellationToken);
        }
        catch
        {
            await imageStorage.DeleteAsync(file.StorageKey, cancellationToken);
            throw;
        }

        return await MapMemberAsync(review, cancellationToken);
    }

    public async Task<MemberReviewDto> DeleteImageAsync(
        string memberUserId,
        Guid reviewPublicId,
        int sortOrder,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var review = await LoadOwnedAsync(memberUserId, reviewPublicId, cancellationToken);
        CheckRowVersion(review.RowVersion, rowVersion);
        if (review.Status == ProductReviewStatus.Hidden)
        {
            throw Error(ReviewWriteException.ErrorCodes.Conflict,
                "A hidden review cannot be edited by the member.");
        }

        var image = await dbContext.ReviewImages.SingleOrDefaultAsync(
            candidate => candidate.ProductReviewId == review.Id &&
                candidate.SortOrder == sortOrder && candidate.DeletedAtUtc == null,
            cancellationToken);
        if (image is null)
        {
            throw Error(ReviewWriteException.ErrorCodes.NotFound, "The review image was not found.");
        }

        var now = UtcNow();
        PreservePublishedRevision(review, ReviewSupersededReason.MemberEdited, now);
        TouchReviewForImageMutation(review, now);

        image.MarkDeleted(now);
        await SaveAsync(cancellationToken);
        await imageStorage.DeleteAsync(image.StorageKey, cancellationToken);
        return await MapMemberAsync(review, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminReviewDto>> ListForModerationAsync(
        string? status,
        CancellationToken cancellationToken)
    {
        ProductReviewStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ProductReviewStatus>(status, true, out var parsed))
            {
                throw Error(ReviewWriteException.ErrorCodes.ValidationFailed,
                    "The review status filter is invalid.");
            }

            parsedStatus = parsed;
        }

        var query = dbContext.ProductReviews.AsNoTracking();
        query = parsedStatus.HasValue
            ? query.Where(review => review.Status == parsedStatus.Value)
            : query.Where(review => review.Status == ProductReviewStatus.PendingReview);
        var reviews = await (
            from review in query
            join item in dbContext.OrderItems.AsNoTracking() on review.OrderItemId equals item.Id
            join product in dbContext.Products.AsNoTracking() on review.ProductId equals product.Id
            orderby review.UpdatedAtUtc
            select new AdminReviewListRow(
                review.Id,
                review.PublicId,
                product.PublicId,
                item.ProductNameSnapshot,
                item.SkuNameSnapshot,
                review.Rating,
                review.Title,
                review.Content,
                review.Status,
                review.RejectionReason,
                review.CreatedAtUtc,
                review.ReviewedAtUtc,
                review.RowVersion))
            .Take(100)
            .ToListAsync(cancellationToken);
        var images = await LoadImagesByReviewAsync(
            reviews.ToDictionary(row => row.ReviewId, row => row.PublicId),
            cancellationToken);
        return reviews.Select(row => new AdminReviewDto(
            row.PublicId,
            row.ProductPublicId,
            row.ProductName,
            row.SkuName,
            row.Rating,
            row.Title,
            row.Content,
            ToApiStatus(row.Status),
            row.RejectionReason,
            AsUtc(row.CreatedAtUtc),
            row.ReviewedAtUtc is null ? null : AsUtc(row.ReviewedAtUtc.Value),
            Convert.ToBase64String(row.RowVersion),
            ImagesFor(images, row.ReviewId))).ToArray();
    }

    public async Task<AdminReviewDto?> GetForModerationAsync(
        Guid reviewPublicId,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.ProductReviews.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.PublicId == reviewPublicId, cancellationToken);
        return review is null ? null : await MapAdminAsync(review, cancellationToken);
    }

    public async Task<AdminReviewDto> ModerateAsync(
        ReviewAdminActor actor,
        Guid reviewPublicId,
        string action,
        ReviewModerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var review = await dbContext.ProductReviews
            .SingleOrDefaultAsync(candidate => candidate.PublicId == reviewPublicId, cancellationToken)
            ?? throw Error(ReviewWriteException.ErrorCodes.NotFound, "The review was not found.");
        CheckRowVersion(review.RowVersion, request.RowVersion);
        var admin = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == actor.UserId, cancellationToken)
            ?? throw Error(ReviewWriteException.ErrorCodes.NotFound, "The administrator was not found.");
        var normalizedAction = action.Trim().ToLowerInvariant();
        var now = UtcNow();
        var before = review.Status;

        try
        {
            switch (normalizedAction)
            {
                case "approve":
                    if (await dbContext.ReviewImages.AnyAsync(
                            image => image.ProductReviewId == review.Id &&
                                image.DeletedAtUtc == null &&
                                image.ScanStatus != ReviewImageScanStatus.Clean,
                            cancellationToken))
                    {
                        throw Error(ReviewWriteException.ErrorCodes.Conflict,
                            "Every active review image must pass scanning before approval.");
                    }

                    review.Review(actor.UserId, approved: true, rejectionReason: null, now);
                    break;
                case "reject":
                    review.Review(actor.UserId, approved: false, ModerationReason(request), now);
                    break;
                case "hide":
                    PreservePublishedRevision(review, ReviewSupersededReason.AdminHidden, now);
                    review.Hide(actor.UserId, now);
                    break;
                case "restore":
                    review.Restore(actor.UserId, now);
                    break;
                default:
                    throw Error(ReviewWriteException.ErrorCodes.ValidationFailed,
                        "The moderation action is invalid.");
            }
        }
        catch (InvalidOperationException exception)
        {
            throw Error(ReviewWriteException.ErrorCodes.Conflict, exception.Message);
        }

        var auditAction = normalizedAction switch
        {
            "approve" => AuditActions.ProductReviewApprove,
            "reject" => AuditActions.ProductReviewReject,
            "hide" => AuditActions.ProductReviewHide,
            "restore" => AuditActions.ProductReviewRestore,
            _ => throw new UnreachableException(),
        };
        auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            AuditActor.Create(AuditActorType.Admin, admin.PublicId, actor.Roles),
            auditAction,
            AuditResourceTypes.ProductReview,
            review.PublicId,
            AuditResult.Success,
            errorCode: null,
            [AuditFieldChange.Code("status", ToApiStatus(before), ToApiStatus(review.Status))],
            RequireReasonCode(request.ReasonCode),
            actor.AuditContext.CorrelationId,
            actor.AuditContext.TraceId,
            jobPublicId: null,
            actor.AuditContext.RemoteIpAddress));
        await SaveAsync(cancellationToken);
        return await MapAdminAsync(review, cancellationToken);
    }

    public async Task<IReadOnlyList<PublicProductReviewDto>> ListPublicAsync(
        Guid productPublicId,
        CancellationToken cancellationToken)
    {
        var reviews = await (
            from review in dbContext.ProductReviews.AsNoTracking()
            join product in dbContext.Products.AsNoTracking() on review.ProductId equals product.Id
            where product.PublicId == productPublicId &&
                product.Status == ProductStatus.Published &&
                review.Status == ProductReviewStatus.Approved
            orderby review.ReviewedAtUtc descending
            select new PublicReviewListRow(
                review.Id,
                review.PublicId,
                review.Rating,
                review.Title,
                review.Content,
                review.ReviewedAtUtc,
                review.UpdatedAtUtc))
            .Take(100)
            .ToListAsync(cancellationToken);
        var images = await LoadImagesByReviewAsync(
            reviews.ToDictionary(row => row.ReviewId, row => row.PublicId),
            cancellationToken);
        return reviews.Select(row => new PublicProductReviewDto(
            row.PublicId,
            row.Rating,
            row.Title,
            row.Content,
            IsVerifiedPurchase: true,
            AsUtc(row.ReviewedAtUtc ?? row.UpdatedAtUtc),
            ImagesFor(images, row.ReviewId))).ToArray();
    }

    private async Task<ProductReview> LoadOwnedAsync(
        string memberUserId,
        Guid reviewPublicId,
        CancellationToken cancellationToken)
    {
        memberUserId = RequireActor(memberUserId);
        return await dbContext.ProductReviews.SingleOrDefaultAsync(
            review => review.PublicId == reviewPublicId && review.MemberUserId == memberUserId,
            cancellationToken) ?? throw Error(
            ReviewWriteException.ErrorCodes.NotFound, "The review was not found.");
    }

    private async Task<MemberReviewDto> MapMemberAsync(
        ProductReview review,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.OrderItems.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == review.OrderItemId, cancellationToken);
        var productPublicId = await dbContext.Products.AsNoTracking()
            .Where(candidate => candidate.Id == review.ProductId)
            .Select(candidate => candidate.PublicId)
            .SingleAsync(cancellationToken);
        return new MemberReviewDto(
            review.PublicId,
            item.PublicId,
            productPublicId,
            item.ProductNameSnapshot,
            item.SkuNameSnapshot,
            review.Rating,
            review.Title,
            review.Content,
            ToApiStatus(review.Status),
            review.RejectionReason,
            AsUtc(review.CreatedAtUtc),
            AsUtc(review.UpdatedAtUtc),
            Convert.ToBase64String(review.RowVersion),
            await LoadImagesAsync(review, cancellationToken));
    }

    private async Task<AdminReviewDto> MapAdminAsync(
        ProductReview review,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.OrderItems.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == review.OrderItemId, cancellationToken);
        var productPublicId = await dbContext.Products.AsNoTracking()
            .Where(candidate => candidate.Id == review.ProductId)
            .Select(candidate => candidate.PublicId)
            .SingleAsync(cancellationToken);
        return new AdminReviewDto(
            review.PublicId,
            productPublicId,
            item.ProductNameSnapshot,
            item.SkuNameSnapshot,
            review.Rating,
            review.Title,
            review.Content,
            ToApiStatus(review.Status),
            review.RejectionReason,
            AsUtc(review.CreatedAtUtc),
            review.ReviewedAtUtc is null ? null : AsUtc(review.ReviewedAtUtc.Value),
            Convert.ToBase64String(review.RowVersion),
            await LoadImagesAsync(review, cancellationToken));
    }

    private async Task<IReadOnlyList<ReviewImageDto>> LoadImagesAsync(
        ProductReview review,
        CancellationToken cancellationToken) =>
        await dbContext.ReviewImages.AsNoTracking()
            .Where(image => image.ProductReviewId == review.Id &&
                image.DeletedAtUtc == null && image.ScanStatus == ReviewImageScanStatus.Clean)
            .OrderBy(image => image.SortOrder)
            .Select(image => new ReviewImageDto(
                image.SortOrder,
                image.OriginalFileName,
                image.MediaType,
                image.FileSizeBytes,
                $"/media/reviews/{review.PublicId}/{image.SortOrder}/800"))
            .ToListAsync(cancellationToken);

    private async Task<Dictionary<long, IReadOnlyList<ReviewImageDto>>> LoadImagesByReviewAsync(
        IReadOnlyDictionary<long, Guid> reviewPublicIds,
        CancellationToken cancellationToken)
    {
        if (reviewPublicIds.Count == 0)
        {
            return [];
        }

        var reviewIds = reviewPublicIds.Keys.ToArray();
        var rows = await dbContext.ReviewImages.AsNoTracking()
            .Where(image => reviewIds.Contains(image.ProductReviewId) &&
                image.DeletedAtUtc == null && image.ScanStatus == ReviewImageScanStatus.Clean)
            .OrderBy(image => image.ProductReviewId)
            .ThenBy(image => image.SortOrder)
            .Select(image => new ReviewImageListRow(
                image.ProductReviewId,
                image.SortOrder,
                image.OriginalFileName,
                image.MediaType,
                image.FileSizeBytes))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.ProductReviewId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ReviewImageDto>)group.Select(row => new ReviewImageDto(
                    row.SortOrder,
                    row.OriginalFileName,
                    row.MediaType,
                    row.FileSizeBytes,
                    $"/media/reviews/{reviewPublicIds[group.Key]}/{row.SortOrder}/800"))
                    .ToArray());
    }

    private static IReadOnlyList<ReviewImageDto> ImagesFor(
        IReadOnlyDictionary<long, IReadOnlyList<ReviewImageDto>> images,
        long reviewId) => images.TryGetValue(reviewId, out var rows) ? rows : [];

    private void PreservePublishedRevision(
        ProductReview review,
        ReviewSupersededReason reason,
        DateTime now)
    {
        if (review.Status != ProductReviewStatus.Approved)
        {
            return;
        }

        dbContext.ProductReviewRevisions.Add(new ProductReviewRevision(
            review.Id,
            review.Rating,
            review.Title,
            review.Content,
            AsUtc(review.ReviewedAtUtc ?? review.UpdatedAtUtc),
            now,
            reason,
            now));
    }

    private void TouchReviewForImageMutation(ProductReview review, DateTime now)
    {
        review.Edit(review.Rating, review.Title, review.Content, now);
        dbContext.Entry(review)
            .Property(candidate => candidate.UpdatedAtUtc)
            .IsModified = true;
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ReviewWriteException(
                ReviewWriteException.ErrorCodes.ConcurrencyConflict,
                "The review changed. Reload it and try again.");
        }
        catch (DbUpdateException)
        {
            throw new ReviewWriteException(
                ReviewWriteException.ErrorCodes.Conflict,
                "The review conflicts with existing data.");
        }
    }

    private static void ValidateContent(byte rating, string? title, string content)
    {
        if (rating is < 1 or > 5 ||
            title?.Trim().Length > ReviewLimits.MaximumTitleLength ||
            string.IsNullOrWhiteSpace(content) ||
            content.Trim().Length > ReviewLimits.MaximumContentLength)
        {
            throw Error(ReviewWriteException.ErrorCodes.ValidationFailed,
                "Rating must be 1-5, title at most 80 characters, and content 1-1000 characters.");
        }
    }

    private static void CheckRowVersion(byte[] actual, string supplied)
    {
        byte[] parsed;
        try
        {
            parsed = Convert.FromBase64String(supplied ?? string.Empty);
        }
        catch (FormatException)
        {
            throw Error(ReviewWriteException.ErrorCodes.ValidationFailed,
                "The review row version is invalid.");
        }

        if (!actual.SequenceEqual(parsed))
        {
            throw Error(ReviewWriteException.ErrorCodes.ConcurrencyConflict,
                "The review changed. Reload it and try again.");
        }
    }

    private static string ModerationReason(ReviewModerationRequest request) =>
        string.IsNullOrWhiteSpace(request.Note)
            ? RequireReasonCode(request.ReasonCode)
            : request.Note.Trim() is { Length: <= 500 } note
                ? note
                : throw Error(ReviewWriteException.ErrorCodes.ValidationFailed,
                    "The moderation note cannot exceed 500 characters.");

    private static string RequireReasonCode(string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 64 || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw Error(ReviewWriteException.ErrorCodes.ValidationFailed,
                "A stable moderation reason code is required.");
        }

        return value;
    }

    private static string RequireActor(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw Error(ReviewWriteException.ErrorCodes.NotFound, "The member was not found.")
            : value.Trim();

    private static ReviewWriteException MapStorageError(ProductImageStoreStatus status) => status switch
    {
        ProductImageStoreStatus.SizeExceeded => Error(
            ReviewWriteException.ErrorCodes.FileTooLarge, "The review image exceeds 5 MB."),
        ProductImageStoreStatus.FormatInvalid or ProductImageStoreStatus.ProcessingFailed => Error(
            ReviewWriteException.ErrorCodes.FileTypeNotAllowed, "The review image is not a valid JPG or PNG."),
        ProductImageStoreStatus.MalwareDetected => Error(
            ReviewWriteException.ErrorCodes.FileMalwareDetected, "Malware was detected in the review image."),
        _ => Error(ReviewWriteException.ErrorCodes.FileScanUnavailable,
            "Review image scanning is temporarily unavailable."),
    };

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed record MemberReviewListRow(
        long ReviewId,
        Guid PublicId,
        Guid OrderItemPublicId,
        Guid ProductPublicId,
        string ProductName,
        string SkuName,
        byte Rating,
        string? Title,
        string Content,
        ProductReviewStatus Status,
        string? RejectionReason,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        byte[] RowVersion);

    private sealed record AdminReviewListRow(
        long ReviewId,
        Guid PublicId,
        Guid ProductPublicId,
        string ProductName,
        string SkuName,
        byte Rating,
        string? Title,
        string Content,
        ProductReviewStatus Status,
        string? RejectionReason,
        DateTime CreatedAtUtc,
        DateTime? ReviewedAtUtc,
        byte[] RowVersion);

    private sealed record PublicReviewListRow(
        long ReviewId,
        Guid PublicId,
        byte Rating,
        string? Title,
        string Content,
        DateTime? ReviewedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record ReviewImageListRow(
        long ProductReviewId,
        int SortOrder,
        string OriginalFileName,
        string MediaType,
        long FileSizeBytes);

    private static string ToApiStatus(ProductReviewStatus status)
    {
        var value = status.ToString();
        return char.ToLowerInvariant(value[0]) + value[1..];
    }

    private static ReviewWriteException Error(string code, string message) => new(code, message);
}
