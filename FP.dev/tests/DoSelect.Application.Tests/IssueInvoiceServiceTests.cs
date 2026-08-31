using DoSelect.Application.Invoicing;
using DoSelect.Application.Orders;
using DoSelect.Domain.Invoicing;

namespace DoSelect.Application.Tests;

public sealed class IssueInvoiceServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OrderPublicId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ItemA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ItemB = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task IssueAsync_PlansAnInvoiceForAPaidOrder()
    {
        var service = CreateService(new FakeOrderInvoiceIssuanceReader(Snapshot()));

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
        var service = CreateService(
            new FakeOrderInvoiceIssuanceReader(Snapshot()),
            new FakeInvoiceNumberSequence(42));

        var result = await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        Assert.Equal("DEMO-202608-000042", result.Plan!.InvoiceNumber);
    }

    [Fact]
    public async Task IssueAsync_TakesTheSequenceAgainstTheIssuingClock()
    {
        var sequence = new FakeInvoiceNumberSequence();
        var service = CreateService(new FakeOrderInvoiceIssuanceReader(Snapshot()), sequence);

        await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        Assert.Equal(NowUtc, sequence.RequestedIssuedAtUtc);
    }

    [Fact]
    public async Task IssueAsync_CarriesTheBuyerDetailsThrough()
    {
        var service = CreateService(new FakeOrderInvoiceIssuanceReader(Snapshot(
            buyerType: SimulatedInvoiceBuyerType.Company,
            companyTaxId: "12345678",
            companyName: "測試股份有限公司")));

        var result = await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        Assert.Equal(SimulatedInvoiceBuyerType.Company, result.Plan!.BuyerType);
        Assert.Equal("12345678", result.Plan.CompanyTaxId);
        Assert.Equal("測試股份有限公司", result.Plan.CompanyName);
    }

    [Fact]
    public async Task IssueAsync_CarriesTheNarrowOrderItemKeyForPersistenceOnly()
    {
        var service = CreateService(new FakeOrderInvoiceIssuanceReader(Snapshot()));

        var result = await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        var line = Assert.Single(result.Plan!.Lines);
        Assert.Equal(11L, line.OrderItemId);
        Assert.Equal(ItemA, line.Breakdown.OrderItemPublicId);
    }

    [Fact]
    public async Task IssueAsync_PreservesTheOrderItemKeyAfterFilteringAFreeLine()
    {
        var snapshot = Snapshot() with
        {
            Lines =
            [
                new InvoiceOrderLineSource(
                    OrderItemId: 11L,
                    new InvoiceOrderLine(
                        ItemA, InvoiceLineKind.Merchandise, "零元贈品", "SKU-FREE", 1, 1000m, 1000m, 0m)),
                new InvoiceOrderLineSource(
                    OrderItemId: 12L,
                    new InvoiceOrderLine(
                        ItemB, InvoiceLineKind.Merchandise, "應開票商品", "SKU-PAID", 1, 1000m, 0m, 1000m)),
            ],
        };
        var service = CreateService(new FakeOrderInvoiceIssuanceReader(snapshot));

        var result = await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        var line = Assert.Single(result.Plan!.Lines);
        Assert.Equal(12L, line.OrderItemId);
        Assert.Equal(ItemB, line.Breakdown.OrderItemPublicId);
    }

    [Fact]
    public async Task IssueAsync_ReportsAnUnknownOrder()
    {
        var service = CreateService(new FakeOrderInvoiceIssuanceReader(snapshot: null));

        var result = await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        Assert.Equal(InvoiceErrorCodes.ResourceNotFound, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Theory]
    // Orders 的埠回的是狀態事實（已取消、已付款），trigger 由本服務映射。
    [InlineData(false, false, InvoiceErrorCodes.InvoiceOrderUnpaid)]
    [InlineData(true, false, InvoiceErrorCodes.InvoiceOrderCancelled)]
    [InlineData(true, true, InvoiceErrorCodes.InvoiceOrderCancelled)]
    public async Task IssueAsync_SurfacesTheIssuanceErrorCode(
        bool cancelled,
        bool paid,
        string expectedErrorCode)
    {
        var service = CreateService(new FakeOrderInvoiceIssuanceReader(
            Snapshot(cancelled: cancelled, paid: paid)));

        var result = await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        Assert.Equal(expectedErrorCode, result.ErrorCode);
    }

    [Fact]
    public async Task IssueAsync_TreatsACancelledOrderAsCancelledEvenWhenItWasPaid()
    {
        // 已付款後取消的訂單不能開票。映射的順序決定這件事：先看取消，再看付款。
        var service = CreateService(new FakeOrderInvoiceIssuanceReader(
            Snapshot(cancelled: true, paid: true)));

        var result = await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        Assert.Equal(InvoiceErrorCodes.InvoiceOrderCancelled, result.ErrorCode);
    }

    [Fact]
    public async Task IssueAsync_ReportsAnOrderThatAlreadyHasAnInvoice()
    {
        var service = CreateService(
            new FakeOrderInvoiceIssuanceReader(Snapshot()), alreadyIssued: true);

        var result = await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        Assert.Equal(InvoiceErrorCodes.InvoiceAlreadyExists, result.ErrorCode);
    }

    [Fact]
    public async Task IssueAsync_DoesNotTakeASequenceWhenTheOrderIsNotInvoiceable()
    {
        var sequence = new FakeInvoiceNumberSequence();
        var service = CreateService(
            new FakeOrderInvoiceIssuanceReader(Snapshot(cancelled: true)), sequence);

        await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId));

        Assert.Null(sequence.RequestedIssuedAtUtc);
    }

    [Theory]
    [InlineData(null, "測試股份有限公司")]
    [InlineData("12345678", null)]
    [InlineData("   ", "測試股份有限公司")]
    [InlineData("12345678", "   ")]
    [InlineData(null, null)]
    public async Task ACompanyInvoiceMissingItsDetailsFailsWithoutTakingASequence(
        string? companyTaxId,
        string? companyName)
    {
        // SimulatedInvoice 建構子會拒絕缺統編或抬頭的公司發票。
        // 若等到那時才失敗，就已經回傳了無法持久化的成功計畫並耗掉一個流水號。
        var sequence = new FakeInvoiceNumberSequence();
        var service = CreateService(
            new FakeOrderInvoiceIssuanceReader(Snapshot(
                buyerType: SimulatedInvoiceBuyerType.Company,
                companyTaxId: companyTaxId,
                companyName: companyName)),
            sequence);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IssueAsync(new IssueInvoiceRequest(OrderPublicId)));

        Assert.Null(sequence.RequestedIssuedAtUtc);
    }

    [Fact]
    public async Task AnIndividualInvoiceDoesNotNeedCompanyDetails()
    {
        var service = CreateService(new FakeOrderInvoiceIssuanceReader(Snapshot(
            buyerType: SimulatedInvoiceBuyerType.Individual,
            companyTaxId: null,
            companyName: null)));

        Assert.True((await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId))).IsSuccess);
    }

    [Fact]
    public async Task ACompleteCompanyInvoiceProducesAPersistablePlan()
    {
        var service = CreateService(new FakeOrderInvoiceIssuanceReader(Snapshot(
            buyerType: SimulatedInvoiceBuyerType.Company,
            companyTaxId: "12345678",
            companyName: "測試股份有限公司")));

        var plan = (await service.IssueAsync(new IssueInvoiceRequest(OrderPublicId))).Plan!;

        // 計畫必須能真的建立出 SimulatedInvoice，否則就是先前那個假成功。
        var invoice = new SimulatedInvoice(Guid.NewGuid(), new SimulatedInvoiceCreation(
            plan.OrderId, plan.InvoiceNumber, plan.BuyerType, plan.BuyerEmail,
            plan.CarrierType, plan.CarrierValueMasked, plan.CompanyTaxId, plan.CompanyName,
            plan.NetAmount, plan.TaxAmount, plan.IssuedAmount), NowUtc);

        Assert.Equal(SimulatedInvoiceBuyerType.Company, invoice.BuyerType);
        Assert.Equal("12345678", invoice.CompanyTaxId);
    }

    private static IssueInvoiceService CreateService(
        IOrderInvoiceIssuanceReader reader,
        IInvoiceNumberSequence? sequence = null,
        bool alreadyIssued = false) =>
        new(
            reader,
            new FakeInvoiceExistenceReader(alreadyIssued),
            sequence ?? new FakeInvoiceNumberSequence(),
            new FixedTimeProvider(new DateTimeOffset(NowUtc, TimeSpan.Zero)));

    private static InvoiceOrderSnapshot Snapshot(
        bool cancelled = false,
        bool paid = true,
        SimulatedInvoiceBuyerType buyerType = SimulatedInvoiceBuyerType.Individual,
        string? companyTaxId = null,
        string? companyName = null) =>
        new(
            OrderId: 7L,
            cancelled,
            paid,
            OrderPaidAmount: 1000m,
            buyerType,
            BuyerEmail: "buyer@example.com",
            CarrierType: null,
            CarrierValueMasked: null,
            CompanyTaxId: companyTaxId,
            CompanyName: companyName,
            [
                new InvoiceOrderLineSource(
                    OrderItemId: 11L,
                    new InvoiceOrderLine(
                        ItemA, InvoiceLineKind.Merchandise, "測試商品", "SKU-1", 1, 1000m, 0m, 1000m)),
            ]);

    private sealed class FakeOrderInvoiceIssuanceReader : IOrderInvoiceIssuanceReader
    {
        private readonly InvoiceOrderSnapshot? _snapshot;

        public FakeOrderInvoiceIssuanceReader(InvoiceOrderSnapshot? snapshot) => _snapshot = snapshot;

        public Task<InvoiceOrderSnapshot?> FindIssuanceSnapshotAsync(
            Guid orderPublicId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshot);
    }

    private sealed class FakeInvoiceExistenceReader : IInvoiceExistenceReader
    {
        private readonly bool _hasInvoice;

        public FakeInvoiceExistenceReader(bool hasInvoice) => _hasInvoice = hasInvoice;

        public Task<bool> HasInvoiceAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_hasInvoice);
    }

    private sealed class FakeInvoiceNumberSequence : IInvoiceNumberSequence
    {
        private readonly int _sequence;

        public FakeInvoiceNumberSequence(int sequence = 1) => _sequence = sequence;

        public DateTime? RequestedIssuedAtUtc { get; private set; }

        public Task<int> NextAsync(DateTime issuedAtUtc, CancellationToken cancellationToken = default)
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
