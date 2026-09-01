using DoSelect.Application.Payments;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Payments;

public sealed class PaymentAttemptReader(DoSelectDbContext context) : IPaymentAttemptReader
{
    public async Task<OrderPaymentSnapshot?> FindOrderPaymentSnapshotAsync(
        Guid orderPublicId,
        CancellationToken cancellationToken = default)
    {
        var order = await context.Orders.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.PublicId == orderPublicId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var attempts = await context.PaymentAttempts.AsNoTracking()
            .Where(attempt => attempt.OrderId == order.Id)
            .OrderByDescending(attempt => attempt.CreatedAtUtc)
            .ThenByDescending(attempt => attempt.Id)
            .Select(attempt => new { attempt.Method, attempt.Status })
            .ToListAsync(cancellationToken);

        // A Confirmed unpaid order can only enter that shape through Checkout's accepted COD
        // path. Its persisted COD attempt is therefore the historical proof that shipping and
        // item restrictions passed at order creation; current mutable catalog/shipping settings
        // must not rewrite that decision during a retry.
        var hasCashOnDeliveryAttempt = attempts.Any(attempt =>
            attempt.Method == PaymentMethod.CashOnDelivery);
        var cashOnDelivery = new CashOnDeliveryEligibility(
            ShippingMethodAllowsCashOnDelivery: hasCashOnDeliveryAttempt,
            ContainsAssemblyBuild: false,
            ContainsPrepaymentOnlySku: false);

        return new OrderPaymentSnapshot(
            order.Id,
            order.RowVersion,
            new OrderPaymentContext(
                order.OrderStatus,
                order.GrandTotal,
                order.PaymentStatus == PaymentStatus.Paid || order.PaidAmount >= order.GrandTotal,
                attempts.FirstOrDefault()?.Status,
                AsUtc(order.PaymentDueAtUtc),
                cashOnDelivery));
    }

    private static DateTime? AsUtc(DateTime? value) => value is { } dateTime
        ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
        : null;
}
