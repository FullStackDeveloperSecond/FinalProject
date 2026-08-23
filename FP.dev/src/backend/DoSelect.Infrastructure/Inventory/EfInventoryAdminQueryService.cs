using System.Globalization;
using DoSelect.Application.Common;
using DoSelect.Application.Inventory;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Inventory;

public sealed class EfInventoryAdminQueryService : IInventoryAdminQueryService
{
    private const int MaxPageSize = 200;
    private static readonly DateTime NeverExpiresSortValue = DateTime.MaxValue;

    private readonly DoSelectDbContext _dbContext;

    public EfInventoryAdminQueryService(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PageResult<InventoryBalanceDto>> ListBalancesAsync(
        InventoryBalanceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var balances = from balance in _dbContext.InventoryBalances.AsNoTracking()
                       join sku in _dbContext.Skus.AsNoTracking() on balance.SkuId equals sku.Id
                       join product in _dbContext.Products.AsNoTracking() on sku.ProductId equals product.Id
                       select new { balance, sku, product };

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var keyword = query.Q.Trim();
            balances = balances.Where(row =>
                EF.Functions.Like(row.sku.SkuCode, $"%{keyword}%") ||
                EF.Functions.Like(row.sku.NameZhTw, $"%{keyword}%"));
        }

        if (!string.IsNullOrWhiteSpace(query.CategoryCode))
        {
            var categoryCode = query.CategoryCode.Trim();
            balances = balances.Where(row => _dbContext.Categories
                .Any(category => category.Id == row.product.CategoryId && category.Code == categoryCode));
        }

        balances = query.StockState?.Trim().ToLowerInvariant() switch
        {
            "out_of_stock" => balances.Where(row => row.balance.AvailableQuantity <= 0),
            "low_stock" => balances.Where(row =>
                row.balance.AvailableQuantity > 0 && row.balance.AvailableQuantity <= row.balance.ReorderLevel),
            "in_stock" => balances.Where(row => row.balance.AvailableQuantity > row.balance.ReorderLevel),
            _ => balances,
        };

        var totalCount = await balances.CountAsync(cancellationToken);
        var page = await balances
            .OrderBy(row => row.sku.SkuCode)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new InventoryBalanceDto(
                row.sku.PublicId,
                row.sku.SkuCode,
                row.sku.NameZhTw,
                row.balance.OnHandQuantity,
                row.balance.ReservedQuantity,
                row.balance.AvailableQuantity,
                row.balance.ReorderLevel,
                row.balance.RowVersion))
            .ToListAsync(cancellationToken);

        return new PageResult<InventoryBalanceDto>(page, pageNumber, pageSize, totalCount);
    }

    public async Task<PageResult<InventoryMovementDto>> ListMovementsAsync(
        InventoryMovementQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var movements = _dbContext.InventoryMovements.AsNoTracking().AsQueryable();
        if (query.SkuPublicId is Guid skuPublicId)
        {
            movements = movements.Where(movement => _dbContext.Skus
                .Any(sku => sku.Id == movement.SkuId && sku.PublicId == skuPublicId));
        }

        if (query.MovementTypes is { Count: > 0 })
        {
            movements = movements.Where(movement => query.MovementTypes.Contains(movement.MovementType));
        }

        if (query.From is DateTime from)
        {
            movements = movements.Where(movement => movement.OccurredAtUtc >= from);
        }

        if (query.To is DateTime to)
        {
            movements = movements.Where(movement => movement.OccurredAtUtc <= to);
        }

        var totalCount = await movements.CountAsync(cancellationToken);
        var page = await movements
            .OrderByDescending(movement => movement.OccurredAtUtc)
            .ThenByDescending(movement => movement.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Join(_dbContext.Skus.AsNoTracking(), movement => movement.SkuId, sku => sku.Id,
                (movement, sku) => new { movement, sku })
            .ToListAsync(cancellationToken);

        var actorIds = page
            .Where(row => row.movement.ActorUserId != null)
            .Select(row => row.movement.ActorUserId!)
            .Distinct()
            .ToArray();
        var actorsById = actorIds.Length == 0
            ? new Dictionary<string, (Guid PublicId, string? Email)>()
            : await _dbContext.Users.AsNoTracking()
                .Where(user => actorIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => (user.PublicId, user.Email), cancellationToken);

        var dtos = page.Select(row => new InventoryMovementDto(
            row.movement.PublicId,
            new InventorySkuSummaryDto(row.sku.PublicId, row.sku.SkuCode, row.sku.NameZhTw),
            row.movement.MovementType,
            row.movement.OnHandDelta,
            row.movement.ReservedDelta,
            row.movement.BeforeOnHand,
            row.movement.AfterOnHand,
            row.movement.BeforeReserved,
            row.movement.AfterReserved,
            row.movement.ReasonCode,
            row.movement.ActorUserId is not null && actorsById.TryGetValue(row.movement.ActorUserId, out var actor)
                ? new InventoryActorSummaryDto(actor.PublicId, actor.Email)
                : null,
            row.movement.ReferenceType,
            row.movement.ReferencePublicId,
            row.movement.OccurredAtUtc))
            .ToList();

        return new PageResult<InventoryMovementDto>(dtos, pageNumber, pageSize, totalCount);
    }

    public async Task<CursorPage<InventoryReservationDto>> ListReservationsAsync(
        InventoryReservationListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var reservations = _dbContext.InventoryReservations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse<InventoryReservationStatus>(query.Status, ignoreCase: true, out var status))
        {
            reservations = reservations.Where(reservation => reservation.Status == status);
        }

        if (TryDecodeCursor(query.Cursor, out var cursorSortValue, out var cursorPublicId))
        {
            reservations = reservations.Where(reservation =>
                (reservation.ExpiresAtUtc ?? NeverExpiresSortValue) < cursorSortValue ||
                ((reservation.ExpiresAtUtc ?? NeverExpiresSortValue) == cursorSortValue &&
                    reservation.PublicId.CompareTo(cursorPublicId) < 0));
        }

        var rows = await reservations
            .OrderByDescending(reservation => reservation.ExpiresAtUtc ?? NeverExpiresSortValue)
            .ThenByDescending(reservation => reservation.PublicId)
            .Take(pageSize + 1)
            .Join(_dbContext.Skus.AsNoTracking(), reservation => reservation.SkuId, sku => sku.Id,
                (reservation, sku) => new { reservation, sku })
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > pageSize;
        var pageRows = hasMore ? rows.Take(pageSize).ToList() : rows;

        var orderIds = pageRows.Select(row => row.reservation.OrderId).Distinct().ToArray();
        var ordersById = orderIds.Length == 0
            ? new Dictionary<long, (Guid PublicId, string OrderNumber)>()
            : await _dbContext.Orders.AsNoTracking()
                .Where(order => orderIds.Contains(order.Id))
                .ToDictionaryAsync(order => order.Id, order => (order.PublicId, order.OrderNumber), cancellationToken);

        var dtos = pageRows.Select(row =>
        {
            var order = ordersById[row.reservation.OrderId];
            return new InventoryReservationDto(
                row.reservation.PublicId,
                new InventoryOrderSummaryDto(order.PublicId, order.OrderNumber),
                new InventorySkuSummaryDto(row.sku.PublicId, row.sku.SkuCode, row.sku.NameZhTw),
                row.reservation.Quantity,
                row.reservation.Status.ToString(),
                row.reservation.ExpiresAtUtc,
                row.reservation.CreatedAtUtc,
                row.reservation.Status == InventoryReservationStatus.Active
                    ? ["release"]
                    : Array.Empty<string>(),
                row.reservation.RowVersion);
        }).ToList();

        var nextCursor = hasMore
            ? EncodeCursor(pageRows[^1].reservation.ExpiresAtUtc ?? NeverExpiresSortValue, pageRows[^1].reservation.PublicId)
            : null;

        return new CursorPage<InventoryReservationDto>(dtos, nextCursor, hasMore);
    }

    private static string EncodeCursor(DateTime sortValue, Guid publicId)
    {
        var raw = $"{sortValue:O}|{publicId:D}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));
    }

    private static bool TryDecodeCursor(string? cursor, out DateTime sortValue, out Guid publicId)
    {
        sortValue = default;
        publicId = default;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|', 2);
            if (parts.Length != 2)
            {
                return false;
            }

            return DateTime.TryParse(
                    parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out sortValue) &&
                Guid.TryParse(parts[1], out publicId);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
