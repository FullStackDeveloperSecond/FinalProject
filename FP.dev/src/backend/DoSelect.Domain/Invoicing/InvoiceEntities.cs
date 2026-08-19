using DoSelect.Domain.Common;

namespace DoSelect.Domain.Invoicing;

public enum SimulatedInvoiceBuyerType
{
    Individual,
    Company,
}

public enum SimulatedInvoiceStatus
{
    Pending,
    Issued,
    Voided,
    PartiallyAllowed,
    FullyAllowed,
}

public sealed record SimulatedInvoiceCreation(
    long OrderId,
    string InvoiceNumber,
    SimulatedInvoiceBuyerType BuyerType,
    string? BuyerEmail,
    string? CarrierType,
    string? CarrierValueMasked,
    string? CompanyTaxId,
    string? CompanyName,
    decimal NetAmount,
    decimal TaxAmount,
    decimal IssuedAmount);

public sealed class SimulatedInvoice : MutablePublicEntity
{
    public const string RequiredDemoMarker = "DEMO-NOT-A-TAX-INVOICE";

    private SimulatedInvoice() { }

    public SimulatedInvoice(Guid publicId, SimulatedInvoiceCreation creation, DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(creation);
        if (creation.OrderId <= 0 || creation.NetAmount < 0 || creation.TaxAmount < 0 ||
            creation.IssuedAmount < 0 || creation.IssuedAmount != creation.NetAmount + creation.TaxAmount ||
            creation.BuyerType == SimulatedInvoiceBuyerType.Company &&
            (string.IsNullOrWhiteSpace(creation.CompanyTaxId) || string.IsNullOrWhiteSpace(creation.CompanyName)))
        {
            throw new ArgumentException("The simulated invoice is invalid.", nameof(creation));
        }

        OrderId = creation.OrderId;
        InvoiceNumber = RequireText(creation.InvoiceNumber, nameof(creation.InvoiceNumber));
        BuyerType = creation.BuyerType;
        BuyerEmail = NormalizeOptional(creation.BuyerEmail);
        CarrierType = NormalizeOptional(creation.CarrierType);
        CarrierValueMasked = NormalizeOptional(creation.CarrierValueMasked);
        CompanyTaxId = NormalizeOptional(creation.CompanyTaxId);
        CompanyName = NormalizeOptional(creation.CompanyName);
        NetAmount = creation.NetAmount;
        TaxAmount = creation.TaxAmount;
        IssuedAmount = creation.IssuedAmount;
        Currency = "TWD";
        Status = SimulatedInvoiceStatus.Pending;
        DemoMarker = RequiredDemoMarker;
    }

    public long OrderId { get; private set; }
    public string InvoiceNumber { get; private set; } = string.Empty;
    public SimulatedInvoiceBuyerType BuyerType { get; private set; }
    public string? BuyerEmail { get; private set; }
    public string? CarrierType { get; private set; }
    public string? CarrierValueMasked { get; private set; }
    public string? CompanyTaxId { get; private set; }
    public string? CompanyName { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal IssuedAmount { get; private set; }
    public string Currency { get; private set; } = "TWD";
    public SimulatedInvoiceStatus Status { get; private set; }
    public DateTime? IssuedAtUtc { get; private set; }
    public DateTime? VoidedAtUtc { get; private set; }
    public string DemoMarker { get; private set; } = RequiredDemoMarker;

    public void Issue(DateTime issuedAtUtc)
    {
        if (Status != SimulatedInvoiceStatus.Pending) throw new InvalidOperationException("Only a pending invoice can be issued.");
        IssuedAtUtc = RequireUtc(issuedAtUtc, nameof(issuedAtUtc));
        Status = SimulatedInvoiceStatus.Issued;
        MarkUpdated(issuedAtUtc);
    }

    public void Void(DateTime voidedAtUtc)
    {
        if (Status != SimulatedInvoiceStatus.Issued) throw new InvalidOperationException("Only an issued invoice can be voided.");
        VoidedAtUtc = RequireUtc(voidedAtUtc, nameof(voidedAtUtc));
        Status = SimulatedInvoiceStatus.Voided;
        MarkUpdated(voidedAtUtc);
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class SimulatedInvoiceItem : PublicEntity
{
    private SimulatedInvoiceItem() { }

    public SimulatedInvoiceItem(Guid publicId, long simulatedInvoiceId, long? orderItemId,
        string productNameSnapshot, string skuCodeSnapshot, int quantity, decimal unitPrice,
        decimal discountAmount, decimal netAmount, decimal taxAmount, decimal grossAmount,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (simulatedInvoiceId <= 0 || orderItemId is <= 0 || quantity <= 0 ||
            new[] { unitPrice, discountAmount, netAmount, taxAmount, grossAmount }.Any(value => value < 0) ||
            grossAmount != netAmount + taxAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(simulatedInvoiceId));
        }
        SimulatedInvoiceId = simulatedInvoiceId;
        OrderItemId = orderItemId;
        ProductNameSnapshot = RequireText(productNameSnapshot, nameof(productNameSnapshot));
        SkuCodeSnapshot = RequireText(skuCodeSnapshot, nameof(skuCodeSnapshot));
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountAmount = discountAmount;
        NetAmount = netAmount;
        TaxAmount = taxAmount;
        GrossAmount = grossAmount;
    }

    public long SimulatedInvoiceId { get; private set; }
    public long? OrderItemId { get; private set; }
    public string ProductNameSnapshot { get; private set; } = string.Empty;
    public string SkuCodeSnapshot { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal GrossAmount { get; private set; }
}

public sealed class SimulatedInvoiceAllowance : PublicEntity
{
    private SimulatedInvoiceAllowance() { }
    public SimulatedInvoiceAllowance(Guid publicId, long simulatedInvoiceId, long refundId,
        string allowanceNumber, decimal netAmount, decimal taxAmount, decimal amount,
        DateTime issuedAtUtc, DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (simulatedInvoiceId <= 0 || refundId <= 0 || netAmount < 0 || taxAmount < 0 ||
            amount <= 0 || amount != netAmount + taxAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(simulatedInvoiceId));
        }
        SimulatedInvoiceId = simulatedInvoiceId;
        RefundId = refundId;
        AllowanceNumber = RequireText(allowanceNumber, nameof(allowanceNumber));
        NetAmount = netAmount;
        TaxAmount = taxAmount;
        Amount = amount;
        IssuedAtUtc = RequireUtc(issuedAtUtc, nameof(issuedAtUtc));
    }
    public long SimulatedInvoiceId { get; private set; }
    public long RefundId { get; private set; }
    public string AllowanceNumber { get; private set; } = string.Empty;
    public decimal NetAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime IssuedAtUtc { get; private set; }
}

public sealed class SimulatedInvoiceAllowanceItem : PublicEntity
{
    private SimulatedInvoiceAllowanceItem() { }
    public SimulatedInvoiceAllowanceItem(Guid publicId, long allowanceId,
        long simulatedInvoiceItemId, int quantity, decimal netAmount, decimal taxAmount,
        decimal grossAmount, DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (allowanceId <= 0 || simulatedInvoiceItemId <= 0 || quantity <= 0 ||
            netAmount < 0 || taxAmount < 0 || grossAmount <= 0 || grossAmount != netAmount + taxAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(allowanceId));
        }
        AllowanceId = allowanceId;
        SimulatedInvoiceItemId = simulatedInvoiceItemId;
        Quantity = quantity;
        NetAmount = netAmount;
        TaxAmount = taxAmount;
        GrossAmount = grossAmount;
    }
    public long AllowanceId { get; private set; }
    public long SimulatedInvoiceItemId { get; private set; }
    public int Quantity { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal GrossAmount { get; private set; }
}
