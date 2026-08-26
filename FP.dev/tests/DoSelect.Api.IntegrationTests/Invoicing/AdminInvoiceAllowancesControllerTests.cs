using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Common;
using DoSelect.Api.Invoicing;
using DoSelect.Api.IntegrationTests.Support;
using DoSelect.Api.Security;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Invoicing;
using DoSelect.Domain.Invoicing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests.Invoicing;

public sealed class AdminInvoiceAllowancesControllerTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminId = "invoice-admin-id";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public AdminInvoiceAllowancesControllerTests(WebApplicationFactory<Program> baseFactory) =>
        _baseFactory = baseFactory;

    [Fact]
    public async Task AnonymousRequestIsRejectedBeforeCallingTheWriter()
    {
        var fake = new FakeWriter();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, Guid.NewGuid(), includeKey: true);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fake.Calls);
    }

    [Theory]
    [InlineData(DoSelectRoles.OrderManager, HttpStatusCode.Forbidden)]
    [InlineData(DoSelectRoles.FinanceManager, HttpStatusCode.Created)]
    [InlineData(DoSelectRoles.SuperAdmin, HttpStatusCode.Created)]
    public async Task InvoiceManageRoleMatrixIsEnforced(string role, HttpStatusCode expected)
    {
        var fake = new FakeWriter();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, role);

        using var response = await PostAsync(client, Guid.NewGuid(), includeKey: true);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.Created ? 1 : 0, fake.Calls);
    }

    [Fact]
    public async Task MissingIdempotencyKeyReturnsValidationProblemWithoutCallingWriter()
    {
        var fake = new FakeWriter();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client, Guid.NewGuid(), includeKey: false);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task FinanceManagerWithoutMfaIsRejectedBeforeCallingTheWriter()
    {
        var fake = new FakeWriter();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.FinanceManager);
        client.DefaultRequestHeaders.Add(TestAuthHandler.WithoutMfaHeaderName, "true");

        using var response = await PostAsync(client, Guid.NewGuid(), includeKey: true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task AuthorizedRequestPassesOnlyTrustedContextAndReturnsTheWriterStatus()
    {
        const string correlationId = "invoice-acceptance-correlation";
        var fake = new FakeWriter();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.FinanceManager);
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, correlationId);
        var invoicePublicId = Guid.NewGuid();
        var refundPublicId = Guid.NewGuid();
        var rowVersion = new byte[8];

        using var response = await PostAsync(
            client,
            invoicePublicId,
            includeKey: true,
            refundPublicId,
            rowVersion);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(fake.Command);
        Assert.Equal(invoicePublicId, fake.Command.InvoicePublicId);
        Assert.Equal(refundPublicId, fake.Command.RefundPublicId);
        Assert.Equal(rowVersion, fake.Command.InvoiceRowVersion);
        Assert.Equal("allowance-key", fake.Command.IdempotencyKey);
        Assert.Equal(AdminId, fake.Command.AdminUserId);
        Assert.Equal(correlationId, fake.Command.CorrelationId);
        Assert.Equal(32, fake.Command.TraceId.Length);
        Assert.Equal(
            SimulatedInvoice.RequiredDemoMarker,
            json.RootElement.GetProperty("demoMarker").GetString());
    }

    private WebApplicationFactory<Program> CreateFactory(FakeWriter fake) =>
        _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            TestAuthHandler.Configure(services);
            services.RemoveAll<IInvoiceAllowanceWriter>();
            services.AddSingleton<IInvoiceAllowanceWriter>(fake);
        }));

    private static HttpClient CreateAdminClient(
        WebApplicationFactory<Program> factory,
        string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.MemberHeaderName, AdminId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeaderName, role);
        return client;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        Guid invoicePublicId,
        bool includeKey,
        Guid? refundPublicId = null,
        byte[]? rowVersion = null)
    {
        using var tokenRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/security/antiforgery-token");
        tokenRequest.Headers.Add(SecurityController.ClientHeaderName, DoSelectClaimValues.Admin);
        using var tokenResponse = await client.SendAsync(tokenRequest);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/invoices/{invoicePublicId}/allowances")
        {
            Content = JsonContent.Create(new
            {
                refundPublicId = refundPublicId ?? Guid.NewGuid(),
                invoiceRowVersion = rowVersion ?? new byte[8],
            }),
        };
        request.Headers.Add("X-XSRF-TOKEN", tokenJson.RootElement.GetProperty("requestToken").GetString());
        if (tokenResponse.Headers.TryGetValues("Set-Cookie", out var values))
        {
            var cookie = values.Select(value => value.Split(';', 2)[0])
                .Single(value => value.StartsWith(".DoSelect.Antiforgery=", StringComparison.Ordinal));
            request.Headers.Add("Cookie", cookie);
        }
        if (includeKey)
        {
            request.Headers.Add(AdminInvoiceAllowancesController.IdempotencyKeyHeaderName, "allowance-key");
        }

        return await client.SendAsync(request);
    }

    private sealed class FakeWriter : IInvoiceAllowanceWriter
    {
        public int Calls { get; private set; }
        public CreateInvoiceAllowanceCommand? Command { get; private set; }

        public Task<IdempotencyExecutionResult<SimulatedInvoiceAllowanceDto>> CreateAsync(
            CreateInvoiceAllowanceCommand command,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Command = command;
            var dto = new SimulatedInvoiceAllowanceDto(
                Guid.NewGuid(),
                "DEMO-A-202608-000001",
                command.InvoicePublicId,
                command.RefundPublicId,
                100m,
                5m,
                105m,
                [
                    new SimulatedInvoiceAllowanceItemDto(
                        Guid.NewGuid(), Guid.NewGuid(), InvoiceLineKind.Shipping,
                        1, 9.52m, .48m, 10m),
                ],
                DateTime.UtcNow,
                SimulatedInvoice.RequiredDemoMarker);
            return Task.FromResult(new IdempotencyExecutionResult<SimulatedInvoiceAllowanceDto>(
                201,
                dto,
                "{}",
                IsReplay: false));
        }
    }
}
