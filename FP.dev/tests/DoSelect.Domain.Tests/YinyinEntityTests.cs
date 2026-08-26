using System.Reflection;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Promotions;
using DoSelect.Domain.Refunds;

namespace DoSelect.Domain.Tests;

public sealed class YinyinEntityTests
{
    private static readonly DateTime CreatedAtUtc =
        new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);

    public static TheoryData<Type> EncapsulatedTypes => new()
    {
        typeof(Coupon), typeof(CouponRedemption), typeof(OrderCoupon),
        typeof(CouponCategory), typeof(CouponProduct), typeof(CouponExcludedProduct),
        typeof(PaymentAttempt), typeof(PaymentEvent), typeof(Refund),
        typeof(RefundAllocation), typeof(SimulatedInvoice), typeof(SimulatedInvoiceItem),
        typeof(SimulatedInvoiceAllowance), typeof(SimulatedInvoiceAllowanceItem),
    };

    [Theory]
    [MemberData(nameof(EncapsulatedTypes))]
    public void Entity_DoesNotExposePublicPropertySetters(Type entityType)
    {
        var publicSetters = entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.SetMethod?.IsPublic == true)
            .Select(property => property.Name);

        Assert.Empty(publicSetters);
    }

    [Fact]
    public void Coupon_NormalizesCodeAndPercentageRange()
    {
        var coupon = new Coupon(Guid.NewGuid(), new CouponCreation(
            "  newuser  ", "新會員", CouponDiscountType.Percentage, 0.1m,
            1000m, 300m, CreatedAtUtc, CreatedAtUtc.AddDays(7), 100, 1,
            true, false, CouponScopeType.All), CreatedAtUtc);

        Assert.Equal("NEWUSER", coupon.Code);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Coupon(Guid.NewGuid(),
            new CouponCreation("BAD", "錯誤", CouponDiscountType.Percentage, 10m,
                null, null, CreatedAtUtc, CreatedAtUtc.AddDays(1), null, null,
                false, false, CouponScopeType.All), CreatedAtUtc));
    }

    [Fact]
    public void CouponRedemption_RequiresExactlyOneOwnerAndOnlyOneTerminalTransition()
    {
        Assert.Throws<ArgumentException>(() => new CouponRedemption(Guid.NewGuid(), 1, 1,
            "member", new byte[32], CreatedAtUtc, null, CreatedAtUtc));
        var redemption = new CouponRedemption(Guid.NewGuid(), 1, 1, "member", null,
            CreatedAtUtc, CreatedAtUtc.AddDays(3), CreatedAtUtc);

        redemption.Consume(CreatedAtUtc.AddHours(1));

        Assert.Equal(CouponRedemptionStatus.Consumed, redemption.Status);
        Assert.Throws<InvalidOperationException>(() => redemption.Release(CreatedAtUtc.AddHours(2)));
    }

    [Fact]
    public void OrderCouponStoresTheCheckoutMinimumSpendSnapshot()
    {
        var coupon = new OrderCoupon(
            Guid.NewGuid(), 1, 1, 1, "SAVE100", "滿額折抵",
            CouponDiscountType.FixedAmount, 1, 100m, 1000m, 100m, 1200m,
            false, CreatedAtUtc);

        Assert.Equal(1000m, coupon.MinimumSpendAmount);
    }

    [Fact]
    public void PaymentAttempt_EnforcesDocumentedStateMachine()
    {
        var attempt = new PaymentAttempt(Guid.NewGuid(), 1, PaymentMethod.CreditCard,
            1000m, "Demo", "pay-1", null, CreatedAtUtc);

        attempt.SetPaymentInstruction("ref-1", CreatedAtUtc.AddMinutes(1));
        attempt.Transition(PaymentAttemptStatus.Processing, CreatedAtUtc.AddMinutes(2));
        attempt.Transition(PaymentAttemptStatus.Paid, CreatedAtUtc.AddMinutes(3));

        Assert.Equal(PaymentAttemptStatus.Paid, attempt.Status);
        Assert.Throws<InvalidOperationException>(() =>
            attempt.Transition(PaymentAttemptStatus.Failed, CreatedAtUtc.AddMinutes(4), "late"));
    }

    [Fact]
    public void Refund_SeparatesApprovalExecutionAndSuccess()
    {
        var refund = new Refund(Guid.NewGuid(), 1, null, 1, "RF-1", 500m,
            "ReturnApproved", "member", "refund-1", CreatedAtUtc);

        refund.Approve(400m, "finance", CreatedAtUtc.AddMinutes(1));
        refund.BeginProcessing("executor", CreatedAtUtc.AddMinutes(2));
        refund.Complete(400m, CreatedAtUtc.AddMinutes(3));

        Assert.Equal("finance", refund.ApprovedBy);
        Assert.Equal("executor", refund.ExecutedByAdminUserId);
        Assert.Equal(RefundStatus.Succeeded, refund.Status);
    }

    [Fact]
    public void SimulatedInvoice_AlwaysCarriesNonTaxInvoiceMarker()
    {
        var invoice = new SimulatedInvoice(Guid.NewGuid(), new SimulatedInvoiceCreation(
            1, "INV-1", SimulatedInvoiceBuyerType.Individual, null, null, null,
            null, null, 952m, 48m, 1000m), CreatedAtUtc);

        Assert.Equal(SimulatedInvoice.RequiredDemoMarker, invoice.DemoMarker);
        Assert.Equal("TWD", invoice.Currency);
    }
}
