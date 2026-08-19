using DoSelect.Application.Refunds;
using DoSelect.Domain.Refunds;

namespace DoSelect.Application.Tests;

public sealed class ExecuteRefundServiceTests
{
    private static readonly Guid RefundPublicId = new("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task ExecuteAsync_ApprovesAnApprovedRefundWithinTheBalance()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(Snapshot()));

        var result = await service.ExecuteAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.False(result.IsReplay);
        var plan = Assert.IsType<RefundExecutionPlan>(result.Plan);
        Assert.Equal(11L, plan.RefundId);
        Assert.Equal(400m, plan.Amount);
        Assert.Equal("finance-1", plan.ExecutedByAdminUserId);
        Assert.Equal("refund-1", plan.IdempotencyKey);
    }

    [Fact]
    public async Task ExecuteAsync_AllowsAnAmountExactlyEqualToTheBalance()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(
            Snapshot(approvedAmount: 400m, refundableBalance: 400m)));

        var result = await service.ExecuteAsync(Request());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsAnAmountAboveTheRefundableBalance()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(
            Snapshot(approvedAmount: 400m, refundableBalance: 399.99m)));

        var result = await service.ExecuteAsync(Request());

        Assert.Equal(RefundErrorCodes.RefundAmountExceeded, result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_ReplaysAnAlreadySucceededRefund()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(
            Snapshot(status: RefundStatus.Succeeded, succeededAmount: 400m)));

        var result = await service.ExecuteAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.True(result.IsReplay);
        Assert.Equal(400m, result.SettledAmount);
        Assert.Null(result.Plan);
    }

    [Theory]
    [InlineData(RefundStatus.PendingReview)]
    [InlineData(RefundStatus.Rejected)]
    [InlineData(RefundStatus.Processing)]
    [InlineData(RefundStatus.Cancelled)]
    [InlineData(RefundStatus.Failed)]
    public async Task ExecuteAsync_RejectsAnyStatusOtherThanApprovedOrSucceeded(RefundStatus status)
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(
            Snapshot(status: status)));

        var result = await service.ExecuteAsync(Request());

        Assert.Equal(RefundErrorCodes.RefundStateConflict, result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsAnApprovedRefundWithoutAnApprovedAmount()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(
            Snapshot(approvedAmount: null)));

        var result = await service.ExecuteAsync(Request());

        Assert.Equal(RefundErrorCodes.RefundStateConflict, result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNotFoundForAnUnknownRefund()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(snapshot: null));

        var result = await service.ExecuteAsync(Request());

        Assert.Equal(RefundErrorCodes.ResourceNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_RequiresAnIdempotencyKey() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new ExecuteRefundService(new FakeRefundExecutionReader(Snapshot()))
                .ExecuteAsync(Request(idempotencyKey: "  ")));

    [Fact]
    public async Task ExecuteAsync_RequiresAnExecutingAdministrator() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new ExecuteRefundService(new FakeRefundExecutionReader(Snapshot()))
                .ExecuteAsync(Request(executedByAdminUserId: "  ")));

    private static ExecuteRefundRequest Request(
        string idempotencyKey = "  refund-1  ",
        string executedByAdminUserId = "  finance-1  ") =>
        new(RefundPublicId, idempotencyKey, executedByAdminUserId);

    private static RefundExecutionSnapshot Snapshot(
        RefundStatus status = RefundStatus.Approved,
        decimal? approvedAmount = 400m,
        decimal? succeededAmount = null,
        decimal refundableBalance = 1000m) =>
        new(RefundId: 11L, status, approvedAmount, succeededAmount, refundableBalance);

    private sealed class FakeRefundExecutionReader : IRefundExecutionReader
    {
        private readonly RefundExecutionSnapshot? _snapshot;

        public FakeRefundExecutionReader(RefundExecutionSnapshot? snapshot) => _snapshot = snapshot;

        public Task<RefundExecutionSnapshot?> FindAsync(
            Guid refundPublicId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshot);
    }
}
