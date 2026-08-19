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

public enum RefundAllocationType
{
    ItemRefund,
    DiscountClawback,
    OriginalShipping,
    ReturnShipping,
    AssemblyFee,
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
        decimal originalDiscountAllocation, DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (refundId <= 0 || orderItemId is <= 0 || amount <= 0 || originalDiscountAllocation < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(refundId));
        }
        RefundId = refundId;
        OrderItemId = orderItemId;
        AllocationType = allocationType;
        Amount = amount;
        OriginalDiscountAllocation = originalDiscountAllocation;
    }

    public long RefundId { get; private set; }
    public long? OrderItemId { get; private set; }
    public RefundAllocationType AllocationType { get; private set; }
    public decimal Amount { get; private set; }
    public decimal OriginalDiscountAllocation { get; private set; }
}
