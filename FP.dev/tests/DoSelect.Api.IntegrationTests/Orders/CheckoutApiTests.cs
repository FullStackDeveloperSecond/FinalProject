using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.IntegrationTests.Shopping;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Shipping;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Orders;

/// <summary>
/// HTTP-layer coverage for <c>POST /api/v1/orders</c> (UC-CHECKOUT-01): routing, the mixed
/// guest/member identity resolution shared with CartController, and that the response is a full
/// OrderDto rather than the thinner CheckoutCreatedOrder the Application layer returns. The
/// underlying transaction (stock, coupon, payment attempt, cart conversion) is already covered by
/// EfCheckoutTransactionGatewayTests — this layer only proves the API wiring on top of that.
/// </summary>
[Collection(nameof(CartApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class CheckoutApiTests
{
    private const string GuestHeaderName = "X-DoSelect-Guest-Cart-Key";
    private const string IdempotencyHeaderName = "Idempotency-Key";

    private readonly CartApiFixture _fixture;

    public CheckoutApiTests(CartApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateOrder_AsGuestWithValidCart_Returns201WithFullOrder()
    {
        using var client = _fixture.CreateClient();
        var guestKey = CartApiFixture.UniqueGuestKey();
        ShippingMethod method;
        Sku sku;
        await using (var context = _fixture.CreateScopedContext())
        {
            method = await CheckoutApiSeeding.SeedHomeDeliveryShippingMethodAsync(context);
            sku = await CheckoutApiSeeding.CreatePurchasableSkuAsync(context, listPrice: 1500m);
        }

        var cart = await AddItemAsync(client, guestKey, sku.PublicId, quantity: 2);

        using var response = await SendCreateOrderAsync(
            client, guestKey, cart, method.Code, Guid.NewGuid().ToString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Expected 201 but received {(int)response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal("pendingPayment", root.GetProperty("orderStatus").GetString());
        Assert.Equal(2, root.GetProperty("items")[0].GetProperty("quantity").GetInt32());
        Assert.Equal(3150m, root.GetProperty("amounts").GetProperty("grandTotal").GetDecimal());
        Assert.NotEqual(Guid.Empty, root.GetProperty("publicId").GetGuid());

        // The order the guest just created must be immediately readable through the ordinary
        // GET /orders/{id} + GuestOrderAccess flow's replacement here: no session was minted by
        // Checkout itself, only the confirmation response above proves ownership.
        await using var dbContext = _fixture.CreateScopedContext();
        var persisted = await dbContext.Orders.SingleAsync(
            order => order.PublicId == root.GetProperty("publicId").GetGuid());
        Assert.Null(persisted.MemberUserId);
    }

    [Fact]
    public async Task CreateOrder_AsAuthenticatedMember_Returns201AndTheOrderIsOwnedByThatMember()
    {
        var client = await _fixture.CreateAuthenticatedMemberClientAsync();
        ShippingMethod method;
        Sku sku;
        await using (var context = _fixture.CreateScopedContext())
        {
            method = await CheckoutApiSeeding.SeedHomeDeliveryShippingMethodAsync(context);
            sku = await CheckoutApiSeeding.CreatePurchasableSkuAsync(context, listPrice: 800m);
        }

        var cart = await AddMemberItemAsync(client, sku.PublicId, quantity: 1);

        using var response = await SendCreateOrderAsync(
            client, guestKey: null, cart, method.Code, Guid.NewGuid().ToString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Expected 201 but received {(int)response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        var orderPublicId = document.RootElement.GetProperty("publicId").GetGuid();

        // Round-trips through the ordinary member-owned GET, proving CheckoutActor.ForMember's
        // resolved MemberPublicId did not stop the order from actually being linked by MemberUserId.
        using var getResponse = await client.GetAsync($"/api/v1/orders/{orderPublicId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_WhenIdempotencyKeyIsReplayed_ReturnsTheSameOrderOnce()
    {
        using var client = _fixture.CreateClient();
        var guestKey = CartApiFixture.UniqueGuestKey();
        ShippingMethod method;
        Sku sku;
        await using (var context = _fixture.CreateScopedContext())
        {
            method = await CheckoutApiSeeding.SeedHomeDeliveryShippingMethodAsync(context);
            sku = await CheckoutApiSeeding.CreatePurchasableSkuAsync(context);
        }

        var cart = await AddItemAsync(client, guestKey, sku.PublicId, quantity: 1);
        var idempotencyKey = Guid.NewGuid().ToString();

        using var firstResponse = await SendCreateOrderAsync(client, guestKey, cart, method.Code, idempotencyKey);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstOrderPublicId = firstBody.GetProperty("publicId").GetGuid();

        using var replayResponse = await SendCreateOrderAsync(client, guestKey, cart, method.Code, idempotencyKey);
        var replayBody = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        Assert.Equal(firstOrderPublicId, replayBody.GetProperty("publicId").GetGuid());

        await using var dbContext = _fixture.CreateScopedContext();
        Assert.Equal(1, await dbContext.Orders.CountAsync(order => order.PublicId == firstOrderPublicId));
    }

    [Fact]
    public async Task CreateOrder_WhenNoIdentityIsPresent_Returns400WithValidationFailed()
    {
        using var client = _fixture.CreateClient();
        var (cartPublicId, cartRowVersion) = (Guid.NewGuid(), Convert.ToBase64String([1, 2, 3]));

        using var request = BuildRequest(cartPublicId, cartRowVersion, "HOME-DOES-NOT-MATTER");
        request.Headers.Add(IdempotencyHeaderName, Guid.NewGuid().ToString());
        using var response = await CartApiFixture.SendWithAntiforgeryAsync(client, request);
        var (status, code, _) = await CartApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("validation_failed", code);
    }

    [Fact]
    public async Task CreateOrder_WhenIdempotencyKeyHeaderIsMissing_Returns400()
    {
        using var client = _fixture.CreateClient();
        var guestKey = CartApiFixture.UniqueGuestKey();

        using var request = BuildRequest(Guid.NewGuid(), Convert.ToBase64String([1, 2, 3]), "HOME-DOES-NOT-MATTER");
        request.Headers.Add(GuestHeaderName, guestKey);
        using var response = await CartApiFixture.SendWithAntiforgeryAsync(client, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<(Guid PublicId, string RowVersion)> AddItemAsync(
        HttpClient client, string guestKey, Guid skuPublicId, int quantity)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cart/items")
        {
            Content = JsonContent.Create(new { skuPublicId, quantity, cartRowVersion = (string?)null }),
        };
        request.Headers.Add(GuestHeaderName, guestKey);
        using var response = await CartApiFixture.SendWithAntiforgeryAsync(client, request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (
            body.GetProperty("publicId").GetGuid(),
            Convert.ToBase64String(body.GetProperty("rowVersion").GetBytesFromBase64()));
    }

    private static async Task<(Guid PublicId, string RowVersion)> AddMemberItemAsync(
        HttpClient client, Guid skuPublicId, int quantity)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cart/items")
        {
            Content = JsonContent.Create(new { skuPublicId, quantity, cartRowVersion = (string?)null }),
        };
        using var response = await CartApiFixture.SendWithAntiforgeryAsync(client, request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (
            body.GetProperty("publicId").GetGuid(),
            Convert.ToBase64String(body.GetProperty("rowVersion").GetBytesFromBase64()));
    }

    private static Task<HttpResponseMessage> SendCreateOrderAsync(
        HttpClient client,
        string? guestKey,
        (Guid PublicId, string RowVersion) cart,
        string shippingMethodCode,
        string idempotencyKey)
    {
        var request = BuildRequest(cart.PublicId, cart.RowVersion, shippingMethodCode);
        if (guestKey is not null)
        {
            request.Headers.Add(GuestHeaderName, guestKey);
        }

        request.Headers.Add(IdempotencyHeaderName, idempotencyKey);
        return CartApiFixture.SendWithAntiforgeryAsync(client, request);
    }

    private static HttpRequestMessage BuildRequest(Guid cartPublicId, string cartRowVersion, string shippingMethodCode) =>
        new(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(new
            {
                cartPublicId,
                cartRowVersion,
                buyer = new
                {
                    email = "checkout-api-test@example.test",
                    name = "Checkout Tester",
                    phone = "0912345678",
                },
                shipping = new
                {
                    methodCode = shippingMethodCode,
                    address = new
                    {
                        recipientName = "Checkout Tester",
                        phone = "0912345678",
                        postalCode = "100",
                        city = "Taipei",
                        district = "Zhongzheng",
                        addressLine1 = "No. 1",
                        addressLine2 = (string?)null,
                    },
                    storePublicId = (Guid?)null,
                    deliveryNote = (string?)null,
                },
                paymentMethod = "creditCard",
                couponCode = (string?)null,
                invoice = new
                {
                    type = "simulated",
                    buyerType = "personal",
                    carrierType = (string?)null,
                    carrierValue = (string?)null,
                    companyTaxId = (string?)null,
                    companyName = (string?)null,
                },
                acceptPolicyVersions = new { terms = 1, @return = 1, privacy = 1 },
            }),
        };
}
