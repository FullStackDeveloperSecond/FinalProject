using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DoSelect.Application.Auditing;
using DoSelect.Application.Builds;
using DoSelect.Application.Common;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Builds;

/// <summary>組長 PR #34 round-4 review: real production write path for a Sku's compatibility facts, with the same category-appropriate whitelist, normalization and RowVersion concurrency any other admin write in this project gets.</summary>
public sealed class EfSkuCompatibilityAttributeAdminService : ISkuCompatibilityAttributeAdminService
{
    private const int MaxAttributeValuesPerKey = 20;
    private const int MaxCodeLength = 64;

    /// <summary>
    /// PR #34 round-6 review, A1 裁定: a stable audit reason code, distinct from any future
    /// free-text reason a caller might add — mirrors EfCompatibilityRuleAdminService's own
    /// AuditReasonSettingChange for the same reason (AuditFieldChange.RequireSafeCode's
    /// identifier-only format).
    /// </summary>
    private const string AuditReasonAttributesReplace = "sku_compatibility_attributes_replace";

    /// <summary>
    /// 組長 PR #34 round-4 review, item 2: an attribute key only means something for the one
    /// component role the rule engine actually reads it from (EfCompatibilityFactsReader) — a
    /// GPU's "required connectors" written onto a Cooler SKU would round-trip fine but the engine
    /// would never look at it, silently no-op. Motherboard's storage-port map is checked the same
    /// way, alongside this table.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AttributeKeyToRequiredCategory =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CompatibilityAttributeKeys.CaseSupportedFormFactors] = BuildComponentCategoryCodes.Case,
            [CompatibilityAttributeKeys.CoolerSupportedSockets] = BuildComponentCategoryCodes.Cooler,
            [CompatibilityAttributeKeys.PsuAvailableConnectors] = BuildComponentCategoryCodes.PowerSupply,
            [CompatibilityAttributeKeys.GpuRequiredConnectors] = BuildComponentCategoryCodes.GraphicsCard,
        };

    private readonly DoSelectDbContext _dbContext;
    private readonly IAuditWriter _auditWriter;

    public EfSkuCompatibilityAttributeAdminService(DoSelectDbContext dbContext, IAuditWriter auditWriter)
    {
        _dbContext = dbContext;
        _auditWriter = auditWriter;
    }

    public async Task<SkuCompatibilityAttributesDto> GetAsync(Guid skuPublicId, CancellationToken cancellationToken)
    {
        var sku = await _dbContext.Skus.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.PublicId == skuPublicId, cancellationToken);
        if (sku is null)
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ResourceNotFound, $"SKU '{skuPublicId}' was not found.");
        }

        return await BuildDtoAsync(sku.Id, skuPublicId, sku.RowVersion, cancellationToken);
    }

    public async Task<SkuCompatibilityAttributesDto> SetAsync(
        Guid skuPublicId,
        string adminUserId,
        SetSkuCompatibilityAttributesRequest request,
        AuditRequestContext auditContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await ResolveActorAsync(adminUserId, cancellationToken);

        var sku = await _dbContext.Skus
            .FirstOrDefaultAsync(candidate => candidate.PublicId == skuPublicId, cancellationToken);
        if (sku is null)
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ResourceNotFound, $"SKU '{skuPublicId}' was not found.");
        }

        // 組長 PR #34 round-5 review, item 2: loaded *tracked* (not AsNoTracking) so its
        // RowVersion becomes part of this write's optimistic-concurrency check below — a
        // concurrent Catalog admin category change on the same Product (EfProductAdminService.
        // UpdateAsync) and this attributes write now share one concurrency boundary without the
        // client having to submit a second RowVersion: whichever SaveChangesAsync commits second
        // finds the Product row's RowVersion has already moved and throws.
        var product = await _dbContext.Products
            .FirstAsync(candidate => candidate.Id == sku.ProductId, cancellationToken);
        var categoryCode = await _dbContext.Categories.AsNoTracking()
            .Where(category => category.Id == product.CategoryId)
            .Select(category => category.Code)
            .SingleAsync(cancellationToken);

        var normalizedAttributes = NormalizeAndValidateAttributes(request.Attributes, categoryCode);
        var normalizedPorts = NormalizeAndValidateStoragePorts(request.StoragePorts, categoryCode);

        var now = DateTime.UtcNow;
        _dbContext.Entry(sku).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Full-replace, mirroring EfSkuAdminService.ReplaceSpecificationsAsync's own shape for
        // the same reason: an admin re-submitting a SKU's compatibility facts means "this is now
        // the complete set", not "add these on top of whatever was there".
        var existingAttributes = await _dbContext.SkuCompatibilityAttributes
            .Where(attribute => attribute.SkuId == sku.Id)
            .ToListAsync(cancellationToken);
        _dbContext.SkuCompatibilityAttributes.RemoveRange(existingAttributes);

        var existingPorts = await _dbContext.SkuStorageInterfacePorts
            .Where(port => port.SkuId == sku.Id)
            .ToListAsync(cancellationToken);
        _dbContext.SkuStorageInterfacePorts.RemoveRange(existingPorts);

        foreach (var (key, values) in normalizedAttributes)
        {
            foreach (var value in values)
            {
                _dbContext.SkuCompatibilityAttributes.Add(
                    new SkuCompatibilityAttribute(sku.Id, key, value, now));
            }
        }

        foreach (var (interfaceCode, portCount) in normalizedPorts)
        {
            _dbContext.SkuStorageInterfacePorts.Add(
                new SkuStorageInterfacePort(sku.Id, interfaceCode, portCount, now));
        }

        // Touch bumps RowVersion/UpdatedAtUtc with no other Sku column changing — this write's
        // only real content lives in the two child tables above, but the concurrency boundary is
        // still the Sku's own RowVersion (round-4 review), so a save must actually move it.
        sku.Touch(now);

        // Round-5 review, item 2: bump the Product's RowVersion too, purely so this write and a
        // concurrent category change on the same Product race on the same row (see the load
        // above) — no other Product column changes.
        product.Touch(now);

        // Round-6 review, A1 裁定: one central Audit entry per write, in the same transaction as
        // the core attribute/port replace — an audit failure rolls the whole write back (caught
        // by the generic `catch` below), and a validation failure or stale RowVersion never
        // reaches this point, so it can never leave a successful Audit behind.
        var beforeAttributes = existingAttributes
            .GroupBy(attribute => attribute.AttributeKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(attribute => attribute.AttributeValue).ToList(),
                StringComparer.Ordinal);
        var beforePorts = existingPorts.ToDictionary(
            port => port.InterfaceCode, port => port.PortCount, StringComparer.Ordinal);

        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            actor,
            AuditActions.SkuCompatibilityAttributesReplace,
            AuditResourceTypes.Sku,
            skuPublicId,
            AuditResult.Success,
            errorCode: null,
            [
                AuditFieldChange.Code("attributesHash", HashAttributes(beforeAttributes), HashAttributes(normalizedAttributes)),
                AuditFieldChange.Code(
                    "attributeKeyCount",
                    beforeAttributes.Count.ToString(CultureInfo.InvariantCulture),
                    normalizedAttributes.Count.ToString(CultureInfo.InvariantCulture)),
                AuditFieldChange.Code("portsHash", HashPorts(beforePorts), HashPorts(normalizedPorts)),
                AuditFieldChange.Code(
                    "portCount",
                    beforePorts.Count.ToString(CultureInfo.InvariantCulture),
                    normalizedPorts.Count.ToString(CultureInfo.InvariantCulture)),
            ],
            AuditReasonAttributesReplace,
            auditContext.CorrelationId,
            auditContext.TraceId,
            jobPublicId: null,
            auditContext.RemoteIpAddress));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ConcurrencyConflict,
                "The SKU or its product was updated by someone else. Reload and try again.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        await transaction.CommitAsync(cancellationToken);

        return await BuildDtoAsync(sku.Id, skuPublicId, sku.RowVersion, cancellationToken);
    }

    /// <summary>Mirrors EfCompatibilityRuleAdminService.ResolveActorAsync exactly — same endpoint policy (CompatibilityRuleManageWarnings: CatalogManager or SuperAdmin), same defensive re-check that a role wasn't revoked between session issuance and this call.</summary>
    private async Task<AuditActor> ResolveActorAsync(string adminUserId, CancellationToken cancellationToken)
    {
        var admin = await _dbContext.Users.AsNoTracking()
            .Where(user => user.Id == adminUserId && user.AccountType == AccountType.Admin)
            .Select(user => new { user.Id, user.PublicId })
            .SingleOrDefaultAsync(cancellationToken);
        if (admin is null)
        {
            throw DomainProblemException.Forbidden("The administrator identity is invalid.");
        }

        var roles = await (
            from userRole in _dbContext.UserRoles.AsNoTracking()
            join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == admin.Id && role.Name != null
            select role.Name!)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (!roles.Contains(AuditRoleNames.CatalogManager, StringComparer.Ordinal) &&
            !roles.Contains(AuditRoleNames.SuperAdmin, StringComparer.Ordinal))
        {
            throw DomainProblemException.Forbidden(
                "The administrator no longer has permission to manage compatibility attributes.");
        }

        return AuditActor.Create(AuditActorType.Admin, admin.PublicId, roles);
    }

    /// <summary>
    /// PR #34 round-6 review, A1 裁定: Before／After must only ever hold the same
    /// normalized/whitelisted data the write itself persists, never raw request JSON — but a
    /// full dictionary dump cannot fit AuditFieldChange.Code's 64-byte identifier-only format.
    /// Hashes the canonical (sorted key, sorted values) text instead, mirroring
    /// EfCompatibilityCheckService.BuildCanonicalInputText／DEC-P310's own "hash instead of raw
    /// content" precedent. "v1|" format-versions the canonical shape.
    /// </summary>
    private static string HashAttributes(IReadOnlyDictionary<string, IReadOnlyList<string>> attributes)
    {
        var canonical = "v1|" + string.Join(';', attributes
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}:{string.Join(',', pair.Value.OrderBy(value => value, StringComparer.Ordinal))}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string HashPorts(IReadOnlyDictionary<string, int> ports)
    {
        var canonical = "v1|" + string.Join(';', ports
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}:{pair.Value.ToString(CultureInfo.InvariantCulture)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private async Task<SkuCompatibilityAttributesDto> BuildDtoAsync(
        long skuId, Guid skuPublicId, byte[] rowVersion, CancellationToken cancellationToken)
    {
        var attributeRows = await _dbContext.SkuCompatibilityAttributes.AsNoTracking()
            .Where(attribute => attribute.SkuId == skuId)
            .ToListAsync(cancellationToken);
        var attributes = attributeRows
            .GroupBy(row => row.AttributeKey)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(row => row.AttributeValue).ToList());

        var portRows = await _dbContext.SkuStorageInterfacePorts.AsNoTracking()
            .Where(port => port.SkuId == skuId)
            .ToListAsync(cancellationToken);
        var ports = portRows.ToDictionary(row => row.InterfaceCode, row => row.PortCount);

        return new SkuCompatibilityAttributesDto(skuPublicId, attributes, ports, rowVersion);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> NormalizeAndValidateAttributes(
        IReadOnlyDictionary<string, IReadOnlyList<string>> attributes, string categoryCode)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (key, values) in attributes)
        {
            if (!AttributeKeyToRequiredCategory.TryGetValue(key, out var requiredCategory))
            {
                throw new BuildWriteException(
                    BuildWriteException.ErrorCodes.ValidationFailed, $"Unknown attribute key '{key}'.");
            }

            if (!string.Equals(requiredCategory, categoryCode, StringComparison.Ordinal))
            {
                throw new BuildWriteException(
                    BuildWriteException.ErrorCodes.ValidationFailed,
                    $"'{key}' only applies to a {requiredCategory} SKU, not {categoryCode}.");
            }

            if (values.Count == 0 || values.Count > MaxAttributeValuesPerKey)
            {
                throw new BuildWriteException(
                    BuildWriteException.ErrorCodes.ValidationFailed,
                    $"'{key}' must have between 1 and {MaxAttributeValuesPerKey} values.");
            }

            var normalizedValues = values.Select(value => NormalizeSystemCode(value, key)).ToArray();
            var duplicates = normalizedValues.GroupBy(value => value, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicates.Length > 0)
            {
                throw new BuildWriteException(
                    BuildWriteException.ErrorCodes.ValidationFailed,
                    $"'{key}' has duplicate value(s): {string.Join(", ", duplicates)}.");
            }

            result[key] = normalizedValues;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, int> NormalizeAndValidateStoragePorts(
        IReadOnlyDictionary<string, int> storagePorts, string categoryCode)
    {
        if (storagePorts.Count == 0)
        {
            return storagePorts;
        }

        if (!string.Equals(categoryCode, BuildComponentCategoryCodes.Motherboard, StringComparison.Ordinal))
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ValidationFailed,
                $"Storage interface ports only apply to a {BuildComponentCategoryCodes.Motherboard} SKU, not {categoryCode}.");
        }

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (interfaceCode, portCount) in storagePorts)
        {
            if (portCount is < 1 or > CompatibilityAttributeLimits.MaxStorageInterfacePortCount)
            {
                throw new BuildWriteException(
                    BuildWriteException.ErrorCodes.ValidationFailed,
                    $"Storage interface '{interfaceCode}' must have a port count between 1 and " +
                    $"{CompatibilityAttributeLimits.MaxStorageInterfacePortCount}.");
            }

            var normalizedCode = NormalizeSystemCode(interfaceCode, "storagePorts");
            if (!result.TryAdd(normalizedCode, portCount))
            {
                throw new BuildWriteException(
                    BuildWriteException.ErrorCodes.ValidationFailed,
                    $"Duplicate storage interface code '{normalizedCode}' after normalization.");
            }
        }

        return result;
    }

    /// <summary>
    /// 組長 PR #34 round-4 review, item 2: sockets／form factors／connector／interface codes are
    /// system codes, not free display text — normalized the same way Catalog's own
    /// `CatalogCode.Normalize` treats a code (Trim + Unicode NFKC + invariant uppercase), so "am5"
    /// and "AM5" collapse to the one value the rule engine's case-sensitive Dictionary lookups
    /// expect. Re-implemented locally rather than reusing `CatalogCode.Normalize` — that type is
    /// internal to the Catalog module, and this module has no existing dependency on it.
    /// </summary>
    private static string NormalizeSystemCode(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ValidationFailed, $"'{fieldName}' values must not be blank.");
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormKC).ToUpper(CultureInfo.InvariantCulture);
        if (Encoding.UTF8.GetByteCount(normalized) > MaxCodeLength)
        {
            throw new BuildWriteException(
                BuildWriteException.ErrorCodes.ValidationFailed,
                $"'{fieldName}' value '{value}' exceeds {MaxCodeLength} bytes after normalization.");
        }

        return normalized;
    }
}
