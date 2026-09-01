using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Shipping;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shipping;

/// <summary>
/// No delete action exists anywhere in this service or its controller — 購物車、訂單、付款與物流.md's
/// "已被購物車或訂單引用的門市不得實體刪除；停用後不可供新訂單選擇" is enforced by construction (deactivate,
/// via Update, is the only removal path) rather than by a runtime reference check.
/// </summary>
public sealed class EfConvenienceStoreAdminService : IConvenienceStoreAdminService
{
    private readonly DoSelectDbContext _dbContext;
    private readonly IAuditWriter _auditWriter;

    public EfConvenienceStoreAdminService(DoSelectDbContext dbContext, IAuditWriter auditWriter)
    {
        _dbContext = dbContext;
        _auditWriter = auditWriter;
    }

    public async Task<PageResult<ConvenienceStoreDto>> ListAsync(
        AdminConvenienceStoreQuery query,
        CancellationToken cancellationToken)
    {
        var stores = _dbContext.ConvenienceStores.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.ProviderCode))
        {
            stores = stores.Where(store => store.ProviderCode == query.ProviderCode);
        }

        if (!string.IsNullOrWhiteSpace(query.City))
        {
            stores = stores.Where(store => store.City == query.City);
        }

        if (!string.IsNullOrWhiteSpace(query.District))
        {
            stores = stores.Where(store => store.District == query.District);
        }

        if (query.IsActive.HasValue)
        {
            stores = stores.Where(store => store.IsActive == query.IsActive.Value);
        }

        var totalCount = await stores.CountAsync(cancellationToken);

        // Same int-overflow guard as EfBrandAdminService/EfProductAdminService (組長 PR #73
        // round-3, item 4): (pageNumber - 1) * pageSize overflows int for a large pageNumber —
        // int.MaxValue passes the [Range] attribute and the multiplication wraps negative, which
        // SQL Server rejects as a 500. Compute in long and answer an out-of-range page with an
        // empty page instead.
        var skip = (long)(query.PageNumber - 1) * query.PageSize;
        var items = skip > int.MaxValue
            ? []
            : await stores
                .OrderByDescending(store => store.UpdatedAtUtc)
                .ThenByDescending(store => store.Id)
                .Skip((int)skip)
                .Take(query.PageSize)
                .Select(store => ToDto(store))
                .ToListAsync(cancellationToken);

        return new PageResult<ConvenienceStoreDto>(items, query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<ConvenienceStoreDto> CreateAsync(
        AuditRequestContext auditContext,
        CreateConvenienceStoreRequest request,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditContext);
        var duplicate = await _dbContext.ConvenienceStores.AnyAsync(
            store => store.ProviderCode == request.ProviderCode && store.StoreCode == request.StoreCode,
            cancellationToken);
        if (duplicate)
        {
            throw new ShippingAdminWriteException(ShippingAdminErrorCodes.StoreCodeDuplicate);
        }

        var store = new ConvenienceStore(
            Guid.CreateVersion7(),
            request.ProviderCode,
            request.StoreCode,
            request.StoreName,
            request.Address,
            request.City,
            request.District,
            // v1 has no real carrier API integration at all (see 購物車、訂單、付款與物流.md's 超商門市維護 —
            // "無真實物流商API整合"), so every store this admin CRUD can create is inherently demo/
            // showcase data, not an import from a real provider feed.
            isDemoData: true,
            DateTime.UtcNow);

        try
        {
            _dbContext.ConvenienceStores.Add(store);
            // 組長 PR #73 review item 2: the audit row is part of the same SaveChanges — EF wraps a
            // single SaveChangesAsync in one transaction, so an audit failure rolls the create back.
            var actor = await ResolveActorAsync(actorUserId, cancellationToken);
            _auditWriter.Add(AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                actor,
                AuditActions.ShippingStoreCreate,
                AuditResourceTypes.ConvenienceStore,
                store.PublicId,
                AuditResult.Success,
                errorCode: null,
                [
                    AuditFieldChange.Code("providerCode", null, request.ProviderCode),
                    AuditFieldChange.Code("storeCode", null, request.StoreCode),
                    AuditFieldChange.Code("isActive", null, "true"),
                ],
                reason: "store_created",
                auditContext.CorrelationId,
                auditContext.TraceId,
                jobPublicId: null,
                auditContext.RemoteIpAddress));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var raceLostToDuplicate = await _dbContext.ConvenienceStores.AnyAsync(
                existing => existing.ProviderCode == request.ProviderCode && existing.StoreCode == request.StoreCode,
                cancellationToken);
            if (!raceLostToDuplicate)
            {
                throw;
            }

            throw new ShippingAdminWriteException(ShippingAdminErrorCodes.StoreCodeDuplicate);
        }

        return ToDto(store);
    }

    public async Task<ConvenienceStoreDto> UpdateAsync(
        AuditRequestContext auditContext,
        Guid publicId,
        UpdateConvenienceStoreRequest request,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditContext);
        var store = await _dbContext.ConvenienceStores.SingleOrDefaultAsync(
            candidate => candidate.PublicId == publicId, cancellationToken)
            ?? throw new ShippingAdminWriteException(ShippingAdminErrorCodes.ResourceNotFound);

        // 門市異動保存操作者、前後值、時間及 Audit Log (購物車、訂單、付款與物流.md 超商門市維護)。
        // Free-text values (name/address) stay out of the safe-code audit fields — the audit
        // records *which* fields changed (the coupon.update precedent); the values themselves live
        // on the row and its rowversion history.
        var changes = new List<AuditFieldChange>
        {
            AuditFieldChange.Code("providerCode", null, store.ProviderCode),
            AuditFieldChange.Code("storeCode", null, store.StoreCode),
        };
        if (!string.Equals(store.StoreName, request.StoreName, StringComparison.Ordinal))
        {
            changes.Add(AuditFieldChange.Changed("storeName"));
        }

        if (!string.Equals(store.Address, request.Address, StringComparison.Ordinal))
        {
            changes.Add(AuditFieldChange.Changed("address"));
        }

        if (!string.Equals(store.City, request.City, StringComparison.Ordinal))
        {
            changes.Add(AuditFieldChange.Changed("city"));
        }

        if (!string.Equals(store.District, request.District, StringComparison.Ordinal))
        {
            changes.Add(AuditFieldChange.Changed("district"));
        }

        if (store.IsActive != request.IsActive)
        {
            changes.Add(AuditFieldChange.Code(
                "isActive",
                store.IsActive ? "true" : "false",
                request.IsActive ? "true" : "false"));
        }

        _dbContext.Entry(store).Property(candidate => candidate.RowVersion).OriginalValue = request.RowVersion;
        store.UpdateDetails(request.StoreName, request.Address, request.City, request.District, request.IsActive, DateTime.UtcNow);

        var actor = await ResolveActorAsync(actorUserId, cancellationToken);
        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            actor,
            AuditActions.ShippingStoreUpdate,
            AuditResourceTypes.ConvenienceStore,
            store.PublicId,
            AuditResult.Success,
            errorCode: null,
            changes,
            reason: "store_updated",
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
            throw new ShippingAdminWriteException(ShippingAdminErrorCodes.ConcurrencyConflict);
        }

        return ToDto(store);
    }

    /// <summary>Same shape as EfPackageLimitService.ResolveActorAsync.</summary>
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

    private static ConvenienceStoreDto ToDto(ConvenienceStore store) => new(
        store.PublicId,
        store.ProviderCode,
        store.StoreCode,
        store.StoreName,
        store.Address,
        store.City,
        store.District,
        store.IsDemoData,
        store.IsActive,
        store.RowVersion);
}
