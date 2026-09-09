using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoSelect.Application.Ai;
using DoSelect.Application.Builds;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Ai;

namespace DoSelect.Infrastructure.Builds;

/// <summary>Bounded persisted owned-part input; shares the existing AI structured-part contract and evaluator.</summary>
internal static class BuildOwnedParts
{
    internal static IReadOnlyList<AiProductSearchExistingPart> Read(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<AiProductSearchExistingPart[]>(json)
            ?? throw Invalid();

    internal static IReadOnlyList<AiProductSearchExistingPart> Validate(IReadOnlyList<AiProductSearchExistingPart>? parts)
    {
        if (parts is null) return [];
        if (parts.Count > 12) throw Invalid();
        var normalized = new List<AiProductSearchExistingPart>();
        foreach (var part in parts)
        {
            if (part is null || !part.ConfirmedByUser || part.Quantity is < 1 or > 8 ||
                part.DisplayName?.Length > 160 || part.Specifications is null || part.Specifications.Count > 12)
                throw Invalid();
            if (part.SourceType == "catalogSku")
            {
                if (part.SkuPublicId is null || part.SkuPublicId == Guid.Empty || part.Specifications.Count != 0) throw Invalid();
                normalized.Add(part with { CategoryCode = null, DisplayName = null });
                continue;
            }
            if (part.SourceType != "structuredManual" || part.SkuPublicId is not null || string.IsNullOrWhiteSpace(part.DisplayName) ||
                part.CategoryCode is null || !CompatibilityCatalogContract.Categories.All.Contains(part.CategoryCode) ||
                part.Specifications.Any(spec => spec is null || string.IsNullOrWhiteSpace(spec.SemanticKey) || spec.SemanticKey.Length > 80 ||
                    string.IsNullOrWhiteSpace(spec.Value) || spec.Value.Length > 256 || spec.Unit?.Length > 24 || spec.Operator is not ("eq" or "in")) ||
                part.Specifications.Select(spec => spec.SemanticKey.Trim().ToUpperInvariant()).Distinct().Count() != part.Specifications.Count ||
                !EfAiProductSearchCatalog.TryCreateManualComponent(part, out _))
                throw Invalid();
            normalized.Add(part with { DisplayName = part.DisplayName.Trim(), Specifications = part.Specifications.OrderBy(spec => spec.SemanticKey, StringComparer.Ordinal).ToArray() });
        }
        if (JsonSerializer.Serialize(normalized).Length > 32_000) throw Invalid();
        return normalized;
    }

    internal static IReadOnlyList<BuildItemInput> NewItems(IReadOnlyList<BuildItemInput> items, IReadOnlyList<AiProductSearchExistingPart> owned)
    {
        if (items is null || items.Count + owned.Count is < 1 or > 20) throw Invalid();
        return items.Count == 0 ? [] : EfCompatibilityCheckService.MergeAndValidateItems(items);
    }

    internal static CompatibilityComponent ManualComponent(AiProductSearchExistingPart part, int index)
    {
        if (!EfAiProductSearchCatalog.TryCreateManualComponent(part, out var component)) throw Invalid();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"owned:{index}:" + JsonSerializer.Serialize(part)));
        return new CompatibilityComponent(new Guid(hash.AsSpan(0, 16)), component!.CategoryCode, component.Quantity, component.Specifications);
    }

    internal static string Canonical(IReadOnlyList<AiProductSearchExistingPart> parts) =>
        JsonSerializer.Serialize(parts.OrderBy(part => part.SourceType, StringComparer.Ordinal)
            .ThenBy(part => part.SkuPublicId).ThenBy(part => part.CategoryCode, StringComparer.Ordinal)
            .ThenBy(part => part.DisplayName, StringComparer.Ordinal));

    private static BuildWriteException Invalid() => new(BuildWriteException.ErrorCodes.ValidationFailed,
        "自有零件資料不完整或超過限制，請確認分類、數量及必要規格；自有零件不會加入購物車。");
}
