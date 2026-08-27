using DoSelect.Application.Auditing;

namespace DoSelect.Application.Builds;

/// <summary>
/// Full-replace write for a Sku's multi-value compatibility facts (組長 PR #34 review, item 4) —
/// the only real production write path for `SkuCompatibilityAttributes`／
/// `SkuStorageInterfacePorts`, which before this had no admin surface at all (only test fixtures
/// and the dev seeder wrote to them). Kept in the Builds module (which owns these tables) rather
/// than folded into Catalog's generic `CreateSkuRequest`／`UpdateSkuRequest` — Catalog's SKU
/// contract has no existing dependency on Builds vocabulary (attribute keys, storage interface
/// codes), and this endpoint only needs the Sku's own already-public identity and RowVersion, not
/// write access to Catalog's own tables. <see cref="RowVersion"/> is the Sku's own RowVersion
/// (round-4 review: reuse the Sku's existing concurrency token rather than inventing a second one
/// for this sub-resource) — required, not nullable, since the target Sku always already exists by
/// the time this is called.
/// </summary>
public sealed record SetSkuCompatibilityAttributesRequest(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Attributes,
    IReadOnlyDictionary<string, int> StoragePorts,
    byte[] RowVersion);

public sealed record SkuCompatibilityAttributesDto(
    Guid SkuPublicId,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Attributes,
    IReadOnlyDictionary<string, int> StoragePorts,
    byte[] RowVersion);

public interface ISkuCompatibilityAttributeAdminService
{
    Task<SkuCompatibilityAttributesDto> GetAsync(Guid skuPublicId, CancellationToken cancellationToken);

    Task<SkuCompatibilityAttributesDto> SetAsync(
        Guid skuPublicId,
        string adminUserId,
        SetSkuCompatibilityAttributesRequest request,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken);
}
