namespace DoSelect.Domain.Refunds;

/// <summary>
/// 退款相關的錯誤碼，值必須與 API錯誤碼目錄 一致。
/// </summary>
public static class RefundErrorCodes
{
    public const string RefundAmountExceeded = "refund_amount_exceeded";
    public const string RefundStateConflict = "refund_state_conflict";
    public const string ReturnQuantityExceeded = "return_quantity_exceeded";
    public const string IdempotencyPayloadConflict = "idempotency_payload_conflict";
    public const string ResourceNotFound = "resource_not_found";
}

/// <summary>
/// 退貨原因。決定退貨寄回運費由誰負擔。
/// </summary>
public enum ReturnReason
{
    /// <summary>七日內合法解除。</summary>
    CoolingOff,

    /// <summary>商品瑕疵。</summary>
    Defective,

    /// <summary>寄錯商品。</summary>
    WrongItem,

    /// <summary>運送損壞。</summary>
    ShippingDamage,

    /// <summary>保固期內確認瑕疵。</summary>
    Warranty,

    /// <summary>超過期限的非瑕疵通融退貨。</summary>
    LateNonDefectiveGoodwill,

    /// <summary>顧客未依流程自行寄送造成額外費用。</summary>
    CustomerProcessDeviation,
}

public enum ReturnShippingBearer
{
    Merchant,
    Customer,
    ManualReview,
}

/// <summary>
/// 組裝費在這次退貨的處置依據。
/// </summary>
public enum AssemblyFeeDisposition
{
    /// <summary>訂單沒有組裝電腦。</summary>
    NotApplicable,

    /// <summary>尚未開始組裝。</summary>
    NotStarted,

    /// <summary>商家取消或無法組裝。</summary>
    MerchantCancelled,

    /// <summary>組裝錯誤或服務瑕疵。</summary>
    AssemblyFault,

    /// <summary>整台因商家責任退回。</summary>
    MerchantFaultWholeUnit,

    /// <summary>組裝正常完成後只退其中一個零件。</summary>
    CompletedPartialReturn,
}

/// <summary>
/// 退款組成的加減方向。金額一律為正值，方向由 <see cref="RefundAllocationType"/> 決定。
/// </summary>
public enum RefundAllocationDirection
{
    /// <summary>增加退款。</summary>
    Credit,

    /// <summary>從退款扣回。</summary>
    Debit,
}

/// <summary>
/// 退款金額的一項組成。<see cref="Amount"/> 一律為正值，與
/// <c>RefundAllocation.Amount &gt; 0</c> 的資料庫限制一致。
/// </summary>
public sealed record RefundComponentAmount(RefundAllocationType Type, decimal Amount)
{
    public RefundAllocationDirection Direction => RefundPolicy.DirectionOf(Type);
}

/// <summary>
/// 訂單品項在成立當時的交易快照。金額不得依目前商品或優惠券設定回推。
/// </summary>
public sealed record RefundOrderLine(
    Guid OrderItemPublicId,
    int Quantity,
    int AlreadyReturnedQuantity,
    decimal FinalUnitPrice,
    decimal DiscountAllocation,
    bool IsCouponEligible);

public sealed record RefundLineRequest(Guid OrderItemPublicId, int Quantity);

/// <summary>
/// 一張訂單計算退款所需的全部快照。
/// </summary>
public sealed record RefundOrderSnapshot(
    IReadOnlyList<RefundOrderLine> Lines,
    decimal ShippingFeePaid,
    decimal ShippingMethodBaseFee,
    decimal? FreeShippingThreshold,
    decimal AssemblyFee,
    decimal CouponDiscountTotal,
    decimal CouponEligibleSubtotal,
    decimal? CouponMinimumSpend);

public sealed record RefundCalculationRequest(
    RefundOrderSnapshot Order,
    IReadOnlyList<RefundLineRequest> Lines,
    ReturnReason Reason,
    AssemblyFeeDisposition AssemblyDisposition,
    decimal ReturnShippingCost);

public sealed record RefundItemBreakdown(
    Guid OrderItemPublicId,
    int Quantity,
    decimal GrossAmount,
    decimal DiscountShare,
    decimal NetAmount);

/// <summary>
/// 退款試算結果。失敗時只帶錯誤碼，不丟例外。
/// </summary>
public sealed class RefundCalculationResult
{
    private RefundCalculationResult(
        string? errorCode,
        decimal netRefundAmount,
        bool requiresManualReview,
        IReadOnlyList<RefundItemBreakdown> items,
        IReadOnlyList<RefundComponentAmount> components)
    {
        ErrorCode = errorCode;
        NetRefundAmount = netRefundAmount;
        RequiresManualReview = requiresManualReview;
        Items = items;
        Components = components;
    }

    public bool IsSuccess => ErrorCode is null;

    public string? ErrorCode { get; }

    /// <summary>
    /// 最終退款金額，等於增加退款類型合計減去扣回類型合計。
    /// </summary>
    public decimal NetRefundAmount { get; }

    /// <summary>退貨運費負擔者需要人工審核時為 <c>true</c>；本計算不預先給顧客或商家有利的結果。</summary>
    public bool RequiresManualReview { get; }

    /// <summary>逐品項的成交金額、折扣分攤與淨退款。</summary>
    public IReadOnlyList<RefundItemBreakdown> Items { get; }

    /// <summary>
    /// 退款明細組成，全為正值。增加退款合計減去扣回合計等於 <see cref="NetRefundAmount"/>。
    /// </summary>
    public IReadOnlyList<RefundComponentAmount> Components { get; }

    public static RefundCalculationResult Failure(string errorCode) =>
        new(errorCode, 0m, false, [], []);

    public static RefundCalculationResult Success(
        decimal netRefundAmount,
        bool requiresManualReview,
        IReadOnlyList<RefundItemBreakdown> items,
        IReadOnlyList<RefundComponentAmount> components) =>
        new(null, netRefundAmount, requiresManualReview, items, components);
}
