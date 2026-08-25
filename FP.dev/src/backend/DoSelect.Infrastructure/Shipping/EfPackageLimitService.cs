using DoSelect.Application.Shipping;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shipping;

/// <summary>
/// A "package-limit version" in the API is a paired (ShippingProviderProfile, PackageLimitVersion)
/// row created and published together — ShippingProviderProfile carries the Draft/Published
/// lifecycle and concurrency token (its RowVersion is what PackageLimitVersionDto.RowVersion
/// actually surfaces), PackageLimitVersion carries the strongly-typed limit columns. Both move
/// in lockstep: same Version number, same effective window, created in the same transaction.
/// </summary>
public sealed class EfPackageLimitService : IPackageLimitService
{
    private readonly DoSelectDbContext _dbContext;

    public EfPackageLimitService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<PackageLimitVersionDto>> ListAsync(
        string providerCode,
        CancellationToken cancellationToken)
    {
        PackageLimitSafeRanges.ForProvider(providerCode); // throws for an unknown code

        var rows = await _dbContext.PackageLimitVersions
            .AsNoTracking()
            .Join(
                _dbContext.ShippingProviderProfiles.AsNoTracking(),
                limit => limit.ProviderProfileId,
                profile => profile.Id,
                (limit, profile) => new { limit, profile })
            .Where(pair => pair.profile.ProviderCode == providerCode)
            .OrderByDescending(pair => pair.profile.Version)
            .ToListAsync(cancellationToken);

        return rows.Select(pair => ToDto(pair.limit, pair.profile)).ToList();
    }

    public async Task<PackageLimitVersionDto> CreateDraftAsync(
        CreatePackageLimitVersionRequest request,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        PackageLimitSafeRange safeRange;
        try
        {
            safeRange = PackageLimitSafeRanges.ForProvider(request.ProviderCode);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ShippingAdminWriteException(
                ShippingAdminErrorCodes.ValidationFailed, $"Unknown providerCode '{request.ProviderCode}'.");
        }

        ValidateAgainstSafeRange(request, safeRange);

        var existingWindows = await _dbContext.PackageLimitVersions
            .AsNoTracking()
            .Join(
                _dbContext.ShippingProviderProfiles.AsNoTracking(),
                limit => limit.ProviderProfileId,
                profile => profile.Id,
                (limit, profile) => new { limit.EffectiveFromUtc, limit.EffectiveToUtc, profile.ProviderCode, profile.Version })
            .Where(pair => pair.ProviderCode == request.ProviderCode)
            .ToListAsync(cancellationToken);

        if (existingWindows.Any(existing => Overlaps(
                existing.EffectiveFromUtc, existing.EffectiveToUtc,
                request.EffectiveFromUtc, request.EffectiveToUtc)))
        {
            throw new ShippingAdminWriteException(ShippingAdminErrorCodes.PackageLimitPeriodOverlap);
        }

        var nextVersion = existingWindows.Count == 0 ? 1 : existingWindows.Max(existing => existing.Version) + 1;
        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var profile = new ShippingProviderProfile(
                Guid.CreateVersion7(),
                request.ProviderCode,
                nextVersion,
                ShippingProviderProfileStatuses.Draft,
                request.EffectiveFromUtc,
                request.EffectiveToUtc,
                BuildConfigurationJson(request),
                schemaVersion: 1,
                now);
            _dbContext.ShippingProviderProfiles.Add(profile);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var limitVersion = new PackageLimitVersion(
                Guid.CreateVersion7(),
                profile.Id,
                nextVersion,
                request.MaxWeightKg,
                request.MaxLengthCm,
                request.MaxWidthCm,
                request.MaxHeightCm,
                request.MaxTotalCm,
                request.MaxDeclaredValue,
                request.EffectiveFromUtc,
                request.EffectiveToUtc,
                now);
            _dbContext.PackageLimitVersions.Add(limitVersion);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return ToDto(limitVersion, profile);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<PackageLimitVersionDto> PublishAsync(
        Guid versionPublicId,
        PublishPackageLimitVersionRequest request,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var limitVersion = await _dbContext.PackageLimitVersions
            .SingleOrDefaultAsync(candidate => candidate.PublicId == versionPublicId, cancellationToken)
            ?? throw new ShippingAdminWriteException(ShippingAdminErrorCodes.ResourceNotFound);
        var profile = await _dbContext.ShippingProviderProfiles
            .SingleAsync(candidate => candidate.Id == limitVersion.ProviderProfileId, cancellationToken);

        _dbContext.Entry(profile).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;

        var now = DateTime.UtcNow;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // 購物車、訂單、付款與物流.md: "同一物流服務在任一時間只有一個有效版本" — publishing a new
            // version ends whichever one is currently Published for this ProviderCode. The cutoff
            // is the new version's own scheduled start (falling back to "now" if it takes effect
            // immediately), not literal wall-clock "now" — the old version may be scheduled to run
            // well into the future and "now" could even fall before its own EffectiveFromUtc,
            // which would violate CK_ShippingProviderProfiles_Period (EffectiveTo > EffectiveFrom).
            var currentlyPublished = await _dbContext.ShippingProviderProfiles
                .SingleOrDefaultAsync(
                    candidate => candidate.ProviderCode == profile.ProviderCode &&
                        candidate.Status == ShippingProviderProfileStatuses.Published &&
                        candidate.Id != profile.Id,
                    cancellationToken);
            // Known narrow gap: if an admin publishes versions out of chronological order (the new
            // one's window starts *before* the currently-published one's own EffectiveFromUtc),
            // this still throws — as an unhandled SqlException (CK_ShippingProviderProfiles_Period),
            // not a clean ShippingAdminWriteException — since the overlap check at draft-creation
            // only rejects overlapping windows, not out-of-order non-overlapping ones. Publishing in
            // chronological order (the expected admin workflow) is unaffected.
            currentlyPublished?.Supersede(profile.EffectiveFromUtc ?? now, now);

            profile.Publish(now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ShippingAdminWriteException(ShippingAdminErrorCodes.ConcurrencyConflict);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return ToDto(limitVersion, profile);
    }

    private static void ValidateAgainstSafeRange(CreatePackageLimitVersionRequest request, PackageLimitSafeRange range)
    {
        var errors = new List<string>();
        void CheckSide(decimal value, string field)
        {
            if (value < range.MinSideCm || value > range.MaxSideCm)
            {
                errors.Add(field);
            }
        }

        CheckSide(request.MaxLengthCm, nameof(request.MaxLengthCm));
        CheckSide(request.MaxWidthCm, nameof(request.MaxWidthCm));
        CheckSide(request.MaxHeightCm, nameof(request.MaxHeightCm));

        if (request.MaxTotalCm < range.MinTotalCm || request.MaxTotalCm > range.MaxTotalCm)
        {
            errors.Add(nameof(request.MaxTotalCm));
        }

        if (request.MaxWeightKg < range.MinWeightKg || request.MaxWeightKg > range.MaxWeightKg)
        {
            errors.Add(nameof(request.MaxWeightKg));
        }

        // 購物車、訂單、付款與物流.md: "管理員設定值需通過正值、單邊不大於三邊和等跨欄位驗證".
        var maxSide = new[] { request.MaxLengthCm, request.MaxWidthCm, request.MaxHeightCm }.Max();
        if (maxSide > request.MaxTotalCm)
        {
            errors.Add(nameof(request.MaxTotalCm) + "_LessThanMaxSide");
        }

        if (request.EffectiveFromUtc.HasValue && request.EffectiveToUtc.HasValue &&
            request.EffectiveToUtc <= request.EffectiveFromUtc)
        {
            errors.Add(nameof(request.EffectiveToUtc));
        }

        if (errors.Count > 0)
        {
            throw new ShippingAdminWriteException(
                ShippingAdminErrorCodes.ValidationFailed,
                $"Fields outside the allowed safe range for provider '{request.ProviderCode}': {string.Join(", ", errors)}.");
        }
    }

    private static bool Overlaps(DateTime? aFrom, DateTime? aTo, DateTime? bFrom, DateTime? bTo)
    {
        var aStart = aFrom ?? DateTime.MinValue;
        var aEnd = aTo ?? DateTime.MaxValue;
        var bStart = bFrom ?? DateTime.MinValue;
        var bEnd = bTo ?? DateTime.MaxValue;
        return aStart < bEnd && bStart < aEnd;
    }

    private static string BuildConfigurationJson(CreatePackageLimitVersionRequest request) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            request.MaxWeightKg,
            request.MaxLengthCm,
            request.MaxWidthCm,
            request.MaxHeightCm,
            request.MaxTotalCm,
            request.MaxDeclaredValue,
        });

    private static PackageLimitVersionDto ToDto(PackageLimitVersion limit, ShippingProviderProfile profile) => new(
        limit.PublicId,
        profile.ProviderCode,
        profile.Version,
        profile.Status,
        limit.MaxWeightKg,
        limit.MaxLengthCm,
        limit.MaxWidthCm,
        limit.MaxHeightCm,
        limit.MaxTotalCm,
        limit.MaxDeclaredValue,
        limit.EffectiveFromUtc,
        limit.EffectiveToUtc,
        profile.RowVersion);
}
