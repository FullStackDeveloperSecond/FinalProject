using System.Globalization;
using DoSelect.Application.Auditing;
using DoSelect.Application.Shipping;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
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
    private readonly IAuditWriter _auditWriter;

    public EfPackageLimitService(DoSelectDbContext dbContext, IAuditWriter auditWriter)
    {
        _dbContext = dbContext;
        _auditWriter = auditWriter;
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
        AuditRequestContext auditContext,
        CreatePackageLimitVersionRequest request,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditContext);
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
        ValidateEffectiveWindow(request.EffectiveFromUtc, request.EffectiveToUtc);

        var now = DateTime.UtcNow;

        // 組長 PR #73 round-2 review (P2): the existing-version query, period-overlap check and
        // nextVersion allocation all used to run BEFORE the transaction — two concurrent creates
        // for the same provider could both observe the same max version, and the loser surfaced
        // as an unhandled DbUpdateException (500) off UX_ProviderProfiles_ProviderCode_Version.
        // The whole read-check-allocate-insert sequence now runs inside one transaction
        // serialized by a provider-scoped application lock: the second caller waits at the lock,
        // then reads the first caller's committed row and allocates the next number — no
        // duplicate version, no surprise 500. A lock timeout or a residual unique-index race maps
        // to the stable 409 concurrency_conflict in the catches below.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await AcquireProviderLockAsync(request.ProviderCode, cancellationToken);

            var existingWindows = await _dbContext.PackageLimitVersions
                .AsNoTracking()
                .Join(
                    _dbContext.ShippingProviderProfiles.AsNoTracking(),
                    limit => limit.ProviderProfileId,
                    profile => profile.Id,
                    (limit, profile) => new { limit.EffectiveFromUtc, limit.EffectiveToUtc, profile.ProviderCode, profile.Version, profile.Status })
                .Where(pair => pair.ProviderCode == request.ProviderCode)
                .ToListAsync(cancellationToken);

            // 組長 PR #73 round-3, item 1：舊的檢查對「所有」版本做重疊比對，而正式／Seed 的目前版本是
            // 開放式窗口（EffectiveToUtc = null），於是任何後續版本都必然重疊、永遠建不出來。裁定 B1
            // 下這是錯的判斷：目前有效的版本會在新版本發布時被收窗到 cutoff，它不是衝突，是被接班的
            // 前一棒。真正的衝突只有兩種——未來版本／其他 Draft 的窗口相撞，或新窗口沒有排在既有版本
            // 之後（起點不晚於它，代表不是接續而是插隊）。
            var newStart = request.EffectiveFromUtc ?? now;
            if (existingWindows.Any(existing => ConflictsWithNewWindow(
                    existing.Status, existing.EffectiveFromUtc, existing.EffectiveToUtc,
                    newStart, request.EffectiveToUtc)))
            {
                throw new ShippingAdminWriteException(ShippingAdminErrorCodes.PackageLimitPeriodOverlap);
            }

            var nextVersion = existingWindows.Count == 0 ? 1 : existingWindows.Max(existing => existing.Version) + 1;

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

            // 組長 PR #73 review item 2: the audit row commits in the same transaction as the new
            // draft — an audit failure rolls the whole create back.
            var actor = await ResolveActorAsync(actorUserId, cancellationToken);
            _auditWriter.Add(AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                actor,
                AuditActions.ShippingPackageLimitCreate,
                AuditResourceTypes.PackageLimitVersion,
                limitVersion.PublicId,
                AuditResult.Success,
                errorCode: null,
                [
                    AuditFieldChange.Code("providerCode", null, request.ProviderCode),
                    AuditFieldChange.Code("version", null, nextVersion.ToString(CultureInfo.InvariantCulture)),
                    AuditFieldChange.Code("status", null, ShippingProviderProfileStatuses.Draft),
                ],
                reason: "package_limit_draft_created",
                auditContext.CorrelationId,
                auditContext.TraceId,
                jobPublicId: null,
                auditContext.RemoteIpAddress));
            await _dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return ToDto(limitVersion, profile);
        }
        catch (SqlException exception) when (exception.Number == ProviderLockTimeoutErrorNumber)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ShippingAdminWriteException(ShippingAdminErrorCodes.ConcurrencyConflict);
        }
        catch (DbUpdateException exception) when (
            exception.GetBaseException() is SqlException sqlException &&
            sqlException.Number is 2601 or 2627)
        {
            // Defense in depth: the provider lock should make a duplicate (ProviderCode, Version)
            // impossible, but if the unique index still fires it is a concurrency race the caller
            // can retry — never an unexplained 500.
            await transaction.RollbackAsync(cancellationToken);
            throw new ShippingAdminWriteException(ShippingAdminErrorCodes.ConcurrencyConflict);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<PackageLimitVersionDto> PublishAsync(
        string providerCode,
        AuditRequestContext auditContext,
        Guid versionPublicId,
        PublishPackageLimitVersionRequest request,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditContext);
        var limitVersion = await _dbContext.PackageLimitVersions
            .SingleOrDefaultAsync(candidate => candidate.PublicId == versionPublicId, cancellationToken)
            ?? throw new ShippingAdminWriteException(ShippingAdminErrorCodes.ResourceNotFound);
        var profile = await _dbContext.ShippingProviderProfiles
            .SingleAsync(candidate => candidate.Id == limitVersion.ProviderProfileId, cancellationToken);

        // 組長 PR #73 review item 4: the route's provider id must own this version — otherwise a
        // request against the wrong provider path could publish another provider's version.
        if (!string.Equals(profile.ProviderCode, providerCode, StringComparison.Ordinal))
        {
            throw new ShippingAdminWriteException(
                ShippingAdminErrorCodes.ResourceNotFound,
                $"Version '{versionPublicId}' does not belong to provider '{providerCode}'.");
        }

        _dbContext.Entry(profile).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;

        var now = DateTime.UtcNow;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // 組長 PR #73 round-3, item 3：Create 有 provider lock 而 Publish 沒有——兩個不同 Draft
            // 各自通過自己的 RowVersion 檢查後同時發布，輸家會撞
            // UX_ProviderProfiles_ProviderCode_Published 並以未映射的 DbUpdateException 變成 500。
            // 同一把 provider-scoped lock 讓同 Provider 的 Create 與 Publish 一起序列化。
            await AcquireProviderLockAsync(profile.ProviderCode, cancellationToken);

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
            // 組長 PR #73 review item 5: publishing out of chronological order (the new version's
            // window starting at or before the currently-published one's own EffectiveFromUtc)
            // used to surface as an unhandled CK_ShippingProviderProfiles_Period SqlException (500)
            // from the Supersede below. Validate the cutoff first and return a stable validation
            // error instead — nothing has been written yet, so no partial state is possible.
            var cutoff = profile.EffectiveFromUtc ?? now;
            if (currentlyPublished is not null &&
                currentlyPublished.EffectiveFromUtc is { } publishedFrom &&
                cutoff <= publishedFrom)
            {
                throw new ShippingAdminWriteException(
                    ShippingAdminErrorCodes.ValidationFailed,
                    $"Version {profile.Version} takes effect at {cutoff:O}, which is not after the currently published version's own start ({publishedFrom:O}). Publish versions in chronological order.");
            }

            if (currentlyPublished is not null)
            {
                currentlyPublished.Supersede(cutoff, now);

                // 組長 PR #73 round-3, item 2：profile 與 limit 的窗口必須一起收在 cutoff，否則舊限制
                // 會留下比 profile 更長的窗口。收窗後舊版本在 cutoff 之前仍然可解析（B1），cutoff 起
                // 才交棒給新版本，中間沒有空窗。
                var supersededLimit = await _dbContext.PackageLimitVersions
                    .SingleOrDefaultAsync(
                        candidate => candidate.ProviderProfileId == currentlyPublished.Id, cancellationToken);
                supersededLimit?.TruncateEffectiveWindow(cutoff, now);
            }

            profile.Publish(now);

            // 組長 PR #73 review item 2: same-transaction audit; failure rolls back the publish.
            var actor = await ResolveActorAsync(actorUserId, cancellationToken);
            _auditWriter.Add(AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                actor,
                AuditActions.ShippingPackageLimitPublish,
                AuditResourceTypes.PackageLimitVersion,
                limitVersion.PublicId,
                AuditResult.Success,
                errorCode: null,
                [
                    AuditFieldChange.Code("providerCode", null, profile.ProviderCode),
                    AuditFieldChange.Code("version", null, profile.Version.ToString(CultureInfo.InvariantCulture)),
                    AuditFieldChange.Code("status", ShippingProviderProfileStatuses.Draft, ShippingProviderProfileStatuses.Published),
                    AuditFieldChange.Code(
                        "supersededVersion",
                        null,
                        currentlyPublished?.Version.ToString(CultureInfo.InvariantCulture) ?? "none"),
                ],
                reason: "package_limit_published",
                auditContext.CorrelationId,
                auditContext.TraceId,
                jobPublicId: null,
                auditContext.RemoteIpAddress));

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ShippingAdminWriteException(ShippingAdminErrorCodes.ConcurrencyConflict);
        }
        catch (SqlException exception) when (exception.Number == ProviderLockTimeoutErrorNumber)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ShippingAdminWriteException(ShippingAdminErrorCodes.ConcurrencyConflict);
        }
        catch (DbUpdateException exception) when (
            exception.GetBaseException() is SqlException sqlException &&
            sqlException.Number is 2601 or 2627)
        {
            // Residual UX_ProviderProfiles_ProviderCode_Published race — the lock should prevent it,
            // but it is a concurrency outcome the caller can retry, never an unexplained 500.
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

    /// <summary>Raised by AcquireProviderLockAsync when sp_getapplock times out.</summary>
    private const int ProviderLockTimeoutErrorNumber = 51000;

    /// <summary>
    /// Serializes a provider's version-lifecycle writes inside the ambient transaction:
    /// sp_getapplock with @LockOwner = 'Transaction' holds an exclusive provider-scoped lock until
    /// commit/rollback, so concurrent creates line up instead of racing the version allocation
    /// (組長 PR #73 round-2 review, P2) and concurrent publishes line up instead of racing the
    /// single-Published invariant (round-3, item 3). 15s is far beyond any legitimate hold time —
    /// hitting the timeout means pathological contention and maps to 409 concurrency_conflict,
    /// not a 500.
    /// </summary>
    private async Task AcquireProviderLockAsync(string providerCode, CancellationToken cancellationToken)
    {
        await _dbContext.Database.ExecuteSqlAsync(
            $"""
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = {"doselect:shipping:package-limit:" + providerCode},
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 15000;
            IF @result < 0
                THROW 51000, 'Could not acquire the package-limit lock for the provider.', 1;
            """,
            cancellationToken);
    }

    /// <summary>Same shape as EfCompatibilityRuleAdminService.ResolveActorAsync — the audit actor
    /// must be a real Admin account still holding one of the roles Shipping.Manage allows.</summary>
    private async Task<AuditActor> ResolveActorAsync(string actorUserId, CancellationToken cancellationToken)
    {
        var admin = await _dbContext.Users.AsNoTracking()
            .Where(user => user.Id == actorUserId && user.AccountType == DoSelect.Domain.Members.AccountType.Admin)
            .Select(user => new { user.Id, user.PublicId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ShippingAdminWriteException(
                ShippingAdminErrorCodes.ValidationFailed, "The administrator identity is invalid.");

        var roles = await (
            from userRole in _dbContext.UserRoles.AsNoTracking()
            join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == admin.Id && role.Name != null
            select role.Name!)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (!roles.Contains(AuditRoleNames.OrderManager, StringComparer.Ordinal) &&
            !roles.Contains(AuditRoleNames.SuperAdmin, StringComparer.Ordinal))
        {
            throw new ShippingAdminWriteException(
                ShippingAdminErrorCodes.ValidationFailed,
                "The administrator no longer has permission to manage shipping settings.");
        }

        return AuditActor.Create(AuditActorType.Admin, admin.PublicId, roles);
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

    /// <summary>
    /// 組長 PR #73 round-3, item 5：Domain 的建構子要求 DateTimeKind.Utc，但 JSON 綁定會把沒有 Z 的
    /// 值給成 Unspecified、帶 offset 的值給成 Local，於是一個純粹的輸入錯誤變成
    /// ArgumentOutOfRangeException 也就是 500。時間欄位在服務入口就驗，回穩定的 validation_failed。
    /// 順帶把「結束不晚於開始」也擋在這裡（同樣是 Domain 會丟例外的輸入）。
    /// </summary>
    private static void ValidateEffectiveWindow(DateTime? effectiveFromUtc, DateTime? effectiveToUtc)
    {
        var errors = new List<string>();
        if (effectiveFromUtc.HasValue && effectiveFromUtc.Value.Kind != DateTimeKind.Utc)
        {
            errors.Add(nameof(CreatePackageLimitVersionRequest.EffectiveFromUtc));
        }

        if (effectiveToUtc.HasValue && effectiveToUtc.Value.Kind != DateTimeKind.Utc)
        {
            errors.Add(nameof(CreatePackageLimitVersionRequest.EffectiveToUtc));
        }

        if (errors.Count > 0)
        {
            throw new ShippingAdminWriteException(
                ShippingAdminErrorCodes.ValidationFailed,
                $"Effective times must be UTC instants ending in 'Z': {string.Join(", ", errors)}.");
        }

        if (effectiveFromUtc.HasValue && effectiveToUtc.HasValue && effectiveToUtc <= effectiveFromUtc)
        {
            throw new ShippingAdminWriteException(
                ShippingAdminErrorCodes.ValidationFailed,
                "EffectiveToUtc must be after EffectiveFromUtc.");
        }
    }

    /// <summary>
    /// 組長 PR #73 round-3, item 1 (裁定 B1)：新窗口只與「真正衝突」的版本相斥。
    /// 目前生效中的版本（起點嚴格早於新版本起點的已發布／已接班版本）會在新版本發布時被收窗到
    /// cutoff，它是前一棒而不是衝突——含正式／Seed 的開放式版本在內。反過來說，任何 Draft、以及
    /// 起點不早於新窗口的版本（未來版本、同起點版本）都是真衝突：發布流程不會替它們收窗，硬寫下去
    /// 會出現同一瞬間兩個有效版本。
    /// </summary>
    private static bool ConflictsWithNewWindow(
        string existingStatus,
        DateTime? existingFrom,
        DateTime? existingTo,
        DateTime newStart,
        DateTime? newTo)
    {
        if (!Overlaps(existingFrom, existingTo, newStart, newTo))
        {
            return false;
        }

        if (ShippingProviderProfileStatuses.IsNeverEffective(existingStatus))
        {
            return true;
        }

        return (existingFrom ?? DateTime.MinValue) >= newStart;
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
