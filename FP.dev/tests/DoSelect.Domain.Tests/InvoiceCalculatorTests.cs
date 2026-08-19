using DoSelect.Domain.Invoicing;

namespace DoSelect.Domain.Tests;

public sealed class InvoiceCalculatorTests
{
    private static readonly DateTime CreatedAtUtc = new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ItemA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ItemB = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void TheAcceptanceCaseIsOneThousandGrossToNineFiveTwoAndFortyEight()
    {
        var result = Calculate([Merchandise(ItemA, quantity: 1, gross: 1000m)]);

        Assert.True(result.IsSuccess);
        Assert.Equal(952m, result.NetAmount);
        Assert.Equal(48m, result.TaxAmount);
        Assert.Equal(1000m, result.IssuedAmount);
    }

    [Fact]
    public void NetPlusTaxAlwaysEqualsTheGrossAmount()
    {
        var grossAmounts = new[] { 1m, 7m, 33m, 99m, 105m, 1000m, 12345m, 29999m };

        foreach (var gross in grossAmounts)
        {
            var result = Calculate([Merchandise(ItemA, quantity: 1, gross: gross)]);

            Assert.Equal(gross, result.NetAmount + result.TaxAmount);
            Assert.Equal(gross, result.IssuedAmount);
        }
    }

    [Fact]
    public void TheHeaderIsBackedOutOfTheTotalNotSummedFromIndependentlyRoundedLines()
    {
        // 兩列各 1,000。逐列回推會得到 952 + 952 = 1904，與由總額回推的 1905 差一元。
        // 表頭必須是 1905，末列吸收尾差。
        var result = Calculate(
        [
            Merchandise(ItemA, quantity: 1, gross: 1000m),
            Merchandise(ItemB, quantity: 1, gross: 1000m),
        ]);

        Assert.Equal(1905m, result.NetAmount);
        Assert.Equal(95m, result.TaxAmount);
        Assert.Equal(result.NetAmount, result.Lines.Sum(line => line.NetAmount));
        Assert.Equal(result.TaxAmount, result.Lines.Sum(line => line.TaxAmount));
    }

    [Fact]
    public void TheLastLineAbsorbsTheRoundingRemainder()
    {
        var result = Calculate(
        [
            Merchandise(ItemA, quantity: 1, gross: 1000m),
            Merchandise(ItemB, quantity: 1, gross: 1000m),
        ]);

        Assert.Equal([953m, 952m], result.Lines.Select(line => line.NetAmount));
        Assert.Equal([47m, 48m], result.Lines.Select(line => line.TaxAmount));
    }

    [Fact]
    public void LineAmountsAlwaysSumToTheHeaderAcrossManyShapes()
    {
        decimal[][] shapes =
        [
            [1m, 1m, 1m],
            [999m, 1m],
            [2000m, 899m, 150m, 300m],
            [33m, 33m, 33m, 33m, 33m],
            [12345m, 6789m, 1m],
        ];

        foreach (var shape in shapes)
        {
            var result = Calculate(shape
                .Select((gross, index) => Merchandise(
                    new Guid(index + 1, 0, 0, new byte[8]), quantity: 1, gross: gross))
                .ToArray());

            Assert.Equal(result.NetAmount, result.Lines.Sum(line => line.NetAmount));
            Assert.Equal(result.TaxAmount, result.Lines.Sum(line => line.TaxAmount));
            Assert.Equal(result.IssuedAmount, result.Lines.Sum(line => line.GrossAmount));
            Assert.All(result.Lines, line => Assert.True(line.NetAmount >= 0m));
        }
    }

    [Fact]
    public void ShippingAndAssemblyAreInvoiceLinesWithoutAnOrderItem()
    {
        var result = Calculate(
        [
            Merchandise(ItemA, quantity: 1, gross: 1000m),
            NonMerchandise(InvoiceLineKind.Shipping, "宅配運費", "SHIPPING", 150m),
            NonMerchandise(InvoiceLineKind.AssemblyFee, "組裝費", "ASSEMBLY", 300m),
        ]);

        Assert.Equal(1450m, result.IssuedAmount);
        Assert.Equal(
            ItemA,
            result.Lines.Single(line => line.Kind == InvoiceLineKind.Merchandise).OrderItemPublicId);
        Assert.All(
            result.Lines.Where(line => line.Kind != InvoiceLineKind.Merchandise),
            line => Assert.Null(line.OrderItemPublicId));
    }

    [Fact]
    public void ZeroAmountLinesAreLeftOffTheInvoice()
    {
        var result = Calculate(
        [
            Merchandise(ItemA, quantity: 1, gross: 1000m),
            NonMerchandise(InvoiceLineKind.Shipping, "免運", "SHIPPING", 0m),
        ]);

        Assert.Single(result.Lines);
        Assert.Equal(1000m, result.IssuedAmount);
    }

    [Theory]
    [InlineData(InvoiceIssuanceTrigger.OnlinePaymentSucceeded)]
    [InlineData(InvoiceIssuanceTrigger.CashOnDeliveryCollected)]
    public void PaidOrders_AreInvoiceable(InvoiceIssuanceTrigger trigger) =>
        Assert.True(Calculate([Merchandise(ItemA, 1, 1000m)], trigger).IsSuccess);

    [Fact]
    public void UnpaidOrders_ReturnTheUnpaidCode() =>
        Assert.Equal(
            InvoiceErrorCodes.InvoiceOrderUnpaid,
            Calculate([Merchandise(ItemA, 1, 1000m)], InvoiceIssuanceTrigger.NotPaid).ErrorCode);

    [Fact]
    public void CancelledOrders_ReturnTheCancelledCode() =>
        Assert.Equal(
            InvoiceErrorCodes.InvoiceOrderCancelled,
            Calculate([Merchandise(ItemA, 1, 1000m)], InvoiceIssuanceTrigger.OrderCancelled).ErrorCode);

    [Fact]
    public void AnOrderNeverGetsASecondInvoice()
    {
        var result = InvoiceCalculator.Calculate(new InvoiceIssuanceRequest(
            InvoiceIssuanceTrigger.OnlinePaymentSucceeded,
            OrderAlreadyHasInvoice: true,
            [Merchandise(ItemA, 1, 1000m)]));

        Assert.Equal(InvoiceErrorCodes.InvoiceAlreadyExists, result.ErrorCode);
    }

    [Fact]
    public void APaidOrderWithoutAnyChargeableLine_IsAProgrammingError() =>
        Assert.Throws<ArgumentException>(() => Calculate([Merchandise(ItemA, 1, 0m)]));

    [Fact]
    public void AMerchandiseLineWithoutItsOrderItem_IsAProgrammingError() =>
        Assert.Throws<ArgumentException>(() => Calculate(
            [NonMerchandise(InvoiceLineKind.Merchandise, "商品", "SKU-1", 1000m)]));

    [Fact]
    public void AMalformedLine_IsAProgrammingError() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Calculate([Merchandise(ItemA, quantity: 0, gross: 1000m)]));

    [Fact]
    public void OnlyAnIssuedInvoiceCanBeVoided()
    {
        Assert.Equal(
            InvoiceErrorCodes.InvoiceStateConflict,
            InvoicePolicy.FindVoidRejection(SimulatedInvoiceStatus.Pending, true, false));
        Assert.Equal(
            InvoiceErrorCodes.InvoiceStateConflict,
            InvoicePolicy.FindVoidRejection(SimulatedInvoiceStatus.Voided, true, false));
        Assert.Null(InvoicePolicy.FindVoidRejection(SimulatedInvoiceStatus.Issued, true, false));
    }

    [Fact]
    public void ASettledRefundForcesAnAllowanceInsteadOfAVoid() =>
        Assert.Equal(
            InvoiceErrorCodes.InvoiceAllowanceRequired,
            VoidRejection(hasSettledRefund: true));

    [Fact]
    public void APartiallyCancelledOrderCannotVoidItsInvoice() =>
        Assert.Equal(
            InvoiceErrorCodes.InvoiceStateConflict,
            InvoicePolicy.FindVoidRejection(
                SimulatedInvoiceStatus.Issued,
                orderFullyCancelled: false,
                hasSettledRefund: false));

    [Fact]
    public void AnAllowedInvoiceCannotBeVoided()
    {
        Assert.Equal(
            InvoiceErrorCodes.InvoiceStateConflict,
            InvoicePolicy.FindVoidRejection(SimulatedInvoiceStatus.PartiallyAllowed, true, false));
        Assert.Equal(
            InvoiceErrorCodes.InvoiceStateConflict,
            InvoicePolicy.FindVoidRejection(SimulatedInvoiceStatus.FullyAllowed, true, false));
    }

    [Fact]
    public void CalculatedAmountsSatisfyTheEntityInvariants()
    {
        var result = Calculate(
        [
            Merchandise(ItemA, quantity: 2, gross: 2000m),
            NonMerchandise(InvoiceLineKind.Shipping, "宅配運費", "SHIPPING", 150m),
        ]);

        var invoice = new SimulatedInvoice(Guid.NewGuid(), new SimulatedInvoiceCreation(
            1, DemoInvoiceNumber.Format(CreatedAtUtc, 1), SimulatedInvoiceBuyerType.Individual,
            "buyer@example.com", null, null, null, null,
            result.NetAmount, result.TaxAmount, result.IssuedAmount), CreatedAtUtc);

        Assert.Equal("DEMO-202608-000001", invoice.InvoiceNumber);
        Assert.Equal(SimulatedInvoice.RequiredDemoMarker, invoice.DemoMarker);

        invoice.Issue(CreatedAtUtc.AddMinutes(1));
        Assert.Equal(SimulatedInvoiceStatus.Issued, invoice.Status);
    }

    private static string? VoidRejection(bool hasSettledRefund) =>
        InvoicePolicy.FindVoidRejection(
            SimulatedInvoiceStatus.Issued,
            orderFullyCancelled: true,
            hasSettledRefund);

    private static InvoiceCalculationResult Calculate(
        IReadOnlyList<InvoiceOrderLine> lines,
        InvoiceIssuanceTrigger trigger = InvoiceIssuanceTrigger.OnlinePaymentSucceeded) =>
        InvoiceCalculator.Calculate(new InvoiceIssuanceRequest(
            trigger, OrderAlreadyHasInvoice: false, lines));

    private static InvoiceOrderLine Merchandise(Guid orderItemPublicId, int quantity, decimal gross) =>
        new(
            orderItemPublicId,
            InvoiceLineKind.Merchandise,
            "測試商品",
            "SKU-1",
            quantity,
            quantity == 0 ? gross : gross / quantity,
            0m,
            gross);

    private static InvoiceOrderLine NonMerchandise(
        InvoiceLineKind kind,
        string name,
        string code,
        decimal gross) =>
        new(null, kind, name, code, 1, gross, 0m, gross);
}
