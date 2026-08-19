namespace DoSelect.Domain.Invoicing;

public static class InvoicePolicy
{
    /// <summary>
    /// 線上付款成功後開立；貨到付款在完成收款時開立；未付款或取消訂單不開立。
    /// 每張訂單最多一張發票。可以開立時回傳 <c>null</c>，否則回傳錯誤碼。
    /// </summary>
    public static string? FindIssuanceRejection(
        InvoiceIssuanceTrigger trigger,
        bool orderAlreadyHasInvoice)
    {
        if (orderAlreadyHasInvoice)
        {
            return InvoiceErrorCodes.InvoiceAlreadyExists;
        }

        return trigger switch
        {
            InvoiceIssuanceTrigger.OnlinePaymentSucceeded or
                InvoiceIssuanceTrigger.CashOnDeliveryCollected => null,
            InvoiceIssuanceTrigger.NotPaid => InvoiceErrorCodes.InvoiceOrderUnpaid,
            InvoiceIssuanceTrigger.OrderCancelled => InvoiceErrorCodes.InvoiceOrderCancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
        };
    }

    /// <summary>
    /// 付款成功後又整筆取消且未進入退款者可作廢。已發生金流退款時必須建立折讓，
    /// 不回寫也不刪除原發票。可以作廢時回傳 <c>null</c>，否則回傳錯誤碼。
    /// </summary>
    public static string? FindVoidRejection(
        SimulatedInvoiceStatus status,
        bool orderFullyCancelled,
        bool hasSettledRefund)
    {
        if (status != SimulatedInvoiceStatus.Issued)
        {
            return InvoiceErrorCodes.InvoiceStateConflict;
        }

        if (hasSettledRefund)
        {
            return InvoiceErrorCodes.InvoiceAllowanceRequired;
        }

        return orderFullyCancelled ? null : InvoiceErrorCodes.InvoiceStateConflict;
    }
}

/// <summary>
/// 模擬發票金額的純計算。訂單成交總額視為含稅金額，未稅由含稅總額回推後再分攤到各明細。
/// 台灣營業稅率 5%，未稅與稅額取整數元，與統一發票的呈現一致。
/// 先算表頭再分攤，最後一筆合法明細吸收尾差，因此明細加總與表頭完全一致。
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

        var rejection = InvoicePolicy.FindIssuanceRejection(
            request.Trigger,
            request.OrderAlreadyHasInvoice);

        if (rejection is not null)
        {
            return InvoiceCalculationResult.Failure(rejection);
        }

        var invoiceableLines = request.Lines.Where(line => line.GrossAmount > 0m).ToArray();
        if (invoiceableLines.Length == 0)
        {
            // 已付款的訂單不可能沒有任何金額；出現代表快照有誤。
            throw new ArgumentException(
                "A paid order must carry at least one chargeable line.",
                nameof(request));
        }

        var issuedAmount = invoiceableLines.Sum(line => line.GrossAmount);
        var netAmount = BackOutNetAmount(issuedAmount);

        return InvoiceCalculationResult.Success(
            netAmount,
            issuedAmount - netAmount,
            issuedAmount,
            AllocateNetAmount(netAmount, issuedAmount, invoiceableLines));
    }

    /// <summary>
    /// 由含稅金額回推未稅金額。稅額另取含稅減未稅，因此兩者相加必定等於含稅金額。
    /// </summary>
    public static decimal BackOutNetAmount(decimal grossAmount) => Math.Round(
        grossAmount / (1m + BusinessTaxRate),
        AmountScale,
        MidpointRounding.AwayFromZero);

    /// <summary>
    /// 依含稅金額比例把表頭未稅分攤到各明細。非末筆夾在剩餘未分攤金額以內，
    /// 最後一筆合法明細吸收尾差，因此明細未稅合計精確等於表頭未稅，且每筆皆不為負。
    /// </summary>
    private static IReadOnlyList<InvoiceLineBreakdown> AllocateNetAmount(
        decimal netAmount,
        decimal issuedAmount,
        IReadOnlyList<InvoiceOrderLine> lines)
    {
        var breakdowns = new InvoiceLineBreakdown[lines.Count];
        var allocated = 0m;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var remaining = netAmount - allocated;
            var lineNetAmount = index == lines.Count - 1
                ? remaining
                : Math.Min(Round(netAmount * line.GrossAmount / issuedAmount), remaining);

            allocated += lineNetAmount;
            breakdowns[index] = new InvoiceLineBreakdown(
                line.OrderItemPublicId,
                line.Kind,
                line.ProductNameSnapshot,
                line.SkuCodeSnapshot,
                line.Quantity,
                line.UnitPrice,
                line.DiscountAmount,
                lineNetAmount,
                line.GrossAmount - lineNetAmount,
                line.GrossAmount);
        }

        return breakdowns;
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, AmountScale, MidpointRounding.AwayFromZero);
}
