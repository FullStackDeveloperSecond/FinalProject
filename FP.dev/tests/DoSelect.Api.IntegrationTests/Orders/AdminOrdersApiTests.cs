using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Domain.Orders;

namespace DoSelect.Api.IntegrationTests.Orders;

/// <summary>
/// HTTP-layer coverage for AdminOrdersController (UC-ADM-ORDER-01/02): routing, model
/// binding, RowVersion base64 round-tripping, ProblemDetails status-code mapping, and
/// authorization. [Trait("Category", "RequiresSqlServer")] — excluded from required Linux
/// CI per DEV-07, mirrors CatalogAdminApiTests.
/// </summary>
[Collection(nameof(AdminOrdersApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class AdminOrdersApiTests
{
    private readonly AdminOrdersApiFixture _fixture;

    public AdminOrdersApiTests(AdminOrdersApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task List_ReturnsOkWithCursorPage()
    {
        await using var context = _fixture.CreateScopedContext();
        var shippingProfileId = await AdminOrdersApiSeeding.SeedShippingProviderProfileAsync(context);
        await AdminOrdersApiSeeding.SeedOrderAsync(context, shippingProfileId);

        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var response = await client.GetAsync("/api/v1/admin/orders?pageSize=5");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("items").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task List_WithUnknownSummaryStatus_Returns400ValidationFailed()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var response = await client.GetAsync("/api/v1/admin/orders?summaryStatus=not-a-real-status");
        var (status, code, _) = await AdminOrdersApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("validation_failed", code);
    }

    [Fact]
    public async Task GetById_WhenNotFound_Returns404ResourceNotFound()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var response = await client.GetAsync($"/api/v1/admin/orders/{Guid.NewGuid()}");
        var (status, code, _) = await AdminOrdersApiFixture.ReadProblemAsync(response);

        Assert.Equal(404, status);
        Assert.Equal("resource_not_found", code);
    }

    [Fact]
    public async Task GetById_ForConfirmedOrder_ExposesStartProcessingAndCancelActions()
    {
        await using var context = _fixture.CreateScopedContext();
        var shippingProfileId = await AdminOrdersApiSeeding.SeedShippingProviderProfileAsync(context);
        var order = await AdminOrdersApiSeeding.SeedOrderAsync(context, shippingProfileId);

        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var response = await client.GetAsync($"/api/v1/admin/orders/{order.PublicId}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actions = body.GetProperty("availableActions").EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();
        Assert.Contains("startProcessing", actions);
        Assert.Contains("cancel", actions);
    }

    [Fact]
    public async Task GetRecipient_ReturnsFullRecipientSnapshot()
    {
        await using var context = _fixture.CreateScopedContext();
        var shippingProfileId = await AdminOrdersApiSeeding.SeedShippingProviderProfileAsync(context);
        var order = await AdminOrdersApiSeeding.SeedOrderAsync(context, shippingProfileId);

        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var response = await client.GetAsync($"/api/v1/admin/orders/{order.PublicId}/recipient");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("測試收件人", body.GetProperty("recipientName").GetString());
        Assert.Equal("buyer@example.com", body.GetProperty("recipientEmail").GetString());
    }

    [Fact]
    public async Task ExecuteAction_StartProcessing_TransitionsOrderToProcessing()
    {
        await using var context = _fixture.CreateScopedContext();
        var shippingProfileId = await AdminOrdersApiSeeding.SeedShippingProviderProfileAsync(context);
        var order = await AdminOrdersApiSeeding.SeedOrderAsync(context, shippingProfileId);
        var adminUserId = await AdminOrdersApiSeeding.SeedAdminUserAsync(context);

        using var client = await _fixture.CreateAuthenticatedAdminClientForUserAsync(adminUserId);
        using var response = await AdminOrdersApiFixture.SendWithAntiforgeryAsync(
            client,
            new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/orders/{order.PublicId}/actions/startProcessing")
            {
                Content = JsonContent.Create(new { reasonCode = (string?)null, note = (string?)null, rowVersion = order.RowVersion }),
            });
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {text}");
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal(nameof(OrderStatus.Processing), body.GetProperty("orderStatus").GetString());
        var history = body.GetProperty("statusHistory").EnumerateArray().ToArray();
        Assert.Contains(history, entry => entry.GetProperty("toStatus").GetString() == nameof(OrderStatus.Processing));
    }

    [Fact]
    public async Task ExecuteAction_CancelWithoutReasonCode_Returns400ValidationFailed()
    {
        await using var context = _fixture.CreateScopedContext();
        var shippingProfileId = await AdminOrdersApiSeeding.SeedShippingProviderProfileAsync(context);
        var order = await AdminOrdersApiSeeding.SeedOrderAsync(context, shippingProfileId);

        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var response = await AdminOrdersApiFixture.SendWithAntiforgeryAsync(
            client,
            new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/orders/{order.PublicId}/actions/cancel")
            {
                Content = JsonContent.Create(new { reasonCode = (string?)null, note = (string?)null, rowVersion = order.RowVersion }),
            });
        var (status, code, _) = await AdminOrdersApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("validation_failed", code);
    }

    [Fact]
    public async Task ExecuteAction_Cancel_TransitionsOrderAndRecordsReason()
    {
        await using var context = _fixture.CreateScopedContext();
        var shippingProfileId = await AdminOrdersApiSeeding.SeedShippingProviderProfileAsync(context);
        var order = await AdminOrdersApiSeeding.SeedOrderAsync(context, shippingProfileId);
        var adminUserId = await AdminOrdersApiSeeding.SeedAdminUserAsync(context);

        using var client = await _fixture.CreateAuthenticatedAdminClientForUserAsync(adminUserId);
        using var response = await AdminOrdersApiFixture.SendWithAntiforgeryAsync(
            client,
            new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/orders/{order.PublicId}/actions/cancel")
            {
                Content = JsonContent.Create(new { reasonCode = "merchant_unfulfillable", note = "缺貨", rowVersion = order.RowVersion }),
            });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(nameof(OrderStatus.Cancelled), body.GetProperty("orderStatus").GetString());
        Assert.Equal("cancelled", body.GetProperty("summaryStatus").GetString());
    }

    [Fact]
    public async Task ExecuteAction_UnknownAction_Returns400ValidationFailed()
    {
        await using var context = _fixture.CreateScopedContext();
        var shippingProfileId = await AdminOrdersApiSeeding.SeedShippingProviderProfileAsync(context);
        var order = await AdminOrdersApiSeeding.SeedOrderAsync(context, shippingProfileId);

        using var client = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var response = await AdminOrdersApiFixture.SendWithAntiforgeryAsync(
            client,
            new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/orders/{order.PublicId}/actions/teleport")
            {
                Content = JsonContent.Create(new { reasonCode = (string?)null, note = (string?)null, rowVersion = order.RowVersion }),
            });
        var (status, code, _) = await AdminOrdersApiFixture.ReadProblemAsync(response);

        Assert.Equal(400, status);
        Assert.Equal("validation_failed", code);
    }

    [Fact]
    public async Task ExecuteAction_WithStaleRowVersion_Returns409ConcurrencyConflict()
    {
        await using var context = _fixture.CreateScopedContext();
        var shippingProfileId = await AdminOrdersApiSeeding.SeedShippingProviderProfileAsync(context);
        var order = await AdminOrdersApiSeeding.SeedOrderAsync(context, shippingProfileId);
        var staleRowVersion = order.RowVersion;
        var adminUserId = await AdminOrdersApiSeeding.SeedAdminUserAsync(context);

        using var client = await _fixture.CreateAuthenticatedAdminClientForUserAsync(adminUserId);
        using var first = await AdminOrdersApiFixture.SendWithAntiforgeryAsync(
            client,
            new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/orders/{order.PublicId}/actions/startProcessing")
            {
                Content = JsonContent.Create(new { reasonCode = (string?)null, note = (string?)null, rowVersion = staleRowVersion }),
            });
        first.EnsureSuccessStatusCode();

        using var second = await AdminOrdersApiFixture.SendWithAntiforgeryAsync(
            client,
            new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/orders/{order.PublicId}/actions/cancel")
            {
                Content = JsonContent.Create(new { reasonCode = "merchant_unfulfillable", note = (string?)null, rowVersion = staleRowVersion }),
            });
        var (status, code, _) = await AdminOrdersApiFixture.ReadProblemAsync(second);

        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", code);
    }

    [Fact]
    public async Task List_WithoutAuthentication_Returns401()
    {
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync("/api/v1/admin/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutOrderManagerRole_Returns403()
    {
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CustomerService);
        using var response = await client.GetAsync("/api/v1/admin/orders");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Actor A/B coverage per Haru-會員與訂單工程包.md §8: Actor A is an OrderManager, Actor B
    // (CustomerService, signed in via the Admin scheme but lacking Order.Manage) must be denied
    // on every endpoint, not just List, with proof that the denial leaked no data and left no
    // state behind.

    [Fact]
    public async Task GetById_WithoutOrderManagerRole_Returns403WithNoOrderDataInBody()
    {
        await using var context = _fixture.CreateScopedContext();
        var shippingProfileId = await AdminOrdersApiSeeding.SeedShippingProviderProfileAsync(context);
        var order = await AdminOrdersApiSeeding.SeedOrderAsync(context, shippingProfileId);

        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CustomerService);
        using var response = await client.GetAsync($"/api/v1/admin/orders/{order.PublicId}");
        var (status, code, body) = await AdminOrdersApiFixture.ReadProblemAsync(response);

        Assert.Equal(403, status);
        Assert.Equal("authorization_forbidden", code);
        Assert.False(body.TryGetProperty("orderStatus", out _));
        Assert.False(body.TryGetProperty("items", out _));
    }

    [Fact]
    public async Task GetRecipient_WithoutOrderManagerRole_Returns403WithNoRecipientDataInBody()
    {
        await using var context = _fixture.CreateScopedContext();
        var shippingProfileId = await AdminOrdersApiSeeding.SeedShippingProviderProfileAsync(context);
        var order = await AdminOrdersApiSeeding.SeedOrderAsync(context, shippingProfileId);

        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CustomerService);
        using var response = await client.GetAsync($"/api/v1/admin/orders/{order.PublicId}/recipient");
        var (status, code, body) = await AdminOrdersApiFixture.ReadProblemAsync(response);

        Assert.Equal(403, status);
        Assert.Equal("authorization_forbidden", code);
        Assert.False(body.TryGetProperty("recipientName", out _));
        Assert.False(body.TryGetProperty("recipientEmail", out _));
        Assert.False(body.TryGetProperty("addressLine1", out _));
    }

    [Fact]
    public async Task ExecuteAction_WithoutOrderManagerRole_Returns403AndLeavesOrderUntouched()
    {
        await using var context = _fixture.CreateScopedContext();
        var shippingProfileId = await AdminOrdersApiSeeding.SeedShippingProviderProfileAsync(context);
        var order = await AdminOrdersApiSeeding.SeedOrderAsync(context, shippingProfileId);

        using var unauthorizedClient = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CustomerService);
        using var forbiddenResponse = await AdminOrdersApiFixture.SendWithAntiforgeryAsync(
            unauthorizedClient,
            new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/orders/{order.PublicId}/actions/startProcessing")
            {
                Content = JsonContent.Create(new { reasonCode = (string?)null, note = (string?)null, rowVersion = order.RowVersion }),
            });
        var (status, code, _) = await AdminOrdersApiFixture.ReadProblemAsync(forbiddenResponse);
        Assert.Equal(403, status);
        Assert.Equal("authorization_forbidden", code);

        // Zero-side-effect evidence: re-read the order through the same API a legitimate
        // OrderManager would use and confirm the denied action changed nothing — status,
        // RowVersion, and status history are all exactly what SeedOrderAsync left behind.
        using var verifyClient = await _fixture.CreateAuthenticatedAdminClientAsync();
        using var verifyResponse = await verifyClient.GetAsync($"/api/v1/admin/orders/{order.PublicId}");
        var body = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(nameof(OrderStatus.Confirmed), body.GetProperty("orderStatus").GetString());
        Assert.Equal(Convert.ToBase64String(order.RowVersion), body.GetProperty("rowVersion").GetString());
        Assert.DoesNotContain(
            body.GetProperty("statusHistory").EnumerateArray(),
            entry => entry.GetProperty("toStatus").GetString() == nameof(OrderStatus.Processing));
    }
}
