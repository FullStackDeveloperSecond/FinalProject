using DoSelect.Application.Refunds;
using DoSelect.Domain.Refunds;

namespace DoSelect.Application.Tests;

public sealed class ExecuteRefundServiceTests
{
    private const string StoredKey = "refund-1";
    private static readonly Guid RefundPublicId = new("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task PreviewAsync_ApprovesAnApprovedRefundWithinTheBalance()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(Snapshot()));

        var result = await service.PreviewAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.False(result.IsReplay);
        var plan = Assert.IsType<RefundExecutionPlan>(result.Plan);
        Assert.Equal(11L, plan.RefundId);
        Assert.Equal(400m, plan.Amount);
        Assert.Equal("finance-1", plan.ExecutedByAdminUserId);
        Assert.Equal(StoredKey, plan.IdempotencyKey);
    }

    [Fact]
    public async Task PreviewAsync_AllowsAnAmountExactlyEqualToTheBalance()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(
            Snapshot(approvedAmount: 400m, refundableBalance: 400m)));

        Assert.True((await service.PreviewAsync(Request())).IsSuccess);
    }

    [Fact]
    public async Task PreviewAsync_RejectsAnAmountAboveTheRefundableBalance()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(
            Snapshot(approvedAmount: 400m, refundableBalance: 399.99m)));

        Assert.Equal(
            RefundErrorCodes.RefundAmountExceeded,
            (await service.PreviewAsync(Request())).ErrorCode);
    }

    [Fact]
    public async Task TheSameKeyOnAnAlreadySucceededRefundReplaysTheSameResult()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(
            Snapshot(status: RefundStatus.Succeeded, succeededAmount: 400m)));

        var result = await service.PreviewAsync(Request());

        Assert.True(result.IsReplay);
        Assert.Equal(400m, result.SettledAmount);
        Assert.Null(result.Plan);
    }

    [Fact]
    public async Task ADifferentKeyOnASucceededRefundNeverProducesASecondEffect()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(
            Snapshot(status: RefundStatus.Succeeded, succeededAmount: 400m)));

        var result = await service.PreviewAsync(Request(idempotencyKey: "someone-elses-key"));

        Assert.Equal(RefundErrorCodes.IdempotencyPayloadConflict, result.ErrorCode);
        Assert.Null(result.Plan);
        Assert.Null(result.SettledAmount);
    }

    [Fact]
    public async Task ADifferentKeyOnAnApprovedRefundConflictsInsteadOfExecuting()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(Snapshot()));

        var result = await service.PreviewAsync(Request(idempotencyKey: "someone-elses-key"));

        Assert.Equal(RefundErrorCodes.IdempotencyPayloadConflict, result.ErrorCode);
    }

    [Fact]
    public async Task TheKeyIsComparedAfterTrimming()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(Snapshot()));

        Assert.True((await service.PreviewAsync(Request(idempotencyKey: "  refund-1  "))).IsSuccess);
    }

    [Theory]
    [InlineData(RefundStatus.PendingReview)]
    [InlineData(RefundStatus.Rejected)]
    [InlineData(RefundStatus.Processing)]
    [InlineData(RefundStatus.Cancelled)]
    [InlineData(RefundStatus.Failed)]
    public async Task PreviewAsync_RejectsAnyStatusOtherThanApprovedOrSucceeded(RefundStatus status)
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(Snapshot(status: status)));

        Assert.Equal(
            RefundErrorCodes.RefundStateConflict,
            (await service.PreviewAsync(Request())).ErrorCode);
    }

    [Fact]
    public async Task PreviewAsync_RejectsAnApprovedRefundWithoutAnApprovedAmount()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(
            Snapshot(approvedAmount: null)));

        Assert.Equal(
            RefundErrorCodes.RefundStateConflict,
            (await service.PreviewAsync(Request())).ErrorCode);
    }

    [Fact]
    public async Task PreviewAsync_ReturnsNotFoundForAnUnknownRefund()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(snapshot: null));

        Assert.Equal(
            RefundErrorCodes.ResourceNotFound,
            (await service.PreviewAsync(Request())).ErrorCode);
    }

    [Fact]
    public async Task PreviewAsync_RequiresAnIdempotencyKey() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new ExecuteRefundService(new FakeRefundExecutionReader(Snapshot()))
                .PreviewAsync(Request(idempotencyKey: "  ")));

    [Fact]
    public async Task PreviewAsync_RequiresAnExecutingAdministrator() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new ExecuteRefundService(new FakeRefundExecutionReader(Snapshot()))
                .PreviewAsync(Request(executedByAdminUserId: "  ")));

    private static ExecuteRefundRequest Request(
        string idempotencyKey = StoredKey,
        string executedByAdminUserId = "  finance-1  ") =>
        new(RefundPublicId, idempotencyKey, executedByAdminUserId);

    private static RefundExecutionSnapshot Snapshot(
        RefundStatus status = RefundStatus.Approved,
        decimal? approvedAmount = 400m,
        decimal? succeededAmount = null,
        decimal refundableBalance = 1000m) =>
        new(11L, status, approvedAmount, succeededAmount, refundableBalance, StoredKey);

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
