using DoSelect.Application.Invoicing;
using DoSelect.Domain.Invoicing;

namespace DoSelect.Application.Tests;

public sealed class IssueInvoiceAllowanceServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);
    private static readonly Guid RefundPublicId = new("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ItemA = new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task IssueAsync_PlansAnAllowanceForASettledRefund()
    {
        var service = CreateService(new FakeInvoiceAllowanceReader(Snapshot()));

        var result = await service.IssueAsync(Request());

        Assert.True(result.IsSuccess);
        var plan = Assert.IsType<InvoiceAllowancePlan>(result.Plan);
        Assert.Equal(5L, plan.SimulatedInvoiceId);
        Assert.Equal(9L, plan.RefundId);
        Assert.Equal(952m, plan.NetAmount);
        Assert.Equal(48m, plan.TaxAmount);
        Assert.Equal(1000m, plan.Amount);
        Assert.Equal(SimulatedInvoiceStatus.PartiallyAllowed, plan.ResultingInvoiceStatus);
    }

    [Fact]
    public async Task IssueAsync_NumbersTheAllowanceWithTheDemoMarker()
    {
        var service = CreateService(new FakeInvoiceAllowanceReader(Snapshot(), sequence: 7));

        var result = await service.IssueAsync(Request());

        Assert.Equal("DEMO-A-202608-000007", result.Plan!.AllowanceNumber);
        Assert.Equal(NowUtc, result.Plan.IssuedAtUtc);
    }

    [Fact]
    public async Task IssueAsync_MarksTheInvoiceFullyAllowedWhenNothingRemains()
    {
        var service = CreateService(new FakeInvoiceAllowanceReader(
            Snapshot(quantity: 1, gross: 1000m)));

        var result = await service.IssueAsync(Request());

        Assert.Equal(SimulatedInvoiceStatus.FullyAllowed, result.Plan!.ResultingInvoiceStatus);
    }

    [Fact]
    public async Task IssueAsync_ReportsAnUnknownRefund()
    {
        var service = CreateService(new FakeInvoiceAllowanceReader(snapshot: null));

        var result = await service.IssueAsync(Request());

        Assert.False(result.IsSuccess);
        Assert.False(result.RefundFound);
    }

    [Fact]
    public async Task IssueAsync_SurfacesTheAllowanceRejection()
    {
        var service = CreateService(new FakeInvoiceAllowanceReader(
            Snapshot(refundAlreadyHasAllowance: true)));

        var result = await service.IssueAsync(Request());

        Assert.False(result.IsSuccess);
        Assert.True(result.RefundFound);
        Assert.Equal(InvoiceAllowanceRejection.RefundAlreadyAllowed, result.Rejection);
    }

    [Fact]
    public async Task IssueAsync_DoesNotTakeASequenceWhenTheAllowanceIsRejected()
    {
        var reader = new FakeInvoiceAllowanceReader(Snapshot(refundAlreadyHasAllowance: true));
        var service = CreateService(reader);

        await service.IssueAsync(Request());

        Assert.Null(reader.RequestedIssuedAtUtc);
    }

    private static IssueInvoiceAllowanceService CreateService(IInvoiceAllowanceReader reader) =>
        new(reader, new FixedTimeProvider(new DateTimeOffset(NowUtc, TimeSpan.Zero)));

    private static IssueInvoiceAllowanceRequest Request() =>
        new(RefundPublicId, [new InvoiceAllowanceLineRequest(ItemA, 1, 1000m)]);

    private static InvoiceAllowanceSnapshot Snapshot(
        int quantity = 2,
        decimal gross = 2000m,
        bool refundAlreadyHasAllowance = false) =>
        new(
            SimulatedInvoiceId: 5L,
            RefundId: 9L,
            SimulatedInvoiceStatus.Issued,
            refundAlreadyHasAllowance,
            [new InvoiceAllowanceCapacity(ItemA, quantity, 0, gross, 0m)]);

    private sealed class FakeInvoiceAllowanceReader : IInvoiceAllowanceReader
    {
        private readonly InvoiceAllowanceSnapshot? _snapshot;
        private readonly int _sequence;

        public FakeInvoiceAllowanceReader(InvoiceAllowanceSnapshot? snapshot, int sequence = 1)
        {
            _snapshot = snapshot;
            _sequence = sequence;
        }

        public DateTime? RequestedIssuedAtUtc { get; private set; }

        public Task<InvoiceAllowanceSnapshot?> FindByRefundAsync(
            Guid refundPublicId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshot);

        public Task<int> NextAllowanceSequenceAsync(
            DateTime issuedAtUtc,
            CancellationToken cancellationToken = default)
        {
            RequestedIssuedAtUtc = issuedAtUtc;
            return Task.FromResult(_sequence);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
