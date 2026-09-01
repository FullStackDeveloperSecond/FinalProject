using DoSelect.Application.Catalog;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Catalog;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Catalog;

/// <summary>
/// A-09 分類規格範本後台（API Endpoint 目錄「M 規格範本」）。規則來源是
/// 資料字典-商品庫存與組裝：結構欄位被使用後不可改、以停用代替刪除、受保護組合由程式碼目錄固定。
/// </summary>
[Collection(nameof(CatalogAdminCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class SpecificationDefinitionAdminServiceTests
{
    [Fact]
    public async Task CreateAsync_WithADecimalDefinition_NormalizesTheSemanticKeyAndStartsActive()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var category = await SeedCategoryAsync(context);
        var service = new EfSpecificationDefinitionAdminService(context);
        var semanticKey = CatalogAdminFixture.UniqueCode("spec").ToLowerInvariant();

        var created = await service.CreateAsync(
            new CreateSpecificationDefinitionRequest(
                category.PublicId, semanticKey, "長度", "Decimal", null,
                IsRequired: true, AllowsMultiple: false, SortOrder: 5, Options: []),
            CancellationToken.None);

        Assert.Equal(semanticKey.ToUpperInvariant(), created.SemanticKey);
        Assert.Equal(category.PublicId, created.CategoryPublicId);
        Assert.True(created.IsActive);
        Assert.True(created.IsRequired);
        Assert.False(created.IsProtected);
        Assert.Empty(created.Options);
    }

    [Fact]
    public async Task CreateAsync_WhenTheSemanticKeyRepeatsInTheSameCategory_ThrowsSemanticKeyDuplicate()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var category = await SeedCategoryAsync(context);
        var service = new EfSpecificationDefinitionAdminService(context);
        var semanticKey = CatalogAdminFixture.UniqueCode("SPEC");
        await service.CreateAsync(Request(category.PublicId, semanticKey), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.CreateAsync(
            Request(category.PublicId, semanticKey) with { DisplayNameZhTw = "另一個名稱" },
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.SpecificationSemanticKeyDuplicate, exception.ErrorCode);
    }

    /// <summary>唯一鍵是 (CategoryId, SemanticKey)：同一把 Key 在另一個分類是合法的。</summary>
    [Fact]
    public async Task CreateAsync_WhenTheSameSemanticKeyIsUsedInAnotherCategory_Succeeds()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var first = await SeedCategoryAsync(context);
        var second = await SeedCategoryAsync(context);
        var service = new EfSpecificationDefinitionAdminService(context);
        var semanticKey = CatalogAdminFixture.UniqueCode("SPEC");
        await service.CreateAsync(Request(first.PublicId, semanticKey), CancellationToken.None);

        var created = await service.CreateAsync(Request(second.PublicId, semanticKey), CancellationToken.None);

        Assert.Equal(second.PublicId, created.CategoryPublicId);
    }

    /// <summary>資料字典：「非 Decimal 不得指定 Unit」——輸入錯誤要回 400，不能讓 Domain 丟例外變 500。</summary>
    [Fact]
    public async Task CreateAsync_WhenANonDecimalDefinitionDeclaresAUnit_ThrowsSpecificationInvalid()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var category = await SeedCategoryAsync(context);
        var service = new EfSpecificationDefinitionAdminService(context);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.CreateAsync(
            Request(category.PublicId, CatalogAdminFixture.UniqueCode("SPEC")) with
            {
                ValueType = "String",
                UnitCode = "MM",
            },
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.SpecificationInvalid, exception.ErrorCode);
    }

    /// <summary>資料字典：「AllowsMultiple=1 僅適用 Option」。</summary>
    [Fact]
    public async Task CreateAsync_WhenANonOptionDefinitionAllowsMultiple_ThrowsSpecificationInvalid()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var category = await SeedCategoryAsync(context);
        var service = new EfSpecificationDefinitionAdminService(context);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.CreateAsync(
            Request(category.PublicId, CatalogAdminFixture.UniqueCode("SPEC")) with { AllowsMultiple = true },
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.SpecificationInvalid, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_WhenANonOptionDefinitionDeclaresOptions_ThrowsSpecificationInvalid()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var category = await SeedCategoryAsync(context);
        var service = new EfSpecificationDefinitionAdminService(context);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.CreateAsync(
            Request(category.PublicId, CatalogAdminFixture.UniqueCode("SPEC")) with
            {
                Options = [new SpecificationOptionInput("A", "選項A", 0, true)],
            },
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.SpecificationInvalid, exception.ErrorCode);
    }

    /// <summary>同一批送出重複 Option Code 會撞 UX_SpecificationOptions_DefinitionId_Code，
    /// 必須在寫入前擋成 400，而不是變成 DbUpdateException。</summary>
    [Fact]
    public async Task CreateAsync_WhenOptionCodesRepeatInTheRequest_ThrowsSpecificationInvalid()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var category = await SeedCategoryAsync(context);
        var service = new EfSpecificationDefinitionAdminService(context);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.CreateAsync(
            Request(category.PublicId, CatalogAdminFixture.UniqueCode("SPEC")) with
            {
                ValueType = "Option",
                Options =
                [
                    new SpecificationOptionInput("dup", "選項一", 0, true),
                    new SpecificationOptionInput("DUP", "選項二", 1, true),
                ],
            },
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.SpecificationInvalid, exception.ErrorCode);
    }

    /// <summary>資料字典：「受保護 Category Code／SemanticKey 組合由程式碼目錄固定」——
    /// IsProtected 不是管理員送進來的欄位，而是依 CompatibilityCatalogContract 判定。</summary>
    [Fact]
    public async Task CreateAsync_WhenTheCombinationIsInTheCompatibilityCatalogue_MarksItProtected()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var category = await SeedCategoryAsync(context, CompatibilityCatalogContract.Categories.Cpu);
        var service = new EfSpecificationDefinitionAdminService(context);

        var created = await service.CreateAsync(
            new CreateSpecificationDefinitionRequest(
                category.PublicId, CompatibilityCatalogContract.SemanticKeys.CpuSocket, "CPU 腳位",
                "Option", null, IsRequired: true, AllowsMultiple: true, SortOrder: 0,
                Options: [new SpecificationOptionInput("AM5", "AM5", 0, true)]),
            CancellationToken.None);

        Assert.True(created.IsProtected);
        Assert.True(created.AllowsMultiple);
        Assert.Equal("AM5", Assert.Single(created.Options).Code);
    }

    /// <summary>受保護的定義是固定相容性引擎的輸入，停用它等於讓該分類的硬性規則永遠缺料。</summary>
    [Fact]
    public async Task DisableAsync_WhenTheDefinitionIsProtected_ThrowsDefinitionReferenced()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var category = await SeedCategoryAsync(context, CompatibilityCatalogContract.Categories.Psu);
        var service = new EfSpecificationDefinitionAdminService(context);
        var created = await service.CreateAsync(
            new CreateSpecificationDefinitionRequest(
                category.PublicId, CompatibilityCatalogContract.SemanticKeys.PsuRatedWatts, "額定瓦數",
                "Decimal", null, IsRequired: true, AllowsMultiple: false, SortOrder: 0, Options: []),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.DisableAsync(
            created.PublicId, new DisableSpecificationDefinitionRequest(created.RowVersion), CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.SpecificationDefinitionReferenced, exception.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenAProtectedDefinitionWouldStopBeingRequired_ThrowsDefinitionReferenced()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var category = await SeedCategoryAsync(context, CompatibilityCatalogContract.Categories.Gpu);
        var service = new EfSpecificationDefinitionAdminService(context);
        var created = await service.CreateAsync(
            new CreateSpecificationDefinitionRequest(
                category.PublicId, CompatibilityCatalogContract.SemanticKeys.GpuLengthMm, "顯卡長度",
                "Decimal", null, IsRequired: true, AllowsMultiple: false, SortOrder: 0, Options: []),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.UpdateAsync(
            created.PublicId,
            new UpdateSpecificationDefinitionRequest("顯卡長度", IsRequired: false, SortOrder: 0, Options: [], created.RowVersion),
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.SpecificationDefinitionReferenced, exception.ErrorCode);
    }

    /// <summary>一般定義以停用代替刪除，且其選項一併停用——留下啟用中的孤兒選項會讓前台篩選與
    /// 匯入仍看得到它們。</summary>
    [Fact]
    public async Task DisableAsync_WhenTheDefinitionIsNotProtected_DeactivatesItAndItsOptions()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var category = await SeedCategoryAsync(context);
        var service = new EfSpecificationDefinitionAdminService(context);
        var created = await service.CreateAsync(
            Request(category.PublicId, CatalogAdminFixture.UniqueCode("SPEC")) with
            {
                ValueType = "Option",
                Options = [new SpecificationOptionInput("A", "選項A", 0, true)],
            },
            CancellationToken.None);

        var disabled = await service.DisableAsync(
            created.PublicId, new DisableSpecificationDefinitionRequest(created.RowVersion), CancellationToken.None);

        Assert.False(disabled.IsActive);
        Assert.False(Assert.Single(disabled.Options).IsActive);
    }

    /// <summary>資料字典：「被使用後不可刪除或改 Code」——請求中消失的 Option 是停用，不是刪除，
    /// 否則已經選了該選項的 SKU 會指向不存在的列。</summary>
    [Fact]
    public async Task UpdateAsync_WhenAnOptionIsOmitted_DeactivatesItInsteadOfDeletingIt()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var category = await SeedCategoryAsync(context);
        var service = new EfSpecificationDefinitionAdminService(context);
        var created = await service.CreateAsync(
            Request(category.PublicId, CatalogAdminFixture.UniqueCode("SPEC")) with
            {
                ValueType = "Option",
                Options =
                [
                    new SpecificationOptionInput("KEEP", "保留", 0, true),
                    new SpecificationOptionInput("DROP", "移除", 1, true),
                ],
            },
            CancellationToken.None);

        var updated = await service.UpdateAsync(
            created.PublicId,
            new UpdateSpecificationDefinitionRequest(
                "新名稱",
                IsRequired: true,
                SortOrder: 3,
                Options: [new SpecificationOptionInput("KEEP", "保留（改名）", 0, true)],
                created.RowVersion),
            CancellationToken.None);

        Assert.Equal("新名稱", updated.DisplayNameZhTw);
        Assert.Equal(3, updated.SortOrder);
        Assert.Equal(2, updated.Options.Count);
        var kept = updated.Options.Single(option => option.Code == "KEEP");
        Assert.True(kept.IsActive);
        Assert.Equal("保留（改名）", kept.DisplayNameZhTw);
        var dropped = updated.Options.Single(option => option.Code == "DROP");
        Assert.False(dropped.IsActive);

        // The row is still there — a SKU that already selected it keeps resolving.
        await using var verify = CatalogAdminFixture.CreateContext();
        Assert.Equal(2, await verify.SpecificationOptions.CountAsync(
            option => option.PublicId == kept.PublicId || option.PublicId == dropped.PublicId));
    }

    [Fact]
    public async Task UpdateAsync_WhenTheRowVersionIsStale_ThrowsConcurrencyConflict()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var category = await SeedCategoryAsync(context);
        var service = new EfSpecificationDefinitionAdminService(context);
        var created = await service.CreateAsync(
            Request(category.PublicId, CatalogAdminFixture.UniqueCode("SPEC")), CancellationToken.None);
        var staleRowVersion = created.RowVersion;

        await service.UpdateAsync(
            created.PublicId,
            new UpdateSpecificationDefinitionRequest("先改一次", true, 1, [], staleRowVersion),
            CancellationToken.None);

        await using var otherContext = CatalogAdminFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<CatalogWriteException>(
            () => new EfSpecificationDefinitionAdminService(otherContext).UpdateAsync(
                created.PublicId,
                new UpdateSpecificationDefinitionRequest("再改一次", true, 2, [], staleRowVersion),
                CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }

    [Fact]
    public async Task ListAsync_FiltersByCategoryAndActiveState()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var category = await SeedCategoryAsync(context);
        var other = await SeedCategoryAsync(context);
        var service = new EfSpecificationDefinitionAdminService(context);
        var wanted = await service.CreateAsync(
            Request(category.PublicId, CatalogAdminFixture.UniqueCode("SPEC")), CancellationToken.None);
        var disabled = await service.CreateAsync(
            Request(category.PublicId, CatalogAdminFixture.UniqueCode("SPEC")), CancellationToken.None);
        await service.DisableAsync(
            disabled.PublicId, new DisableSpecificationDefinitionRequest(disabled.RowVersion), CancellationToken.None);
        await service.CreateAsync(Request(other.PublicId, CatalogAdminFixture.UniqueCode("SPEC")), CancellationToken.None);

        var page = await service.ListAsync(
            new SpecificationDefinitionQuery(category.PublicId, null, IsActive: true, 1, 20), CancellationToken.None);

        Assert.Equal(wanted.PublicId, Assert.Single(page.Items).PublicId);
    }

    /// <summary>與 EfBrandAdminService 相同的 int 溢位護欄：極大頁碼不得變成負的 OFFSET。</summary>
    [Fact]
    public async Task ListAsync_WithAnExtremePageNumber_ReturnsAnEmptyPage()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var category = await SeedCategoryAsync(context);
        var service = new EfSpecificationDefinitionAdminService(context);
        await service.CreateAsync(Request(category.PublicId, CatalogAdminFixture.UniqueCode("SPEC")), CancellationToken.None);

        var page = await service.ListAsync(
            new SpecificationDefinitionQuery(category.PublicId, null, null, int.MaxValue, 20), CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task CreateAsync_WhenTheCategoryDoesNotExist_ThrowsReferenceNotFound()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var service = new EfSpecificationDefinitionAdminService(context);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.CreateAsync(
            Request(Guid.CreateVersion7(), CatalogAdminFixture.UniqueCode("SPEC")), CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ReferenceNotFound, exception.ErrorCode);
    }

    private static CreateSpecificationDefinitionRequest Request(Guid categoryPublicId, string semanticKey) =>
        new(categoryPublicId, semanticKey, "測試規格", "Decimal", null,
            IsRequired: true, AllowsMultiple: false, SortOrder: 0, Options: []);

    private static async Task<Category> SeedCategoryAsync(DoSelectDbContext context, string? code = null)
    {
        var now = DateTime.UtcNow;
        var category = new Category(
            Guid.CreateVersion7(),
            code ?? CatalogAdminFixture.UniqueCode("CAT"),
            "cat-" + Guid.NewGuid().ToString("N")[..12],
            "測試分類",
            null,
            now);
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return category;
    }
}
