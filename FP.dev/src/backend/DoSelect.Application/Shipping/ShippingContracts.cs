using DoSelect.Application.Common;

namespace DoSelect.Application.Shipping;

// ---- M 配送選項支撐 (public: GET /api/v1/cart/shipping-options, GET /api/v1/convenience-stores) ----

public sealed record ShippingMethodOptionDto(
    string Code,
    string NameZhTw,
    decimal BaseFee,
    decimal? FreeShippingThreshold,
    bool AllowsCod,
    bool RequiresPrepayment);

public sealed record ShippingOptionsDto(IReadOnlyList<ShippingMethodOptionDto> Methods);

public sealed record ConvenienceStoreQuery(
    string? Q,
    string? City,
    string? District,
    int PageNumber = 1,
    int PageSize = 20);

public sealed record ConvenienceStoreOptionDto(
    Guid PublicId,
    string ProviderCode,
    string StoreCode,
    string StoreName,
    string Address,
    string City,
    string District);

public interface IShippingOptionsQueryService
{
    Task<ShippingOptionsDto> GetShippingOptionsAsync(CancellationToken cancellationToken);

    Task<PageResult<ConvenienceStoreOptionDto>> SearchConvenienceStoresAsync(
        ConvenienceStoreQuery query, CancellationToken cancellationToken);
}

// ---- UC-ADM-STORE-01: convenience-store admin CRUD ----

public sealed record ConvenienceStoreAdminQuery(
    string? Q,
    string? City,
    string? District,
    bool? IsActive,
    int PageNumber = 1,
    int PageSize = 20);

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

public sealed record CreateConvenienceStoreRequest(
    string ProviderCode,
    string StoreCode,
    string StoreName,
    string Address,
    string City,
    string District);

public sealed record UpdateConvenienceStoreRequest(
    string StoreName,
    string Address,
    string City,
    string District,
    bool IsActive,
    byte[] RowVersion);

/// <summary>UC-ADM-STORE-01: OrderManager／SuperAdmin can create/edit/deactivate; CatalogManager is read-only (enforced at the controller via DoSelectPolicies.ConvenienceStoreView vs OrderManager).</summary>
public interface IConvenienceStoreAdminService
{
    Task<PageResult<ConvenienceStoreDto>> ListAsync(
        ConvenienceStoreAdminQuery query, CancellationToken cancellationToken);

    Task<ConvenienceStoreDto> CreateAsync(
        CreateConvenienceStoreRequest request, DateTime now, CancellationToken cancellationToken);

    /// <summary>Stores already referenced by a cart/order are never hard-deleted — deactivating (IsActive=false) is the only removal path (資料字典-購物交易與售後.md).</summary>
    Task<ConvenienceStoreDto> UpdateAsync(
        Guid publicId, UpdateConvenienceStoreRequest request, DateTime now, CancellationToken cancellationToken);
}

// ---- UC-ADM-SHIP-01: package-limit-version draft／publish ----

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
    decimal MaxWeightKg,
    decimal MaxLengthCm,
    decimal MaxWidthCm,
    decimal MaxHeightCm,
    decimal MaxTotalCm,
    decimal MaxDeclaredValue,
    DateTime? EffectiveFromUtc,
    DateTime? EffectiveToUtc);

public sealed record PublishPackageLimitVersionRequest(byte[] RowVersion);

/// <summary>
/// UC-ADM-SHIP-01. Route uses {providerId} — there is no standalone "ShippingProvider" entity in
/// the schema (only versioned ShippingProviderProfile rows), so providerId is ProviderCode itself
/// (Terry's own routing design call, since 購物車、訂單、付款與物流.md never names the identifier
/// shape and only two fixed provider codes exist for v1). versionId in the publish route is the
/// draft ShippingProviderProfile's PublicId.
/// </summary>
public interface IPackageLimitVersionAdminService
{
    Task<IReadOnlyList<PackageLimitVersionDto>> ListAsync(
        string providerCode, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new Draft ShippingProviderProfile (Version = current max + 1) with its child
    /// PackageLimitVersion in one transaction. Throws ShippingWriteException with
    /// ErrorCodes.PackageLimitPeriodOverlap if the requested effective period overlaps another
    /// non-superseded (Draft／Published) version for the same provider, or ValidationFailed if any
    /// dimension falls outside the provider's declared safe configuration range.
    /// </summary>
    Task<PackageLimitVersionDto> CreateDraftAsync(
        string providerCode, CreatePackageLimitVersionRequest request, DateTime now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a Draft version and supersedes whichever version was previously Published for the
    /// same provider, atomically. 409 concurrency_conflict on a RowVersion mismatch.
    /// </summary>
    Task<PackageLimitVersionDto> PublishAsync(
        string providerCode, Guid versionPublicId, PublishPackageLimitVersionRequest request, DateTime now,
        CancellationToken cancellationToken);
}

// ---- UC-ADM-SHIP-02: batch shipment ----

public sealed record BatchShipmentRequest(IReadOnlyList<Guid> OrderPublicIds);

public sealed record BatchShipmentLineResultDto(
    Guid OrderPublicId,
    bool Success,
    Guid? ShipmentPublicId,
    string? TrackingNumber,
    string? ErrorCode);

public sealed record BatchShipmentResultDto(IReadOnlyList<BatchShipmentLineResultDto> Results);

/// <summary>
/// UC-ADM-SHIP-02. Every order is validated and committed independently (its own transaction) — one
/// failure never rolls back another order's successful shipment (購物車、訂單、付款與物流.md
/// §批次出貨). The `.../batches/{id}/result.csv` re-download endpoint in API Endpoint目錄 implies a
/// persisted batch record, but no ShipmentBatch entity exists in the schema yet — this service only
/// returns the synchronous result inline; the CSV-by-id retrieval is flagged as a gap for 組長
/// rather than inventing a new table unilaterally.
/// </summary>
public interface IBatchShipmentService
{
    Task<BatchShipmentResultDto> ShipBatchAsync(
        BatchShipmentRequest request, string adminUserId, DateTime now, CancellationToken cancellationToken);
}
