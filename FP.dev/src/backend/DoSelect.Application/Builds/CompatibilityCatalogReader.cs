using DoSelect.Domain.Builds;

namespace DoSelect.Application.Builds;

public sealed record CompatibilityItemReference(Guid SkuPublicId, int Quantity);

public sealed record CompatibilityCatalogReadResult(
    IReadOnlyList<CompatibilityComponent> Components,
    IReadOnlyList<Guid> MissingSkuPublicIds);

public interface ICompatibilityCatalogReader
{
    Task<CompatibilityCatalogReadResult> ReadAsync(
        IReadOnlyCollection<CompatibilityItemReference> items,
        CancellationToken cancellationToken);
}
