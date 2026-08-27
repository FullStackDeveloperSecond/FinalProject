using DoSelect.Application.Returns;
using DoSelect.Domain.Refunds;

namespace DoSelect.Application.Tests.Returns;

public sealed class ReturnRefundInputPolicyTests
{
    [Fact]
    public void RefundSnapshotUnavailable_UsesTheRegisteredStableCode() =>
        Assert.Equal("refund_snapshot_unavailable", RefundErrorCodes.RefundSnapshotUnavailable);

    [Theory]
    [InlineData("CoolingOff", ReturnReason.CoolingOff)]
    [InlineData("Defective", ReturnReason.Defective)]
    [InlineData("WrongItem", ReturnReason.WrongItem)]
    [InlineData("ShippingDamage", ReturnReason.ShippingDamage)]
    [InlineData("Warranty", ReturnReason.Warranty)]
    public void TryMapRefundReason_MapsEveryCanonicalReturnReason(string reasonCode, ReturnReason expected)
    {
        Assert.True(ReturnEligibilityPolicy.TryMapRefundReason(reasonCode, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("LateNonDefectiveGoodwill")]
    [InlineData("CustomerProcessDeviation")]
    [InlineData("defective")]
    public void TryMapRefundReason_RejectsCodesOutsideTheReturnContract(string reasonCode) =>
        Assert.False(ReturnEligibilityPolicy.TryMapRefundReason(reasonCode, out _));
}
