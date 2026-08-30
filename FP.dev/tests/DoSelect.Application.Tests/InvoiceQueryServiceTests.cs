using DoSelect.Application.Common;
using DoSelect.Application.Invoicing;
using DoSelect.Application.Orders;
using DoSelect.Domain.Invoicing;

namespace DoSelect.Application.Tests;

/// <summary>
/// 合併 Invoicing 與 Orders 兩邊資料、擁有者比對與個資遮蔽。
/// </summary>
/// <remarks>
/// 這三件事都放在 Application 層，所以不必啟動 HTTP 就測得到 ——
/// 擁有者比對如果留在 Controller，就只能靠整合測試覆蓋。
/// </remarks>
public sealed class InvoiceQueryServiceTests
{
    private static readonly Guid OrderPublicId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid InvoicePublicId = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task FindForOrderAsync_GivesTheOwnerTheirInvoice()
    {
        var service = CreateService();

        var invoice = await service.FindForOrderAsync(
            new InvoiceViewer.Member("member-1"), OrderPublicId);

        Assert.Equal(InvoicePublicId, invoice!.PublicId);
        Assert.Equal(OrderPublicId, invoice.OrderPublicId);
    }

    [Fact]
    public async Task FindForOrderAsync_HidesTheInvoiceFromAnotherMember()
    {
        // 回 null 讓端點折成 404，而不是 403 —— 區分「不存在」與「不是你的」
        // 等於告訴外人這個 id 存在。
        var service = CreateService();

        var invoice = await service.FindForOrderAsync(
            new InvoiceViewer.Member("someone-else"), OrderPublicId);

        Assert.Null(invoice);
    }

    [Fact]
    public async Task FindForOrderAsync_LetsAVerifiedGuestThrough()
    {
        // 訪客的 Scope 已由 GuestOrderAccessScopeAuthorizer 對「這一筆訂單」驗過，
        // 這裡不再比一次 —— 那會變成第二份平行的驗證邏輯。
        var service = CreateService(memberUserId: null, guestEmail: "guest@example.test");

        var invoice = await service.FindForOrderAsync(new InvoiceViewer.Guest(), OrderPublicId);

        Assert.NotNull(invoice);
    }

    [Fact]
    public async Task FindForOrderAsync_ReturnsNullWhenTheOrderHasNoInvoice()
    {
        var service = CreateService(withoutInvoice: true);

        Assert.Null(await service.FindForOrderAsync(
            new InvoiceViewer.Member("member-1"), OrderPublicId));
    }

    [Fact]
    public async Task FindForOrderAsync_MasksTheBuyerDetails()
    {
        // `API Endpoint目錄` 第 74 行：前台只回遮蔽後的買受人資料。
        var service = CreateService(
            invoice: Row(buyerEmail: "buyer@example.test", companyTaxId: "12345678"));

        var invoice = await service.FindForOrderAsync(
            new InvoiceViewer.Member("member-1"), OrderPublicId);

        Assert.Equal("b****@example.test", invoice!.BuyerEmailMasked);
        Assert.Equal("*****678", invoice.CompanyTaxIdMasked);
    }

    [Fact]
    public async Task FindForOrderAsync_MasksAnEmailThatIsNotOneAtAll()
    {
        // 資料若不是可辨識的 Email，整段遮掉而不是原樣回傳 ——
        // 「看起來不像 Email 所以應該不是個資」不是一個安全的推論。
        var service = CreateService(invoice: Row(buyerEmail: "not-an-email"));

        var invoice = await service.FindForOrderAsync(
            new InvoiceViewer.Member("member-1"), OrderPublicId);

        Assert.Equal("************", invoice!.BuyerEmailMasked);
    }

    [Fact]
    public async Task FindAsync_CarriesTheOrderNumberAndAvailableActions()
    {
        var service = CreateService(invoice: Row(status: SimulatedInvoiceStatus.Issued));

        var admin = await service.FindAsync(InvoicePublicId);

        Assert.Equal("SO-0001", admin!.OrderNumber);
        Assert.Equal([InvoiceActions.Void, InvoiceActions.CreateAllowance], admin.AvailableActions);
    }

    [Theory]
    [InlineData(SimulatedInvoiceStatus.Voided)]
    [InlineData(SimulatedInvoiceStatus.FullyAllowed)]
    public async Task FindAsync_OffersNoActionForATerminalStatus(SimulatedInvoiceStatus status)
    {
        var service = CreateService(invoice: Row(status: status));

        var admin = await service.FindAsync(InvoicePublicId);

        Assert.Empty(admin!.AvailableActions);
    }

    [Fact]
    public async Task FindAsync_TreatsAnInvoiceWithNoOrderAsNotFound()
    {
        // OrderId 是外鍵，訂單不見代表資料不一致。回一張沒有訂單的發票，
        // 呼叫端會拿到一份殘缺的內容而不自知。
        var service = new InvoiceQueryService(
            new FakeInvoiceQueryReader(Row(), []),
            new FakeOrderReferenceReader(orders: []));

        Assert.Null(await service.FindAsync(InvoicePublicId));
    }

    [Fact]
    public async Task ListAsync_ResolvesEveryOrderInOneBatch()
    {
        // alex Issue #65 的驗收條件：後台摘要必須批次查詢。
        var rows = new[]
        {
            Row(orderId: 1L, publicId: new Guid("33333333-3333-3333-3333-333333333333")),
            Row(orderId: 2L, publicId: new Guid("44444444-4444-4444-4444-444444444444")),
            Row(orderId: 3L, publicId: new Guid("55555555-5555-5555-5555-555555555555")),
        };
        var orders = new FakeOrderReferenceReader(
        [
            Reference(1L, "SO-0001"),
            Reference(2L, "SO-0002"),
            Reference(3L, "SO-0003"),
        ]);
        var service = new InvoiceQueryService(new FakeInvoiceQueryReader(null, rows), orders);

        var page = await service.ListAsync(new AdminInvoiceQuery(null, null, null, null, 1, 20));

        Assert.Equal(3, page.Items.Count);
        Assert.Equal("SO-0002", page.Items[1].OrderNumber);

        // 一次呼叫、三個 id —— 逐筆補會是三次。
        Assert.Equal(1, orders.FindManyCalls);
        Assert.Equal(3, orders.LastRequestedIds.Count);
    }

    [Fact]
    public async Task ListAsync_DoesNotInventAnOrderForARowItCannotResolve()
    {
        var rows = new[] { Row(orderId: 9L) };
        var service = new InvoiceQueryService(
            new FakeInvoiceQueryReader(null, rows),
            new FakeOrderReferenceReader(orders: []));

        var page = await service.ListAsync(new AdminInvoiceQuery(null, null, null, null, 1, 20));

        var item = Assert.Single(page.Items);
        Assert.Equal(Guid.Empty, item.OrderPublicId);
        Assert.Equal(string.Empty, item.OrderNumber);
    }

    /// <remarks>
    /// <paramref name="withoutInvoice"/> 是獨立的旗標，不用 <c>invoice: null</c> 表示 ——
    /// 先前用 <c>invoice ?? Row()</c> 當預設值，傳 null 會被悄悄換成預設那一列，
    /// 「訂單沒有發票」那條測試因此永遠測不到它要測的東西。
    /// </remarks>
    private static InvoiceQueryService CreateService(
        InvoiceRow? invoice = null,
        string? memberUserId = "member-1",
        string? guestEmail = null,
        bool withoutInvoice = false) =>
        new(
            new FakeInvoiceQueryReader(withoutInvoice ? null : invoice ?? Row(), []),
            new FakeOrderReferenceReader([Reference(7L, "SO-0001", memberUserId, guestEmail)]));

    private static OrderInvoiceReference Reference(
        long orderId,
        string orderNumber,
        string? memberUserId = "member-1",
        string? guestEmail = null) =>
        new(orderId, OrderPublicIdFor(orderId), orderNumber, memberUserId, guestEmail);

    private static Guid OrderPublicIdFor(long orderId) =>
        orderId == 7L ? OrderPublicId : new Guid($"000000{orderId:D2}-0000-0000-0000-000000000000");

    private static InvoiceRow Row(
        long orderId = 7L,
        Guid? publicId = null,
        SimulatedInvoiceStatus status = SimulatedInvoiceStatus.Issued,
        string? buyerEmail = "buyer@example.test",
        string? companyTaxId = null) =>
        new(
            orderId,
            publicId ?? InvoicePublicId,
            "DEMO-202608-000001",
            status,
            SimulatedInvoiceBuyerType.Individual,
            buyerEmail,
            null,
            null,
            companyTaxId,
            952m,
            48m,
            1000m,
            "TWD",
            new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc),
            null,
            SimulatedInvoice.RequiredDemoMarker,
            [1, 2, 3],
            [],
            []);

    private sealed class FakeInvoiceQueryReader : IInvoiceQueryReader
    {
        private readonly InvoiceRow? _single;
        private readonly IReadOnlyList<InvoiceRow> _page;

        public FakeInvoiceQueryReader(InvoiceRow? single, IReadOnlyList<InvoiceRow> page)
        {
            _single = single;
            _page = page;
        }

        public Task<InvoiceRow?> FindByOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_single);

        public Task<InvoiceRow?> FindAsync(Guid invoicePublicId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_single);

        public Task<PageResult<InvoiceRow>> ListAsync(
            AdminInvoiceQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PageResult<InvoiceRow>(_page, 1, 20, _page.Count));
    }

    private sealed class FakeOrderReferenceReader : IOrderInvoiceReferenceReader
    {
        private readonly IReadOnlyList<OrderInvoiceReference> _orders;

        public FakeOrderReferenceReader(IReadOnlyList<OrderInvoiceReference> orders) => _orders = orders;

        public int FindManyCalls { get; private set; }

        public IReadOnlyCollection<long> LastRequestedIds { get; private set; } = [];

        public Task<OrderInvoiceReference?> FindAsync(
            Guid orderPublicId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_orders.FirstOrDefault(order => order.OrderPublicId == orderPublicId));

        public Task<IReadOnlyDictionary<long, OrderInvoiceReference>> FindManyAsync(
            IReadOnlyCollection<long> orderIds, CancellationToken cancellationToken = default)
        {
            FindManyCalls++;
            LastRequestedIds = orderIds;

            return Task.FromResult<IReadOnlyDictionary<long, OrderInvoiceReference>>(
                _orders.Where(order => orderIds.Contains(order.OrderId))
                    .ToDictionary(order => order.OrderId));
        }
    }
}
