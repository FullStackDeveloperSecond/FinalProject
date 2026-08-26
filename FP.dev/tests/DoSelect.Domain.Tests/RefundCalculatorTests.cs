using DoSelect.Domain.Refunds;

namespace DoSelect.Domain.Tests;

public sealed class RefundCalculatorTests
{
    private static readonly Guid LineA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LineB = new("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData(ReturnReason.CoolingOff, ReturnShippingBearer.Merchant)]
    [InlineData(ReturnReason.Defective, ReturnShippingBearer.Merchant)]
    [InlineData(ReturnReason.WrongItem, ReturnShippingBearer.Merchant)]
    [InlineData(ReturnReason.ShippingDamage, ReturnShippingBearer.Merchant)]
    [InlineData(ReturnReason.Warranty, ReturnShippingBearer.Merchant)]
    [InlineData(ReturnReason.LateNonDefectiveGoodwill, ReturnShippingBearer.ManualReview)]
    [InlineData(ReturnReason.CustomerProcessDeviation, ReturnShippingBearer.Customer)]
    public void ReturnShippingBearer_FollowsThePolicyTable(
        ReturnReason reason,
        ReturnShippingBearer expected) =>
        Assert.Equal(expected, RefundPolicy.ResolveReturnShippingBearer(reason));

    [Theory]
    [InlineData(AssemblyFeeDisposition.NotStarted, true)]
    [InlineData(AssemblyFeeDisposition.MerchantCancelled, true)]
    [InlineData(AssemblyFeeDisposition.AssemblyFault, true)]
    [InlineData(AssemblyFeeDisposition.MerchantFaultWholeUnit, true)]
    [InlineData(AssemblyFeeDisposition.CompletedPartialReturn, false)]
    [InlineData(AssemblyFeeDisposition.NotApplicable, false)]
    public void AssemblyFeeRefund_FollowsThePolicyTable(
        AssemblyFeeDisposition disposition,
        bool expected) =>
        Assert.Equal(expected, RefundPolicy.RefundsAssemblyFee(disposition));

    [Fact]
    public void PartialReturn_RefundsTheItemNetOfItsDiscountShare()
    {
        var result = Calculate(
            Snapshot(shippingFeePaid: 150m, couponDiscountTotal: 300m, couponMinimumSpend: null),
            [new RefundLineRequest(LineA, 1)]);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Items);
        Assert.Equal(1000m, item.GrossAmount);
        Assert.Equal(100m, item.DiscountShare);
        Assert.Equal(900m, item.NetAmount);
        Assert.Equal(900m, result.NetRefundAmount);
    }

    [Theory]
    [InlineData(RefundAllocationType.ItemRefund, RefundAllocationDirection.Credit)]
    [InlineData(RefundAllocationType.OriginalShipping, RefundAllocationDirection.Credit)]
    [InlineData(RefundAllocationType.ReturnShipping, RefundAllocationDirection.Credit)]
    [InlineData(RefundAllocationType.AssemblyFee, RefundAllocationDirection.Credit)]
    [InlineData(RefundAllocationType.DiscountClawback, RefundAllocationDirection.Debit)]
    [InlineData(RefundAllocationType.ShippingClawback, RefundAllocationDirection.Debit)]
    public void AllocationDirectionFollowsTheType(
        RefundAllocationType type,
        RefundAllocationDirection expected) =>
        Assert.Equal(expected, RefundPolicy.DirectionOf(type));

    [Fact]
    public void OtherAdjustmentIsBannedInTheFirstVersion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RefundPolicy.DirectionOf(RefundAllocationType.OtherAdjustment));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RefundAllocation(
            Guid.NewGuid(), 1, 1, RefundAllocationType.OtherAdjustment, 100m, 0m,
            new DateTime(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void ItemRefundAllocationRequiresAnOrderItemAndPositiveQuantity()
    {
        var occurredAtUtc = new DateTime(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);

        Assert.Throws<ArgumentException>(() => new RefundAllocation(
            Guid.NewGuid(), 1, null, RefundAllocationType.ItemRefund, 100m, 0m,
            occurredAtUtc, quantity: 1));
        Assert.Throws<ArgumentException>(() => new RefundAllocation(
            Guid.NewGuid(), 1, 1, RefundAllocationType.ItemRefund, 100m, 0m,
            occurredAtUtc, quantity: null));

        var allocation = new RefundAllocation(
            Guid.NewGuid(), 1, 1, RefundAllocationType.ItemRefund, 100m, 0m,
            occurredAtUtc, quantity: 2);

        Assert.Equal(2, allocation.Quantity);
    }

    [Fact]
    public void NonItemAllocationRejectsAnOrderItemOrQuantity()
    {
        var occurredAtUtc = new DateTime(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);

        Assert.Throws<ArgumentException>(() => new RefundAllocation(
            Guid.NewGuid(), 1, 1, RefundAllocationType.ShippingClawback, 100m, 0m,
            occurredAtUtc));
        Assert.Throws<ArgumentException>(() => new RefundAllocation(
            Guid.NewGuid(), 1, null, RefundAllocationType.ShippingClawback, 100m, 0m,
            occurredAtUtc, quantity: 1));
    }

    [Fact]
    public void SuccessfulCalculationBuildsImmutableAllocationDraftsWithItemQuantities()
    {
        var result = Calculate(
            Snapshot(
                shippingFeePaid: 0m,
                shippingMethodBaseFee: 150m,
                freeShippingThreshold: 5000m,
                couponMinimumSpend: null),
            [new RefundLineRequest(LineA, 1), new RefundLineRequest(LineB, 1)]);

        var drafts = RefundAllocationDrafts.From(result);

        Assert.Equal(3, drafts.Count);
        Assert.Collection(
            drafts.Where(draft => draft.Type == RefundAllocationType.ItemRefund)
                .OrderBy(draft => draft.OrderItemPublicId),
            first =>
            {
                Assert.Equal(LineA, first.OrderItemPublicId);
                Assert.Equal(1, first.Quantity);
                Assert.Equal(900m, first.Amount);
                Assert.Equal(100m, first.OriginalDiscountAllocation);
            },
            second =>
            {
                Assert.Equal(LineB, second.OrderItemPublicId);
                Assert.Equal(1, second.Quantity);
                Assert.Equal(1900m, second.Amount);
                Assert.Equal(100m, second.OriginalDiscountAllocation);
            });
        var clawback = drafts.Single(draft =>
            draft.Type == RefundAllocationType.ShippingClawback);
        Assert.Null(clawback.OrderItemPublicId);
        Assert.Null(clawback.Quantity);
        Assert.Equal(150m, clawback.Amount);
    }

    [Fact]
    public void EveryComponentAmountIsPositive()
    {
        var result = Calculate(
            Snapshot(
                shippingFeePaid: 0m,
                shippingMethodBaseFee: 150m,
                freeShippingThreshold: 5000m,
                assemblyFee: 300m,
                couponDiscountTotal: 300m,
                couponMinimumSpend: 3500m),
            [new RefundLineRequest(LineA, 1)],
            reason: ReturnReason.Defective,
            assemblyDisposition: AssemblyFeeDisposition.AssemblyFault,
            returnShippingCost: 80m);

        Assert.True(result.IsSuccess);
        Assert.All(result.Components, component => Assert.True(component.Amount > 0m));
    }

    [Fact]
    public void PartialReturn_DoesNotRefundTheOriginalShipping()
    {
        var result = Calculate(
            Snapshot(shippingFeePaid: 150m, couponMinimumSpend: null),
            [new RefundLineRequest(LineA, 1)]);

        Assert.DoesNotContain(
            result.Components,
            component => component.Type == RefundAllocationType.OriginalShipping);
    }

    [Fact]
    public void FullReturn_RefundsTheOriginalShippingActuallyPaid()
    {
        var result = Calculate(
            Snapshot(shippingFeePaid: 150m, couponMinimumSpend: null),
            [new RefundLineRequest(LineA, 2), new RefundLineRequest(LineB, 1)]);

        Assert.True(result.IsSuccess);
        Assert.Equal(150m, ComponentAmount(result, RefundAllocationType.OriginalShipping));
    }

    [Fact]
    public void FullReturn_OnAFreeShippingOrder_DoesNotRecollectTheWaivedFee()
    {
        var result = Calculate(
            Snapshot(shippingFeePaid: 0m, freeShippingThreshold: 5000m, couponMinimumSpend: null),
            [new RefundLineRequest(LineA, 2), new RefundLineRequest(LineB, 1)]);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(
            result.Components,
            component => component.Type == RefundAllocationType.ShippingClawback);
    }

    [Fact]
    public void PartialReturn_BelowTheFreeShippingThreshold_ClawsBackTheShippingFee()
    {
        // 保留 1 個 LineA 共 1,000 元，未達 5,000 免運門檻，因此扣回 150 元運費。
        var result = Calculate(
            Snapshot(
                shippingFeePaid: 0m,
                shippingMethodBaseFee: 150m,
                freeShippingThreshold: 5000m,
                couponMinimumSpend: null),
            [new RefundLineRequest(LineA, 1), new RefundLineRequest(LineB, 1)]);

        Assert.True(result.IsSuccess);
        Assert.Equal(150m, ComponentAmount(result, RefundAllocationType.ShippingClawback));
        Assert.Equal(
            RefundAllocationDirection.Debit,
            result.Components.Single(component =>
                component.Type == RefundAllocationType.ShippingClawback).Direction);
    }

    [Fact]
    public void PartialReturn_StillAboveTheThreshold_KeepsTheFreeShipping()
    {
        var result = Calculate(
            Snapshot(
                shippingFeePaid: 0m,
                shippingMethodBaseFee: 150m,
                freeShippingThreshold: 1000m,
                couponMinimumSpend: null),
            [new RefundLineRequest(LineA, 1)]);

        Assert.DoesNotContain(
            result.Components,
            component => component.Type == RefundAllocationType.ShippingClawback);
    }

    [Fact]
    public void PartialReturn_BelowTheCouponMinimumSpend_ClawsBackTheRetainedDiscount()
    {
        // 退貨後保留 3,000 元未達 3,500 門檻。折扣總額 300，退貨品項已扣 100，
        // 保留商品上的 200 必須追回。
        var result = Calculate(
            Snapshot(couponDiscountTotal: 300m, couponMinimumSpend: 3500m),
            [new RefundLineRequest(LineA, 1)]);

        Assert.True(result.IsSuccess);
        Assert.Equal(200m, ComponentAmount(result, RefundAllocationType.DiscountClawback));
        Assert.Equal(700m, result.NetRefundAmount);
    }

    [Fact]
    public void PartialReturn_StillAboveTheCouponMinimumSpend_KeepsTheDiscount()
    {
        var result = Calculate(
            Snapshot(couponDiscountTotal: 300m, couponMinimumSpend: 1000m),
            [new RefundLineRequest(LineA, 1)]);

        Assert.DoesNotContain(
            result.Components,
            component => component.Type == RefundAllocationType.DiscountClawback);
    }

    [Fact]
    public void AssemblyFee_IsNotRefundedWhenOnlyOnePartComesBack()
    {
        var result = Calculate(
            Snapshot(assemblyFee: 300m, couponMinimumSpend: null),
            [new RefundLineRequest(LineA, 1)],
            assemblyDisposition: AssemblyFeeDisposition.CompletedPartialReturn);

        Assert.DoesNotContain(
            result.Components,
            component => component.Type == RefundAllocationType.AssemblyFee);
    }

    [Fact]
    public void AssemblyFee_IsRefundedWhenTheWholeUnitComesBackOnMerchantFault()
    {
        var result = Calculate(
            Snapshot(assemblyFee: 300m, couponMinimumSpend: null),
            [new RefundLineRequest(LineA, 2), new RefundLineRequest(LineB, 1)],
            assemblyDisposition: AssemblyFeeDisposition.MerchantFaultWholeUnit);

        Assert.Equal(300m, ComponentAmount(result, RefundAllocationType.AssemblyFee));
    }

    [Fact]
    public void ReturnShipping_IsRefundedOnlyWhenTheMerchantBearsIt()
    {
        var merchant = Calculate(
            Snapshot(couponMinimumSpend: null),
            [new RefundLineRequest(LineA, 1)],
            reason: ReturnReason.Defective,
            returnShippingCost: 80m);

        var customer = Calculate(
            Snapshot(couponMinimumSpend: null),
            [new RefundLineRequest(LineA, 1)],
            reason: ReturnReason.CustomerProcessDeviation,
            returnShippingCost: 80m);

        Assert.Equal(80m, ComponentAmount(merchant, RefundAllocationType.ReturnShipping));
        Assert.DoesNotContain(
            customer.Components,
            component => component.Type == RefundAllocationType.ReturnShipping);
    }

    [Fact]
    public void GoodwillReturn_FlagsManualReviewWithoutGuessingTheBearer()
    {
        var result = Calculate(
            Snapshot(couponMinimumSpend: null),
            [new RefundLineRequest(LineA, 1)],
            reason: ReturnReason.LateNonDefectiveGoodwill,
            returnShippingCost: 80m);

        Assert.True(result.IsSuccess);
        Assert.True(result.RequiresManualReview);
        Assert.DoesNotContain(
            result.Components,
            component => component.Type == RefundAllocationType.ReturnShipping);
    }

    [Fact]
    public void ComponentAmounts_AlwaysSumToTheNetRefund()
    {
        var result = Calculate(
            Snapshot(
                shippingFeePaid: 0m,
                shippingMethodBaseFee: 150m,
                freeShippingThreshold: 5000m,
                assemblyFee: 300m,
                couponDiscountTotal: 300m,
                couponMinimumSpend: 3000m),
            [new RefundLineRequest(LineA, 2)],
            reason: ReturnReason.Defective,
            assemblyDisposition: AssemblyFeeDisposition.AssemblyFault,
            returnShippingCost: 80m);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            result.NetRefundAmount,
            result.Components.Sum(component =>
                component.Direction == RefundAllocationDirection.Credit
                    ? component.Amount
                    : -component.Amount));
    }

    [Fact]
    public void SplitReturnsOfOneLine_AllocateTheDiscountExactlyOnce()
    {
        var line = new RefundOrderLine(LineA, Quantity: 3, AlreadyReturnedQuantity: 0,
            FinalUnitPrice: 100m, DiscountAllocation: 10m, IsCouponEligible: true);

        var first = RefundCalculator.Calculate(new RefundCalculationRequest(
            SnapshotOf([line]), [new RefundLineRequest(LineA, 1)],
            ReturnReason.Defective, AssemblyFeeDisposition.NotApplicable, 0m));

        var second = RefundCalculator.Calculate(new RefundCalculationRequest(
            SnapshotOf([line with { AlreadyReturnedQuantity = 1 }]),
            [new RefundLineRequest(LineA, 2)],
            ReturnReason.Defective, AssemblyFeeDisposition.NotApplicable, 0m));

        var firstShare = first.Items.Single().DiscountShare;
        var secondShare = second.Items.Single().DiscountShare;

        Assert.Equal(3.33m, firstShare);
        Assert.Equal(6.67m, secondShare);
        Assert.Equal(10m, firstShare + secondShare);
    }

    [Fact]
    public void RequestingMoreThanRemains_IsRejected()
    {
        var result = Calculate(
            Snapshot(couponMinimumSpend: null),
            [new RefundLineRequest(LineA, 3)]);

        Assert.Equal(RefundErrorCodes.ReturnQuantityExceeded, result.ErrorCode);
    }

    [Fact]
    public void RequestingTheSameLineTwice_IsRejected()
    {
        var result = Calculate(
            Snapshot(couponMinimumSpend: null),
            [new RefundLineRequest(LineA, 1), new RefundLineRequest(LineA, 1)]);

        Assert.Equal(RefundErrorCodes.ReturnQuantityExceeded, result.ErrorCode);
    }

    [Fact]
    public void RequestingAnUnknownLine_IsNotFound()
    {
        var result = Calculate(
            Snapshot(couponMinimumSpend: null),
            [new RefundLineRequest(Guid.NewGuid(), 1)]);

        Assert.Equal(RefundErrorCodes.ResourceNotFound, result.ErrorCode);
    }

    [Fact]
    public void EmptyRequest_IsRejected()
    {
        var result = Calculate(Snapshot(couponMinimumSpend: null), []);

        Assert.Equal(RefundErrorCodes.ReturnQuantityExceeded, result.ErrorCode);
    }

    [Fact]
    public void WhenTheClawbackSwallowsTheWholeRefund_TheAmountIsRejected()
    {
        var line = new RefundOrderLine(LineA, Quantity: 1, AlreadyReturnedQuantity: 0,
            FinalUnitPrice: 100m, DiscountAllocation: 0m, IsCouponEligible: true);

        var result = RefundCalculator.Calculate(new RefundCalculationRequest(
            SnapshotOf([line], couponDiscountTotal: 500m, couponMinimumSpend: 3000m),
            [new RefundLineRequest(LineA, 1)],
            ReturnReason.Defective,
            AssemblyFeeDisposition.NotApplicable,
            0m));

        Assert.Equal(RefundErrorCodes.RefundAmountExceeded, result.ErrorCode);
    }

    private static decimal ComponentAmount(
        RefundCalculationResult result,
        RefundAllocationType type) =>
        result.Components.Single(component => component.Type == type).Amount;

    private static RefundCalculationResult Calculate(
        RefundOrderSnapshot order,
        IReadOnlyList<RefundLineRequest> lines,
        ReturnReason reason = ReturnReason.CoolingOff,
        AssemblyFeeDisposition assemblyDisposition = AssemblyFeeDisposition.NotApplicable,
        decimal returnShippingCost = 0m) =>
        RefundCalculator.Calculate(new RefundCalculationRequest(
            order, lines, reason, assemblyDisposition, returnShippingCost));

    /// <summary>LineA 1,000 x2 分攤折扣 200；LineB 2,000 x1 分攤折扣 100。</summary>
    private static RefundOrderSnapshot Snapshot(
        decimal shippingFeePaid = 0m,
        decimal shippingMethodBaseFee = 150m,
        decimal? freeShippingThreshold = null,
        decimal assemblyFee = 0m,
        decimal couponDiscountTotal = 300m,
        decimal? couponMinimumSpend = 3000m) =>
        SnapshotOf(
            [
                new RefundOrderLine(LineA, 2, 0, 1000m, 200m, IsCouponEligible: true),
                new RefundOrderLine(LineB, 1, 0, 2000m, 100m, IsCouponEligible: true),
            ],
            shippingFeePaid,
            shippingMethodBaseFee,
            freeShippingThreshold,
            assemblyFee,
            couponDiscountTotal,
            couponMinimumSpend);

    private static RefundOrderSnapshot SnapshotOf(
        IReadOnlyList<RefundOrderLine> lines,
        decimal shippingFeePaid = 0m,
        decimal shippingMethodBaseFee = 150m,
        decimal? freeShippingThreshold = null,
        decimal assemblyFee = 0m,
        decimal couponDiscountTotal = 0m,
        decimal? couponMinimumSpend = null) =>
        new(
            lines,
            shippingFeePaid,
            shippingMethodBaseFee,
            freeShippingThreshold,
            assemblyFee,
            couponDiscountTotal,
            CouponEligibleSubtotal: lines.Sum(line => line.FinalUnitPrice * line.Quantity),
            couponMinimumSpend);
}
