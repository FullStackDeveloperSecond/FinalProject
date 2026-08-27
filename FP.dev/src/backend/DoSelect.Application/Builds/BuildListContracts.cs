using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Common;
using DoSelect.Application.Shopping;

namespace DoSelect.Application.Builds;

public sealed record BuildItemDto(
    Guid PublicId,
    Guid SkuPublicId,
    string SkuCode,
    string Name,
    int Quantity,
    int SortOrder,
    decimal UnitPrice,
    decimal LineTotal,
    string Availability);

public sealed record BuildCompatibilitySummaryDto(
    string Overall,
    int RuleSetVersion,
    int SettingsVersion,
    IReadOnlyList<CompatibilityFindingDto> Results);

/// <summary>Matches API DTO與Schema契約.md's `totals{merchandise,assemblyFee,grandTotal,currency}`.</summary>
public sealed record BuildTotalsDto(
    decimal Merchandise,
    decimal AssemblyFee,
    decimal GrandTotal,
    string Currency);

public sealed record BuildListDto(
    Guid PublicId,
    string Name,
    IReadOnlyList<BuildItemDto> Items,
    BuildCompatibilitySummaryDto Compatibility,
    BuildTotalsDto Totals,
    DateTime UpdatedAtUtc,
    byte[] RowVersion);

public sealed record BuildListSummaryDto(
    Guid PublicId,
    string Name,
    int ItemCount,
    string CompatibilityOverall,
    decimal GrandTotal,
    bool IsShared,
    DateTime UpdatedAtUtc,
    byte[] RowVersion);

public sealed record CreateBuildListRequest(
    [Required, StringLength(160, MinimumLength = 1)] string Name,
    IReadOnlyList<BuildItemInput> Items);

public sealed record UpdateBuildListRequest(
    [Required, StringLength(160, MinimumLength = 1)] string Name,
    IReadOnlyList<BuildItemInput> Items,
    byte[] RowVersion);

/// <summary>Same bounds as ProductSearchQuery's paging convention (API DTO與Schema契約.md).</summary>
public sealed record BuildListListQuery(
    [Range(1, int.MaxValue)] int PageNumber = 1,
    [Range(1, 50)] int PageSize = 20);

/// <summary>
/// <c>Url</c> is the relative path of this backend's own public read endpoint
/// (<c>/api/v1/build-shares/{token}</c>) — no frontend share-page route is defined in any
/// contract doc yet, so this points at the resource this token actually unlocks rather than
/// guessing a frontend URL shape.
/// </summary>
public sealed record BuildShareDto(Guid SharePublicId, string Url, DateTime? ExpiresAtUtc);

public sealed record AddBuildToCartRequest([Range(1, 8)] int Quantity, byte[] BuildRowVersion);

/// <summary>De-identified public view of a shared build list — never includes the owner.</summary>
public sealed record SharedBuildDto(
    Guid SharePublicId,
    string Name,
    IReadOnlyList<BuildItemDto> Items,
    BuildCompatibilitySummaryDto Compatibility,
    BuildTotalsDto Totals,
    bool CanCopy,
    bool CanAddToCart);

public interface IBuildListService
{
    Task<PageResult<BuildListSummaryDto>> ListAsync(
        string memberUserId,
        BuildListListQuery query,
        CancellationToken cancellationToken);

    Task<BuildListDto> GetAsync(
        string memberUserId,
        Guid buildListPublicId,
        CancellationToken cancellationToken);

    Task<BuildListDto> CreateAsync(
        string memberUserId,
        CreateBuildListRequest request,
        CancellationToken cancellationToken);

    Task<BuildListDto> UpdateAsync(
        string memberUserId,
        Guid buildListPublicId,
        UpdateBuildListRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string memberUserId,
        Guid buildListPublicId,
        byte[] rowVersion,
        CancellationToken cancellationToken);

    Task<BuildShareDto> CreateShareAsync(
        string memberUserId,
        Guid buildListPublicId,
        CancellationToken cancellationToken);

    Task RevokeShareAsync(
        string memberUserId,
        Guid buildListPublicId,
        CancellationToken cancellationToken);

    /// <summary>UC-BUILD-01 分享: public, unauthenticated read by opaque token (never the BuildShareToken's own PublicId).</summary>
    Task<SharedBuildDto> GetSharedBuildAsync(string rawToken, CancellationToken cancellationToken);

    /// <summary>
    /// UC-BUILD-01 加入購物車: owner-member path only (a shared-list viewer's "copy into my own
    /// list, then add that" flow isn't specified by any contract doc yet — flagged for 組長,
    /// not implemented here). Always re-validates price/stock/publish-state/compatibility fresh
    /// (never trusts <c>BuildList.CompatibilityStatus</c>); <paramref name="idempotencyKey"/> is
    /// the request's <c>Idempotency-Key</c> header value.
    /// </summary>
    Task<CartDto> AddToCartAsync(
        string memberUserId,
        Guid buildListPublicId,
        AddBuildToCartRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
