using DoSelect.Application.Common;
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
/// 內部 Id 不得離開 Infrastructure（DEC-P290）。清單與明細共用同一個批次投影，
/// 避免每一列各自查詢訂單、退貨、分攤與管理員。
/// </remarks>
public sealed class RefundReader : IRefundReader
{
    private readonly DoSelectDbContext _context;

    public RefundReader(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<PageResult<RefundDto>> ListAsync(
        AdminRefundQuery query,
        CancellationToken cancellationToken = default)
    {
        AdminRefundQueryValidator.RequireValid(query);

        var filtered = _context.Refunds.AsNoTracking().AsQueryable();

        if (query.Statuses is { Count: > 0 } requestedStatuses)
        {
            var statuses = requestedStatuses.ToArray();
            filtered = filtered.Where(refund => statuses.Contains(refund.Status));
        }

        if (query.FromUtc is { } fromUtc)
        {
            filtered = filtered.Where(refund => refund.CreatedAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            filtered = filtered.Where(refund => refund.CreatedAtUtc < toUtc);
        }

        var keyword = query.Q?.Trim();
        if (!string.IsNullOrEmpty(keyword))
        {
            filtered = filtered.Where(refund => refund.RefundNumber.Contains(keyword));
        }

        var totalCount = await filtered.CountAsync(cancellationToken);
        var skip = ((long)query.PageNumber - 1) * query.PageSize;
        if (skip > int.MaxValue)
        {
            return new PageResult<RefundDto>(
                [], query.PageNumber, query.PageSize, totalCount);
        }

        var headers = await Project(filtered
                .OrderByDescending(refund => refund.CreatedAtUtc)
                .ThenByDescending(refund => refund.Id))
            .Skip((int)skip)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);

        var items = await ComposeAsync(headers, cancellationToken);
        return new PageResult<RefundDto>(
            items, query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<RefundDto?> FindByPublicIdAsync(
        Guid refundPublicId,
        CancellationToken cancellationToken = default)
    {
        if (refundPublicId == Guid.Empty)
        {
            return null;
        }

        var header = await Project(_context.Refunds
                .AsNoTracking()
                .Where(candidate => candidate.PublicId == refundPublicId))
            .SingleOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        return await ComposeAsync([header], cancellationToken) is [var dto] ? dto : null;
    }

    private async Task<IReadOnlyList<RefundDto>> ComposeAsync(
        IReadOnlyList<RefundHeader> headers,
        CancellationToken cancellationToken)
    {
        if (headers.Count == 0)
        {
            return [];
        }

        var refundIds = headers.Select(header => header.Id).ToArray();
        var orderIds = headers.Select(header => header.OrderId).Distinct().ToArray();
        var returnIds = headers
            .Where(header => header.ReturnRequestId is not null)
            .Select(header => header.ReturnRequestId!.Value)
            .Distinct()
            .ToArray();

        var orderPublicIds = await _context.Orders
            .AsNoTracking()
            .Where(order => orderIds.Contains(order.Id))
            .ToDictionaryAsync(order => order.Id, order => order.PublicId, cancellationToken);

        var returnPublicIds = returnIds.Length == 0
            ? new Dictionary<long, Guid>()
            : await _context.ReturnRequests
                .AsNoTracking()
                .Where(request => returnIds.Contains(request.Id))
                .ToDictionaryAsync(
                    request => request.Id, request => request.PublicId, cancellationToken);

        var allocations = await _context.RefundAllocations
            .AsNoTracking()
            .Where(allocation => refundIds.Contains(allocation.RefundId))
            .OrderBy(allocation => allocation.Id)
            .Select(allocation => new AllocationRow(
                allocation.RefundId,
                allocation.OrderItemId,
                allocation.Quantity,
                allocation.AllocationType,
                allocation.Amount))
            .ToArrayAsync(cancellationToken);

        var itemIds = allocations
            .Where(allocation => allocation.OrderItemId is not null)
            .Select(allocation => allocation.OrderItemId!.Value)
            .Distinct()
            .ToArray();

        var itemPublicIds = itemIds.Length == 0
            ? new Dictionary<long, Guid>()
            : await _context.OrderItems
                .AsNoTracking()
                .Where(item => itemIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, item => item.PublicId, cancellationToken);

        var allocationsByRefund = allocations
            .GroupBy(allocation => allocation.RefundId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RefundAllocationDto>)[.. group.Select(allocation =>
                    new RefundAllocationDto(
                        allocation.OrderItemId is { } itemId ? itemPublicIds[itemId] : null,
                        allocation.Quantity,
                        allocation.AllocationType,
                        allocation.Amount))]);

        var admins = await ResolveAdminsAsync(
            [.. headers.SelectMany(header => new[]
            {
                header.RequestedBy,
                header.ApprovedBy,
                header.ExecutedByAdminUserId,
            })],
            cancellationToken);

        return
        [
            .. headers.Select(header => new RefundDto(
                header.PublicId,
                header.RefundNumber,
                orderPublicIds[header.OrderId],
                header.ReturnRequestId is { } returnRequestId &&
                    returnPublicIds.TryGetValue(returnRequestId, out var returnPublicId)
                        ? returnPublicId
                        : null,
                header.Status,
                header.RequestedAmount,
                header.ApprovedAmount,
                header.SucceededAmount,
                allocationsByRefund.GetValueOrDefault(header.Id) ?? [],
                Summarize(admins, header.RequestedBy),
                Summarize(admins, header.ApprovedBy),
                Summarize(admins, header.ExecutedByAdminUserId),
                AsUtc(header.CreatedAtUtc),
                header.SucceededAtUtc is { } succeededAtUtc ? AsUtc(succeededAtUtc) : null,
                header.RowVersion)),
        ];
    }

    private static IQueryable<RefundHeader> Project(
        IQueryable<DoSelect.Domain.Refunds.Refund> refunds) =>
        refunds.Select(refund => new RefundHeader(
            refund.Id,
            refund.OrderId,
            refund.ReturnRequestId,
            refund.PublicId,
            refund.RefundNumber,
            refund.Status,
            refund.RequestedAmount,
            refund.ApprovedAmount,
            refund.SucceededAmount,
            refund.RequestedBy,
            refund.ApprovedBy,
            refund.ExecutedByAdminUserId,
            refund.CreatedAtUtc,
            refund.SucceededAtUtc,
            refund.RowVersion));

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

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record RefundHeader(
        long Id,
        long OrderId,
        long? ReturnRequestId,
        Guid PublicId,
        string RefundNumber,
        DoSelect.Domain.Refunds.RefundStatus Status,
        decimal RequestedAmount,
        decimal? ApprovedAmount,
        decimal? SucceededAmount,
        string? RequestedBy,
        string? ApprovedBy,
        string? ExecutedByAdminUserId,
        DateTime CreatedAtUtc,
        DateTime? SucceededAtUtc,
        byte[] RowVersion);

    private sealed record AllocationRow(
        long RefundId,
        long? OrderItemId,
        int? Quantity,
        DoSelect.Domain.Refunds.RefundAllocationType AllocationType,
        decimal Amount);
}
