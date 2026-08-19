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

        var amount = allowableLines.Sum(line => line.GrossAmount);
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
            fullyAllowed,
            AllocateNetAmount(netAmount, amount, allowableLines));
    }

    /// <summary>
    /// 依含稅金額比例把折讓表頭未稅分攤到各明細，非末筆夾在剩餘未分攤金額以內，
    /// 最後一筆吸收尾差，因此明細未稅與稅額合計精確等於表頭。
    /// </summary>
    private static IReadOnlyList<InvoiceAllowanceLineBreakdown> AllocateNetAmount(
        decimal netAmount,
        decimal amount,
        IReadOnlyList<RefundedInvoiceLine> lines)
    {
        var breakdowns = new InvoiceAllowanceLineBreakdown[lines.Count];
        var allocated = 0m;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var remaining = netAmount - allocated;
            var lineNetAmount = index == lines.Count - 1
                ? remaining
                : Math.Min(
                    Math.Round(
                        netAmount * line.GrossAmount / amount,
                        InvoiceCalculator.AmountScale,
                        MidpointRounding.AwayFromZero),
                    remaining);

            allocated += lineNetAmount;
            breakdowns[index] = new InvoiceAllowanceLineBreakdown(
                line.SimulatedInvoiceItemPublicId,
                line.Quantity,
                lineNetAmount,
                line.GrossAmount - lineNetAmount,
                line.GrossAmount);
        }

        return breakdowns;
    }
}
