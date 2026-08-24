using DoSelect.Application.Shipping;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shipping;

/// <summary>UC-ADM-SHIP-01. See <see cref="IPackageLimitVersionAdminService"/> for the providerId/versionId routing design note.</summary>
public sealed class EfPackageLimitVersionAdminService : IPackageLimitVersionAdminService
{
    private static readonly IReadOnlyDictionary<string, SafeRange> SafeRanges = new Dictionary<string, SafeRange>
    {
        // 購物車、訂單、付款與物流.md §超商門市與包裹限制: 超商 1～45cm／3～105cm／0.1～5kg，宅配 1～150cm／3～150cm／0.1～20kg。
        [ShippingProviderCodes.ConvenienceStore] = new(1m, 45m, 3m, 105m, 0.1m, 5m),
        [ShippingProviderCodes.HomeDelivery] = new(1m, 150m, 3m, 150m, 0.1m, 20m),
    };

    private readonly DoSelectDbContext _dbContext;

    public EfPackageLimitVersionAdminService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<PackageLimitVersionDto>> ListAsync(
        string providerCode, CancellationToken cancellationToken)
    {
        var normalizedCode = RequireProviderCode(providerCode);

        var rows = await (
            from profile in _dbContext.ShippingProviderProfiles.AsNoTracking()
            join limit in _dbContext.PackageLimitVersions.AsNoTracking()
                on profile.Id equals limit.ProviderProfileId
            where profile.ProviderCode == normalizedCode
            orderby profile.Version descending
            select new { profile, limit })
            .ToListAsync(cancellationToken);

        return rows.Select(row => ToDto(row.profile, row.limit)).ToList();
    }

    public async Task<PackageLimitVersionDto> CreateDraftAsync(
        string providerCode, CreatePackageLimitVersionRequest request, DateTime now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedCode = RequireProviderCode(providerCode);
        var range = SafeRanges[normalizedCode];

        ValidateDimensions(request, range);

        // Overlap only matters between still-Draft versions being staged for a future rollout —
        // publishing one always supersedes whichever version is currently Published regardless of
        // its own EffectiveFrom／ToUtc (the DB's filtered unique index is what actually guarantees
        // "one live version at a time"), so the currently-Published row never counts as a sibling
        // here or every ordinary "just draft the next version" call would falsely conflict with it.
        var siblingPeriods = await _dbContext.PackageLimitVersions.AsNoTracking()
            .Join(_dbContext.ShippingProviderProfiles.AsNoTracking(),
                limit => limit.ProviderProfileId, profile => profile.Id,
                (limit, profile) => new { limit.EffectiveFromUtc, limit.EffectiveToUtc, profile.Status, profile.ProviderCode })
            .Where(row => row.ProviderCode == normalizedCode && row.Status == ShippingProviderProfile.DraftStatus)
            .ToListAsync(cancellationToken);

        if (siblingPeriods.Any(sibling => PeriodsOverlap(
                sibling.EffectiveFromUtc, sibling.EffectiveToUtc,
                request.EffectiveFromUtc, request.EffectiveToUtc)))
        {
            throw new ShippingWriteException(
                ShippingWriteException.ErrorCodes.PackageLimitPeriodOverlap,
                $"The requested effective period overlaps an existing version for provider '{providerCode}'.");
        }

        var currentMaxVersion = await _dbContext.ShippingProviderProfiles.AsNoTracking()
            .Where(profile => profile.ProviderCode == normalizedCode)
            .Select(profile => (int?)profile.Version)
            .MaxAsync(cancellationToken) ?? 0;
        var nextVersion = currentMaxVersion + 1;

        var draftProfile = new ShippingProviderProfile(
            Guid.CreateVersion7(), normalizedCode, nextVersion, ShippingProviderProfile.DraftStatus,
            request.EffectiveFromUtc, request.EffectiveToUtc, configurationJson: "{}", schemaVersion: 1, now);
        _dbContext.ShippingProviderProfiles.Add(draftProfile);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var draftLimit = new PackageLimitVersion(
            Guid.CreateVersion7(), draftProfile.Id, nextVersion,
            request.MaxWeightKg, request.MaxLengthCm, request.MaxWidthCm, request.MaxHeightCm,
            request.MaxTotalCm, request.MaxDeclaredValue,
            request.EffectiveFromUtc, request.EffectiveToUtc, now);
        _dbContext.PackageLimitVersions.Add(draftLimit);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(draftProfile, draftLimit);
    }

    public async Task<PackageLimitVersionDto> PublishAsync(
        string providerCode, Guid versionPublicId, PublishPackageLimitVersionRequest request, DateTime now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedCode = RequireProviderCode(providerCode);

        var draftProfile = await _dbContext.ShippingProviderProfiles
            .FirstOrDefaultAsync(
                profile => profile.PublicId == versionPublicId && profile.ProviderCode == normalizedCode,
                cancellationToken);
        if (draftProfile is null)
        {
            throw new ShippingWriteException(
                ShippingWriteException.ErrorCodes.ResourceNotFound,
                $"Package-limit version '{versionPublicId}' was not found for provider '{providerCode}'.");
        }

        if (draftProfile.Status != ShippingProviderProfile.DraftStatus)
        {
            throw new ShippingWriteException(
                ShippingWriteException.ErrorCodes.ValidationFailed,
                "Only a Draft version can be published; already-published or superseded versions must be replaced by a new draft instead.");
        }

        _dbContext.Entry(draftProfile).Property(entity => entity.RowVersion).OriginalValue = request.RowVersion;
        draftProfile.Publish(now);

        var previouslyPublished = await _dbContext.ShippingProviderProfiles
            .Where(profile => profile.ProviderCode == normalizedCode &&
                profile.Status == ShippingProviderProfile.PublishedStatus &&
                profile.Id != draftProfile.Id)
            .ToListAsync(cancellationToken);
        foreach (var profile in previouslyPublished)
        {
            profile.Supersede(now);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ShippingWriteException(
                ShippingWriteException.ErrorCodes.ConcurrencyConflict,
                "The package-limit version was updated by someone else. Reload and try again.");
        }

        var publishedLimit = await _dbContext.PackageLimitVersions.AsNoTracking()
            .SingleAsync(limit => limit.ProviderProfileId == draftProfile.Id, cancellationToken);
        return ToDto(draftProfile, publishedLimit);
    }

    private static void ValidateDimensions(CreatePackageLimitVersionRequest request, SafeRange range)
    {
        var singleSides = new[] { request.MaxLengthCm, request.MaxWidthCm, request.MaxHeightCm };
        var isValid =
            singleSides.All(side => side >= range.MinSideCm && side <= range.MaxSideCm) &&
            request.MaxTotalCm >= range.MinTotalCm && request.MaxTotalCm <= range.MaxTotalCm &&
            request.MaxWeightKg >= range.MinWeightKg && request.MaxWeightKg <= range.MaxWeightKg &&
            request.MaxDeclaredValue > 0 &&
            singleSides.All(side => side <= request.MaxTotalCm);
        if (!isValid)
        {
            throw new ShippingWriteException(
                ShippingWriteException.ErrorCodes.ValidationFailed,
                "The package-limit values fall outside this provider's declared safe configuration range.");
        }
    }

    private static bool PeriodsOverlap(
        DateTime? aFrom, DateTime? aTo, DateTime? bFrom, DateTime? bTo)
    {
        var aStarts = aFrom ?? DateTime.MinValue;
        var aEnds = aTo ?? DateTime.MaxValue;
        var bStarts = bFrom ?? DateTime.MinValue;
        var bEnds = bTo ?? DateTime.MaxValue;
        return aStarts < bEnds && bStarts < aEnds;
    }

    private static string RequireProviderCode(string providerCode)
    {
        var normalized = providerCode?.Trim() ?? string.Empty;
        if (!SafeRanges.ContainsKey(normalized))
        {
            throw new ShippingWriteException(
                ShippingWriteException.ErrorCodes.ResourceNotFound,
                $"Unknown shipping provider '{providerCode}'.");
        }

        return normalized;
    }

    private static PackageLimitVersionDto ToDto(ShippingProviderProfile profile, PackageLimitVersion limit) => new(
        profile.PublicId, profile.ProviderCode, profile.Version, profile.Status,
        limit.MaxWeightKg, limit.MaxLengthCm, limit.MaxWidthCm, limit.MaxHeightCm,
        limit.MaxTotalCm, limit.MaxDeclaredValue, limit.EffectiveFromUtc, limit.EffectiveToUtc,
        profile.RowVersion);

    private readonly record struct SafeRange(
        decimal MinSideCm, decimal MaxSideCm, decimal MinTotalCm, decimal MaxTotalCm,
        decimal MinWeightKg, decimal MaxWeightKg);
}
