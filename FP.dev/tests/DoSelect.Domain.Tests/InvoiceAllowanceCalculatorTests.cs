using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Refunds;

namespace DoSelect.Domain.Tests;

public sealed class InvoiceAllowanceCalculatorTests
{
    private static readonly DateTime IssuedAtUtc = new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ItemA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ItemB = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ItemC = new("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData(RefundAllocationType.ItemRefund, true)]
    [InlineData(RefundAllocationType.OriginalShipping, true)]
    [InlineData(RefundAllocationType.AssemblyFee, true)]
    [InlineData(RefundAllocationType.ReturnShipping, false)]
    [InlineData(RefundAllocationType.DiscountClawback, false)]
    [InlineData(RefundAllocationType.ShippingClawback, false)]
    public void OnlyAmountsTheInvoiceActuallyChargedBecomeAllowanceLines(
        RefundAllocationType allocationType,
        bool expected) =>
        Assert.Equal(expected, InvoiceAllowancePolicy.CreatesAllowanceLine(allocationType));

    [Fact]
    public void OtherAdjustmentIsRejectedInsteadOfSilentlyFiltered() =>
        Assert.Throws<InvoiceAllowanceSourceException>(() =>
            InvoiceAllowancePolicy.CreatesAllowanceLine(RefundAllocationType.OtherAdjustment));

    [Fact]
    public void EveryAllocationTypeHasARuling()
    {
        // 新增分攤類型時必須同時裁定折讓行為，不能靜默落到預設值。
        foreach (var allocationType in Enum.GetValues<RefundAllocationType>()
                     .Where(value => value != RefundAllocationType.OtherAdjustment))
        {
            InvoiceAllowancePolicy.CreatesAllowanceLine(allocationType);
        }
    }

    [Theory]
    [InlineData(RefundAllocationType.ItemRefund, true)]
    [InlineData(RefundAllocationType.OriginalShipping, false)]
    [InlineData(RefundAllocationType.AssemblyFee, false)]
    public void OnlyItemRefundsMapToAnOrderItem(
        RefundAllocationType allocationType,
        bool expected) =>
        Assert.Equal(expected, InvoiceAllowancePolicy.MapsToAnOrderItem(allocationType));

    [Fact]
    public void AMerchandiseLineIsIdentifiedByItsOrderItem() =>
        Assert.Equal(
            InvoiceLineKind.Merchandise,
            InvoiceLineSkuCodes.ResolveKind(orderItemId: 7L, skuCodeSnapshot: "SKU-1"));

    [Fact]
    public void AShippingLineIsIdentifiedByItsReservedSkuCode() =>
        Assert.Equal(
            InvoiceLineKind.Shipping,
            InvoiceLineSkuCodes.ResolveKind(null, InvoiceLineSkuCodes.Shipping));

    [Fact]
    public void AnAssemblyFeeLineIsIdentifiedByItsReservedSkuCode() =>
        Assert.Equal(
            InvoiceLineKind.AssemblyFee,
            InvoiceLineSkuCodes.ResolveKind(null, InvoiceLineSkuCodes.AssemblyFee));

    [Theory]
    [InlineData("SHIPPING")]
    [InlineData("__INVOICE_UNKNOWN__")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnknownNonMerchandiseLineIsRejected(string? skuCodeSnapshot) =>
        // 靜默略過會讓折讓金額短少卻沒有任何跡象，歸類成別的種類則會扭曲財務歷史。
        Assert.Throws<ArgumentException>(() =>
            InvoiceLineSkuCodes.ResolveKind(null, skuCodeSnapshot));

    [Theory]
    [InlineData("__INVOICE_SHIPPING__")]
    [InlineData("__INVOICE_ASSEMBLY_FEE__")]
    public void AMerchandiseLineCannotBorrowAReservedSkuCode(string skuCodeSnapshot) =>
        Assert.Throws<ArgumentException>(() =>
            InvoiceLineSkuCodes.ResolveKind(orderItemId: 7L, skuCodeSnapshot));

    [Theory]
    [InlineData(InvoiceLineKind.Merchandise, "merchandise")]
    [InlineData(InvoiceLineKind.Shipping, "shipping")]
    [InlineData(InvoiceLineKind.AssemblyFee, "assemblyFee")]
    public void ThePublicKindNeverLeaksTheReservedSkuCode(
        InvoiceLineKind kind,
        string expected)
    {
        var publicKind = InvoiceLineSkuCodes.ToPublicKind(kind);

        Assert.Equal(expected, publicKind);
        Assert.DoesNotContain("__", publicKind, StringComparison.Ordinal);
    }

    [Fact]
    public void ATinyLineNextToALargeOneNeverProducesNegativeTax()
    {
        // alex 在 review 給的實例：分攤未稅時 0.53 這列會取整成 Net=1，
        // 再以 Gross - Net 求稅額就變成 -0.47。分攤稅額才不會出現這種明細。
        var result = Calculate(
            [Capacity(ItemA, 1, 0.53m), Capacity(ItemB, 1, 1000m)],
            [new RefundedInvoiceLine(ItemA, 1, 0.53m), new RefundedInvoiceLine(ItemB, 1, 1000m)]);

        Assert.True(result.IsSuccess);
        Assert.All(result.Lines, line =>
        {
            Assert.InRange(line.TaxAmount, 0m, line.GrossAmount);
            Assert.InRange(line.NetAmount, 0m, line.GrossAmount);
            Assert.Equal(line.GrossAmount, line.NetAmount + line.TaxAmount);
        });
    }

    [Fact]
    public void TheHeaderIsWholeTwdAndTheRoundingAdjustmentRecordsTheDifference()
    {
        var result = Calculate(
            [Capacity(ItemA, 1, 0.53m), Capacity(ItemB, 1, 1000m)],
            [new RefundedInvoiceLine(ItemA, 1, 0.53m), new RefundedInvoiceLine(ItemB, 1, 1000m)]);

        // 明細含稅合計 1000.53，表頭取整數元為 1001，差額記在 RoundingAdjustment。
        Assert.Equal(1001m, result.Amount);
        Assert.Equal(decimal.Truncate(result.Amount), result.Amount);
        Assert.Equal(0.47m, result.RoundingAdjustment);
        Assert.Equal(result.Amount, result.NetAmount + result.TaxAmount);
    }

    [Fact]
    public void LineTaxAlwaysSumsExactlyToTheHeaderTax()
    {
        var result = Calculate(
            [Capacity(ItemA, 1, 333.33m), Capacity(ItemB, 1, 333.33m), Capacity(ItemC, 1, 333.34m)],
            [
                new RefundedInvoiceLine(ItemA, 1, 333.33m),
                new RefundedInvoiceLine(ItemB, 1, 333.33m),
                new RefundedInvoiceLine(ItemC, 1, 333.34m),
            ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(result.TaxAmount, result.Lines.Sum(line => line.TaxAmount));
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("0.49")]
    [InlineData("0.50")]
    [InlineData("0.99")]
    public void AnExtremelySmallAllowanceStaysConsistent(string grossText)
    {
        var gross = decimal.Parse(grossText, System.Globalization.CultureInfo.InvariantCulture);

        var result = Calculate(
            [Capacity(ItemA, 1, gross)],
            [new RefundedInvoiceLine(ItemA, 1, gross)]);

        if (!result.IsSuccess)
        {
            // 取整後為零的折讓寫不進去，Amount > 0 是資料庫層的限制。
            Assert.Equal(InvoiceErrorCodes.InvoiceStateConflict, result.ErrorCode);
            return;
        }

        Assert.True(result.Amount > 0m);
        Assert.Equal(result.Amount, result.NetAmount + result.TaxAmount);
        Assert.All(result.Lines, line =>
        {
            Assert.InRange(line.TaxAmount, 0m, line.GrossAmount);
            Assert.InRange(line.NetAmount, 0m, line.GrossAmount);
        });
    }

    [Fact]
    public void TheAllowanceUsesTheSameRoundingContractAsTheInvoice()
    {
        // 折讓與發票共用 InvoiceCalculator 的分攤函式，兩邊的取位不可能漂移。
        var source = File.ReadAllText(CalculatorSourcePath());

        Assert.Contains("InvoiceCalculator.AllocateTaxByGrossShare", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MidpointRounding", source, StringComparison.Ordinal);
    }

    private static string CalculatorSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(
            directory!.FullName,
            "src", "backend", "DoSelect.Domain", "Invoicing", "InvoiceAllowanceCalculator.cs");
    }

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
        // 兩列各 1,000。表頭取整數元後回推未稅 1905、稅額 95。
        // 明細依 DEC-BATCH-017 可保留兩位小數，因此稅額平分為 47.5，未稅為 952.5，
        // 不需要把尾差硬塞給某一列 —— 合計仍精確等於表頭。
        var result = Calculate(
            [Capacity(ItemA, quantity: 1, gross: 1000m), Capacity(ItemB, quantity: 1, gross: 1000m)],
            [new RefundedInvoiceLine(ItemA, 1, 1000m), new RefundedInvoiceLine(ItemB, 1, 1000m)]);

        Assert.Equal(1905m, result.NetAmount);
        Assert.Equal(95m, result.TaxAmount);
        Assert.Equal([952.5m, 952.5m], result.Lines.Select(line => line.NetAmount));
        Assert.Equal([47.5m, 47.5m], result.Lines.Select(line => line.TaxAmount));
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
