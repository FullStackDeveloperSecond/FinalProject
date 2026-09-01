using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

/// <summary>
/// A-09 分類規格範本後台。可變的只有展示、必填與排序，以及 Option 的展示／排序／啟用——
/// 結構欄位（Category、SemanticKey、ValueType、Unit、AllowsMultiple）與 Option 的 Code 依資料字典
/// 「被使用後不可改」，這個 API 一律不開放編輯，改型別的正確做法是新增定義並停用舊的。
/// </summary>
public sealed class EfSpecificationDefinitionAdminService : ISpecificationDefinitionAdminService
{
    private readonly DoSelectDbContext _dbContext;

    public EfSpecificationDefinitionAdminService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PageResult<SpecificationDefinitionDto>> ListAsync(
        SpecificationDefinitionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var definitions = _dbContext.SpecificationDefinitions.AsNoTracking().AsQueryable();

        if (query.CategoryPublicId.HasValue)
        {
            var categoryId = await _dbContext.Categories.AsNoTracking()
                .Where(category => category.PublicId == query.CategoryPublicId.Value)
                .Select(category => (long?)category.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (categoryId is null)
            {
                throw new CatalogWriteException(
                    CatalogWriteException.ErrorCodes.ReferenceNotFound,
                    $"Category '{query.CategoryPublicId}' was not found.");
            }

            definitions = definitions.Where(definition => definition.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var keyword = query.Q.Trim();
            definitions = definitions.Where(definition =>
                definition.SemanticKey.Contains(keyword) || definition.DisplayNameZhTw.Contains(keyword));
        }

        if (query.IsActive.HasValue)
        {
            definitions = definitions.Where(definition => definition.IsActive == query.IsActive.Value);
        }

        var totalCount = await definitions.CountAsync(cancellationToken);

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        // Same int-overflow guard as EfBrandAdminService/EfProductAdminService: an extreme
        // pageNumber would otherwise wrap the offset negative and fail in SQL Server.
        var skip = (long)(pageNumber - 1) * pageSize;
        var entities = skip > int.MaxValue
            ? []
            : await definitions
                .OrderBy(definition => definition.SortOrder)
                .ThenBy(definition => definition.SemanticKey)
                .Skip((int)skip)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

        var items = await ToDtosAsync(entities, cancellationToken);
        return new PageResult<SpecificationDefinitionDto>(items, pageNumber, pageSize, totalCount);
    }

    public async Task<SpecificationDefinitionDto> CreateAsync(
        CreateSpecificationDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var category = await _dbContext.Categories.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.PublicId == request.CategoryPublicId, cancellationToken)
            ?? throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ReferenceNotFound,
                $"Category '{request.CategoryPublicId}' was not found.");

        var valueType = ParseValueType(request.ValueType);
        var semanticKey = NormalizeCode(request.SemanticKey);
        if (semanticKey.Length == 0 || semanticKey.Length > 64)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "semanticKey must be 1-64 characters.");
        }

        ValidateDisplayName(request.DisplayNameZhTw);
        var measurementUnitId = await ResolveMeasurementUnitAsync(valueType, request.UnitCode, cancellationToken);
        ValidateOptionInputs(valueType, request.AllowsMultiple, request.Options);

        var duplicate = await _dbContext.SpecificationDefinitions.AsNoTracking()
            .AnyAsync(
                candidate => candidate.CategoryId == category.Id && candidate.SemanticKey == semanticKey,
                cancellationToken);
        if (duplicate)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.SpecificationSemanticKeyDuplicate,
                $"Semantic key '{semanticKey}' already exists in category '{category.Code}'.");
        }

        var now = DateTime.UtcNow;
        var definition = new SpecificationDefinition(
            Guid.CreateVersion7(),
            category.Id,
            semanticKey,
            request.DisplayNameZhTw,
            valueType,
            measurementUnitId,
            request.IsRequired,
            isProtected: false,
            request.SortOrder,
            now,
            request.AllowsMultiple);

        // 受保護與否不是管理員可填的欄位：由 CompatibilityCatalogContract 這份程式碼目錄決定
        // （資料字典：「受保護 Category Code／SemanticKey 組合由程式碼目錄固定」）。
        if (IsProtectedCombination(category.Code, semanticKey))
        {
            definition.MarkProtected(now);
        }

        // The options need the definition's identity, so this takes two SaveChanges — which means
        // it needs a transaction. Without one, a failure on the second save (an option that the
        // database rejects, a concurrent writer) left a committed definition with no options
        // behind, and the caller saw a 500 with half the write applied.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _dbContext.SpecificationDefinitions.Add(definition);
            await _dbContext.SaveChangesAsync(cancellationToken);

            foreach (var option in request.Options)
            {
                var created = new SpecificationOption(
                    Guid.CreateVersion7(),
                    definition.Id,
                    option.Code,
                    option.DisplayNameZhTw,
                    option.SortOrder,
                    now);

                // 組長 PR #77 review item 2：SpecificationOption 建構後固定是啟用，輸入的
                // IsActive 先前被整個忽略——管理員送 isActive: false 也會得到一個啟用的選項。
                if (!option.IsActive)
                {
                    created.SetActive(false, now);
                }

                _dbContext.SpecificationOptions.Add(created);
            }

            if (request.Options.Count > 0)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // The duplicate check above is a separate query, so two concurrent creates can both
            // pass it and race to the insert. UX_SpecificationDefinitions_CategoryId_SemanticKey
            // catches the loser — report the same stable 409 the pre-check would have, not an
            // unmapped 500.
            await transaction.RollbackAsync(cancellationToken);
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.SpecificationSemanticKeyDuplicate,
                $"Semantic key '{semanticKey}' already exists in category '{category.Code}'.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return (await ToDtosAsync([definition], cancellationToken))[0];
    }

    public async Task<SpecificationDefinitionDto> UpdateAsync(
        Guid publicId,
        UpdateSpecificationDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var definition = await _dbContext.SpecificationDefinitions
            .FirstOrDefaultAsync(candidate => candidate.PublicId == publicId, cancellationToken)
            ?? throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ResourceNotFound,
                $"Specification definition '{publicId}' was not found.");

        // 受保護的定義是固定相容性引擎的必要欄位；把 IsRequired 關掉等於讓該分類的硬性規則
        // 失去輸入，與停用受保護定義是同一件事，因此用同一個錯誤碼擋下。
        if (definition.IsProtected && !request.IsRequired)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.SpecificationDefinitionReferenced,
                $"Semantic key '{definition.SemanticKey}' is required by the fixed compatibility rules and must stay required.");
        }

        ValidateDisplayName(request.DisplayNameZhTw);
        ValidateOptionInputs(definition.ValueType, definition.AllowsMultiple, request.Options);

        _dbContext.Entry(definition).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;

        var now = DateTime.UtcNow;
        definition.UpdateDetails(request.DisplayNameZhTw, request.IsRequired, request.SortOrder, now);
        await ApplyOptionsAsync(definition, request.Options, now, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ConcurrencyConflict,
                "The specification definition was updated by someone else. Reload and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Two admins adding the same option code at the same time both pass the in-request
            // duplicate check and race UX_SpecificationOptions_DefinitionId_Code. That is a
            // concurrent modification, so it gets the same stable 409 as a stale RowVersion.
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ConcurrencyConflict,
                "The specification definition's options were changed by someone else. Reload and try again.");
        }

        return (await ToDtosAsync([definition], cancellationToken))[0];
    }

    public async Task<SpecificationDefinitionDto> DisableAsync(
        Guid publicId,
        DisableSpecificationDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var definition = await _dbContext.SpecificationDefinitions
            .FirstOrDefaultAsync(candidate => candidate.PublicId == publicId, cancellationToken)
            ?? throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ResourceNotFound,
                $"Specification definition '{publicId}' was not found.");

        // API錯誤碼目錄：「規格定義已被…相容性規則引用，只能停用」。受保護組合連停用都不行——
        // 停掉它，該分類的硬性相容規則就永遠缺輸入而只能回 InsufficientData。
        if (definition.IsProtected)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.SpecificationDefinitionReferenced,
                $"Semantic key '{definition.SemanticKey}' is referenced by the fixed compatibility rules and cannot be disabled.");
        }

        _dbContext.Entry(definition).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;

        var now = DateTime.UtcNow;
        definition.SetActive(false, now);

        // 停用定義時，其選項一併停用：留著啟用中的孤兒選項會讓前台篩選與匯入仍看得到它們。
        var options = await _dbContext.SpecificationOptions
            .Where(option => option.SpecificationDefinitionId == definition.Id && option.IsActive)
            .ToListAsync(cancellationToken);
        foreach (var option in options)
        {
            option.SetActive(false, now);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ConcurrencyConflict,
                "The specification definition was updated by someone else. Reload and try again.");
        }

        return (await ToDtosAsync([definition], cancellationToken))[0];
    }

    /// <summary>
    /// Options are upserted by Code, never deleted: 資料字典「被使用後不可刪除或改 Code」。A code
    /// missing from the request is deactivated instead, so an option a SKU already selected stays
    /// resolvable while disappearing from every new selection.
    /// </summary>
    private async Task ApplyOptionsAsync(
        SpecificationDefinition definition,
        IReadOnlyList<SpecificationOptionInput> inputs,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.SpecificationOptions
            .Where(option => option.SpecificationDefinitionId == definition.Id)
            .ToListAsync(cancellationToken);
        var existingByCode = existing.ToDictionary(option => option.Code, StringComparer.Ordinal);
        var requestedCodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var input in inputs)
        {
            var code = NormalizeCode(input.Code);
            requestedCodes.Add(code);
            if (existingByCode.TryGetValue(code, out var option))
            {
                option.UpdateDetails(input.DisplayNameZhTw, input.SortOrder, now);
                option.SetActive(input.IsActive, now);
                continue;
            }

            var added = new SpecificationOption(
                Guid.CreateVersion7(), definition.Id, code, input.DisplayNameZhTw, input.SortOrder, now);
            // 同 item 2：更新時新增的選項也要套用輸入的 IsActive。
            if (!input.IsActive)
            {
                added.SetActive(false, now);
            }

            _dbContext.SpecificationOptions.Add(added);
        }

        foreach (var option in existing.Where(option => option.IsActive && !requestedCodes.Contains(option.Code)))
        {
            option.SetActive(false, now);
        }
    }

    private async Task<long?> ResolveMeasurementUnitAsync(
        SpecificationValueType valueType,
        string? unitCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(unitCode))
        {
            return null;
        }

        // 資料字典：「非 Decimal 不得指定 Unit」。Domain 建構子也會擋，但那會變成 500，這裡先回 400。
        if (valueType != SpecificationValueType.Decimal)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.SpecificationInvalid,
                "Only a Decimal specification may declare a measurement unit.");
        }

        var normalized = NormalizeCode(unitCode);
        var unitId = await _dbContext.MeasurementUnits.AsNoTracking()
            .Where(unit => unit.Code == normalized)
            .Select(unit => (long?)unit.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return unitId ?? throw new CatalogWriteException(
            CatalogWriteException.ErrorCodes.ReferenceNotFound,
            $"Measurement unit '{unitCode}' was not found.");
    }

    /// <summary>
    /// DisplayNameZhTw is nvarchar(160) in SpecificationConfigurations for both the definition and
    /// its options. Without this check an over-long name reached SQL Server and came back as an
    /// unmapped DbUpdateException — a plain input mistake surfacing as a 500.
    /// </summary>
    private static void ValidateDisplayName(string displayNameZhTw)
    {
        if (string.IsNullOrWhiteSpace(displayNameZhTw) || displayNameZhTw.Length > MaxDisplayNameLength)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                $"displayNameZhTw must be 1-{MaxDisplayNameLength} characters.");
        }
    }

    /// <summary>2601/2627: unique index / unique constraint violation.</summary>
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.GetBaseException() is SqlException { Number: 2601 or 2627 };

    private const int MaxDisplayNameLength = 160;

    private static void ValidateOptionInputs(
        SpecificationValueType valueType,
        bool allowsMultiple,
        IReadOnlyList<SpecificationOptionInput> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (valueType != SpecificationValueType.Option)
        {
            if (options.Count > 0)
            {
                throw new CatalogWriteException(
                    CatalogWriteException.ErrorCodes.SpecificationInvalid,
                    "Only an Option specification may declare options.");
            }

            // 資料字典：「AllowsMultiple=1 僅適用 Option」。Domain 也會擋，這裡先給 400。
            if (allowsMultiple)
            {
                throw new CatalogWriteException(
                    CatalogWriteException.ErrorCodes.SpecificationInvalid,
                    "Only an Option specification may allow multiple selections.");
            }

            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in options)
        {
            var code = NormalizeCode(option.Code);
            if (code.Length == 0 || code.Length > 64)
            {
                throw new CatalogWriteException(
                    CatalogWriteException.ErrorCodes.SpecificationInvalid,
                    "Every option code must be 1-64 characters.");
            }

            if (string.IsNullOrWhiteSpace(option.DisplayNameZhTw) ||
                option.DisplayNameZhTw.Length > MaxDisplayNameLength)
            {
                throw new CatalogWriteException(
                    CatalogWriteException.ErrorCodes.SpecificationInvalid,
                    $"Every option's displayNameZhTw must be 1-{MaxDisplayNameLength} characters.");
            }

            // UX_SpecificationOptions_DefinitionId_Code：同一批送出重複的 Code 會撞唯一索引，
            // 先在這裡擋成 400，而不是讓它變成 DbUpdateException。
            if (!seen.Add(code))
            {
                throw new CatalogWriteException(
                    CatalogWriteException.ErrorCodes.SpecificationInvalid,
                    $"Option code '{code}' is repeated in the request.");
            }
        }
    }

    private static SpecificationValueType ParseValueType(string value) =>
        Enum.TryParse<SpecificationValueType>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.SpecificationInvalid,
                $"Unknown specification value type '{value}'.");

    private static bool IsProtectedCombination(string categoryCode, string semanticKey) =>
        CompatibilityCatalogContract.RequiredSemanticKeysByCategory
            .TryGetValue(categoryCode, out var keys) && keys.Contains(semanticKey);

    private static string NormalizeCode(string value) =>
        value.Trim().Normalize(System.Text.NormalizationForm.FormKC).ToUpperInvariant();

    private async Task<IReadOnlyList<SpecificationDefinitionDto>> ToDtosAsync(
        IReadOnlyList<SpecificationDefinition> definitions,
        CancellationToken cancellationToken)
    {
        if (definitions.Count == 0)
        {
            return [];
        }

        var definitionIds = definitions.Select(definition => definition.Id).ToArray();
        var categoryIds = definitions.Select(definition => definition.CategoryId).Distinct().ToArray();
        var unitIds = definitions
            .Where(definition => definition.MeasurementUnitId.HasValue)
            .Select(definition => definition.MeasurementUnitId!.Value)
            .Distinct()
            .ToArray();

        var categories = await _dbContext.Categories.AsNoTracking()
            .Where(category => categoryIds.Contains(category.Id))
            .Select(category => new { category.Id, category.PublicId, category.Code })
            .ToDictionaryAsync(category => category.Id, cancellationToken);
        var units = unitIds.Length == 0
            ? []
            : await _dbContext.MeasurementUnits.AsNoTracking()
                .Where(unit => unitIds.Contains(unit.Id))
                .ToDictionaryAsync(unit => unit.Id, unit => unit.Code, cancellationToken);
        var options = await _dbContext.SpecificationOptions.AsNoTracking()
            .Where(option => definitionIds.Contains(option.SpecificationDefinitionId))
            .OrderBy(option => option.SortOrder)
            .ThenBy(option => option.Code)
            .ToListAsync(cancellationToken);

        return definitions.Select(definition =>
        {
            var category = categories[definition.CategoryId];
            return new SpecificationDefinitionDto(
                definition.PublicId,
                category.PublicId,
                category.Code,
                definition.SemanticKey,
                definition.DisplayNameZhTw,
                definition.ValueType.ToString(),
                definition.MeasurementUnitId is { } unitId && units.TryGetValue(unitId, out var unitCode)
                    ? unitCode
                    : null,
                definition.IsRequired,
                definition.AllowsMultiple,
                definition.IsProtected,
                definition.IsActive,
                definition.SortOrder,
                options
                    .Where(option => option.SpecificationDefinitionId == definition.Id)
                    .Select(option => new SpecificationOptionDto(
                        option.PublicId, option.Code, option.DisplayNameZhTw, option.IsActive, option.SortOrder))
                    .ToList(),
                definition.RowVersion);
        }).ToList();
    }
}
