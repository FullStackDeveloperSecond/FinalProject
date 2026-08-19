namespace DoSelect.Domain.Refunds;

public static class RefundPolicy
{
    /// <summary>
    /// 退貨寄回運費的負擔者。依 02-領域需求/退貨與退款政策 的負擔表判定。
    /// </summary>
    public static ReturnShippingBearer ResolveReturnShippingBearer(ReturnReason reason) => reason switch
    {
        ReturnReason.CoolingOff or ReturnReason.Defective or ReturnReason.WrongItem or
            ReturnReason.ShippingDamage or ReturnReason.Warranty => ReturnShippingBearer.Merchant,
        ReturnReason.LateNonDefectiveGoodwill => ReturnShippingBearer.ManualReview,
        ReturnReason.CustomerProcessDeviation => ReturnShippingBearer.Customer,
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    /// <summary>
    /// 組裝費是否退還。組裝正常完成後只退其中一個零件時不退。
    /// </summary>
    public static bool RefundsAssemblyFee(AssemblyFeeDisposition disposition) => disposition switch
    {
        AssemblyFeeDisposition.NotStarted or AssemblyFeeDisposition.MerchantCancelled or
            AssemblyFeeDisposition.AssemblyFault or
            AssemblyFeeDisposition.MerchantFaultWholeUnit => true,
        AssemblyFeeDisposition.CompletedPartialReturn or
            AssemblyFeeDisposition.NotApplicable => false,
        _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
    };
}

/// <summary>
/// 退款金額與明細的純計算。全部依訂單成立時的交易快照，不依目前商品或優惠券設定重算。
/// 計算順序：品項成交金額 → 扣除該品項的折扣分攤 → 原始運費 → 失效折扣追回 → 組裝費 → 退貨運費。
/// </summary>
public static class RefundCalculator
{
    public const int AmountScale = 2;

    public static RefundCalculationResult Calculate(RefundCalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Order);
        ArgumentNullException.ThrowIfNull(request.Order.Lines);
        ArgumentNullException.ThrowIfNull(request.Lines);

        if (request.ReturnShippingCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (request.Lines.Count == 0)
        {
            return RefundCalculationResult.Failure(RefundErrorCodes.ReturnQuantityExceeded);
        }

        var order = request.Order;
        var items = new List<RefundItemBreakdown>(request.Lines.Count);
        var returnedByLine = new Dictionary<Guid, int>();

        foreach (var requested in request.Lines)
        {
            var line = order.Lines.SingleOrDefault(
                candidate => candidate.OrderItemPublicId == requested.OrderItemPublicId);

            if (line is null)
            {
                return RefundCalculationResult.Failure(RefundErrorCodes.ResourceNotFound);
            }

            if (returnedByLine.ContainsKey(requested.OrderItemPublicId))
            {
                return RefundCalculationResult.Failure(RefundErrorCodes.ReturnQuantityExceeded);
            }

            var remaining = line.Quantity - line.AlreadyReturnedQuantity;
            if (requested.Quantity <= 0 || requested.Quantity > remaining)
            {
                return RefundCalculationResult.Failure(RefundErrorCodes.ReturnQuantityExceeded);
            }

            returnedByLine[requested.OrderItemPublicId] = requested.Quantity;
            items.Add(BuildItemBreakdown(line, requested.Quantity));
        }

        var itemRefundTotal = items.Sum(item => item.NetAmount);
        var isFullReturn = order.Lines.All(line =>
            line.AlreadyReturnedQuantity +
            returnedByLine.GetValueOrDefault(line.OrderItemPublicId) == line.Quantity);

        var retainedEligibleSubtotal = CalculateRetainedEligibleSubtotal(order, returnedByLine);
        var originalShipping = ResolveOriginalShipping(order, isFullReturn, retainedEligibleSubtotal);
        var discountClawback = ResolveDiscountClawback(order, returnedByLine, retainedEligibleSubtotal);

        var assemblyFee = RefundPolicy.RefundsAssemblyFee(request.AssemblyDisposition)
            ? Round(order.AssemblyFee)
            : 0m;

        var bearer = RefundPolicy.ResolveReturnShippingBearer(request.Reason);
        var returnShipping = bearer == ReturnShippingBearer.Merchant
            ? Round(request.ReturnShippingCost)
            : 0m;

        var components = new List<RefundComponentAmount>
        {
            new(RefundComponent.ItemRefund, itemRefundTotal),
        };

        AddWhenNonZero(components, RefundComponent.OriginalShipping, originalShipping);
        AddWhenNonZero(components, RefundComponent.DiscountClawback, -discountClawback);
        AddWhenNonZero(components, RefundComponent.AssemblyFee, assemblyFee);
        AddWhenNonZero(components, RefundComponent.ReturnShipping, returnShipping);

        var netRefundAmount = components.Sum(component => component.Amount);
        if (netRefundAmount <= 0m)
        {
            return RefundCalculationResult.Failure(RefundErrorCodes.RefundAmountExceeded);
        }

        return RefundCalculationResult.Success(
            netRefundAmount,
            bearer == ReturnShippingBearer.ManualReview,
            items,
            components);
    }

    /// <summary>
    /// 品項的成交金額扣掉該次退貨數量分攤到的訂單級折扣。
    /// 退到最後一批數量時，折扣分攤取剩餘未分攤的部分，確保多次部分退款的分攤合計等於原始分攤。
    /// </summary>
    private static RefundItemBreakdown BuildItemBreakdown(RefundOrderLine line, int quantity)
    {
        var grossAmount = Round(line.FinalUnitPrice * quantity);
        var alreadyAllocated = Round(
            line.DiscountAllocation * line.AlreadyReturnedQuantity / line.Quantity);

        var discountShare = line.AlreadyReturnedQuantity + quantity == line.Quantity
            ? line.DiscountAllocation - alreadyAllocated
            : Round(line.DiscountAllocation * (line.AlreadyReturnedQuantity + quantity) / line.Quantity)
                - alreadyAllocated;

        return new RefundItemBreakdown(
            line.OrderItemPublicId,
            quantity,
            grossAmount,
            discountShare,
            grossAmount - discountShare);
    }

    /// <summary>這次退貨後仍保留在訂單上、且符合優惠券範圍的商品成交金額。</summary>
    private static decimal CalculateRetainedEligibleSubtotal(
        RefundOrderSnapshot order,
        IReadOnlyDictionary<Guid, int> returnedByLine) =>
        Round(order.Lines
            .Where(line => line.IsCouponEligible)
            .Sum(line => line.FinalUnitPrice * (
                line.Quantity -
                line.AlreadyReturnedQuantity -
                returnedByLine.GetValueOrDefault(line.OrderItemPublicId))));

    /// <summary>
    /// 整筆合法退貨退還原本實際支付的運費，且不補收原免運。部分退貨不退原始運費；
    /// 但原本免運而退貨後剩餘金額未達門檻時，從退款中重新收取原配送方式運費。
    /// </summary>
    private static decimal ResolveOriginalShipping(
        RefundOrderSnapshot order,
        bool isFullReturn,
        decimal retainedEligibleSubtotal)
    {
        if (isFullReturn)
        {
            return Round(order.ShippingFeePaid);
        }

        var wasFreeShipping = order.ShippingFeePaid <= 0m;
        return wasFreeShipping &&
            order.FreeShippingThreshold is { } threshold &&
            retainedEligibleSubtotal < threshold
            ? -Round(order.ShippingMethodBaseFee)
            : 0m;
    }

    /// <summary>
    /// 退貨後仍符合優惠門檻則保留優惠；不符合時取消優惠，追回仍留在保留商品上的折扣。
    /// 已隨退貨品項扣除的折扣分攤不重複追回。
    /// </summary>
    private static decimal ResolveDiscountClawback(
        RefundOrderSnapshot order,
        IReadOnlyDictionary<Guid, int> returnedByLine,
        decimal retainedEligibleSubtotal)
    {
        if (order.CouponDiscountTotal <= 0m ||
            order.CouponMinimumSpend is not { } minimumSpend ||
            retainedEligibleSubtotal >= minimumSpend)
        {
            return 0m;
        }

        var returnedDiscount = Round(order.Lines.Sum(line =>
            line.DiscountAllocation *
            (line.AlreadyReturnedQuantity + returnedByLine.GetValueOrDefault(line.OrderItemPublicId)) /
            line.Quantity));

        var clawback = Round(order.CouponDiscountTotal) - returnedDiscount;
        return clawback > 0m ? clawback : 0m;
    }

    private static void AddWhenNonZero(
        List<RefundComponentAmount> components,
        RefundComponent component,
        decimal amount)
    {
        if (amount != 0m)
        {
            components.Add(new RefundComponentAmount(component, amount));
        }
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, AmountScale, MidpointRounding.AwayFromZero);
}
