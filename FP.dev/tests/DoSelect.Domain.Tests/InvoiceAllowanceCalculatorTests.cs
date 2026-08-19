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
            [new RefundedInvoiceLine(ItemA, 1, 1000m)]);

        Assert.True(result.IsSuccess);
        Assert.Equal(952m, result.NetAmount);
        Assert.Equal(48m, result.TaxAmount);
        Assert.Equal(1000m, result.Amount);
        Assert.Equal(result.Amount, result.NetAmount + result.TaxAmount);
    }

    [Fact]
    public void TheHeaderIsBackedOutOfTheAllowanceTotalAndLinesAbsorbTheRemainder()
    {
        // 兩列各 1,000。逐列回推會得到 1904，由總額回推是 1905，末列吸收尾差。
        var result = Calculate(
            [Capacity(ItemA, quantity: 1, gross: 1000m), Capacity(ItemB, quantity: 1, gross: 1000m)],
            [new RefundedInvoiceLine(ItemA, 1, 1000m), new RefundedInvoiceLine(ItemB, 1, 1000m)]);

        Assert.Equal(1905m, result.NetAmount);
        Assert.Equal(95m, result.TaxAmount);
        Assert.Equal([953m, 952m], result.Lines.Select(line => line.NetAmount));
        Assert.Equal(result.NetAmount, result.Lines.Sum(line => line.NetAmount));
        Assert.Equal(result.TaxAmount, result.Lines.Sum(line => line.TaxAmount));
    }

    [Fact]
    public void PartialAllowanceLeavesTheInvoicePartiallyAllowed()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m)],
            [new RefundedInvoiceLine(ItemA, 1, 1000m)]);

        Assert.False(result.FullyAllowed);
        Assert.Equal(SimulatedInvoiceStatus.PartiallyAllowed, result.ResultingInvoiceStatus);
    }

    [Fact]
    public void AllowingEveryLineInFullMarksTheInvoiceFullyAllowed()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m), Capacity(ItemB, quantity: 1, gross: 899m)],
            [new RefundedInvoiceLine(ItemA, 2, 2000m), new RefundedInvoiceLine(ItemB, 1, 899m)]);

        Assert.True(result.FullyAllowed);
        Assert.Equal(SimulatedInvoiceStatus.FullyAllowed, result.ResultingInvoiceStatus);
    }

    [Fact]
    public void TheFinalAllowanceAfterAnEarlierOneMarksTheInvoiceFullyAllowed()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m, allowedQuantity: 1, allowedGross: 1000m)],
            [new RefundedInvoiceLine(ItemA, 1, 1000m)]);

        Assert.True(result.IsSuccess);
        Assert.True(result.FullyAllowed);
    }

    [Fact]
    public void LeavingALineOutKeepsTheInvoicePartiallyAllowed()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m), Capacity(ItemB, quantity: 1, gross: 899m)],
            [new RefundedInvoiceLine(ItemA, 2, 2000m)]);

        Assert.True(result.IsSuccess);
        Assert.False(result.FullyAllowed);
    }

    [Fact]
    public void CumulativeAllowanceCannotExceedTheLineQuantity() =>
        Assert.Equal(
            InvoiceErrorCodes.InvoiceStateConflict,
            Calculate(
                [Capacity(ItemA, quantity: 2, gross: 2000m, allowedQuantity: 1, allowedGross: 1000m)],
                [new RefundedInvoiceLine(ItemA, 2, 1000m)]).ErrorCode);

    [Fact]
    public void CumulativeAllowanceCannotExceedTheLineAmount() =>
        Assert.Equal(
            InvoiceErrorCodes.InvoiceStateConflict,
            Calculate(
                [Capacity(ItemA, quantity: 2, gross: 2000m, allowedQuantity: 1, allowedGross: 1000m)],
                [new RefundedInvoiceLine(ItemA, 1, 1000.01m)]).ErrorCode);

    [Fact]
    public void AllowingTheSameLineTwiceInOneRequestIsRejected() =>
        Assert.Equal(
            InvoiceErrorCodes.InvoiceStateConflict,
            Calculate(
                [Capacity(ItemA, quantity: 2, gross: 2000m)],
                [new RefundedInvoiceLine(ItemA, 1, 1000m), new RefundedInvoiceLine(ItemA, 1, 1000m)])
                .ErrorCode);

    [Fact]
    public void ALineThatIsNotOnTheInvoiceIsNotFound() =>
        Assert.Equal(
            InvoiceErrorCodes.ResourceNotFound,
            Calculate(
                [Capacity(ItemA, quantity: 2, gross: 2000m)],
                [new RefundedInvoiceLine(Guid.NewGuid(), 1, 100m)]).ErrorCode);

    [Theory]
    [InlineData(SimulatedInvoiceStatus.Pending)]
    [InlineData(SimulatedInvoiceStatus.Voided)]
    [InlineData(SimulatedInvoiceStatus.FullyAllowed)]
    public void OnlyAnIssuedOrPartiallyAllowedInvoiceCanBeAllowed(SimulatedInvoiceStatus status) =>
        Assert.Equal(
            InvoiceErrorCodes.InvoiceStateConflict,
            Calculate(
                [Capacity(ItemA, quantity: 2, gross: 2000m)],
                [new RefundedInvoiceLine(ItemA, 1, 1000m)],
                status).ErrorCode);

    [Fact]
    public void APartiallyAllowedInvoiceCanBeAllowedAgain() =>
        Assert.True(Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m, allowedQuantity: 1, allowedGross: 1000m)],
            [new RefundedInvoiceLine(ItemA, 1, 1000m)],
            SimulatedInvoiceStatus.PartiallyAllowed).IsSuccess);

    [Fact]
    public void ARefundNeverGetsASecondAllowance()
    {
        var result = InvoiceAllowanceCalculator.Calculate(new InvoiceAllowanceRequest(
            SimulatedInvoiceStatus.Issued,
            RefundAlreadyHasAllowance: true,
            [Capacity(ItemA, quantity: 2, gross: 2000m)],
            [new RefundedInvoiceLine(ItemA, 1, 1000m)]));

        Assert.Equal(InvoiceErrorCodes.InvoiceStateConflict, result.ErrorCode);
    }

    [Fact]
    public void ARefundWithNoChargeableLineIsRejected() =>
        Assert.Equal(
            InvoiceErrorCodes.InvoiceStateConflict,
            Calculate(
                [Capacity(ItemA, quantity: 2, gross: 2000m)],
                [new RefundedInvoiceLine(ItemA, 1, 0m)]).ErrorCode);

    [Fact]
    public void AMalformedQuantityIsAProgrammingError() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m)],
            [new RefundedInvoiceLine(ItemA, 0, 1000m)]));

    [Fact]
    public void LineAmountsAlwaysSumToTheHeader()
    {
        decimal[][] shapes = [[1m, 1m, 1m], [999m, 1m], [2000m, 899m, 150m], [33m, 33m, 33m]];

        foreach (var shape in shapes)
        {
            var capacities = shape
                .Select((gross, index) => Capacity(
                    new Guid(index + 1, 0, 0, new byte[8]), quantity: 1, gross: gross))
                .ToArray();
            var refunded = shape
                .Select((gross, index) => new RefundedInvoiceLine(
                    new Guid(index + 1, 0, 0, new byte[8]), 1, gross))
                .ToArray();

            var result = Calculate(capacities, refunded);

            Assert.Equal(result.NetAmount, result.Lines.Sum(line => line.NetAmount));
            Assert.Equal(result.TaxAmount, result.Lines.Sum(line => line.TaxAmount));
            Assert.Equal(result.Amount, result.Lines.Sum(line => line.GrossAmount));
            Assert.All(result.Lines, line => Assert.True(line.NetAmount >= 0m));
        }
    }

    [Fact]
    public void AllowanceAmountsSatisfyTheEntityInvariants()
    {
        var result = Calculate(
            [Capacity(ItemA, quantity: 2, gross: 2000m)],
            [new RefundedInvoiceLine(ItemA, 1, 1000m)]);

        var allowance = new SimulatedInvoiceAllowance(
            Guid.NewGuid(), 1, 1, DemoAllowanceNumber.Format(IssuedAtUtc, 1),
            result.NetAmount, result.TaxAmount, result.Amount, IssuedAtUtc, IssuedAtUtc);

        Assert.Equal("DEMO-A-202608-000001", allowance.AllowanceNumber);
        Assert.Equal(result.Amount, allowance.NetAmount + allowance.TaxAmount);
    }

    [Fact]
    public void RecordingAnAllowanceMovesTheInvoiceThroughTheDocumentedStates()
    {
        var invoice = CreateIssuedInvoice("DEMO-202608-000001");

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
        var invoice = CreateIssuedInvoice("DEMO-202608-000002");
        invoice.Void(IssuedAtUtc.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() =>
            invoice.RecordAllowance(fullyAllowed: false, IssuedAtUtc.AddMinutes(2)));
    }

    private static SimulatedInvoice CreateIssuedInvoice(string invoiceNumber)
    {
        var invoice = new SimulatedInvoice(Guid.NewGuid(), new SimulatedInvoiceCreation(
            1, invoiceNumber, SimulatedInvoiceBuyerType.Individual, null, null, null,
            null, null, 952m, 48m, 1000m), IssuedAtUtc);

        invoice.Issue(IssuedAtUtc);
        return invoice;
    }

    private static InvoiceAllowanceResult Calculate(
        IReadOnlyList<InvoiceAllowanceCapacity> capacities,
        IReadOnlyList<RefundedInvoiceLine> refundedLines,
        SimulatedInvoiceStatus status = SimulatedInvoiceStatus.Issued) =>
        InvoiceAllowanceCalculator.Calculate(new InvoiceAllowanceRequest(
            status, RefundAlreadyHasAllowance: false, capacities, refundedLines));

    private static InvoiceAllowanceCapacity Capacity(
        Guid itemPublicId,
        int quantity,
        decimal gross,
        int allowedQuantity = 0,
        decimal allowedGross = 0m) =>
        new(itemPublicId, quantity, allowedQuantity, gross, allowedGross);
}
