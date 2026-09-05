using System.Net;
using System.Text.Json;
using DoSelect.Api.Common;
using DoSelect.Api.IntegrationTests.Support;
using DoSelect.Api.Security;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Favorites;

/// <summary>
/// End-to-end tests against the real ASP.NET Core pipeline (Controller -> Application ->
/// Infrastructure -> SQL Server), mirroring SupportTicketsControllerTests' shape. Every action
/// is Actor-scoped to the caller's own MemberUserId via ClaimTypes.NameIdentifier, so there is
/// no other-member's-id route parameter to probe — the equivalent negative case here is proving
/// one member's list never surfaces another member's favorite.
/// </summary>
[Collection(nameof(FavoritesApiCollection))]
public sealed class FavoritesControllerTests : IAsyncLifetime
{
    private const string MemberAEmail = "favorites-test-member-a@doselect.local";
    private const string MemberBEmail = "favorites-test-member-b@doselect.local";

    private readonly WebApplicationFactory<Program> _factory;
    private string _memberAId = string.Empty;
    private string _memberBId = string.Empty;
    private Guid _productPublicId;

    public FavoritesControllerTests(FavoritesApiFixture fixture)
    {
        _factory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                TestAuthHandler.Configure(services);
            });
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        _memberAId = await EnsureMemberAsync(dbContext, MemberAEmail);
        _memberBId = await EnsureMemberAsync(dbContext, MemberBEmail);
        _productPublicId = await EnsureProductAsync(dbContext);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_WhenAnonymous_Returns401WithAuthenticationRequiredCode()
    {
        using var client = CreateClient(memberUserId: null);

        using var response = await client.GetAsync("/api/v1/members/me/favorites");
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApiErrorCodes.AuthenticationRequired, document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Add_WhenProductPublicIdDoesNotResolve_Returns404()
    {
        using var client = CreateClient(_memberAId);

        using var response = await client.PutWithAntiforgeryAsync(
            $"/api/v1/members/me/favorites/{Guid.NewGuid()}",
            DoSelectClaimValues.Member);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ApiErrorCodes.ResourceNotFound, document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AddThenList_RoundTripsTheFavoritedProduct()
    {
        using var client = CreateClient(_memberAId);

        using var addResponse = await client.PutWithAntiforgeryAsync(
            $"/api/v1/members/me/favorites/{_productPublicId}",
            DoSelectClaimValues.Member);
        Assert.Equal(HttpStatusCode.NoContent, addResponse.StatusCode);

        using var listResponse = await client.GetAsync("/api/v1/members/me/favorites");
        using var list = await ReadJsonAsync(listResponse);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var item = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(_productPublicId, item.GetProperty("productPublicId").GetGuid());
    }

    // 評價收藏檢舉與模擬發票規格.md: MemberId + ProductId 唯一，重複加入視為成功且不建立第二筆。
    [Fact]
    public async Task Add_CalledTwice_StaysIdempotentWithOneRow()
    {
        using var client = CreateClient(_memberAId);

        using var firstResponse = await client.PutWithAntiforgeryAsync(
            $"/api/v1/members/me/favorites/{_productPublicId}",
            DoSelectClaimValues.Member);
        using var secondResponse = await client.PutWithAntiforgeryAsync(
            $"/api/v1/members/me/favorites/{_productPublicId}",
            DoSelectClaimValues.Member);

        Assert.Equal(HttpStatusCode.NoContent, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondResponse.StatusCode);

        using var listResponse = await client.GetAsync("/api/v1/members/me/favorites");
        using var list = await ReadJsonAsync(listResponse);
        Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Remove_WhenNeverFavorited_StillReturns204()
    {
        using var client = CreateClient(_memberAId);

        using var response = await client.DeleteWithAntiforgeryAsync(
            $"/api/v1/members/me/favorites/{_productPublicId}",
            DoSelectClaimValues.Member);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AddThenRemove_RemovesItFromTheList()
    {
        using var client = CreateClient(_memberAId);
        using var addResponse = await client.PutWithAntiforgeryAsync(
            $"/api/v1/members/me/favorites/{_productPublicId}",
            DoSelectClaimValues.Member);
        Assert.Equal(HttpStatusCode.NoContent, addResponse.StatusCode);

        using var removeResponse = await client.DeleteWithAntiforgeryAsync(
            $"/api/v1/members/me/favorites/{_productPublicId}",
            DoSelectClaimValues.Member);
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        using var listResponse = await client.GetAsync("/api/v1/members/me/favorites");
        using var list = await ReadJsonAsync(listResponse);
        Assert.Empty(list.RootElement.GetProperty("items").EnumerateArray());
    }

    /// <summary>
    /// Actor Scope isolation: this endpoint has no other-member's-id route parameter to probe
    /// (it always resolves "me" server-side from the auth claim), so the meaningful negative
    /// case is that Member A's favorite is simply never visible to Member B's own list call.
    /// </summary>
    [Fact]
    public async Task List_WhenProductIsFavoritedByActorA_IsNotVisibleToActorB()
    {
        using var actorAClient = CreateClient(_memberAId);
        using var addResponse = await actorAClient.PutWithAntiforgeryAsync(
            $"/api/v1/members/me/favorites/{_productPublicId}",
            DoSelectClaimValues.Member);
        Assert.Equal(HttpStatusCode.NoContent, addResponse.StatusCode);

        using var actorBClient = CreateClient(_memberBId);
        using var actorBListResponse = await actorBClient.GetAsync("/api/v1/members/me/favorites");
        using var actorBList = await ReadJsonAsync(actorBListResponse);

        Assert.Equal(HttpStatusCode.OK, actorBListResponse.StatusCode);
        Assert.Empty(actorBList.RootElement.GetProperty("items").EnumerateArray());
    }

    /// <summary>
    /// Actor Scope isolation, write side: Member B deleting Member A's favorite by product id
    /// must not be able to affect Member A's row — DELETE is scoped to (caller's own
    /// MemberUserId, productId), so it can only ever remove a row that belongs to Member B.
    /// </summary>
    [Fact]
    public async Task Remove_ByActorB_DoesNotAffectActorAsFavorite()
    {
        using var actorAClient = CreateClient(_memberAId);
        using var addResponse = await actorAClient.PutWithAntiforgeryAsync(
            $"/api/v1/members/me/favorites/{_productPublicId}",
            DoSelectClaimValues.Member);
        Assert.Equal(HttpStatusCode.NoContent, addResponse.StatusCode);

        using var actorBClient = CreateClient(_memberBId);
        using var removeResponse = await actorBClient.DeleteWithAntiforgeryAsync(
            $"/api/v1/members/me/favorites/{_productPublicId}",
            DoSelectClaimValues.Member);
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        using var actorAListResponse = await actorAClient.GetAsync("/api/v1/members/me/favorites");
        using var actorAList = await ReadJsonAsync(actorAListResponse);
        Assert.Single(actorAList.RootElement.GetProperty("items").EnumerateArray());
    }

    private HttpClient CreateClient(string? memberUserId)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        if (memberUserId is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.MemberHeaderName, memberUserId);
        }

        return client;
    }

    private static async Task<string> EnsureMemberAsync(DoSelectDbContext dbContext, string email)
    {
        var existing = await dbContext.Users.SingleOrDefaultAsync(u => u.Email == email);
        if (existing is not null)
        {
            return existing.Id;
        }

        var user = ApplicationUser.CreateMember(Guid.NewGuid(), email, DateTime.UtcNow);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<Guid> EnsureProductAsync(DoSelectDbContext dbContext)
    {
        const string productCode = "FAVORITES-API-TEST-PRODUCT";
        var existing = await dbContext.Products.AsNoTracking()
            .SingleOrDefaultAsync(product => product.ProductCode == productCode);
        if (existing is not null)
        {
            return existing.PublicId;
        }

        var now = DateTime.UtcNow;
        var brand = new Brand(Guid.CreateVersion7(), "FAVORITES-API-TEST-BRAND", "測試品牌", now);
        var category = new Category(Guid.CreateVersion7(), "FAVORITES-API-TEST-CATEGORY", "favorites-api-test", "測試分類", null, now);
        dbContext.Brands.Add(brand);
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync();

        var product = new Product(Guid.CreateVersion7(), productCode, brand.Id, category.Id, "收藏測試商品", now);
        product.ChangeStatus(ProductStatus.Published, now);
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        var sku = new Sku(Guid.CreateVersion7(), $"{productCode}-SKU", product.Id, "收藏測試商品 SKU", 1_000m, 700m, now);
        sku.ChangeStatus(SkuStatus.Published, now);
        sku.UpdateCommercialDetails(sku.NameZhTw, sku.ListPrice, sku.UnitCost, isDefault: true, requiresPrepayment: false, now);
        dbContext.Skus.Add(sku);
        await dbContext.SaveChangesAsync();

        dbContext.InventoryBalances.Add(
            new InventoryBalance(Guid.CreateVersion7(), sku.Id, onHandQuantity: 5, reorderLevel: 1, now));
        await dbContext.SaveChangesAsync();

        return product.PublicId;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}
