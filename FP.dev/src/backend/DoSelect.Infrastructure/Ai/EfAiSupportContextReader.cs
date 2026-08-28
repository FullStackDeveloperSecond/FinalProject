using System.Data.Common;
using System.Text.Json;
using DoSelect.Application.Ai;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Ai;

public sealed class EfAiSupportContextReader(DoSelectDbContext dbContext)
    : IAiSupportContextReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AiSupportContextReadResult> ReadAsync(
        Guid memberId,
        IReadOnlyList<Guid> referencedOrderPublicIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(referencedOrderPublicIds);
        if (memberId == Guid.Empty)
        {
            throw new ArgumentException("A member identifier is required.", nameof(memberId));
        }

        var requestedIds = referencedOrderPublicIds.Distinct().ToArray();
        if (requestedIds.Length == 0)
        {
            return new AiSupportContextReadResult(
                AiSupportContextStatus.Allowed,
                DataItems: []);
        }

        try
        {
            var memberUserId = memberId.ToString("D");
            var orders = await dbContext.Orders
                .AsNoTracking()
                .Where(order =>
                    requestedIds.Contains(order.PublicId) &&
                    order.MemberUserId == memberUserId)
                .Select(order => new OrderRow(
                    order.Id,
                    order.PublicId,
                    order.OrderNumber,
                    order.OrderStatus,
                    order.PaymentStatus,
                    order.FulfillmentStatus))
                .ToListAsync(cancellationToken);
            if (orders.Count != requestedIds.Length)
            {
                return new AiSupportContextReadResult(
                    AiSupportContextStatus.ResourceNotFound,
                    DataItems: []);
            }

            var orderIds = orders.Select(order => order.Id).ToArray();
            var items = await dbContext.OrderItems
                .AsNoTracking()
                .Where(item => orderIds.Contains(item.OrderId))
                .OrderBy(item => item.Id)
                .Select(item => new OrderItemRow(
                    item.OrderId,
                    item.ProductNameSnapshot,
                    item.SkuNameSnapshot,
                    item.Quantity))
                .ToListAsync(cancellationToken);

            var byPublicId = orders.ToDictionary(order => order.PublicId);
            var dataItems = new List<string>(requestedIds.Length);
            foreach (var publicId in requestedIds)
            {
                var order = byPublicId[publicId];
                var payload = new
                {
                    orderPublicId = order.PublicId,
                    orderNumber = order.OrderNumber,
                    orderStatus = order.OrderStatus.ToString(),
                    paymentStatus = order.PaymentStatus.ToString(),
                    shippingStatus = order.FulfillmentStatus.ToString(),
                    items = items
                        .Where(item => item.OrderId == order.Id)
                        .Select(item => new
                        {
                            productName = item.ProductName,
                            skuName = item.SkuName,
                            item.Quantity,
                        })
                        .ToArray(),
                };
                var json = JsonSerializer.Serialize(payload, JsonOptions);
                if (!AiOutboundContentGuard.Inspect(json).IsAllowed)
                {
                    return new AiSupportContextReadResult(
                        AiSupportContextStatus.Unavailable,
                        DataItems: []);
                }

                dataItems.Add(json);
            }

            return new AiSupportContextReadResult(
                AiSupportContextStatus.Allowed,
                dataItems);
        }
        catch (DbException)
        {
            return new AiSupportContextReadResult(
                AiSupportContextStatus.Unavailable,
                DataItems: []);
        }
        catch (InvalidOperationException exception) when (exception.InnerException is DbException)
        {
            return new AiSupportContextReadResult(
                AiSupportContextStatus.Unavailable,
                DataItems: []);
        }
    }

    private sealed record OrderRow(
        long Id,
        Guid PublicId,
        string OrderNumber,
        Domain.Orders.OrderStatus OrderStatus,
        Domain.Orders.PaymentStatus PaymentStatus,
        Domain.Orders.FulfillmentStatus FulfillmentStatus);

    private sealed record OrderItemRow(
        long OrderId,
        string ProductName,
        string SkuName,
        int Quantity);
}
