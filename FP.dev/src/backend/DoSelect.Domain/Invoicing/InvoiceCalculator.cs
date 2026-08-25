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
    /// 買受人資料是否完整。公司發票必須同時有統編與公司抬頭，
    /// 與 <see cref="SimulatedInvoice"/> 建構子的檢查一致。
    /// 取號前先問這個，避免產生無法持久化的成功計畫並白白消耗流水號。
    /// </summary>
    public static bool HasCompleteBuyerDetails(
        SimulatedInvoiceBuyerType buyerType,
        string? companyTaxId,
        string? companyName) =>
        buyerType != SimulatedInvoiceBuyerType.Company ||
        (!string.IsNullOrWhiteSpace(companyTaxId) && !string.IsNullOrWhiteSpace(companyName));
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
/// 模擬發票金額的純計算，依 alex 依財政部電子發票 MIG 4.1 與商家介接實務重新裁定的規則。
/// </summary>
/// <remarks>
/// 表頭與明細採不同位數：
/// <code>
/// RawGrossAmount     = Sum(Line.GrossAmount)
/// IssuedAmount       = Round(RawGrossAmount, 0, AwayFromZero)
/// NetAmount          = Round(IssuedAmount / 1.05, 0, AwayFromZero)
/// TaxAmount          = IssuedAmount - NetAmount
/// RoundingAdjustment = IssuedAmount - RawGrossAmount
/// </code>
/// 表頭三個金額皆為整數元；明細的 Gross、Net、Tax 可以保留兩位小數。
/// 分攤的是**稅額**而不是未稅額，明細未稅一律以 <c>Net = Gross - Tax</c> 回推，
/// 因此每列必然滿足 <c>0 &lt;= Tax &lt;= Gross</c> 與 <c>0 &lt;= Net &lt;= Gross</c>。
/// </remarks>
public static class InvoiceCalculator
{
    /// <summary>台灣營業稅率。</summary>
    public const decimal BusinessTaxRate = 0.05m;

    /// <summary>表頭未稅金額、稅額與含稅總額取整數元。</summary>
    public const int AmountScale = 0;

    /// <summary>明細金額保留兩位小數。</summary>
    public const int LineAmountScale = 2;

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

        var rawGrossAmount = invoiceableLines.Sum(line => line.GrossAmount);
        var issuedAmount = RoundToWholeAmount(rawGrossAmount);

        // 明細帶小數本身不是拒絕理由，但四捨五入後必須等於訂單實付金額；
        // 不一致代表訂單快照有問題，且不得自行改動發票或實付金額。
        if (issuedAmount != request.OrderPaidAmount)
        {
            throw new ArgumentException(
                "The invoice lines do not reconcile with the amount the order was paid.",
                nameof(request));
        }

        var netAmount = BackOutNetAmount(issuedAmount);
        var taxAmount = issuedAmount - netAmount;

        return InvoiceCalculationResult.Success(
            netAmount,
            taxAmount,
            issuedAmount,
            issuedAmount - rawGrossAmount,
            AllocateTaxAmount(taxAmount, rawGrossAmount, invoiceableLines));
    }

    /// <summary>
    /// 由整數元含稅總額回推整數元未稅金額，再夾在含稅金額以內。
    /// 夾住上限是必要的：含稅金額小於約 0.5 時，取整會讓未稅超過含稅而使稅額變成負數。
    /// </summary>
    public static decimal BackOutNetAmount(decimal grossAmount)
    {
        if (grossAmount <= 0m)
        {
            return 0m;
        }

        return Math.Min(RoundToWholeAmount(grossAmount / (1m + BusinessTaxRate)), grossAmount);
    }

    internal static decimal RoundToWholeAmount(decimal value) =>
        Math.Round(value, AmountScale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// 依含稅金額比例把表頭稅額分攤到各明細，每列取兩位小數並夾在 <c>0..GrossAmount</c> 之間，
    /// 尾差由最後一筆合法明細吸收。明細稅額合計精確等於表頭稅額。
    /// </summary>
    private static IReadOnlyList<InvoiceLineBreakdown> AllocateTaxAmount(
        decimal taxAmount,
        decimal rawGrossAmount,
        IReadOnlyList<InvoiceOrderLine> lines)
    {
        var taxes = AllocateTaxByGrossShare(
            taxAmount,
            rawGrossAmount,
            lines.Select(line => line.GrossAmount).ToArray());

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
    /// 依含稅金額比例把表頭稅額分攤到各明細，每列取兩位小數並夾在 <c>0..GrossAmount</c> 之間，
    /// 尾差由最後一筆仍有空間的明細吸收。回傳的稅額合計精確等於 <paramref name="taxAmount"/>。
    /// </summary>
    /// <remarks>
    /// 分攤的是**稅額**而不是未稅金額。分攤未稅再以「含稅減未稅」求稅額時，
    /// 極小金額的明細會因為取整而讓未稅超過含稅，產生負稅額。
    /// 發票與退款折讓共用這一份，兩邊的取位口徑不可能漂移。
    /// </remarks>
    internal static decimal[] AllocateTaxByGrossShare(
        decimal taxAmount,
        decimal rawGrossAmount,
        decimal[] grossAmounts)
    {
        var taxes = new decimal[grossAmounts.Length];
        if (grossAmounts.Length == 0 || rawGrossAmount <= 0m)
        {
            return taxes;
        }

        var allocated = 0m;
        for (var index = 0; index < grossAmounts.Length; index++)
        {
            var share = Math.Round(
                taxAmount * grossAmounts[index] / rawGrossAmount,
                LineAmountScale,
                MidpointRounding.AwayFromZero);

            taxes[index] = Math.Clamp(share, 0m, grossAmounts[index]);
            allocated += taxes[index];
        }

        DistributeRemainder(taxes, grossAmounts, taxAmount - allocated);
        return taxes;
    }

    /// <summary>
    /// 把尾差**從最後一筆往前**補進仍有空間的明細，讓最後一筆合法明細吸收尾差。
    /// 多的往上加到含稅金額為止，少的往下扣到零為止。
    /// </summary>
    private static void DistributeRemainder(
        decimal[] taxes,
        decimal[] grossAmounts,
        decimal remainder)
    {
        for (var index = taxes.Length - 1; index >= 0 && remainder != 0m; index--)
        {
            if (remainder > 0m)
            {
                var headroom = grossAmounts[index] - taxes[index];
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
