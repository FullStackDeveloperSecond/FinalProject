using DoSelect.Domain.Common;

namespace DoSelect.Domain.Refunds;

public enum RefundStatus
{
    PendingReview,
    Approved,
    Rejected,
    Processing,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>
/// 退款金額的組成類型。金額一律為正值，加減方向由類型決定（DEC-BATCH-014 第 8 項）。
/// </summary>
public enum RefundAllocationType
{
    /// <summary>增加退款：品項成交金額扣除折扣分攤後的淨額。</summary>
    ItemRefund,

    /// <summary>從退款扣回：退貨後不符優惠門檻，追回仍留在保留商品上的折扣。</summary>
    DiscountClawback,

    /// <summary>增加退款：整筆退貨時退還原本實際支付的運費。</summary>
    OriginalShipping,

    /// <summary>從退款扣回：原本免運但退貨後未達門檻，重新收取原配送方式運費。</summary>
    ShippingClawback,

    /// <summary>增加退款：退貨寄回運費由商家負擔時退還。</summary>
    ReturnShipping,

    /// <summary>增加退款：依政策退還的組裝費。</summary>
    AssemblyFee,

    /// <summary>第一版禁止寫入，避免出現方向不明的金額。</summary>
    OtherAdjustment,
}

public sealed class Refund : MutablePublicEntity
{
    private static readonly IReadOnlyDictionary<RefundStatus, RefundStatus[]> AllowedTransitions =
        new Dictionary<RefundStatus, RefundStatus[]>
        {
            [RefundStatus.PendingReview] = [RefundStatus.Approved, RefundStatus.Rejected, RefundStatus.Cancelled],
            [RefundStatus.Approved] = [RefundStatus.Processing],
            [RefundStatus.Processing] = [RefundStatus.Succeeded, RefundStatus.Failed],
            [RefundStatus.Failed] = [RefundStatus.Processing],
            [RefundStatus.Succeeded] = [],
            [RefundStatus.Rejected] = [],
            [RefundStatus.Cancelled] = [],
        };

    private Refund() { }

    public Refund(
        Guid publicId,
        long orderId,
        long? returnRequestId,
        long paymentAttemptId,
        string refundNumber,
        decimal requestedAmount,
        string reasonCode,
        string? requestedBy,
        string idempotencyKey,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (orderId <= 0 || returnRequestId is <= 0 || paymentAttemptId <= 0 || requestedAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(orderId));
        }

        OrderId = orderId;
        ReturnRequestId = returnRequestId;
        PaymentAttemptId = paymentAttemptId;
        RefundNumber = RequireText(refundNumber, nameof(refundNumber));
        Status = RefundStatus.PendingReview;
        RequestedAmount = requestedAmount;
        ReasonCode = RequireText(reasonCode, nameof(reasonCode));
        RequestedBy = NormalizeOptional(requestedBy);
        IdempotencyKey = RequireText(idempotencyKey, nameof(idempotencyKey));
    }

    public long OrderId { get; private set; }
    public long? ReturnRequestId { get; private set; }
    public long PaymentAttemptId { get; private set; }
    public string RefundNumber { get; private set; } = string.Empty;
    public RefundStatus Status { get; private set; }
    public decimal RequestedAmount { get; private set; }
    public decimal? ApprovedAmount { get; private set; }
    public decimal? SucceededAmount { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public string? RequestedBy { get; private set; }
    public string? ApprovedBy { get; private set; }
    public string? ExecutedByAdminUserId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public DateTime? SucceededAtUtc { get; private set; }

    public void Approve(decimal approvedAmount, string approvedBy, DateTime occurredAtUtc)
    {
        if (approvedAmount <= 0 || approvedAmount > RequestedAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(approvedAmount));
        }
        ApprovedAmount = approvedAmount;
        ApprovedBy = RequireText(approvedBy, nameof(approvedBy));
        Transition(RefundStatus.Approved, occurredAtUtc);
    }

    public void BeginProcessing(string executedByAdminUserId, DateTime occurredAtUtc)
    {
        ExecutedByAdminUserId = RequireText(executedByAdminUserId, nameof(executedByAdminUserId));
        Transition(RefundStatus.Processing, occurredAtUtc);
    }

    public void Complete(decimal succeededAmount, DateTime occurredAtUtc)
    {
        if (ApprovedAmount is null || succeededAmount <= 0 || succeededAmount > ApprovedAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(succeededAmount));
        }
        SucceededAmount = succeededAmount;
        SucceededAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        Transition(RefundStatus.Succeeded, occurredAtUtc);
    }

    public void Transition(RefundStatus next, DateTime occurredAtUtc)
    {
        if (!AllowedTransitions[Status].Contains(next))
        {
            throw new InvalidOperationException($"Refund cannot move from {Status} to {next}.");
        }
        Status = next;
        MarkUpdated(occurredAtUtc);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class RefundAllocation : PublicEntity
{
    private RefundAllocation() { }

    public RefundAllocation(Guid publicId, long refundId, long? orderItemId,
        RefundAllocationType allocationType, decimal amount,
        decimal originalDiscountAllocation, DateTime createdAtUtc, int? quantity = null)
        : base(publicId, createdAtUtc)
    {
        if (refundId <= 0 || orderItemId is <= 0 || amount <= 0 || originalDiscountAllocation < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(refundId));
        }

        if (allocationType == RefundAllocationType.OtherAdjustment)
        {
            throw new ArgumentOutOfRangeException(
                nameof(allocationType),
                "OtherAdjustment cannot be written in the first version.");
        }

        var isItemRefund = allocationType == RefundAllocationType.ItemRefund;
        if (isItemRefund && (orderItemId is null || quantity is null or <= 0) ||
            !isItemRefund && (orderItemId is not null || quantity is not null))
        {
            throw new ArgumentException(
                "Item refunds require an order item and positive quantity; non-item allocations require neither.",
                nameof(allocationType));
        }

        RefundId = refundId;
        OrderItemId = orderItemId;
        AllocationType = allocationType;
        Amount = amount;
        OriginalDiscountAllocation = originalDiscountAllocation;
        Quantity = quantity;
    }

    public long RefundId { get; private set; }
    public long? OrderItemId { get; private set; }
    public RefundAllocationType AllocationType { get; private set; }
    public decimal Amount { get; private set; }
    public decimal OriginalDiscountAllocation { get; private set; }
    public int? Quantity { get; private set; }
}
