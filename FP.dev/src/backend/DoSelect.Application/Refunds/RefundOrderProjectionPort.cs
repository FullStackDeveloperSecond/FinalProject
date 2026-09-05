namespace DoSelect.Application.Refunds;

/// <summary>
/// Refund（M-13）在自己的交易內，把權威退款彙總暫存回 Order（M-01）的窄介面。
/// </summary>
/// <remarks>
/// 實作不得呼叫 <c>SaveChanges</c> 或自行開交易。呼叫端先依 Refund 資料算出成功退款
/// 累計與是否仍有待處理退款，再由 Order 端依自己的 <c>PaidAmount</c> 推導正式
/// <c>OrderRefundStatus</c> 並寫入 <c>OrderStatusHistory</c>。如此 Refund 不直接操作
/// Order aggregate，Order 也不需要回頭讀 Refund 資料表。
/// </remarks>
public interface IRefundOrderProjectionPort
{
    Task StageAsync(
        RefundOrderProjectionCommand command,
        CancellationToken cancellationToken);
}

/// <summary>一筆 Refund 狀態變更後的訂單退款彙總快照。</summary>
public sealed record RefundOrderProjectionCommand(
    long OrderId,
    bool HasPendingRefund,
    decimal RefundedAmount,
    string ReasonCode,
    string? ActorUserId,
    DateTime OccurredAtUtc,
    string TraceId);
