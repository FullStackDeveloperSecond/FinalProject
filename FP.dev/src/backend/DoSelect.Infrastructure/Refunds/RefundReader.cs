using DoSelect.Application.Members;
using DoSelect.Application.Refunds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Refunds;

/// <summary>
/// 讀出退款的正式 <see cref="RefundDto"/>。
/// </summary>
/// <remarks>
/// 這是 Identity 內部 Id 轉成管理員 <c>PublicId</c> 與遮蔽標籤的唯一轉換點；
/// 內部 Id 不得離開 Infrastructure（DEC-P290）。
/// </remarks>
public sealed class RefundReader : IRefundReader
{
    private readonly DoSelectDbContext _context;

    public RefundReader(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<RefundDto?> FindByPublicIdAsync(
        Guid refundPublicId,
        CancellationToken cancellationToken = default)
    {
        if (refundPublicId == Guid.Empty)
        {
            return null;
        }

        var refund = await _context.Refunds
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.PublicId == refundPublicId, cancellationToken);

        if (refund is null)
        {
            return null;
        }

        var orderPublicId = await _context.Orders
            .AsNoTracking()
            .Where(order => order.Id == refund.OrderId)
            .Select(order => order.PublicId)
            .SingleAsync(cancellationToken);

        Guid? returnPublicId = null;
        if (refund.ReturnRequestId is { } returnRequestId)
        {
            returnPublicId = await _context.ReturnRequests
                .AsNoTracking()
                .Where(request => request.Id == returnRequestId)
                .Select(request => (Guid?)request.PublicId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var allocations = await _context.RefundAllocations
            .AsNoTracking()
            .Where(allocation => allocation.RefundId == refund.Id)
            .OrderBy(allocation => allocation.Id)
            .Select(allocation => new
            {
                allocation.OrderItemId,
                allocation.Quantity,
                allocation.AllocationType,
                allocation.Amount,
            })
            .ToArrayAsync(cancellationToken);

        var itemIds = allocations
            .Where(allocation => allocation.OrderItemId is not null)
            .Select(allocation => allocation.OrderItemId!.Value)
            .Distinct()
            .ToArray();

        var itemPublicIds = await _context.OrderItems
            .AsNoTracking()
            .Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.PublicId, cancellationToken);

        var admins = await ResolveAdminsAsync(
            [refund.RequestedBy, refund.ApprovedBy, refund.ExecutedByAdminUserId],
            cancellationToken);

        return new RefundDto(
            refund.PublicId,
            refund.RefundNumber,
            orderPublicId,
            returnPublicId,
            refund.Status,
            refund.RequestedAmount,
            refund.ApprovedAmount,
            refund.SucceededAmount,
            allocations
                .Select(allocation => new RefundAllocationDto(
                    allocation.OrderItemId is { } itemId ? itemPublicIds[itemId] : null,
                    allocation.Quantity,
                    allocation.AllocationType,
                    allocation.Amount))
                .ToArray(),
            Summarize(admins, refund.RequestedBy),
            Summarize(admins, refund.ApprovedBy),
            Summarize(admins, refund.ExecutedByAdminUserId),
            AsUtc(refund.CreatedAtUtc),
            refund.SucceededAtUtc is { } succeededAtUtc ? AsUtc(succeededAtUtc) : null,
            refund.RowVersion);
    }

    /// <summary>
    /// 一次查出三個管理員欄位對應的 PublicId 與遮蔽標籤。
    /// </summary>
    /// <remarks>
    /// 三個欄位常常指向同一個人，因此去重後一次查詢，不逐欄位往返資料庫。
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, MaskedAdminSummaryDto>> ResolveAdminsAsync(
        IReadOnlyList<string?> adminUserIds,
        CancellationToken cancellationToken)
    {
        var ids = adminUserIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<string, MaskedAdminSummaryDto>(StringComparer.Ordinal);
        }

        var users = await _context.Users
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .Select(user => new { user.Id, user.PublicId, user.Email })
            .ToArrayAsync(cancellationToken);

        return users.ToDictionary(
            user => user.Id,
            user => new MaskedAdminSummaryDto(
                user.PublicId,
                // 契約要求優先使用遮蔽 DisplayName，缺少時才用遮蔽 Email。
                // ApplicationUser 目前沒有 DisplayName 欄位，因此一律走 Email 這條。
                // 完整 Email 絕不外流。
                string.IsNullOrWhiteSpace(user.Email)
                    ? "***"
                    : EmailMasking.Mask(user.Email)),
            StringComparer.Ordinal);
    }

    private static MaskedAdminSummaryDto? Summarize(
        IReadOnlyDictionary<string, MaskedAdminSummaryDto> admins,
        string? adminUserId) =>
        !string.IsNullOrWhiteSpace(adminUserId) &&
        admins.TryGetValue(adminUserId.Trim(), out var summary)
            ? summary
            : null;

    /// <summary>
    /// 把 SQL Server 讀回來的 <c>datetime2</c> 標記為 UTC。
    /// </summary>
    /// <remarks>
    /// <c>datetime2</c> 不保存時區，EF 讀回來一律是 <see cref="DateTimeKind.Unspecified"/>，
    /// 序列化時不會帶 <c>Z</c>，客戶端只能猜。資料庫存的本來就是 UTC
    /// （Entity 建構子都以 <c>RequireUtc</c> 把關），這裡只是把事實重新標上。
    /// </remarks>
    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
