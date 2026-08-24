using DoSelect.Application.Common;
using DoSelect.Application.Members;
using DoSelect.Domain.Members;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Returns;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Members;

public sealed class AdminMemberQueryReader : IAdminMemberQueryReader
{
    private const int RecentOrderLimit = 10;
    private const int ActivityLogLimit = 20;

    private readonly DoSelectDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public AdminMemberQueryReader(DoSelectDbContext dbContext, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<AdminMemberListResult> ListAsync(
        AdminMemberQuery query, CancellationToken cancellationToken = default)
    {
        var baseQuery = _dbContext.MemberProfiles.AsNoTracking()
            .Join(
                _dbContext.Users.AsNoTracking(),
                profile => profile.UserId,
                user => user.Id,
                (profile, user) => new { profile, user })
            .Where(x => x.user.AccountType == AccountType.Member);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search;
            baseQuery = baseQuery.Where(x =>
                EF.Functions.Like(x.profile.DisplayName, $"%{term}%") ||
                (x.user.Email != null && EF.Functions.Like(x.user.Email, $"%{term}%")));
        }

        if (query.Status is { } status)
        {
            baseQuery = baseQuery.Where(x => x.user.AccountStatus == status);
        }

        if (query.RegisteredFrom is { } from)
        {
            var fromUtc = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            baseQuery = baseQuery.Where(x => x.user.CreatedAtUtc >= fromUtc);
        }

        if (query.RegisteredTo is { } to)
        {
            var toUtc = DateTime.SpecifyKind(to.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);
            baseQuery = baseQuery.Where(x => x.user.CreatedAtUtc <= toUtc);
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var items = await baseQuery
            .OrderByDescending(x => x.user.CreatedAtUtc)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new AdminMemberRow(
                x.user.PublicId,
                x.profile.DisplayName,
                x.user.Email ?? string.Empty,
                x.user.CreatedAtUtc,
                x.user.AccountStatus))
            .ToArrayAsync(cancellationToken);

        var today = _timeProvider.GetUtcNow().UtcDateTime.Date;
        var totalMembers = await _dbContext.Users
            .CountAsync(u => u.AccountType == AccountType.Member, cancellationToken);
        var newTodayCount = await _dbContext.Users.CountAsync(
            u => u.AccountType == AccountType.Member && u.CreatedAtUtc >= today, cancellationToken);
        var activeCount = await _dbContext.Users.CountAsync(
            u => u.AccountType == AccountType.Member && u.AccountStatus == AccountStatus.Active,
            cancellationToken);

        return new AdminMemberListResult(
            new PageResult<AdminMemberRow>(items, query.PageNumber, query.PageSize, totalCount),
            new AdminMemberListStats(totalMembers, newTodayCount, activeCount));
    }

    public async Task<AdminMemberDetailSnapshot?> FindDetailAsync(
        Guid publicId, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.MemberProfiles.AsNoTracking()
            .Join(
                _dbContext.Users.AsNoTracking(),
                profile => profile.UserId,
                user => user.Id,
                (profile, user) => new { profile, user })
            .FirstOrDefaultAsync(
                x => x.user.PublicId == publicId && x.user.AccountType == AccountType.Member,
                cancellationToken);

        if (record is null)
        {
            return null;
        }

        var userId = record.user.Id;

        var phone = await _dbContext.MemberAddresses.AsNoTracking()
            .Where(a => a.MemberUserId == userId && a.IsDefault && a.DeletedAtUtc == null)
            .Select(a => (string?)a.Phone)
            .FirstOrDefaultAsync(cancellationToken);

        var orders = await _dbContext.Orders.AsNoTracking()
            .Where(o => o.MemberUserId == userId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);

        var totalSpend = orders
            .Where(o => o.OrderStatus == OrderStatus.Completed)
            .Sum(o => o.GrandTotal);
        var totalOrderCount = orders.Count(o => o.OrderStatus != OrderStatus.Cancelled);

        var orderIds = orders.Select(o => o.Id).ToArray();
        var completedReturnOrderCount = orderIds.Length == 0
            ? 0
            : await _dbContext.ReturnRequests.AsNoTracking()
                .Where(r => orderIds.Contains(r.OrderId) && r.Status == ReturnRequestStatus.Completed)
                .Select(r => r.OrderId)
                .Distinct()
                .CountAsync(cancellationToken);

        var returnRatePercent = totalOrderCount == 0
            ? 0m
            : Math.Round(completedReturnOrderCount * 100m / totalOrderCount, 1);

        var recentOrders = orders
            .Take(RecentOrderLimit)
            .Select(o => new AdminMemberOrderRow(
                o.PublicId, o.OrderNumber, o.CreatedAtUtc, o.OrderStatus.ToString(), o.GrandTotal))
            .ToArray();

        var orderPublicIdsById = orders.ToDictionary(o => o.Id, o => o.PublicId);

        var statusHistoryEvents = orderIds.Length == 0
            ? []
            : await _dbContext.OrderStatusHistories.AsNoTracking()
                .Where(h => orderIds.Contains(h.OrderId) && h.StateDimension == OrderStateDimension.OrderStatus)
                .OrderByDescending(h => h.OccurredAtUtc)
                .Take(ActivityLogLimit)
                .ToArrayAsync(cancellationToken);

        // ⚠ 活動日誌只組得出訂單相關事件（狀態變更、下單）——沒有登入/Session紀錄表，
        // 畫面上不做「登入」事件列，這是相對於畫面稿的已知落差，不是漏做。
        var activityLog = statusHistoryEvents
            .Select(h => new AdminMemberActivityEvent(
                h.OccurredAtUtc,
                "order_status_changed",
                $"訂單 {DescribeOrder(h.OrderId, orderPublicIdsById)} 狀態變更為 {h.ToStatus}"))
            .Concat(orders
                .Take(ActivityLogLimit)
                .Select(o => new AdminMemberActivityEvent(
                    o.CreatedAtUtc, "order_placed", $"下單 #{o.OrderNumber}")))
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(ActivityLogLimit)
            .ToArray();

        return new AdminMemberDetailSnapshot(
            record.user.PublicId,
            record.profile.DisplayName,
            record.user.Email ?? string.Empty,
            phone,
            record.profile.BirthDate,
            record.user.CreatedAtUtc,
            record.user.AccountStatus,
            record.profile.RowVersion,
            new AdminMemberStats(totalSpend, totalOrderCount, returnRatePercent),
            recentOrders,
            activityLog);
    }

    private static string DescribeOrder(long orderId, IReadOnlyDictionary<long, Guid> orderPublicIdsById) =>
        orderPublicIdsById.TryGetValue(orderId, out var publicId) ? publicId.ToString() : orderId.ToString();
}
