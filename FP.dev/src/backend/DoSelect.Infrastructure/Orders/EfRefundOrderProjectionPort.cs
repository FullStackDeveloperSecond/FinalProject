using DoSelect.Application.Refunds;
using DoSelect.Domain.Orders;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Orders;

/// <summary>
/// 在呼叫端既有 Unit of Work 上暫存 Order 退款投影與 append-only 歷程。
/// </summary>
public sealed class EfRefundOrderProjectionPort : IRefundOrderProjectionPort
{
    private readonly DoSelectDbContext _context;

    public EfRefundOrderProjectionPort(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task StageAsync(
        RefundOrderProjectionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ReasonCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.TraceId);

        if (command.OrderId <= 0 || command.RefundedAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        var order = await _context.Orders
            .SingleOrDefaultAsync(candidate => candidate.Id == command.OrderId, cancellationToken)
            ?? throw new InvalidOperationException(
                "A refund's own order must always resolve while staging its projection.");

        var status = command.HasPendingRefund
            ? OrderRefundStatus.Pending
            : command.RefundedAmount <= 0m
                ? OrderRefundStatus.None
                : command.RefundedAmount >= order.PaidAmount
                    ? OrderRefundStatus.Refunded
                    : OrderRefundStatus.PartiallyRefunded;

        var previousStatus = order.OrderRefundStatus;
        var previousAmount = order.RefundedAmount;
        if (previousStatus == status && previousAmount == command.RefundedAmount)
        {
            return;
        }

        order.ApplyRefundProjection(status, command.RefundedAmount, command.OccurredAtUtc);
        _context.OrderStatusHistories.Add(new OrderStatusHistory(
            Guid.CreateVersion7(),
            order.Id,
            OrderStateDimension.OrderRefundStatus,
            previousStatus.ToString(),
            status.ToString(),
            command.ReasonCode,
            command.ActorUserId,
            command.OccurredAtUtc,
            command.TraceId));
    }
}
