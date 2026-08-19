namespace DoSelect.Domain.Invoicing;

/// <summary>
/// 退款折讓金額的純計算。含稅折讓金額來自退款分攤，未稅與稅額以與發票相同的方式回推。
/// 累計折讓不得超過原發票各明細的可折讓數量與金額；折讓不回寫也不刪除原發票。
/// </summary>
public static class InvoiceAllowanceCalculator
{
    public static InvoiceAllowanceResult Calculate(InvoiceAllowanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Capacities);
        ArgumentNullException.ThrowIfNull(request.Lines);

        if (request.RefundAlreadyHasAllowance)
        {
            return InvoiceAllowanceResult.Failure(InvoiceAllowanceRejection.RefundAlreadyAllowed);
        }

        if (request.InvoiceStatus is not (SimulatedInvoiceStatus.Issued or
            SimulatedInvoiceStatus.PartiallyAllowed))
        {
            return InvoiceAllowanceResult.Failure(InvoiceAllowanceRejection.InvoiceNotAllowable);
        }

        var allowableLines = request.Lines.Where(line => line.GrossAmount > 0m).ToArray();
        if (allowableLines.Length == 0)
        {
            return InvoiceAllowanceResult.Failure(InvoiceAllowanceRejection.NoAllowableLines);
        }

        var lines = new List<InvoiceAllowanceLineBreakdown>(allowableLines.Length);
        var requestedByItem = new Dictionary<Guid, (int Quantity, decimal GrossAmount)>();

        foreach (var requested in allowableLines)
        {
            if (requested.Quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "An allowance line is malformed.");
            }

            var capacity = request.Capacities.SingleOrDefault(candidate =>
                candidate.SimulatedInvoiceItemPublicId == requested.SimulatedInvoiceItemPublicId);

            if (capacity is null)
            {
                return InvoiceAllowanceResult.Failure(InvoiceAllowanceRejection.LineNotOnInvoice);
            }

            if (requestedByItem.ContainsKey(requested.SimulatedInvoiceItemPublicId) ||
                requested.Quantity > capacity.RemainingQuantity ||
                requested.GrossAmount > capacity.RemainingGrossAmount)
            {
                return InvoiceAllowanceResult.Failure(InvoiceAllowanceRejection.LineCapacityExceeded);
            }

            requestedByItem[requested.SimulatedInvoiceItemPublicId] =
                (requested.Quantity, requested.GrossAmount);

            var netAmount = BackOutNetAmount(requested.GrossAmount);
            lines.Add(new InvoiceAllowanceLineBreakdown(
                requested.SimulatedInvoiceItemPublicId,
                requested.Quantity,
                netAmount,
                requested.GrossAmount - netAmount,
                requested.GrossAmount));
        }

        var fullyAllowed = request.Capacities.All(capacity =>
        {
            var requested = requestedByItem.GetValueOrDefault(capacity.SimulatedInvoiceItemPublicId);
            return capacity.AlreadyAllowedQuantity + requested.Quantity == capacity.Quantity &&
                capacity.AlreadyAllowedGrossAmount + requested.GrossAmount == capacity.GrossAmount;
        });

        return InvoiceAllowanceResult.Success(
            lines.Sum(line => line.NetAmount),
            lines.Sum(line => line.TaxAmount),
            lines.Sum(line => line.GrossAmount),
            fullyAllowed,
            lines);
    }

    /// <summary>
    /// 與 <see cref="InvoiceCalculator"/> 使用同一套回推方式，確保折讓與發票的未稅稅額口徑一致。
    /// </summary>
    private static decimal BackOutNetAmount(decimal grossAmount) => Math.Round(
        grossAmount / (1m + InvoiceCalculator.BusinessTaxRate),
        InvoiceCalculator.AmountScale,
        MidpointRounding.AwayFromZero);
}
