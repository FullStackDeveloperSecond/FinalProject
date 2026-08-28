using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Common;
using DoSelect.Application.Shopping;

namespace DoSelect.Application.Builds;

/// <summary>
/// PR #35 review, item 1: <c>CategoryCode</c> added so the frontend can group an existing build's
/// items back into their compatibility-catalog category slot (CPU／主機板／…) without a second
/// lookup — matches <see cref="DoSelect.Domain.Catalog.CompatibilityCatalogContract.Categories"/>.
/// </summary>
public sealed record BuildItemDto(
    Guid PublicId,
    Guid SkuPublicId,
    string SkuCode,
    string Name,
    string CategoryCode,
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

/// <summary>
/// PR #35 review, item 3: only the share token's SHA-256 hash is ever persisted
/// (<see cref="DoSelect.Domain.Builds.BuildShareToken.TokenHash"/>), so an existing share's raw
/// token — and therefore its openable URL — cannot be reconstructed after the moment it was
/// created, by design (same one-time-reveal shape as an API key). This carries only what CAN be
/// recovered: that a share is currently active, and when (if ever) it expires — enough for the
/// detail page to offer "revoke" or "regenerate" after a reload without pretending it can show
/// the original link text again.
/// </summary>
public sealed record BuildActiveShareDto(Guid SharePublicId, DateTime? ExpiresAtUtc);

public sealed record BuildListDto(
    Guid PublicId,
    string Name,
    IReadOnlyList<BuildItemDto> Items,
    BuildCompatibilitySummaryDto Compatibility,
    BuildTotalsDto Totals,
    BuildActiveShareDto? ActiveShare,
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
/// PR #35 round-3 review (non-blocking doc note): <c>Url</c> is the full, openable frontend URL —
/// <c>{FrontendLinkOptions.BaseUrl}/builds/shared/{token}</c> (customer-web's
/// <c>SharedBuildPage.vue</c> route), not this backend's own API path. See
/// <see cref="DoSelect.Infrastructure.Builds.EfBuildListService.CreateShareAsync"/> for the actual
/// construction.
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
