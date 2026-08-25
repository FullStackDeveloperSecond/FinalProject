namespace DoSelect.Domain.Invoicing;

/// <summary>
/// 退款折讓金額的純計算。折讓含稅金額由成功 Refund 的分攤推導，本層只檢查上限並拆出稅額。
/// 未稅與稅額沿用發票的口徑：先由折讓含稅總額回推表頭，再分攤到各明細、末筆吸收尾差。
/// 累計折讓不得超過原發票各明細的可折讓數量與金額；折讓不回寫也不刪除原發票。
/// </summary>
public static class InvoiceAllowanceCalculator
{
    public static InvoiceAllowanceResult Calculate(InvoiceAllowanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Capacities);
        ArgumentNullException.ThrowIfNull(request.RefundedLines);

        if (request.RefundAlreadyHasAllowance)
        {
            return InvoiceAllowanceResult.Failure(InvoiceErrorCodes.InvoiceStateConflict);
        }

        if (request.InvoiceStatus is not (SimulatedInvoiceStatus.Issued or
            SimulatedInvoiceStatus.PartiallyAllowed))
        {
            return InvoiceAllowanceResult.Failure(InvoiceErrorCodes.InvoiceStateConflict);
        }

        var allowableLines = request.RefundedLines.Where(line => line.GrossAmount > 0m).ToArray();
        if (allowableLines.Length == 0)
        {
            return InvoiceAllowanceResult.Failure(InvoiceErrorCodes.InvoiceStateConflict);
        }

        var requestedByItem = new Dictionary<Guid, (int Quantity, decimal GrossAmount)>();

        foreach (var refunded in allowableLines)
        {
            if (refunded.Quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "An allowance line is malformed.");
            }

            var capacity = request.Capacities.SingleOrDefault(candidate =>
                candidate.SimulatedInvoiceItemPublicId == refunded.SimulatedInvoiceItemPublicId);

            if (capacity is null)
            {
                return InvoiceAllowanceResult.Failure(InvoiceErrorCodes.ResourceNotFound);
            }

            if (requestedByItem.ContainsKey(refunded.SimulatedInvoiceItemPublicId) ||
                refunded.Quantity > capacity.RemainingQuantity ||
                refunded.GrossAmount > capacity.RemainingGrossAmount)
            {
                return InvoiceAllowanceResult.Failure(InvoiceErrorCodes.InvoiceStateConflict);
            }

            requestedByItem[refunded.SimulatedInvoiceItemPublicId] =
                (refunded.Quantity, refunded.GrossAmount);
        }

        // 表頭取整數元，與發票同口徑；明細維持自身含稅金額並可保留兩位小數。
        // 兩者的差額由 RoundingAdjustment 記錄，不塞進任何一列。
        var rawGrossAmount = allowableLines.Sum(line => line.GrossAmount);
        var amount = InvoiceCalculator.RoundToWholeAmount(rawGrossAmount);
        if (amount <= 0m)
        {
            return InvoiceAllowanceResult.Failure(InvoiceErrorCodes.InvoiceStateConflict);
        }

        var netAmount = InvoiceCalculator.BackOutNetAmount(amount);

        var fullyAllowed = request.Capacities.All(capacity =>
        {
            var requested = requestedByItem.GetValueOrDefault(capacity.SimulatedInvoiceItemPublicId);
            return capacity.AlreadyAllowedQuantity + requested.Quantity == capacity.Quantity &&
                capacity.AlreadyAllowedGrossAmount + requested.GrossAmount == capacity.GrossAmount;
        });

        return InvoiceAllowanceResult.Success(
            netAmount,
            amount - netAmount,
            amount,
            amount - rawGrossAmount,
            fullyAllowed,
            AllocateTaxAmount(amount - netAmount, rawGrossAmount, allowableLines));
    }

    /// <summary>
    /// 把折讓表頭稅額分攤到各明細。直接沿用 <see cref="InvoiceCalculator"/> 的正式口徑，
    /// 不另外實作一份，兩邊的取位規則因此不可能漂移。
    /// </summary>
    private static IReadOnlyList<InvoiceAllowanceLineBreakdown> AllocateTaxAmount(
        decimal taxAmount,
        decimal rawGrossAmount,
        IReadOnlyList<RefundedInvoiceLine> lines)
    {
        var taxes = InvoiceCalculator.AllocateTaxByGrossShare(
            taxAmount,
            rawGrossAmount,
            lines.Select(line => line.GrossAmount).ToArray());

        return lines
            .Select((line, index) => new InvoiceAllowanceLineBreakdown(
                line.SimulatedInvoiceItemPublicId,
                line.Quantity,
                line.GrossAmount - taxes[index],
                taxes[index],
                line.GrossAmount))
            .ToArray();
    }
}
