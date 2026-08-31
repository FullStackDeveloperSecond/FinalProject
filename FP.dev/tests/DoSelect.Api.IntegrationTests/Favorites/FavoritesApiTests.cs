using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.IntegrationTests.Catalog;
using DoSelect.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Favorites;

[Collection(nameof(FavoritesApiCollection))]
public sealed class FavoritesApiTests(FavoritesApiFixture fixture)
{
    [Fact]
    public async Task AnonymousRequest_IsRejected()
    {
        using var client = fixture.CreateClient();

        using var list = await client.GetAsync("/api/v1/members/me/favorites");
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
    }

    [Fact]
    public async Task Add_IsIdempotent_AndDoesNotCreateASecondRow()
    {
        var (client, memberUserId) = await fixture.CreateAuthenticatedMemberClientAsync();
        Guid productPublicId;
        await using (var context = fixture.CreateScopedContext())
        {
            var (product, _, _) = await ProductsApiSeeding.CreatePublishedProductAsync(context);
            productPublicId = product.PublicId;
        }

        using var first = await SendWithAntiforgeryAsync(
            client, HttpMethod.Post, "/api/v1/members/me/favorites", new { productPublicId });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await SendWithAntiforgeryAsync(
            client, HttpMethod.Post, "/api/v1/members/me/favorites", new { productPublicId });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        await using var verifyContext = fixture.CreateScopedContext();
        var count = await verifyContext.Favorites.CountAsync(
            favorite => favorite.MemberUserId == memberUserId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Add_UnknownProduct_ReturnsNotFound()
    {
        var (client, _) = await fixture.CreateAuthenticatedMemberClientAsync();

        using var response = await SendWithAntiforgeryAsync(
            client, HttpMethod.Post, "/api/v1/members/me/favorites", new { productPublicId = Guid.NewGuid() });

        var (status, code, _) = await FavoritesApiFixture.ReadProblemAsync(response);
        Assert.Equal((int)HttpStatusCode.NotFound, status);
        Assert.Equal("favorite_product_not_found", code);
    }

    [Fact]
    public async Task List_ReflectsOutOfStockAndUnlistedProductsWithoutRemovingTheFavorite()
    {
        var (client, _) = await fixture.CreateAuthenticatedMemberClientAsync();
        Guid availableProductId, outOfStockProductId, unlistedProductId;
        await using (var context = fixture.CreateScopedContext())
        {
            var (available, _, _) = await ProductsApiSeeding.CreatePublishedProductAsync(context, onHandQuantity: 5);
            availableProductId = available.PublicId;

            var (outOfStock, _, _) = await ProductsApiSeeding.CreatePublishedProductAsync(context, onHandQuantity: 0);
            outOfStockProductId = outOfStock.PublicId;

            var (unlisted, _, _) = await ProductsApiSeeding.CreatePublishedProductAsync(context, onHandQuantity: 5);
            unlisted.ChangeStatus(ProductStatus.Unpublished, DateTime.UtcNow);
            await context.SaveChangesAsync();
            unlistedProductId = unlisted.PublicId;
        }

        foreach (var productPublicId in new[] { availableProductId, outOfStockProductId, unlistedProductId })
        {
            using var add = await SendWithAntiforgeryAsync(
                client, HttpMethod.Post, "/api/v1/members/me/favorites", new { productPublicId });
            add.EnsureSuccessStatusCode();
        }

        using var list = await client.GetAsync("/api/v1/members/me/favorites");
        list.EnsureSuccessStatusCode();
        var favorites = await list.Content.ReadFromJsonAsync<JsonElement>();
        var byProduct = favorites.EnumerateArray()
            .ToDictionary(item => item.GetProperty("product").GetProperty("productPublicId").GetGuid());

        Assert.Equal("available", byProduct[availableProductId].GetProperty("product").GetProperty("availability").GetString());
        Assert.Equal("outOfStock", byProduct[outOfStockProductId].GetProperty("product").GetProperty("availability").GetString());
        Assert.Equal("unlisted", byProduct[unlistedProductId].GetProperty("product").GetProperty("availability").GetString());
    }

    [Fact]
    public async Task Remove_IsIdempotent_AndOnlyAffectsTheOwningMember()
    {
        var (ownerClient, ownerId) = await fixture.CreateAuthenticatedMemberClientAsync();
        var (otherClient, _) = await fixture.CreateAuthenticatedMemberClientAsync();
        Guid productPublicId;
        await using (var context = fixture.CreateScopedContext())
        {
            var (product, _, _) = await ProductsApiSeeding.CreatePublishedProductAsync(context);
            productPublicId = product.PublicId;
        }

        using var add = await SendWithAntiforgeryAsync(
            ownerClient, HttpMethod.Post, "/api/v1/members/me/favorites", new { productPublicId });
        add.EnsureSuccessStatusCode();

        // Another member removing the same productPublicId only ever acts on their own scope
        // (memberUserId comes from the caller's own claim, not a route parameter) — it must not
        // touch the owner's favorite.
        using (var otherRemove = await SendWithAntiforgeryAsync(
                   otherClient, HttpMethod.Delete, $"/api/v1/members/me/favorites/{productPublicId}", body: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, otherRemove.StatusCode);
        }

        await using (var context = fixture.CreateScopedContext())
        {
            var stillFavorited = await context.Favorites.AnyAsync(
                favorite => favorite.MemberUserId == ownerId);
            Assert.True(stillFavorited);
        }

        using (var ownerRemove = await SendWithAntiforgeryAsync(
                   ownerClient, HttpMethod.Delete, $"/api/v1/members/me/favorites/{productPublicId}", body: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, ownerRemove.StatusCode);
        }

        // Idempotent: removing again (already gone) still succeeds.
        using (var repeatRemove = await SendWithAntiforgeryAsync(
                   ownerClient, HttpMethod.Delete, $"/api/v1/members/me/favorites/{productPublicId}", body: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, repeatRemove.StatusCode);
        }
    }

    private static async Task<HttpResponseMessage> SendWithAntiforgeryAsync(
        HttpClient client, HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await FavoritesApiFixture.SendWithAntiforgeryAsync(client, request);
    }
}
