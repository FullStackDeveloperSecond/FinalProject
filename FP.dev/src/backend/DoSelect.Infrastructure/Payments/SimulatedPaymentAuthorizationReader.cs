using DoSelect.Application.Payments;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Payments;

public sealed class SimulatedPaymentAuthorizationReader(DoSelectDbContext context)
    : ISimulatedPaymentAuthorizationReader
{
    public Task<SimulatedPaymentOrderReference?> FindOrderAsync(
        Guid paymentAttemptPublicId,
        CancellationToken cancellationToken = default) =>
        (
            from attempt in context.PaymentAttempts.AsNoTracking()
            join order in context.Orders.AsNoTracking() on attempt.OrderId equals order.Id
            where attempt.PublicId == paymentAttemptPublicId
            select new SimulatedPaymentOrderReference(order.PublicId, order.MemberUserId)
        ).SingleOrDefaultAsync(cancellationToken);
}
