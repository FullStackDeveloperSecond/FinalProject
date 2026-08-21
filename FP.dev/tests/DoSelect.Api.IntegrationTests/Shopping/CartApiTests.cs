using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Domain.Catalog;

namespace DoSelect.Api.IntegrationTests.Shopping;

/// <summary>
/// HTTP-layer coverage for <c>CartController</c>: routing, the mixed guest/member identity
/// resolution, JSON (de)serialization, and ProblemDetails status-code mapping. The equivalent
/// business rules are already unit-tested in-process at DoSelect.Infrastructure.Tests
/// (CartServiceTests) — this layer only proves the wiring on top of that.
/// </summary>
[Collection(nameof(CartApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class CartApiTests
{
    private const string GuestHeaderName = "X-DoSelect-Guest-Cart-Key";

    private readonly CartApiFixture _fixture;

    public CartApiTests(CartApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetCart_WhenGuestHeaderPresent_ReturnsOkWithEmptyCart()
    {
        using var client = _fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cart");
        request.Headers.Add(GuestHeaderName, CartApiFixture.UniqueGuestKey());

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task GetCart_WhenNoIdentityIsPresent_Returns400WithValidationFailed()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.GetAsync("/api/v1/cart");
        var (status, code, _) = await CartApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("validation_failed", code);
    }

    [Fact]
    public async Task AddItem_WhenSkuIsPublished_ReturnsOkWithTheItem()
    {
        using var client = _fixture.CreateClient();
        var guestKey = CartApiFixture.UniqueGuestKey();
        Sku sku;
        await using (var context = _fixture.CreateScopedContext())
        {
            sku = await CartApiSeeding.CreatePublishedSkuAsync(context, listPrice: 1500m);
        }

        using var response = await PostAddItemAsync(client, guestKey, sku.PublicId, quantity: 3);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(sku.PublicId, item.GetProperty("skuPublicId").GetGuid());
        Assert.Equal(3, item.GetProperty("quantity").GetInt32());
        Assert.Equal(1500m, item.GetProperty("unitPrice").GetDecimal());
    }

    [Fact]
    public async Task AddItem_WhenSkuDoesNotExist_Returns404WithResourceNotFound()
    {
        using var client = _fixture.CreateClient();
        var guestKey = CartApiFixture.UniqueGuestKey();

        using var response = await PostAddItemAsync(client, guestKey, Guid.NewGuid(), quantity: 1);
        var (status, code, _) = await CartApiFixture.ReadProblemAsync(response);

        Assert.Equal(404, status);
        Assert.Equal("resource_not_found", code);
    }

    [Fact]
    public async Task AddItem_WhenSkuIsNotPublished_Returns409WithSkuUnavailable()
    {
        using var client = _fixture.CreateClient();
        var guestKey = CartApiFixture.UniqueGuestKey();
        Sku sku;
        await using (var context = _fixture.CreateScopedContext())
        {
            sku = await CartApiSeeding.CreatePublishedSkuAsync(context, listPrice: 100m);
            sku.ChangeStatus(SkuStatus.Unpublished, DateTime.UtcNow);
            await context.SaveChangesAsync();
        }

        using var response = await PostAddItemAsync(client, guestKey, sku.PublicId, quantity: 1);
        var (status, code, _) = await CartApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("sku_unavailable", code);
    }

    [Fact]
    public async Task UpdateItemQuantity_WhenSuccessful_ReturnsOkWithNewQuantityAndRowVersion()
    {
        using var client = _fixture.CreateClient();
        var guestKey = CartApiFixture.UniqueGuestKey();
        Sku sku;
        await using (var context = _fixture.CreateScopedContext())
        {
            sku = await CartApiSeeding.CreatePublishedSkuAsync(context);
        }

        using var addResponse = await PostAddItemAsync(client, guestKey, sku.PublicId, quantity: 1);
        var addBody = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        var item = addBody.GetProperty("items")[0];
        var itemPublicId = item.GetProperty("publicId").GetGuid();
        var itemRowVersion = item.GetProperty("rowVersion").GetString();
        var cartRowVersion = addBody.GetProperty("rowVersion").GetString();

        using var updateRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/cart/items/{itemPublicId}")
        {
            Content = JsonContent.Create(new { quantity = 5, itemRowVersion, cartRowVersion }),
        };
        updateRequest.Headers.Add(GuestHeaderName, guestKey);
        using var updateResponse = await SendWithAntiforgeryAsync(client, updateRequest);
        var updateBody = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updatedItem = updateBody.GetProperty("items")[0];
        Assert.Equal(5, updatedItem.GetProperty("quantity").GetInt32());
        Assert.NotEqual(itemRowVersion, updatedItem.GetProperty("rowVersion").GetString());
    }

    [Fact]
    public async Task UpdateItemQuantity_WhenRowVersionIsStale_Returns409WithConcurrencyConflict()
    {
        using var client = _fixture.CreateClient();
        var guestKey = CartApiFixture.UniqueGuestKey();
        Sku sku;
        await using (var context = _fixture.CreateScopedContext())
        {
            sku = await CartApiSeeding.CreatePublishedSkuAsync(context);
        }

        using var addResponse = await PostAddItemAsync(client, guestKey, sku.PublicId, quantity: 1);
        var addBody = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        var item = addBody.GetProperty("items")[0];
        var itemPublicId = item.GetProperty("publicId").GetGuid();
        var staleItemRowVersion = item.GetProperty("rowVersion").GetString();
        var staleCartRowVersion = addBody.GetProperty("rowVersion").GetString();

        using var firstUpdate = await SendWithAntiforgeryAsync(client, GuestPatch(guestKey, itemPublicId, 2, staleItemRowVersion, staleCartRowVersion));
        firstUpdate.EnsureSuccessStatusCode();

        using var response = await SendWithAntiforgeryAsync(client, GuestPatch(guestKey, itemPublicId, 3, staleItemRowVersion, staleCartRowVersion));
        var (status, code, _) = await CartApiFixture.ReadProblemAsync(response);

        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", code);
    }

    [Fact]
    public async Task RemoveItem_WhenSuccessful_ReturnsOkWithEmptyCart()
    {
        using var client = _fixture.CreateClient();
        var guestKey = CartApiFixture.UniqueGuestKey();
        Sku sku;
        await using (var context = _fixture.CreateScopedContext())
        {
            sku = await CartApiSeeding.CreatePublishedSkuAsync(context);
        }

        using var addResponse = await PostAddItemAsync(client, guestKey, sku.PublicId, quantity: 1);
        var addBody = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        var item = addBody.GetProperty("items")[0];
        var itemPublicId = item.GetProperty("publicId").GetGuid();
        var itemRowVersion = item.GetProperty("rowVersion").GetString();

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/cart/items/{itemPublicId}")
        {
            Content = JsonContent.Create(new { itemRowVersion }),
        };
        deleteRequest.Headers.Add(GuestHeaderName, guestKey);
        using var response = await SendWithAntiforgeryAsync(client, deleteRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task AddItem_WithAuthenticatedMember_IsScopedToTheMemberNotAGuestKey()
    {
        using var client = await _fixture.CreateAuthenticatedMemberClientAsync();
        Sku sku;
        await using (var context = _fixture.CreateScopedContext())
        {
            sku = await CartApiSeeding.CreatePublishedSkuAsync(context);
        }

        using var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cart/items")
        {
            Content = JsonContent.Create(new { skuPublicId = sku.PublicId, quantity = 1, cartRowVersion = (string?)null }),
        };
        using var addResponse = await CartApiFixture.SendWithAntiforgeryAsync(client, addRequest);
        addResponse.EnsureSuccessStatusCode();

        using var getResponse = await client.GetAsync("/api/v1/cart");
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(1, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Revalidate_WhenSkuBecomesUnpublished_ReturnsAnIssueAndBlocksCheckout()
    {
        using var client = _fixture.CreateClient();
        var guestKey = CartApiFixture.UniqueGuestKey();
        Sku sku;
        await using (var context = _fixture.CreateScopedContext())
        {
            sku = await CartApiSeeding.CreatePublishedSkuAsync(context);
        }

        using var addResponse = await PostAddItemAsync(client, guestKey, sku.PublicId, quantity: 1);
        addResponse.EnsureSuccessStatusCode();

        await using (var context = _fixture.CreateScopedContext())
        {
            var tracked = await context.Skus.FindAsync(sku.Id);
            tracked!.ChangeStatus(SkuStatus.Unpublished, DateTime.UtcNow);
            await context.SaveChangesAsync();
        }

        using var revalidateRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cart/actions/revalidate");
        revalidateRequest.Headers.Add(GuestHeaderName, guestKey);
        using var response = await SendWithAntiforgeryAsync(client, revalidateRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.GetProperty("isCheckoutReady").GetBoolean());
        Assert.True(body.GetProperty("issues").GetArrayLength() >= 1);
    }

    private static Task<HttpResponseMessage> PostAddItemAsync(
        HttpClient client,
        string guestKey,
        Guid skuPublicId,
        int quantity)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cart/items")
        {
            Content = JsonContent.Create(new { skuPublicId, quantity, cartRowVersion = (string?)null }),
        };
        request.Headers.Add(GuestHeaderName, guestKey);
        return SendWithAntiforgeryAsync(client, request);
    }

    private static HttpRequestMessage GuestPatch(
        string guestKey,
        Guid itemPublicId,
        int quantity,
        string? itemRowVersion,
        string? cartRowVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/cart/items/{itemPublicId}")
        {
            Content = JsonContent.Create(new { quantity, itemRowVersion, cartRowVersion }),
        };
        request.Headers.Add(GuestHeaderName, guestKey);
        return request;
    }

    private static Task<HttpResponseMessage> SendWithAntiforgeryAsync(HttpClient client, HttpRequestMessage request) =>
        CartApiFixture.SendWithAntiforgeryAsync(client, request);
}
