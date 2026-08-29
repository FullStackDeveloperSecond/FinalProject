using DoSelect.Domain.Common;

namespace DoSelect.Domain.Reviews;

public enum ProductReviewStatus
{
    Draft,
    PendingReview,
    Approved,
    Rejected,
    Hidden,
}

public enum ReviewImageScanStatus
{
    Pending,
    Clean,
    Rejected,
    Failed,
}

public enum ReviewSupersededReason
{
    MemberEdited,
    AdminHidden,
    AdminRejectedAfterPublish,
}

public sealed class ProductReview : MutablePublicEntity
{
    private ProductReview() { }

    public ProductReview(
        Guid publicId,
        string memberUserId,
        long orderItemId,
        long productId,
        byte rating,
        string? title,
        string content,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (orderItemId <= 0 || productId <= 0 || rating is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(rating));
        }

        MemberUserId = RequireText(memberUserId, nameof(memberUserId));
        OrderItemId = orderItemId;
        ProductId = productId;
        Rating = rating;
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        Content = RequireText(content, nameof(content));
        Status = ProductReviewStatus.Draft;
    }

    public string MemberUserId { get; private set; } = string.Empty;
    public long OrderItemId { get; private set; }
    public long ProductId { get; private set; }
    public byte Rating { get; private set; }
    public string? Title { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public ProductReviewStatus Status { get; private set; }
    public string? ReviewedByAdminUserId { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }

    public void Submit(DateTime submittedAtUtc)
    {
        if (Status is not (ProductReviewStatus.Draft or ProductReviewStatus.Rejected))
        {
            throw new InvalidOperationException("The review cannot be submitted from its state.");
        }

        Status = ProductReviewStatus.PendingReview;
        RejectionReason = null;
        MarkUpdated(submittedAtUtc);
    }

    public void Review(
        string adminUserId,
        bool approved,
        string? rejectionReason,
        DateTime reviewedAtUtc)
    {
        if (Status != ProductReviewStatus.PendingReview ||
            !approved && string.IsNullOrWhiteSpace(rejectionReason))
        {
            throw new InvalidOperationException("The review decision is invalid.");
        }

        ReviewedByAdminUserId = RequireText(adminUserId, nameof(adminUserId));
        ReviewedAtUtc = RequireUtc(reviewedAtUtc, nameof(reviewedAtUtc));
        Status = approved ? ProductReviewStatus.Approved : ProductReviewStatus.Rejected;
        RejectionReason = approved ? null : rejectionReason!.Trim();
        MarkUpdated(reviewedAtUtc);
    }

    public void Edit(byte rating, string? title, string content, DateTime updatedAtUtc)
    {
        if (rating is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(rating));
        }

        Rating = rating;
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        Content = RequireText(content, nameof(content));
        Status = Status == ProductReviewStatus.Draft
            ? ProductReviewStatus.Draft
            : ProductReviewStatus.PendingReview;
        ReviewedByAdminUserId = null;
        ReviewedAtUtc = null;
        RejectionReason = null;
        MarkUpdated(updatedAtUtc);
    }

    public void Hide(string adminUserId, DateTime hiddenAtUtc)
    {
        if (Status != ProductReviewStatus.Approved)
        {
            throw new InvalidOperationException("Only an approved review can be hidden.");
        }

        ReviewedByAdminUserId = RequireText(adminUserId, nameof(adminUserId));
        ReviewedAtUtc = RequireUtc(hiddenAtUtc, nameof(hiddenAtUtc));
        Status = ProductReviewStatus.Hidden;
        RejectionReason = null;
        MarkUpdated(hiddenAtUtc);
    }

    public void Restore(string adminUserId, DateTime restoredAtUtc)
    {
        if (Status != ProductReviewStatus.Hidden)
        {
            throw new InvalidOperationException("Only a hidden review can be restored.");
        }

        ReviewedByAdminUserId = RequireText(adminUserId, nameof(adminUserId));
        ReviewedAtUtc = RequireUtc(restoredAtUtc, nameof(restoredAtUtc));
        Status = ProductReviewStatus.Approved;
        RejectionReason = null;
        MarkUpdated(restoredAtUtc);
    }
}

public sealed class ReviewImage : MutableEntity
{
    private ReviewImage() { }

    public ReviewImage(
        long productReviewId,
        string storageKey,
        string originalFileName,
        string mediaType,
        long fileSizeBytes,
        byte[] sha256,
        int sortOrder,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        if (productReviewId <= 0 || fileSizeBytes is < 1 or > 5 * 1024 * 1024 ||
            sha256 is null || sha256.Length != 32)
        {
            throw new ArgumentException("The review image is invalid.");
        }

        ProductReviewId = productReviewId;
        StorageKey = RequireText(storageKey, nameof(storageKey));
        OriginalFileName = RequireText(originalFileName, nameof(originalFileName));
        MediaType = RequireText(mediaType, nameof(mediaType));
        FileSizeBytes = fileSizeBytes;
        Sha256 = sha256.ToArray();
        ScanStatus = ReviewImageScanStatus.Pending;
        SortOrder = sortOrder;
    }

    public long ProductReviewId { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string MediaType { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public byte[] Sha256 { get; private set; } = [];
    public ReviewImageScanStatus ScanStatus { get; private set; }
    public int SortOrder { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    public void RecordScan(ReviewImageScanStatus status, DateTime updatedAtUtc)
    {
        if (status == ReviewImageScanStatus.Pending)
        {
            throw new ArgumentException("A completed scan status is required.", nameof(status));
        }

        ScanStatus = status;
        MarkUpdated(updatedAtUtc);
    }

    public void MarkDeleted(DateTime deletedAtUtc)
    {
        DeletedAtUtc = RequireUtc(deletedAtUtc, nameof(deletedAtUtc));
        MarkUpdated(deletedAtUtc);
    }
}

public sealed class ProductReviewRevision : Entity
{
    private ProductReviewRevision() { }

    public ProductReviewRevision(
        long productReviewId,
        byte rating,
        string? title,
        string content,
        DateTime publishedAtUtc,
        DateTime supersededAtUtc,
        ReviewSupersededReason supersededReason,
        DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        if (productReviewId <= 0 || rating is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(rating));
        }

        publishedAtUtc = RequireUtc(publishedAtUtc, nameof(publishedAtUtc));
        supersededAtUtc = RequireUtc(supersededAtUtc, nameof(supersededAtUtc));
        if (supersededAtUtc < publishedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(supersededAtUtc));
        }

        ProductReviewId = productReviewId;
        Rating = rating;
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        Content = RequireText(content, nameof(content));
        PublishedAtUtc = publishedAtUtc;
        SupersededAtUtc = supersededAtUtc;
        SupersededReason = supersededReason;
    }

    public long ProductReviewId { get; private set; }
    public byte Rating { get; private set; }
    public string? Title { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public DateTime PublishedAtUtc { get; private set; }
    public DateTime SupersededAtUtc { get; private set; }
    public ReviewSupersededReason SupersededReason { get; private set; }
}
