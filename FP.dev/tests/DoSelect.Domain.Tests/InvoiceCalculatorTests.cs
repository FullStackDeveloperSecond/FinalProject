using DoSelect.Domain.Invoicing;

namespace DoSelect.Domain.Tests;

public sealed class InvoiceCalculatorTests
{
    private static readonly Guid ItemA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ItemB = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void TaxIsBackedOutOfTheGrossAmountToWholeDollars()
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
    public void LineAmountsSumToTheInvoiceTotals()
    {
        var result = Calculate(
        [
            Merchandise(ItemA, quantity: 2, gross: 2000m),
            Merchandise(ItemB, quantity: 1, gross: 899m),
            new InvoiceOrderLine(null, InvoiceLineKind.Shipping, "宅配運費", "SHIPPING", 1, 150m, 0m, 150m),
            new InvoiceOrderLine(null, InvoiceLineKind.AssemblyFee, "組裝費", "ASSEMBLY", 1, 300m, 0m, 300m),
        ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Lines.Count);
        Assert.Equal(result.NetAmount, result.Lines.Sum(line => line.NetAmount));
        Assert.Equal(result.TaxAmount, result.Lines.Sum(line => line.TaxAmount));
        Assert.Equal(3349m, result.IssuedAmount);
        Assert.Equal(result.IssuedAmount, result.NetAmount + result.TaxAmount);
    }

    [Fact]
    public void ShippingAndAssemblyLinesCarryNoOrderItem()
    {
        var result = Calculate(
        [
            Merchandise(ItemA, quantity: 1, gross: 1000m),
            new InvoiceOrderLine(null, InvoiceLineKind.Shipping, "宅配運費", "SHIPPING", 1, 150m, 0m, 150m),
        ]);

        Assert.Equal(
            ItemA,
            result.Lines.Single(line => line.Kind == InvoiceLineKind.Merchandise).OrderItemPublicId);
        Assert.Null(result.Lines.Single(line => line.Kind == InvoiceLineKind.Shipping).OrderItemPublicId);
    }

    [Fact]
    public void ZeroAmountLinesAreLeftOffTheInvoice()
    {
        var result = Calculate(
        [
            Merchandise(ItemA, quantity: 1, gross: 1000m),
            new InvoiceOrderLine(null, InvoiceLineKind.Shipping, "免運", "SHIPPING", 1, 0m, 0m, 0m),
        ]);

        Assert.Single(result.Lines);
        Assert.Equal(1000m, result.IssuedAmount);
    }

    [Theory]
    [InlineData(InvoiceIssuanceTrigger.OnlinePaymentSucceeded)]
    [InlineData(InvoiceIssuanceTrigger.CashOnDeliveryCollected)]
    public void PaidOrders_AreInvoiceable(InvoiceIssuanceTrigger trigger)
    {
        var result = Calculate([Merchandise(ItemA, quantity: 1, gross: 1000m)], trigger);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void UnpaidOrders_AreNotInvoiced()
    {
        var result = Calculate(
            [Merchandise(ItemA, quantity: 1, gross: 1000m)],
            InvoiceIssuanceTrigger.NotPaid);

        Assert.Equal(InvoiceIssuanceRejection.OrderNotPaid, result.Rejection);
    }

    [Fact]
    public void CancelledOrders_AreNotInvoiced()
    {
        var result = Calculate(
            [Merchandise(ItemA, quantity: 1, gross: 1000m)],
            InvoiceIssuanceTrigger.OrderCancelled);

        Assert.Equal(InvoiceIssuanceRejection.OrderCancelled, result.Rejection);
    }

    [Fact]
    public void AnOrderNeverGetsASecondInvoice()
    {
        var result = InvoiceCalculator.Calculate(new InvoiceIssuanceRequest(
            InvoiceIssuanceTrigger.OnlinePaymentSucceeded,
            OrderAlreadyHasInvoice: true,
            [Merchandise(ItemA, quantity: 1, gross: 1000m)]));

        Assert.Equal(InvoiceIssuanceRejection.AlreadyIssued, result.Rejection);
    }

    [Fact]
    public void AnOrderWithoutAnyChargeableLine_IsNotInvoiced()
    {
        var result = Calculate([Merchandise(ItemA, quantity: 1, gross: 0m)]);

        Assert.Equal(InvoiceIssuanceRejection.NoInvoiceableLines, result.Rejection);
    }

    [Fact]
    public void AMerchandiseLineWithoutItsOrderItem_IsAProgrammingError() =>
        Assert.Throws<ArgumentException>(() => Calculate(
        [
            new InvoiceOrderLine(null, InvoiceLineKind.Merchandise, "商品", "SKU-1", 1, 1000m, 0m, 1000m),
        ]));

    [Fact]
    public void AMalformedLine_IsAProgrammingError() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(
        [
            Merchandise(ItemA, quantity: 0, gross: 1000m),
        ]));

    [Fact]
    public void OnlyAnIssuedInvoiceCanBeVoided()
    {
        Assert.Equal(
            InvoiceVoidRejection.NotIssued,
            InvoicePolicy.FindVoidRejection(SimulatedInvoiceStatus.Pending, true, false));
        Assert.Equal(
            InvoiceVoidRejection.NotIssued,
            InvoicePolicy.FindVoidRejection(SimulatedInvoiceStatus.Voided, true, false));
        Assert.Equal(
            InvoiceVoidRejection.None,
            InvoicePolicy.FindVoidRejection(SimulatedInvoiceStatus.Issued, true, false));
    }

    [Fact]
    public void ASettledRefundForcesAnAllowanceInsteadOfAVoid() =>
        Assert.Equal(
            InvoiceVoidRejection.RefundAlreadySettled,
            InvoicePolicy.FindVoidRejection(
                SimulatedInvoiceStatus.Issued,
                orderFullyCancelled: true,
                hasSettledRefund: true));

    [Fact]
    public void APartiallyCancelledOrderCannotVoidItsInvoice() =>
        Assert.Equal(
            InvoiceVoidRejection.OrderNotFullyCancelled,
            InvoicePolicy.FindVoidRejection(
                SimulatedInvoiceStatus.Issued,
                orderFullyCancelled: false,
                hasSettledRefund: false));

    [Fact]
    public void AnAllowedInvoiceCannotBeVoided()
    {
        Assert.Equal(
            InvoiceVoidRejection.NotIssued,
            InvoicePolicy.FindVoidRejection(SimulatedInvoiceStatus.PartiallyAllowed, true, false));
        Assert.Equal(
            InvoiceVoidRejection.NotIssued,
            InvoicePolicy.FindVoidRejection(SimulatedInvoiceStatus.FullyAllowed, true, false));
    }

    [Fact]
    public void CalculatedAmountsSatisfyTheEntityInvariants()
    {
        var result = Calculate(
        [
            Merchandise(ItemA, quantity: 2, gross: 2000m),
            new InvoiceOrderLine(null, InvoiceLineKind.Shipping, "宅配運費", "SHIPPING", 1, 150m, 0m, 150m),
        ]);

        var invoice = new SimulatedInvoice(Guid.NewGuid(), new SimulatedInvoiceCreation(
            1, "DEMO-2026-000001", SimulatedInvoiceBuyerType.Individual, "buyer@example.com",
            null, null, null, null,
            result.NetAmount, result.TaxAmount, result.IssuedAmount), CreatedAtUtc);

        Assert.Equal(SimulatedInvoice.RequiredDemoMarker, invoice.DemoMarker);
        Assert.Equal(SimulatedInvoiceStatus.Pending, invoice.Status);

        invoice.Issue(CreatedAtUtc.AddMinutes(1));
        Assert.Equal(SimulatedInvoiceStatus.Issued, invoice.Status);
    }

    private static readonly DateTime CreatedAtUtc = new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);

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
}
