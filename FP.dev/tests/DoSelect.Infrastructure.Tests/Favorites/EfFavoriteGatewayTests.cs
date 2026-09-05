using DoSelect.Application.Catalog;
using DoSelect.Application.Favorites;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Favorites;

[CollectionDefinition(nameof(EfFavoriteGatewayCollection))]
public sealed class EfFavoriteGatewayCollection : ICollectionFixture<EfFavoriteGatewayFixture>;

[Collection(nameof(EfFavoriteGatewayCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfFavoriteGatewayTests
{
    [Fact]
    public async Task AddAsync_WhenProductDoesNotExist_ReturnsProductNotFound()
    {
        await using var context = EfFavoriteGatewayFixture.CreateContext();
        var gateway = new EfFavoriteGateway(context, TimeProvider.System);

        var result = await gateway.AddAsync(
            EfFavoriteGatewayFixture.MemberAId,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(AddFavoriteResult.ProductNotFound, result);
    }

    [Fact]
    public async Task AddAsync_CalledTwiceForSameMemberAndProduct_StaysASingleRow()
    {
        await using var context = EfFavoriteGatewayFixture.CreateContext();
        var gateway = new EfFavoriteGateway(context, TimeProvider.System);

        try
        {
            var first = await gateway.AddAsync(
                EfFavoriteGatewayFixture.MemberAId,
                EfFavoriteGatewayFixture.InStockProductPublicId,
                CancellationToken.None);
            var second = await gateway.AddAsync(
                EfFavoriteGatewayFixture.MemberAId,
                EfFavoriteGatewayFixture.InStockProductPublicId,
                CancellationToken.None);

            Assert.Equal(AddFavoriteResult.Success, first);
            Assert.Equal(AddFavoriteResult.Success, second);

            await using var verifyContext = EfFavoriteGatewayFixture.CreateContext();
            var rowCount = await verifyContext.Favorites.CountAsync(favorite =>
                favorite.MemberUserId == EfFavoriteGatewayFixture.MemberAId);
            Assert.Equal(1, rowCount);
        }
        finally
        {
            // The fixture's DB and MemberAId are shared across every [Fact] in this class
            // (ICollectionFixture, initialized once) — leaving this row behind would leak into
            // whichever test runs next, so every test that adds a favorite removes it again.
            await gateway.RemoveAsync(
                EfFavoriteGatewayFixture.MemberAId,
                EfFavoriteGatewayFixture.InStockProductPublicId,
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task RemoveAsync_WhenNeverFavorited_StillSucceeds()
    {
        await using var context = EfFavoriteGatewayFixture.CreateContext();
        var gateway = new EfFavoriteGateway(context, TimeProvider.System);

        // 評價收藏檢舉與模擬發票規格.md: 移除收藏可直接刪除，沒有獨立稽核價值 — a no-op delete must not
        // throw or otherwise behave as an error.
        await gateway.RemoveAsync(
            EfFavoriteGatewayFixture.MemberAId,
            EfFavoriteGatewayFixture.InStockProductPublicId,
            CancellationToken.None);
    }

    [Fact]
    public async Task AddThenRemove_RemovesTheRowAndAnotherMembersFavoriteIsUnaffected()
    {
        await using var context = EfFavoriteGatewayFixture.CreateContext();
        var gateway = new EfFavoriteGateway(context, TimeProvider.System);

        await gateway.AddAsync(
            EfFavoriteGatewayFixture.MemberAId,
            EfFavoriteGatewayFixture.InStockProductPublicId,
            CancellationToken.None);
        await gateway.AddAsync(
            EfFavoriteGatewayFixture.MemberBId,
            EfFavoriteGatewayFixture.InStockProductPublicId,
            CancellationToken.None);

        try
        {
            await gateway.RemoveAsync(
                EfFavoriteGatewayFixture.MemberAId,
                EfFavoriteGatewayFixture.InStockProductPublicId,
                CancellationToken.None);

            var memberAList = await gateway.ListAsync(EfFavoriteGatewayFixture.MemberAId, 1, 20, CancellationToken.None);
            var memberBList = await gateway.ListAsync(EfFavoriteGatewayFixture.MemberBId, 1, 20, CancellationToken.None);

            Assert.Empty(memberAList.Items);
            Assert.Single(memberBList.Items);
        }
        finally
        {
            await gateway.RemoveAsync(
                EfFavoriteGatewayFixture.MemberBId,
                EfFavoriteGatewayFixture.InStockProductPublicId,
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task ListAsync_MapsEachProductToItsAvailabilityState()
    {
        await using var context = EfFavoriteGatewayFixture.CreateContext();
        var gateway = new EfFavoriteGateway(context, TimeProvider.System);

        await gateway.AddAsync(EfFavoriteGatewayFixture.MemberAId, EfFavoriteGatewayFixture.InStockProductPublicId, CancellationToken.None);
        await gateway.AddAsync(EfFavoriteGatewayFixture.MemberAId, EfFavoriteGatewayFixture.OutOfStockProductPublicId, CancellationToken.None);
        await gateway.AddAsync(EfFavoriteGatewayFixture.MemberAId, EfFavoriteGatewayFixture.DelistedProductPublicId, CancellationToken.None);

        try
        {
            var result = await gateway.ListAsync(EfFavoriteGatewayFixture.MemberAId, 1, 20, CancellationToken.None);

            Assert.Equal(3, result.TotalCount);
            var inStock = Assert.Single(result.Items, item => item.ProductPublicId == EfFavoriteGatewayFixture.InStockProductPublicId);
            Assert.Equal(ProductAvailabilityCodes.InStock, inStock.Availability);
            Assert.True(inStock.IsPurchasable);

            var outOfStock = Assert.Single(result.Items, item => item.ProductPublicId == EfFavoriteGatewayFixture.OutOfStockProductPublicId);
            Assert.Equal(ProductAvailabilityCodes.OutOfStock, outOfStock.Availability);
            Assert.False(outOfStock.IsPurchasable);

            // 商品下架時保留但顯示不可購買，不允許由收藏頁加入購物車.
            var delisted = Assert.Single(result.Items, item => item.ProductPublicId == EfFavoriteGatewayFixture.DelistedProductPublicId);
            Assert.Equal(FavoriteAvailabilityCodes.Delisted, delisted.Availability);
            Assert.False(delisted.IsPurchasable);
        }
        finally
        {
            await gateway.RemoveAsync(EfFavoriteGatewayFixture.MemberAId, EfFavoriteGatewayFixture.InStockProductPublicId, CancellationToken.None);
            await gateway.RemoveAsync(EfFavoriteGatewayFixture.MemberAId, EfFavoriteGatewayFixture.OutOfStockProductPublicId, CancellationToken.None);
            await gateway.RemoveAsync(EfFavoriteGatewayFixture.MemberAId, EfFavoriteGatewayFixture.DelistedProductPublicId, CancellationToken.None);
        }
    }

    [Fact]
    public async Task ListAsync_AfterProductIsRepublished_ReturnsToNormalAvailability()
    {
        await using var context = EfFavoriteGatewayFixture.CreateContext();
        var gateway = new EfFavoriteGateway(context, TimeProvider.System);
        await gateway.AddAsync(EfFavoriteGatewayFixture.MemberAId, EfFavoriteGatewayFixture.DelistedProductPublicId, CancellationToken.None);

        try
        {
            // 商品重新上架後原收藏自動恢復正常顯示.
            await using (var writeContext = EfFavoriteGatewayFixture.CreateContext())
            {
                var product = await writeContext.Products.SingleAsync(
                    p => p.PublicId == EfFavoriteGatewayFixture.DelistedProductPublicId);
                product.ChangeStatus(ProductStatus.Published, DateTime.UtcNow);
                await writeContext.SaveChangesAsync();
            }

            var result = await gateway.ListAsync(EfFavoriteGatewayFixture.MemberAId, 1, 20, CancellationToken.None);

            var item = Assert.Single(result.Items, i => i.ProductPublicId == EfFavoriteGatewayFixture.DelistedProductPublicId);
            Assert.NotEqual(FavoriteAvailabilityCodes.Delisted, item.Availability);
            Assert.True(item.IsPurchasable);
        }
        finally
        {
            // The product's status is shared fixture state (ICollectionFixture, initialized
            // once) — other tests in this class rely on DelistedProductPublicId staying
            // Discontinued, so republishing it here must be undone regardless of outcome.
            await gateway.RemoveAsync(EfFavoriteGatewayFixture.MemberAId, EfFavoriteGatewayFixture.DelistedProductPublicId, CancellationToken.None);
            await using var writeContext = EfFavoriteGatewayFixture.CreateContext();
            var product = await writeContext.Products.SingleAsync(
                p => p.PublicId == EfFavoriteGatewayFixture.DelistedProductPublicId);
            product.ChangeStatus(ProductStatus.Discontinued, DateTime.UtcNow);
            await writeContext.SaveChangesAsync();
        }
    }
}

public sealed class EfFavoriteGatewayFixture : IAsyncLifetime
{
    public const string MemberAId = "favorite-gateway-tests-member-a";
    public const string MemberBId = "favorite-gateway-tests-member-b";

    public static Guid InStockProductPublicId { get; private set; }
    public static Guid OutOfStockProductPublicId { get; private set; }
    public static Guid DelistedProductPublicId { get; private set; }

    private static readonly string ConnectionString =
        SqlServerTestConnection.Build("DoSelectFavoriteGatewayTests");

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;

        var memberA = ApplicationUser.CreateMember(Guid.CreateVersion7(), "favorite-tests-member-a@doselect.test", now);
        var memberB = ApplicationUser.CreateMember(Guid.CreateVersion7(), "favorite-tests-member-b@doselect.test", now);
        memberA.Id = MemberAId;
        memberB.Id = MemberBId;
        context.Users.AddRange(memberA, memberB);

        var brand = new Brand(Guid.CreateVersion7(), "FAV-BRAND", "收藏測試品牌", now);
        context.Brands.Add(brand);
        var category = new Category(Guid.CreateVersion7(), "FAV-CAT", "fav-cat", "收藏測試分類", null, now);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var inStockProduct = new Product(Guid.CreateVersion7(), "FAV-IN-STOCK", brand.Id, category.Id, "有貨商品", now);
        inStockProduct.ChangeStatus(ProductStatus.Published, now);
        var outOfStockProduct = new Product(Guid.CreateVersion7(), "FAV-OUT-OF-STOCK", brand.Id, category.Id, "缺貨商品", now);
        outOfStockProduct.ChangeStatus(ProductStatus.Published, now);
        var delistedProduct = new Product(Guid.CreateVersion7(), "FAV-DELISTED", brand.Id, category.Id, "已下架商品", now);
        delistedProduct.ChangeStatus(ProductStatus.Published, now);
        delistedProduct.ChangeStatus(ProductStatus.Discontinued, now);
        context.Products.AddRange(inStockProduct, outOfStockProduct, delistedProduct);
        await context.SaveChangesAsync();

        InStockProductPublicId = inStockProduct.PublicId;
        OutOfStockProductPublicId = outOfStockProduct.PublicId;
        DelistedProductPublicId = delistedProduct.PublicId;

        var inStockSku = CreateDefaultSku(inStockProduct.Id, "FAV-IN-STOCK-A1", now);
        var outOfStockSku = CreateDefaultSku(outOfStockProduct.Id, "FAV-OUT-OF-STOCK-B1", now);
        var delistedSku = CreateDefaultSku(delistedProduct.Id, "FAV-DELISTED-C1", now);
        context.Skus.AddRange(inStockSku, outOfStockSku, delistedSku);
        await context.SaveChangesAsync();

        context.InventoryBalances.AddRange(
            new InventoryBalance(Guid.CreateVersion7(), inStockSku.Id, onHandQuantity: 10, reorderLevel: 2, now),
            new InventoryBalance(Guid.CreateVersion7(), outOfStockSku.Id, onHandQuantity: 0, reorderLevel: 2, now),
            // Not favorited while delisted, but ListAsync_AfterProductIsRepublished_ReturnsToNormalAvailability
            // republishes this product mid-test and expects it to show as purchasable in-stock again.
            new InventoryBalance(Guid.CreateVersion7(), delistedSku.Id, onHandQuantity: 10, reorderLevel: 2, now));
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public static DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new DoSelectDbContext(options);
    }

    private static Sku CreateDefaultSku(long productId, string skuCode, DateTime now)
    {
        var sku = new Sku(Guid.CreateVersion7(), skuCode, productId, skuCode, 1_000m, 700m, now);
        sku.ChangeStatus(SkuStatus.Published, now);
        sku.UpdateCommercialDetails(sku.NameZhTw, sku.ListPrice, sku.UnitCost, isDefault: true, requiresPrepayment: false, now);
        return sku;
    }
}
