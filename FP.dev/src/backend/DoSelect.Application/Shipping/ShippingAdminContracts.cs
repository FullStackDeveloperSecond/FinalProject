using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Common;

namespace DoSelect.Application.Shipping;

public static class ShippingAdminErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string PackageLimitPeriodOverlap = "package_limit_period_overlap";
    public const string ConcurrencyConflict = "concurrency_conflict";
    public const string StoreCodeDuplicate = "store_code_duplicate";
    public const string ResourceNotFound = "resource_not_found";
}

public sealed class ShippingAdminWriteException(string errorCode, string? detail = null)
    : Exception(detail ?? errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

// ---- Package limit versions (UC-ADM-SHIP-01) ----

public sealed record PackageLimitVersionDto(
    Guid PublicId,
    string ProviderCode,
    int Version,
    string Status,
    decimal MaxWeightKg,
    decimal MaxLengthCm,
    decimal MaxWidthCm,
    decimal MaxHeightCm,
    decimal MaxTotalCm,
    decimal MaxDeclaredValue,
    DateTime? EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    byte[] RowVersion);

public sealed record CreatePackageLimitVersionRequest(
    [Required] string ProviderCode,
    [Range(0.01, double.MaxValue)] decimal MaxWeightKg,
    [Range(0.01, double.MaxValue)] decimal MaxLengthCm,
    [Range(0.01, double.MaxValue)] decimal MaxWidthCm,
    [Range(0.01, double.MaxValue)] decimal MaxHeightCm,
    [Range(0.01, double.MaxValue)] decimal MaxTotalCm,
    [Range(0.01, double.MaxValue)] decimal MaxDeclaredValue,
    DateTime? EffectiveFromUtc,
    DateTime? EffectiveToUtc);

public sealed record PublishPackageLimitVersionRequest(byte[] RowVersion);

public interface IPackageLimitService
{
    Task<IReadOnlyList<PackageLimitVersionDto>> ListAsync(string providerCode, CancellationToken cancellationToken);

    Task<PackageLimitVersionDto> CreateDraftAsync(
        CreatePackageLimitVersionRequest request,
        string actorUserId,
        CancellationToken cancellationToken);

    Task<PackageLimitVersionDto> PublishAsync(
        Guid versionPublicId,
        PublishPackageLimitVersionRequest request,
        string actorUserId,
        CancellationToken cancellationToken);
}

// ---- Convenience stores admin CRUD (UC-ADM-STORE-01) ----

public sealed record ConvenienceStoreDto(
    Guid PublicId,
    string ProviderCode,
    string StoreCode,
    string StoreName,
    string Address,
    string City,
    string District,
    bool IsDemoData,
    bool IsActive,
    byte[] RowVersion);

public sealed record AdminConvenienceStoreQuery(
    string? ProviderCode,
    string? City,
    string? District,
    bool? IsActive,
    int PageNumber,
    int PageSize);

public sealed record CreateConvenienceStoreRequest(
    [Required, StringLength(64)] string ProviderCode,
    [Required, StringLength(64)] string StoreCode,
    [Required, StringLength(160)] string StoreName,
    [Required, StringLength(500)] string Address,
    [Required, StringLength(60)] string City,
    [Required, StringLength(60)] string District);

public sealed record UpdateConvenienceStoreRequest(
    [Required, StringLength(160)] string StoreName,
    [Required, StringLength(500)] string Address,
    [Required, StringLength(60)] string City,
    [Required, StringLength(60)] string District,
    bool IsActive,
    [Required] byte[] RowVersion);

public interface IConvenienceStoreAdminService
{
    Task<PageResult<ConvenienceStoreDto>> ListAsync(AdminConvenienceStoreQuery query, CancellationToken cancellationToken);

    Task<ConvenienceStoreDto> CreateAsync(
        CreateConvenienceStoreRequest request,
        string actorUserId,
        CancellationToken cancellationToken);

    Task<ConvenienceStoreDto> UpdateAsync(
        Guid publicId,
        UpdateConvenienceStoreRequest request,
        string actorUserId,
        CancellationToken cancellationToken);
}
