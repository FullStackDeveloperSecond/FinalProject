using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DoSelect.Api.Orders;
using DoSelect.Api.Security;
using DoSelect.Application.Checkout;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Orders;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests.Orders;

public sealed class CheckoutApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string GuestCartKey = "checkout-api-guest-cart-key-32-bytes";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public CheckoutApiTests(WebApplicationFactory<Program> baseFactory) =>
        _baseFactory = baseFactory;

    [Fact]
    public async Task CreateOrder_AsGuest_ReturnsTheFullOrderContract()
    {
        var expected = CreateOrderDto();
        var gateway = new FakeGateway(expected);
        using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, includeIdempotencyKey: true);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(expected.PublicId, body.RootElement.GetProperty("publicId").GetGuid());
        Assert.Equal(expected.OrderNumber, body.RootElement.GetProperty("orderNumber").GetString());
        Assert.Equal("pendingPayment", body.RootElement.GetProperty("orderStatus").GetString());
        Assert.Equal(1_150m, body.RootElement.GetProperty("amounts").GetProperty("grandTotal").GetDecimal());
        Assert.Equal("Guest", body.RootElement.GetProperty("recipient").GetProperty("recipientName").GetString());
        Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Contains(
            body.RootElement.GetProperty("availableActions").EnumerateArray(),
            element => element.GetString() == "cancel");
        Assert.Equal(1, gateway.Calls);
        Assert.Equal(GuestCartKey, gateway.Command?.Actor.GuestCartKey);
        Assert.Equal("checkout-api-key", gateway.Command?.IdempotencyKey);
    }

    [Fact]
    public async Task CreateOrder_WithoutIdempotencyKey_ReturnsValidationProblemBeforeCheckout()
    {
        var gateway = new FakeGateway(CreateOrderDto());
        using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, includeIdempotencyKey: false);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task GetPolicyVersions_AsAnonymous_ReturnsOnlyTheCurrentAcceptedVersions()
    {
        var gateway = new FakeGateway(CreateOrderDto());
        using var factory = CreateFactory(
            gateway,
            new CheckoutPolicySnapshot(7, 8, 9, 99));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/checkout/policy-versions");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, body.RootElement.EnumerateObject().Count());
        Assert.Equal(7, body.RootElement.GetProperty("terms").GetInt32());
        Assert.Equal(8, body.RootElement.GetProperty("return").GetInt32());
        Assert.Equal(9, body.RootElement.GetProperty("privacy").GetInt32());
        Assert.DoesNotContain(
            body.RootElement.EnumerateObject(),
            property => property.NameEquals("shippingConstraint"));
    }

    private WebApplicationFactory<Program> CreateFactory(
        FakeGateway gateway,
        CheckoutPolicySnapshot? policy = null) =>
        _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<ICheckoutTransactionGateway>();
            services.RemoveAll<IIdempotencyExecutor>();
            services.RemoveAll<ICheckoutPolicyProvider>();
            services.AddSingleton<ICheckoutTransactionGateway>(gateway);
            services.AddSingleton<IIdempotencyExecutor, PassthroughIdempotencyExecutor>();
            services.AddSingleton<ICheckoutPolicyProvider>(
                new StaticPolicyProvider(policy ?? new CheckoutPolicySnapshot(1, 1, 1, 1)));
        }));

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        bool includeIdempotencyKey)
    {
        using var tokenRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/security/antiforgery-token");
        tokenRequest.Headers.Add(SecurityController.ClientHeaderName, DoSelectClaimValues.Member);
        using var tokenResponse = await client.SendAsync(tokenRequest);
        tokenResponse.EnsureSuccessStatusCode();
        var tokenBody = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(CreateRequest(), options: jsonOptions),
        };
        request.Headers.Add("X-XSRF-TOKEN", tokenBody.GetProperty("requestToken").GetString());
        request.Headers.Add(SecurityController.ClientHeaderName, DoSelectClaimValues.Member);
        request.Headers.Add("X-DoSelect-Guest-Cart-Key", GuestCartKey);
        if (includeIdempotencyKey)
        {
            request.Headers.Add(OrdersController.IdempotencyKeyHeaderName, "checkout-api-key");
        }

        return await client.SendAsync(request);
    }

    private static CreateOrderRequest CreateRequest() => new(
        Guid.NewGuid(),
        [1],
        new CheckoutBuyerInput("guest@example.test", "Guest", "0912345678"),
        new CheckoutShippingInput("CVS_PICKUP", null, Guid.NewGuid()),
        PaymentMethod.CreditCard,
        null,
        new CheckoutInvoiceInput(
            CheckoutInvoiceType.Simulated,
            CheckoutInvoiceBuyerType.Personal,
            null,
            null,
            null,
            null),
        new AcceptedPolicyVersions(1, 1, 1));

    private static OrderDto CreateOrderDto() => new(
        Guid.NewGuid(),
        "DS202609010001",
        OrderStatus.PendingPayment,
        PaymentStatus.AwaitingPayment,
        FulfillmentStatus.Pending,
        AssemblyStatus.NotRequired,
        OrderRefundStatus.None,
        [new OrderItemDto(Guid.NewGuid(), "SKU-1", "Product", "SKU", 1, 1_000m, 1_000m, 1, 0)],
        new OrderRecipientSummaryDto("Guest", "CVS_PICKUP", "Store"),
        new OrderAmountsDto(1_000m, 0m, 150m, 0m, 1_150m, 0m, 0m, "TWD"),
        DateTime.UtcNow.AddDays(3),
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        ["cancel"],
        [1, 2, 3, 4, 5, 6, 7, 8]);

    private sealed class FakeGateway(OrderDto result) : ICheckoutTransactionGateway
    {
        public int Calls { get; private set; }
        public CheckoutCommand? Command { get; private set; }

        public Task<OrderDto> ExecuteAsync(
            CheckoutCommand command,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Command = command;
            return Task.FromResult(result);
        }

        public Task<OrderDto?> FindCreatedOrderAsync(
            Guid orderPublicId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OrderDto?>(orderPublicId == result.PublicId ? result : null);
    }

    private sealed class PassthroughIdempotencyExecutor : IIdempotencyExecutor
    {
        public async Task<IdempotencyExecutionResult<T>> ExecuteAsync<T>(
            IdempotencyCommand command,
            Func<CancellationToken, Task<IdempotencyResponse<T>>> handler,
            Func<StoredIdempotencyResponse, CancellationToken, Task<T>> replayFactory,
            CancellationToken cancellationToken,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            var response = await handler(cancellationToken);
            return new IdempotencyExecutionResult<T>(
                response.StatusCode,
                response.Body,
                response.ResponseHeadersJson,
                IsReplay: false);
        }
    }

    private sealed class StaticPolicyProvider(CheckoutPolicySnapshot current)
        : ICheckoutPolicyProvider
    {
        public CheckoutPolicySnapshot Current => current;
    }
}
