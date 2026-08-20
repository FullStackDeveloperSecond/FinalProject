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
/// 模擬發票金額的純計算。訂單成交總額視為含稅金額，稅額由含稅總額回推後再分攤到各明細。
/// 台灣營業稅率 5%，表頭未稅與稅額取整數元，與統一發票的呈現一致。
/// </summary>
/// <remarks>
/// 分攤的是**稅額**而不是未稅額，明細未稅一律以 <c>Net = Gross - Tax</c> 回推。
/// 這樣每列都必然滿足 <c>0 &lt;= Tax &lt;= Gross</c> 與 <c>0 &lt;= Net &lt;= Gross</c>。
/// 先前分攤未稅額時只限制不超過剩餘未稅，含稅金額帶小數的明細會產生負稅額
/// （例如兩列含稅 0.40 與 0.60，表頭未稅取整為 1，末列未稅得到 1、稅額成為 -0.40）。
/// </remarks>
public static class InvoiceCalculator
{
    /// <summary>台灣營業稅率。</summary>
    public const decimal BusinessTaxRate = 0.05m;

    /// <summary>表頭未稅金額與稅額取整數元。</summary>
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
        var taxAmount = issuedAmount - netAmount;

        return InvoiceCalculationResult.Success(
            netAmount,
            taxAmount,
            issuedAmount,
            AllocateTaxAmount(taxAmount, issuedAmount, invoiceableLines));
    }

    /// <summary>
    /// 由含稅金額回推未稅金額並取整數元，再夾在含稅金額以內。
    /// 夾住上限是必要的：含稅金額小於約 0.5 時，取整會讓未稅超過含稅而使稅額變成負數。
    /// </summary>
    public static decimal BackOutNetAmount(decimal grossAmount)
    {
        if (grossAmount <= 0m)
        {
            return 0m;
        }

        var netAmount = Math.Round(
            grossAmount / (1m + BusinessTaxRate),
            AmountScale,
            MidpointRounding.AwayFromZero);

        return Math.Min(netAmount, grossAmount);
    }

    /// <summary>
    /// 依含稅金額比例把表頭稅額分攤到各明細，每列夾在 <c>0..GrossAmount</c> 之間，
    /// 尾差再依序分配給仍有空間的明細。明細稅額合計精確等於表頭稅額，
    /// 未稅以 <c>Gross - Tax</c> 回推，因此明細未稅合計也精確等於表頭未稅。
    /// </summary>
    private static IReadOnlyList<InvoiceLineBreakdown> AllocateTaxAmount(
        decimal taxAmount,
        decimal issuedAmount,
        IReadOnlyList<InvoiceOrderLine> lines)
    {
        var taxes = new decimal[lines.Count];
        var allocated = 0m;

        for (var index = 0; index < lines.Count; index++)
        {
            var share = Math.Round(
                taxAmount * lines[index].GrossAmount / issuedAmount,
                AmountScale,
                MidpointRounding.AwayFromZero);

            taxes[index] = Math.Clamp(share, 0m, lines[index].GrossAmount);
            allocated += taxes[index];
        }

        DistributeRemainder(taxes, lines, taxAmount - allocated);

        return lines
            .Select((line, index) => new InvoiceLineBreakdown(
                line.OrderItemPublicId,
                line.Kind,
                line.ProductNameSnapshot,
                line.SkuCodeSnapshot,
                line.Quantity,
                line.UnitPrice,
                line.DiscountAmount,
                line.GrossAmount - taxes[index],
                taxes[index],
                line.GrossAmount))
            .ToArray();
    }

    /// <summary>
    /// 把尾差依序補進仍有空間的明細。多的往上加到含稅金額為止，少的往下扣到零為止。
    /// 因為各列含稅金額合計等於表頭含稅金額，且表頭稅額介於零與含稅金額之間，尾差必定分配得完。
    /// </summary>
    private static void DistributeRemainder(
        decimal[] taxes,
        IReadOnlyList<InvoiceOrderLine> lines,
        decimal remainder)
    {
        for (var index = 0; index < taxes.Length && remainder != 0m; index++)
        {
            if (remainder > 0m)
            {
                var headroom = lines[index].GrossAmount - taxes[index];
                var added = Math.Min(remainder, headroom);
                taxes[index] += added;
                remainder -= added;
            }
            else
            {
                var removed = Math.Min(-remainder, taxes[index]);
                taxes[index] -= removed;
                remainder += removed;
            }
        }
    }
}
