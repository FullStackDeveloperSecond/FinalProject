using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Common;
using DoSelect.Api.IntegrationTests.Support;
using DoSelect.Api.Security;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Invoicing;
using DoSelect.Application.Orders;
using DoSelect.Domain.Invoicing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests.Invoicing;

public sealed class AdminInvoiceCommandsControllerTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminId = "invoice-command-admin";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public AdminInvoiceCommandsControllerTests(WebApplicationFactory<Program> baseFactory) =>
        _baseFactory = baseFactory;

    [Fact]
    public async Task IssuanceSnapshotReturnsOnlyApprovedFieldsForFinanceManager()
    {
        var orderPublicId = Guid.NewGuid();
        var rowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var fake = new FakeWriter();
        using var factory = CreateFactory(
            fake,
            new InvoiceIssuanceOrderSummary(
                42L, orderPublicId, "ORD-20260901-0042",
                OrderIsCancelled: false, OrderIsPaid: true, rowVersion),
            hasInvoice: false);
        using var client = CreateAdminClient(factory, DoSelectRoles.FinanceManager);

        using var response = await client.GetAsync(
            $"/api/v1/admin/orders/{orderPublicId}/invoice-issuance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            ["hasInvoice", "orderIsCancelled", "orderIsPaid", "orderNumber", "orderPublicId", "rowVersion"],
            json.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(orderPublicId, json.RootElement.GetProperty("orderPublicId").GetGuid());
        Assert.Equal("ORD-20260901-0042", json.RootElement.GetProperty("orderNumber").GetString());
        Assert.Equal(Convert.ToBase64String(rowVersion), json.RootElement.GetProperty("rowVersion").GetString());
        Assert.False(json.RootElement.GetProperty("hasInvoice").GetBoolean());
    }

    [Theory]
    [InlineData(DoSelectRoles.OrderManager, false)]
    [InlineData(DoSelectRoles.FinanceManager, true)]
    public async Task IssuanceSnapshotRejectsCallersOutsideInvoiceManage(
        string role,
        bool withoutMfa)
    {
        var orderPublicId = Guid.NewGuid();
        var fake = new FakeWriter();
        using var factory = CreateFactory(fake, Summary(orderPublicId));
        using var client = CreateAdminClient(factory, role);
        if (withoutMfa)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.WithoutMfaHeaderName, "1");
        }

        using var response = await client.GetAsync(
            $"/api/v1/admin/orders/{orderPublicId}/invoice-issuance");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task IssuanceSnapshotRejectsAnonymousCaller()
    {
        var orderPublicId = Guid.NewGuid();
        var fake = new FakeWriter();
        using var factory = CreateFactory(fake, Summary(orderPublicId));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/v1/admin/orders/{orderPublicId}/invoice-issuance");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IssuePassesTrustedAdminContextRowVersionAndIdempotencyKey()
    {
        var fake = new FakeWriter();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.FinanceManager);
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, "invoice-issue-correlation");
        var orderPublicId = Guid.NewGuid();
        var rowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        using var response = await SendWithAntiforgeryAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/admin/orders/{orderPublicId}/invoices",
            new { orderRowVersion = rowVersion },
            idempotencyKey: "issue-key");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(fake.IssueCommand);
        Assert.Equal(orderPublicId, fake.IssueCommand.OrderPublicId);
        Assert.Equal(rowVersion, fake.IssueCommand.OrderRowVersion);
        Assert.Equal("issue-key", fake.IssueCommand.IdempotencyKey);
        Assert.Equal(AdminId, fake.IssueCommand.AdminUserId);
        Assert.Equal("invoice-issue-correlation", fake.IssueCommand.CorrelationId);
    }

    [Fact]
    public async Task VoidPassesReasonNoteAndInvoiceRowVersion()
    {
        var fake = new FakeWriter();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.SuperAdmin);
        var invoicePublicId = Guid.NewGuid();
        var rowVersion = new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 };

        using var response = await SendWithAntiforgeryAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/admin/invoices/{invoicePublicId}/actions/void",
            new { reasonCode = "order_cancelled", note = "已確認整筆取消", rowVersion });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(fake.VoidCommand);
        Assert.Equal(invoicePublicId, fake.VoidCommand.InvoicePublicId);
        Assert.Equal("order_cancelled", fake.VoidCommand.ReasonCode);
        Assert.Equal("已確認整筆取消", fake.VoidCommand.Note);
        Assert.Equal(rowVersion, fake.VoidCommand.InvoiceRowVersion);
        Assert.Equal(AdminId, fake.VoidCommand.AdminUserId);
    }

    [Theory]
    [InlineData("issue")]
    [InlineData("void")]
    public async Task WrongRoleCannotReachEitherInvoiceCommand(string command)
    {
        var fake = new FakeWriter();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.OrderManager);
        var id = Guid.NewGuid();

        using var response = command == "issue"
            ? await SendWithAntiforgeryAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/admin/orders/{id}/invoices",
                new { orderRowVersion = new byte[8] },
                "issue-key")
            : await SendWithAntiforgeryAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/admin/invoices/{id}/actions/void",
                new { reasonCode = "order_cancelled", rowVersion = new byte[8] });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(fake.IssueCommand);
        Assert.Null(fake.VoidCommand);
    }

    private WebApplicationFactory<Program> CreateFactory(
        FakeWriter fake,
        InvoiceIssuanceOrderSummary? summary = null,
        bool hasInvoice = false) =>
        _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            TestAuthHandler.Configure(services);
            services.RemoveAll<IAdminInvoiceWriter>();
            services.AddSingleton<IAdminInvoiceWriter>(fake);
            services.RemoveAll<IOrderInvoiceIssuanceReader>();
            services.AddSingleton<IOrderInvoiceIssuanceReader>(new FakeOrderReader(summary));
            services.RemoveAll<IInvoiceExistenceReader>();
            services.AddSingleton<IInvoiceExistenceReader>(new FakeExistenceReader(hasInvoice));
        }));

    private static InvoiceIssuanceOrderSummary Summary(Guid orderPublicId) =>
        new(42L, orderPublicId, "ORD-20260901-0042", false, true, new byte[8]);

    private static HttpClient CreateAdminClient(
        WebApplicationFactory<Program> factory,
        string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.MemberHeaderName, AdminId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeaderName, role);
        return client;
    }

    private static async Task<HttpResponseMessage> SendWithAntiforgeryAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object body,
        string? idempotencyKey = null)
    {
        using var tokenRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/security/antiforgery-token");
        tokenRequest.Headers.Add(SecurityController.ClientHeaderName, DoSelectClaimValues.Admin);
        using var tokenResponse = await client.SendAsync(tokenRequest);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", tokenJson.RootElement.GetProperty("requestToken").GetString());
        if (tokenResponse.Headers.TryGetValues("Set-Cookie", out var values))
        {
            request.Headers.Add(
                "Cookie",
                values.Select(value => value.Split(';', 2)[0])
                    .Single(value => value.StartsWith(".DoSelect.Antiforgery=", StringComparison.Ordinal)));
        }
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    private sealed class FakeWriter : IAdminInvoiceWriter
    {
        public IssueSimulatedInvoiceCommand? IssueCommand { get; private set; }
        public VoidSimulatedInvoiceCommand? VoidCommand { get; private set; }

        public Task<IdempotencyExecutionResult<AdminInvoiceDto>> IssueAsync(
            IssueSimulatedInvoiceCommand command,
            CancellationToken cancellationToken = default)
        {
            IssueCommand = command;
            return Task.FromResult(new IdempotencyExecutionResult<AdminInvoiceDto>(
                201,
                Dto(command.OrderPublicId),
                "{}",
                IsReplay: false));
        }

        public Task<AdminInvoiceDto> VoidAsync(
            VoidSimulatedInvoiceCommand command,
            CancellationToken cancellationToken = default)
        {
            VoidCommand = command;
            return Task.FromResult(Dto(Guid.NewGuid(), command.InvoicePublicId));
        }

        private static AdminInvoiceDto Dto(Guid orderPublicId, Guid? invoicePublicId = null)
        {
            var invoice = new SimulatedInvoiceDto(
                invoicePublicId ?? Guid.NewGuid(),
                "DEMO-202609-000001",
                orderPublicId,
                SimulatedInvoiceStatus.Issued,
                SimulatedInvoiceBuyerType.Individual,
                "a***@example.com",
                null,
                null,
                null,
                952m,
                48m,
                1000m,
                "TWD",
                0.05m,
                [],
                [],
                DateTime.UtcNow,
                null,
                SimulatedInvoice.RequiredDemoMarker,
                new byte[8]);
            return new AdminInvoiceDto(invoice, "ORD-1", [InvoiceActions.Void]);
        }
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

    private sealed class FakeExistenceReader : IInvoiceExistenceReader
    {
        private readonly bool _hasInvoice;

        public FakeExistenceReader(bool hasInvoice) => _hasInvoice = hasInvoice;

        public Task<bool> HasInvoiceAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_hasInvoice);
    }
}
