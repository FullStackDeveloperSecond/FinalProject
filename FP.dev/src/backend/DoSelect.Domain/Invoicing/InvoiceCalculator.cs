namespace DoSelect.Domain.Invoicing;

public static class InvoicePolicy
{
    /// <summary>
    /// 線上付款成功後開立；貨到付款在完成收款時開立；未付款或取消訂單不開立。
    /// 每張訂單最多一張發票。
    /// </summary>
    public static InvoiceIssuanceRejection FindIssuanceRejection(
        InvoiceIssuanceTrigger trigger,
        bool orderAlreadyHasInvoice,
        int invoiceableLineCount)
    {
        if (orderAlreadyHasInvoice)
        {
            return InvoiceIssuanceRejection.AlreadyIssued;
        }

        var triggerRejection = trigger switch
        {
            InvoiceIssuanceTrigger.OnlinePaymentSucceeded or
                InvoiceIssuanceTrigger.CashOnDeliveryCollected => InvoiceIssuanceRejection.None,
            InvoiceIssuanceTrigger.NotPaid => InvoiceIssuanceRejection.OrderNotPaid,
            InvoiceIssuanceTrigger.OrderCancelled => InvoiceIssuanceRejection.OrderCancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
        };

        if (triggerRejection != InvoiceIssuanceRejection.None)
        {
            return triggerRejection;
        }

        return invoiceableLineCount > 0
            ? InvoiceIssuanceRejection.None
            : InvoiceIssuanceRejection.NoInvoiceableLines;
    }

    /// <summary>
    /// 付款成功後又整筆取消且未進入退款者可作廢。已發生金流退款時必須建立折讓，
    /// 不回寫也不刪除原發票。
    /// </summary>
    public static InvoiceVoidRejection FindVoidRejection(
        SimulatedInvoiceStatus status,
        bool orderFullyCancelled,
        bool hasSettledRefund)
    {
        if (status != SimulatedInvoiceStatus.Issued)
        {
            return InvoiceVoidRejection.NotIssued;
        }

        if (hasSettledRefund)
        {
            return InvoiceVoidRejection.RefundAlreadySettled;
        }

        return orderFullyCancelled
            ? InvoiceVoidRejection.None
            : InvoiceVoidRejection.OrderNotFullyCancelled;
    }
}

/// <summary>
/// 模擬發票金額的純計算。含稅金額來自訂單交易快照，未稅與稅額由含稅金額回推。
/// 台灣營業稅率 5%，未稅與稅額取整數元，與統一發票的呈現一致。
/// 逐列回推可確保各明細合計精確等於發票總額，也精確等於顧客實付金額。
/// </summary>
public static class InvoiceCalculator
{
    /// <summary>台灣營業稅率。</summary>
    public const decimal BusinessTaxRate = 0.05m;

    /// <summary>未稅金額與稅額取整數元。</summary>
    public const int AmountScale = 0;

    public static InvoiceCalculationResult Calculate(InvoiceIssuanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Lines);

        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0 || line.UnitPrice < 0 ||
                line.DiscountAmount < 0 || line.GrossAmount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "An invoice line is malformed.");
            }

            if (line.Kind == InvoiceLineKind.Merchandise && line.OrderItemPublicId is null)
            {
                throw new ArgumentException(
                    "A merchandise line requires its order item.",
                    nameof(request));
            }
        }

        var invoiceableLines = request.Lines.Where(line => line.GrossAmount > 0m).ToArray();
        var rejection = InvoicePolicy.FindIssuanceRejection(
            request.Trigger,
            request.OrderAlreadyHasInvoice,
            invoiceableLines.Length);

        if (rejection != InvoiceIssuanceRejection.None)
        {
            return InvoiceCalculationResult.Failure(rejection);
        }

        var lines = invoiceableLines.Select(BuildLineBreakdown).ToArray();

        return InvoiceCalculationResult.Success(
            lines.Sum(line => line.NetAmount),
            lines.Sum(line => line.TaxAmount),
            lines.Sum(line => line.GrossAmount),
            lines);
    }

    /// <summary>
    /// 由含稅金額回推未稅與稅額。稅額取含稅減未稅，因此兩者相加必定等於含稅金額，
    /// 不會因為分別四捨五入而產生一元誤差。
    /// </summary>
    private static InvoiceLineBreakdown BuildLineBreakdown(InvoiceOrderLine line)
    {
        var netAmount = Round(line.GrossAmount / (1m + BusinessTaxRate));

        return new InvoiceLineBreakdown(
            line.OrderItemPublicId,
            line.Kind,
            line.ProductNameSnapshot,
            line.SkuCodeSnapshot,
            line.Quantity,
            line.UnitPrice,
            line.DiscountAmount,
            netAmount,
            line.GrossAmount - netAmount,
            line.GrossAmount);
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, AmountScale, MidpointRounding.AwayFromZero);
}
