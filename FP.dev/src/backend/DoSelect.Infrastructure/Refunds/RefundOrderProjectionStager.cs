using DoSelect.Application.Refunds;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Refunds;

/// <summary>
/// 由 Refund 模組依所有退款交易計算權威彙總，再透過 Application port 交給 Order。
/// </summary>
public sealed class RefundOrderProjectionStager
{
    private readonly DoSelectDbContext _context;
    private readonly IRefundOrderProjectionPort _orderProjectionPort;

    public RefundOrderProjectionStager(
        DoSelectDbContext context,
        IRefundOrderProjectionPort orderProjectionPort)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(orderProjectionPort);
        _context = context;
        _orderProjectionPort = orderProjectionPort;
    }

    public async Task StageAsync(
        Refund currentRefund,
        string reasonCode,
        string? actorUserId,
        DateTime occurredAtUtc,
        string traceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentRefund);

        // 目前這筆 Refund 已在 ChangeTracker 裡變更，資料庫查詢看見的仍是舊狀態；
        // 明確排除它，再把記憶體中的新狀態合回去，避免成功、取消或新建時讀到舊值。
        var settledAmount = await _context.Refunds
            .Where(candidate =>
                candidate.OrderId == currentRefund.OrderId &&
                candidate.Id != currentRefund.Id &&
                candidate.Status == RefundStatus.Succeeded &&
                candidate.SucceededAmount != null)
            .SumAsync(candidate => candidate.SucceededAmount!.Value, cancellationToken);

        var hasPendingRefund = await _context.Refunds
            .AnyAsync(candidate =>
                candidate.OrderId == currentRefund.OrderId &&
                candidate.Id != currentRefund.Id &&
                (candidate.Status == RefundStatus.PendingReview ||
                 candidate.Status == RefundStatus.Approved ||
                 candidate.Status == RefundStatus.Processing),
                cancellationToken);

        if (currentRefund.Status == RefundStatus.Succeeded)
        {
            settledAmount += currentRefund.SucceededAmount
                ?? throw new InvalidOperationException(
                    "A succeeded refund must carry its settled amount.");
        }

        hasPendingRefund |= currentRefund.Status is
            RefundStatus.PendingReview or RefundStatus.Approved or RefundStatus.Processing;

        await _orderProjectionPort.StageAsync(
            new RefundOrderProjectionCommand(
                currentRefund.OrderId,
                hasPendingRefund,
                settledAmount,
                reasonCode,
                actorUserId,
                occurredAtUtc,
                traceId),
            cancellationToken);
    }
}
