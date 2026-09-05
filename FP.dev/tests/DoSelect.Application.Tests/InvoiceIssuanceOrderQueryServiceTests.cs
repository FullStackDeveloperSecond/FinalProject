using DoSelect.Application.Invoicing;
using DoSelect.Application.Orders;

namespace DoSelect.Application.Tests;

public sealed class InvoiceIssuanceOrderQueryServiceTests
{
    private static readonly Guid OrderPublicId = new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task FindAsync_ReturnsOnlyTheInvoiceIssuanceFactsAndExistence()
    {
        var rowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var invoices = new FakeInvoiceExistenceReader(hasInvoice: true);
        var service = new InvoiceIssuanceOrderQueryService(
            new FakeOrderReader(new InvoiceIssuanceOrderSummary(
                42L, OrderPublicId, "ORD-20260901-0042",
                OrderIsCancelled: false, OrderIsPaid: true, rowVersion)),
            invoices);

        var result = await service.FindAsync(OrderPublicId);

        Assert.NotNull(result);
        Assert.Equal(OrderPublicId, result.OrderPublicId);
        Assert.Equal("ORD-20260901-0042", result.OrderNumber);
        Assert.True(result.OrderIsPaid);
        Assert.False(result.OrderIsCancelled);
        Assert.Equal(rowVersion, result.RowVersion);
        Assert.True(result.HasInvoice);
        Assert.Equal(42L, invoices.LastOrderId);
    }

    [Fact]
    public async Task FindAsync_ReturnsNullWithoutQueryingInvoicesWhenOrderDoesNotExist()
    {
        var invoices = new FakeInvoiceExistenceReader(hasInvoice: true);
        var service = new InvoiceIssuanceOrderQueryService(new FakeOrderReader(null), invoices);

        Assert.Null(await service.FindAsync(OrderPublicId));
        Assert.Null(invoices.LastOrderId);
    }

    private sealed class FakeOrderReader : IOrderInvoiceIssuanceReader
    {
        private readonly InvoiceIssuanceOrderSummary? _summary;

        public FakeOrderReader(InvoiceIssuanceOrderSummary? summary) => _summary = summary;

        public Task<InvoiceOrderSnapshot?> FindIssuanceSnapshotAsync(
            Guid orderPublicId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<InvoiceOrderSnapshot?>(null);

        public Task<InvoiceIssuanceOrderSummary?> FindAdminSummaryAsync(
            Guid orderPublicId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_summary);
    }

    private sealed class FakeInvoiceExistenceReader : IInvoiceExistenceReader
    {
        private readonly bool _hasInvoice;

        public FakeInvoiceExistenceReader(bool hasInvoice) => _hasInvoice = hasInvoice;

        public long? LastOrderId { get; private set; }

        public Task<bool> HasInvoiceAsync(long orderId, CancellationToken cancellationToken = default)
        {
            LastOrderId = orderId;
            return Task.FromResult(_hasInvoice);
        }
    }
}
