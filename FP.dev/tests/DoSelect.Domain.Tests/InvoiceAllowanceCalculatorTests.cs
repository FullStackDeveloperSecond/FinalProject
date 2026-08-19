using DoSelect.Domain.Invoicing;

namespace DoSelect.Domain.Tests;

public sealed class InvoiceAllowanceCalculatorTests
{
    private static readonly DateTime IssuedAtUtc = new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ItemA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ItemB = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void AllowanceBacksTaxOutTheSameWayTheInvoiceDid()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m)],
            [new InvoiceAllowanceLineRequest(ItemA, 1, 1000m)]);

        Assert.True(result.IsSuccess);
        Assert.Equal(952m, result.NetAmount);
        Assert.Equal(48m, result.TaxAmount);
        Assert.Equal(1000m, result.Amount);
        Assert.Equal(result.Amount, result.NetAmount + result.TaxAmount);
    }

    [Fact]
    public void PartialAllowanceLeavesTheInvoicePartiallyAllowed()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m)],
            [new InvoiceAllowanceLineRequest(ItemA, 1, 1000m)]);

        Assert.False(result.FullyAllowed);
        Assert.Equal(SimulatedInvoiceStatus.PartiallyAllowed, result.ResultingInvoiceStatus);
    }

    [Fact]
    public void AllowingEveryLineInFullMarksTheInvoiceFullyAllowed()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m), Capacity(ItemB, quantity: 1, gross: 899m)],
            [
                new InvoiceAllowanceLineRequest(ItemA, 2, 2000m),
                new InvoiceAllowanceLineRequest(ItemB, 1, 899m),
            ]);

        Assert.True(result.FullyAllowed);
        Assert.Equal(SimulatedInvoiceStatus.FullyAllowed, result.ResultingInvoiceStatus);
    }

    [Fact]
    public void TheFinalAllowanceAfterAnEarlierOneMarksTheInvoiceFullyAllowed()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m, allowedQuantity: 1, allowedGross: 1000m)],
            [new InvoiceAllowanceLineRequest(ItemA, 1, 1000m)]);

        Assert.True(result.IsSuccess);
        Assert.True(result.FullyAllowed);
    }

    [Fact]
    public void LeavingALineOutKeepsTheInvoicePartiallyAllowed()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m), Capacity(ItemB, quantity: 1, gross: 899m)],
            [new InvoiceAllowanceLineRequest(ItemA, 2, 2000m)]);

        Assert.True(result.IsSuccess);
        Assert.False(result.FullyAllowed);
    }

    [Fact]
    public void CumulativeAllowanceCannotExceedTheLineQuantity()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m, allowedQuantity: 1, allowedGross: 1000m)],
            [new InvoiceAllowanceLineRequest(ItemA, 2, 1000m)]);

        Assert.Equal(InvoiceAllowanceRejection.LineCapacityExceeded, result.Rejection);
    }

    [Fact]
    public void CumulativeAllowanceCannotExceedTheLineAmount()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m, allowedQuantity: 1, allowedGross: 1000m)],
            [new InvoiceAllowanceLineRequest(ItemA, 1, 1000.01m)]);

        Assert.Equal(InvoiceAllowanceRejection.LineCapacityExceeded, result.Rejection);
    }

    [Fact]
    public void AllowingTheSameLineTwiceInOneRequestIsRejected()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m)],
            [
                new InvoiceAllowanceLineRequest(ItemA, 1, 1000m),
                new InvoiceAllowanceLineRequest(ItemA, 1, 1000m),
            ]);

        Assert.Equal(InvoiceAllowanceRejection.LineCapacityExceeded, result.Rejection);
    }

    [Fact]
    public void ALineThatIsNotOnTheInvoiceIsRejected()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m)],
            [new InvoiceAllowanceLineRequest(Guid.NewGuid(), 1, 100m)]);

        Assert.Equal(InvoiceAllowanceRejection.LineNotOnInvoice, result.Rejection);
    }

    [Theory]
    [InlineData(SimulatedInvoiceStatus.Pending)]
    [InlineData(SimulatedInvoiceStatus.Voided)]
    [InlineData(SimulatedInvoiceStatus.FullyAllowed)]
    public void OnlyAnIssuedOrPartiallyAllowedInvoiceCanBeAllowed(SimulatedInvoiceStatus status)
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m)],
            [new InvoiceAllowanceLineRequest(ItemA, 1, 1000m)],
            status);

        Assert.Equal(InvoiceAllowanceRejection.InvoiceNotAllowable, result.Rejection);
    }

    [Fact]
    public void APartiallyAllowedInvoiceCanBeAllowedAgain()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m, allowedQuantity: 1, allowedGross: 1000m)],
            [new InvoiceAllowanceLineRequest(ItemA, 1, 1000m)],
            SimulatedInvoiceStatus.PartiallyAllowed);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ARefundNeverGetsASecondAllowance()
    {
        var result = InvoiceAllowanceCalculator.Calculate(new InvoiceAllowanceRequest(
            SimulatedInvoiceStatus.Issued,
            RefundAlreadyHasAllowance: true,
            [Capacity(ItemA, quantity: 2, gross: 2000m)],
            [new InvoiceAllowanceLineRequest(ItemA, 1, 1000m)]));

        Assert.Equal(InvoiceAllowanceRejection.RefundAlreadyAllowed, result.Rejection);
    }

    [Fact]
    public void ARequestWithNoChargeableLineIsRejected()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m)],
            [new InvoiceAllowanceLineRequest(ItemA, 1, 0m)]);

        Assert.Equal(InvoiceAllowanceRejection.NoAllowableLines, result.Rejection);
    }

    [Fact]
    public void AMalformedQuantityIsAProgrammingError() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m)],
            [new InvoiceAllowanceLineRequest(ItemA, 0, 1000m)]));

    [Fact]
    public void LineAmountsSumToTheAllowanceTotals()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m), Capacity(ItemB, quantity: 1, gross: 899m)],
            [
                new InvoiceAllowanceLineRequest(ItemA, 1, 1000m),
                new InvoiceAllowanceLineRequest(ItemB, 1, 899m),
            ]);

        Assert.Equal(result.NetAmount, result.Lines.Sum(line => line.NetAmount));
        Assert.Equal(result.TaxAmount, result.Lines.Sum(line => line.TaxAmount));
        Assert.Equal(result.Amount, result.Lines.Sum(line => line.GrossAmount));
        Assert.Equal(result.Amount, result.NetAmount + result.TaxAmount);
    }

    [Fact]
    public void AllowanceAmountsSatisfyTheEntityInvariants()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m)],
            [new InvoiceAllowanceLineRequest(ItemA, 1, 1000m)]);

        var allowance = new SimulatedInvoiceAllowance(
            Guid.NewGuid(), 1, 1, DemoAllowanceNumber.Format(IssuedAtUtc, 1),
            result.NetAmount, result.TaxAmount, result.Amount, IssuedAtUtc, IssuedAtUtc);

        Assert.Equal("DEMO-A-202608-000001", allowance.AllowanceNumber);
        Assert.Equal(result.Amount, allowance.NetAmount + allowance.TaxAmount);
    }

    [Fact]
    public void RecordingAnAllowanceMovesTheInvoiceThroughTheDocumentedStates()
    {
        var invoice = new SimulatedInvoice(Guid.NewGuid(), new SimulatedInvoiceCreation(
            1, "DEMO-202608-000001", SimulatedInvoiceBuyerType.Individual, null, null, null,
            null, null, 952m, 48m, 1000m), IssuedAtUtc);

        invoice.Issue(IssuedAtUtc);
        invoice.RecordAllowance(fullyAllowed: false, IssuedAtUtc.AddMinutes(1));
        Assert.Equal(SimulatedInvoiceStatus.PartiallyAllowed, invoice.Status);

        invoice.RecordAllowance(fullyAllowed: true, IssuedAtUtc.AddMinutes(2));
        Assert.Equal(SimulatedInvoiceStatus.FullyAllowed, invoice.Status);

        Assert.Throws<InvalidOperationException>(() =>
            invoice.RecordAllowance(fullyAllowed: true, IssuedAtUtc.AddMinutes(3)));
    }

    [Fact]
    public void AVoidedInvoiceCannotRecordAnAllowance()
    {
        var invoice = new SimulatedInvoice(Guid.NewGuid(), new SimulatedInvoiceCreation(
            1, "DEMO-202608-000002", SimulatedInvoiceBuyerType.Individual, null, null, null,
            null, null, 952m, 48m, 1000m), IssuedAtUtc);

        invoice.Issue(IssuedAtUtc);
        invoice.Void(IssuedAtUtc.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() =>
            invoice.RecordAllowance(fullyAllowed: false, IssuedAtUtc.AddMinutes(2)));
    }

    private static InvoiceAllowanceResult Calculate(
        IReadOnlyList<InvoiceAllowanceCapacity> capacities,
        IReadOnlyList<InvoiceAllowanceLineRequest> lines,
        SimulatedInvoiceStatus status = SimulatedInvoiceStatus.Issued) =>
        InvoiceAllowanceCalculator.Calculate(new InvoiceAllowanceRequest(
            status, RefundAlreadyHasAllowance: false, capacities, lines));

    private static InvoiceAllowanceCapacity Capacity(
        Guid itemPublicId,
        int quantity,
        decimal gross,
        int allowedQuantity = 0,
        decimal allowedGross = 0m) =>
        new(itemPublicId, quantity, allowedQuantity, gross, allowedGross);
}
