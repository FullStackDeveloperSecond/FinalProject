using DoSelect.Application.Invoicing;
using DoSelect.Domain.Invoicing;

namespace DoSelect.Application.Tests;

public sealed class IssueInvoiceServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OrderPublicId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ItemA = new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task IssueAsync_PlansAnInvoiceForAPaidOrder()
    {
        var service = CreateService(new FakeInvoiceIssuanceReader(Snapshot()));

        var result = await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        Assert.True(result.IsSuccess);
        var plan = Assert.IsType<InvoiceIssuancePlan>(result.Plan);
        Assert.Equal(7L, plan.OrderId);
        Assert.Equal(952m, plan.NetAmount);
        Assert.Equal(48m, plan.TaxAmount);
        Assert.Equal(1000m, plan.IssuedAmount);
        Assert.Equal(plan.IssuedAmount, plan.NetAmount + plan.TaxAmount);
    }

    [Fact]
    public async Task IssueAsync_NumbersTheInvoiceWithTheDemoMarker()
    {
        var service = CreateService(new FakeInvoiceIssuanceReader(Snapshot(), sequence: 42));

        var result = await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        Assert.Equal("DEMO-202608-000042", result.Plan!.InvoiceNumber);
    }

    [Fact]
    public async Task IssueAsync_TakesTheSequenceAgainstTheIssuingClock()
    {
        var reader = new FakeInvoiceIssuanceReader(Snapshot());
        var service = CreateService(reader);

        await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        Assert.Equal(NowUtc, reader.RequestedIssuedAtUtc);
    }

    [Fact]
    public async Task IssueAsync_CarriesTheBuyerDetailsThrough()
    {
        var service = CreateService(new FakeInvoiceIssuanceReader(Snapshot(
            buyerType: SimulatedInvoiceBuyerType.Company,
            companyTaxId: "12345678",
            companyName: "測試股份有限公司")));

        var result = await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        Assert.Equal(SimulatedInvoiceBuyerType.Company, result.Plan!.BuyerType);
        Assert.Equal("12345678", result.Plan.CompanyTaxId);
        Assert.Equal("測試股份有限公司", result.Plan.CompanyName);
    }

    [Fact]
    public async Task IssueAsync_ReportsAnUnknownOrder()
    {
        var service = CreateService(new FakeInvoiceIssuanceReader(snapshot: null));

        var result = await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        Assert.False(result.IsSuccess);
        Assert.False(result.OrderFound);
        Assert.Null(result.Plan);
    }

    [Fact]
    public async Task IssueAsync_SurfacesTheIssuanceRejection()
    {
        var service = CreateService(new FakeInvoiceIssuanceReader(
            Snapshot(trigger: InvoiceIssuanceTrigger.NotPaid)));

        var result = await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        Assert.False(result.IsSuccess);
        Assert.True(result.OrderFound);
        Assert.Equal(InvoiceIssuanceRejection.OrderNotPaid, result.Rejection);
    }

    [Fact]
    public async Task IssueAsync_DoesNotTakeASequenceWhenTheOrderIsNotInvoiceable()
    {
        var reader = new FakeInvoiceIssuanceReader(
            Snapshot(trigger: InvoiceIssuanceTrigger.OrderCancelled));
        var service = CreateService(reader);

        await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        Assert.Null(reader.RequestedIssuedAtUtc);
    }

    private static IssueInvoiceService CreateService(IInvoiceIssuanceReader reader) =>
        new(reader, new FixedTimeProvider(new DateTimeOffset(NowUtc, TimeSpan.Zero)));

    private static InvoiceOrderSnapshot Snapshot(
        InvoiceIssuanceTrigger trigger = InvoiceIssuanceTrigger.OnlinePaymentSucceeded,
        SimulatedInvoiceBuyerType buyerType = SimulatedInvoiceBuyerType.Individual,
        string? companyTaxId = null,
        string? companyName = null) =>
        new(
            OrderId: 7L,
            trigger,
            OrderAlreadyHasInvoice: false,
            buyerType,
            BuyerEmail: "buyer@example.com",
            CarrierType: null,
            CarrierValueMasked: null,
            companyTaxId,
            companyName,
            [
                new InvoiceOrderLine(
                    ItemA, InvoiceLineKind.Merchandise, "測試商品", "SKU-1", 1, 1000m, 0m, 1000m),
            ]);

    private sealed class FakeInvoiceIssuanceReader : IInvoiceIssuanceReader
    {
        private readonly InvoiceOrderSnapshot? _snapshot;
        private readonly int _sequence;

        public FakeInvoiceIssuanceReader(InvoiceOrderSnapshot? snapshot, int sequence = 1)
        {
            _snapshot = snapshot;
            _sequence = sequence;
        }

        public DateTime? RequestedIssuedAtUtc { get; private set; }

        public Task<InvoiceOrderSnapshot?> FindOrderSnapshotAsync(
            Guid orderPublicId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshot);

        public Task<int> NextInvoiceSequenceAsync(
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
