using DoSelect.Application.Auditing;
using DoSelect.Application.Files;

namespace DoSelect.Application.Reviews;

public static class ReviewLimits
{
    public const int MaximumTitleLength = 80;
    public const int MaximumContentLength = 1_000;
    public const int MaximumImages = 3;
    public const long MaximumImageSizeBytes = 5 * 1024 * 1024;
}

public sealed record EligibleReviewOrderItemDto(
    Guid OrderItemPublicId,
    Guid ProductPublicId,
    string SkuCode,
    string ProductName,
    string SkuName,
    DateTime CompletedAtUtc,
    Guid? ReviewPublicId,
    string? ReviewStatus);

public sealed record ReviewImageDto(
    int SortOrder,
    string OriginalFileName,
    string MediaType,
    long FileSizeBytes,
    string Url);

public sealed record MemberReviewDto(
    Guid PublicId,
    Guid OrderItemPublicId,
    Guid ProductPublicId,
    string ProductName,
    string SkuName,
    byte Rating,
    string? Title,
    string Content,
    string Status,
    string? RejectionReason,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string RowVersion,
    IReadOnlyList<ReviewImageDto> Images);

public sealed record AdminReviewDto(
    Guid PublicId,
    Guid ProductPublicId,
    string ProductName,
    string SkuName,
    byte Rating,
    string? Title,
    string Content,
    string Status,
    string? RejectionReason,
    DateTime CreatedAtUtc,
    DateTime? ReviewedAtUtc,
    string RowVersion,
    IReadOnlyList<ReviewImageDto> Images);

public sealed record PublicProductReviewDto(
    Guid PublicId,
    byte Rating,
    string? Title,
    string Content,
    bool IsVerifiedPurchase,
    DateTime PublishedAtUtc,
    IReadOnlyList<ReviewImageDto> Images);

public sealed record CreateReviewRequest(
    Guid OrderItemPublicId,
    byte Rating,
    string? Title,
    string Content,
    bool Submit = true);

public sealed record UpdateReviewRequest(
    byte Rating,
    string? Title,
    string Content,
    string RowVersion);

public sealed record ReviewRowVersionRequest(string RowVersion);

public sealed record ReviewModerationRequest(
    string ReasonCode,
    string? Note,
    string RowVersion);

public sealed record ReviewAdminActor(
    string UserId,
    IReadOnlyList<string> Roles,
    AuditRequestContext AuditContext);

public interface IReviewService
{
    Task<IReadOnlyList<EligibleReviewOrderItemDto>> ListEligibleOrderItemsAsync(
        string memberUserId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MemberReviewDto>> ListMineAsync(
        string memberUserId,
        CancellationToken cancellationToken);

    Task<MemberReviewDto> CreateAsync(
        string memberUserId,
        CreateReviewRequest request,
        CancellationToken cancellationToken);

    Task<MemberReviewDto> UpdateAsync(
        string memberUserId,
        Guid reviewPublicId,
        UpdateReviewRequest request,
        CancellationToken cancellationToken);

    Task<MemberReviewDto> SubmitAsync(
        string memberUserId,
        Guid reviewPublicId,
        ReviewRowVersionRequest request,
        CancellationToken cancellationToken);

    Task WithdrawAsync(
        string memberUserId,
        Guid reviewPublicId,
        string rowVersion,
        CancellationToken cancellationToken);

    Task<MemberReviewDto> UploadImageAsync(
        string memberUserId,
        Guid reviewPublicId,
        ProductImageUpload upload,
        long? declaredLength,
        string rowVersion,
        CancellationToken cancellationToken);

    Task<MemberReviewDto> DeleteImageAsync(
        string memberUserId,
        Guid reviewPublicId,
        int sortOrder,
        string rowVersion,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminReviewDto>> ListForModerationAsync(
        string? status,
        CancellationToken cancellationToken);

    Task<AdminReviewDto?> GetForModerationAsync(
        Guid reviewPublicId,
        CancellationToken cancellationToken);

    Task<AdminReviewDto> ModerateAsync(
        ReviewAdminActor actor,
        Guid reviewPublicId,
        string action,
        ReviewModerationRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PublicProductReviewDto>> ListPublicAsync(
        Guid productPublicId,
        CancellationToken cancellationToken);
}

public sealed class ReviewWriteException : Exception
{
    public ReviewWriteException(string code, string message) : base(message) => Code = code;

    public string Code { get; }

    public static class ErrorCodes
    {
        public const string ValidationFailed = "review_validation_failed";
        public const string NotEligible = "review_not_eligible";
        public const string NotFound = "review_not_found";
        public const string Conflict = "review_state_conflict";
        public const string ConcurrencyConflict = "review_concurrency_conflict";
        public const string ImageLimitExceeded = "review_image_limit_exceeded";
        public const string FileTooLarge = "file_too_large";
        public const string FileTypeNotAllowed = "file_type_not_allowed";
        public const string FileMalwareDetected = "file_malware_detected";
        public const string FileScanUnavailable = "file_scan_unavailable";
    }
}
