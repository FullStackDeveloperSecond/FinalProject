using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using DoSelect.Api.Security;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests.Imports;

/// <summary>
/// Real SQL Server-backed <see cref="WebApplicationFactory{TEntryPoint}"/> for
/// <c>AdminProductImportsController</c> (組長 PR #74 round-2 review, P2). Mirrors
/// <c>AdminCompatibilityRulesApiFixture</c>: environment overrides via
/// <c>EnvironmentOverrideScope</c>, and the admin sign-in helper seeds a REAL Users/Roles/UserRoles
/// row set and passes the userId to the test sign-in endpoint, because the import service's audit
/// actor resolution re-queries those tables server-side.
/// </summary>
public sealed class ProductImportsApiFixture : IAsyncLifetime
{
    private static readonly string ConnectionString =
        SqlServerTestConnection.Build("DoSelectProductImportsApiTests");

    private static readonly IReadOnlyDictionary<string, string> EnvironmentOverrides = new Dictionary<string, string>
    {
        ["ConnectionStrings__DefaultConnection"] = ConnectionString,
        ["Observability__FileLoggingEnabled"] = "false",
        ["Features__AiEnabled"] = "false",
        ["Features__EmailEnabled"] = "false",
        ["Demo__SimulationEndpointsEnabled"] = "false",
    };

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "DoSelectProductImportsApiTests",
        Guid.NewGuid().ToString("N"));

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await ResetDatabaseAsync();

        var allOverrides = new Dictionary<string, string>(EnvironmentOverrides)
        {
            ["Storage__DataRoot"] = _dataRoot,
        };

        using (new EnvironmentOverrideScope(allOverrides))
        {
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                    services
                        .AddControllers()
                        .AddApplicationPart(typeof(SecurityFoundationTestController).Assembly);
                });
            });
            // The host builds lazily on the first CreateClient — it MUST happen inside this
            // scope or Program.cs's eager configuration reads (the DB connection string above
            // all) see the real environment instead of the overrides.
            _factory.CreateClient().Dispose();
        }
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public HttpClient CreateClient() => _factory.CreateClient();

    public static string UniqueCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    public Task<HttpClient> CreateAuthenticatedCatalogManagerClientAsync() =>
        CreateAuthenticatedAdminClientAsync(DoSelectRoles.CatalogManager);

    /// <summary>庫存匯入的 Policy 是 InventoryAdjust.*，所以同一個 fixture 也要能簽入 InventoryManager。</summary>
    public async Task<HttpClient> CreateAuthenticatedAdminClientAsync(params string[] roles)
    {
        string adminUserId;
        await using (var context = CreateContext())
        {
            var admin = ApplicationUser.CreateAdmin(
                Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
            context.Users.Add(admin);
            await context.SaveChangesAsync();
            adminUserId = admin.Id;

            foreach (var roleName in roles)
            {
                var role = await context.Roles.SingleOrDefaultAsync(candidate => candidate.Name == roleName);
                if (role is null)
                {
                    role = new IdentityRole(roleName);
                    context.Roles.Add(role);
                    await context.SaveChangesAsync();
                }

                context.UserRoles.Add(new IdentityUserRole<string> { UserId = admin.Id, RoleId = role.Id });
            }

            await context.SaveChangesAsync();
        }

        var client = CreateClient();
        var signInToken = await GetAdminAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/sign-in/admin")
        {
            Content = JsonContent.Create(new { includeMfa = true, roles, userId = adminUserId }),
        };
        request.Headers.Add("X-XSRF-TOKEN", signInToken);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Seeds one active brand + category and drives the real Preview endpoint with a
    /// single-row products CSV, returning the staged batch's publicId for the read endpoints.</summary>
    public async Task<Guid> StagePreviewBatchAsync(HttpClient authenticatedClient)
    {
        string brandCode;
        string categoryCode;
        await using (var context = CreateContext())
        {
            var now = DateTime.UtcNow;
            var brand = new Brand(Guid.CreateVersion7(), UniqueCode("BRAND"), "測試品牌", now);
            var category = new Category(Guid.CreateVersion7(), UniqueCode("CAT"), UniqueCode("slot"), "測試分類", null, now);
            context.AddRange(brand, category);
            await context.SaveChangesAsync();
            brandCode = brand.Code;
            categoryCode = category.Code;
        }

        var productsCsv =
            "product_key,product_code,name_zh_tw,brand_code,category_code,description_zh_tw,warranty_months,status\r\n" +
            $"PK1,{UniqueCode("PROD")},匯入商品,{brandCode},{categoryCode},\\N,\\N,Draft\r\n";
        var skusCsv = "sku_key,sku_code,product_key,name_zh_tw,list_price,unit_cost,weight_kg,length_cm,width_cm,height_cm,requires_prepayment,status\r\n";
        var specificationsCsv = "sku_key,semantic_key,value_type,string_value,decimal_value,boolean_value,option_code\r\n";

        using var form = new MultipartFormDataContent
        {
            { CsvContent(productsCsv), "productsFile", "products.csv" },
            { CsvContent(skusCsv), "skusFile", "skus.csv" },
            { CsvContent(specificationsCsv), "specificationsFile", "specifications.csv" },
            { new StringContent("1"), "templateVersion" },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/product-imports/preview")
        {
            Content = form,
        };
        request.Headers.Add("X-XSRF-TOKEN", await GetAdminAntiforgeryTokenAsync(authenticatedClient));
        using var response = await authenticatedClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Preview failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("publicId").GetGuid();
    }

    /// <summary>單一 XLSX 走同一支 Preview 端點，只是換成 workbookFile 這個 part。</summary>
    public async Task<Guid> StageWorkbookPreviewBatchAsync(HttpClient authenticatedClient)
    {
        string brandCode;
        string categoryCode;
        await using (var context = CreateContext())
        {
            var now = DateTime.UtcNow;
            var brand = new Brand(Guid.CreateVersion7(), UniqueCode("BRAND"), "測試品牌", now);
            var category = new Category(Guid.CreateVersion7(), UniqueCode("CAT"), UniqueCode("slot"), "測試分類", null, now);
            context.AddRange(brand, category);
            await context.SaveChangesAsync();
            brandCode = brand.Code;
            categoryCode = category.Code;
        }

        byte[] workbookBytes;
        using (var workbook = new XLWorkbook())
        {
            var products = workbook.Worksheets.Add("Products");
            var header = new[] { "product_key", "product_code", "name_zh_tw", "brand_code", "category_code", "description_zh_tw", "warranty_months", "status" };
            var row = new[] { "PK1", UniqueCode("PROD"), "匯入商品", brandCode, categoryCode, "\\N", "\\N", "Draft" };
            for (var column = 0; column < header.Length; column++)
            {
                products.Cell(1, column + 1).Value = header[column];
                products.Cell(2, column + 1).Value = row[column];
            }

            var skus = workbook.Worksheets.Add("Skus");
            var skuHeader = new[] { "sku_key", "sku_code", "product_key", "name_zh_tw", "list_price", "unit_cost", "weight_kg", "length_cm", "width_cm", "height_cm", "requires_prepayment", "status" };
            for (var column = 0; column < skuHeader.Length; column++)
            {
                skus.Cell(1, column + 1).Value = skuHeader[column];
            }

            var specifications = workbook.Worksheets.Add("Specifications");
            var specificationHeader = new[] { "sku_key", "semantic_key", "value_type", "string_value", "decimal_value", "boolean_value", "option_code" };
            for (var column = 0; column < specificationHeader.Length; column++)
            {
                specifications.Cell(1, column + 1).Value = specificationHeader[column];
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            workbookBytes = stream.ToArray();
        }

        var content = new ByteArrayContent(workbookBytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        using var form = new MultipartFormDataContent
        {
            { content, "workbookFile", "catalog.xlsx" },
            { new StringContent("1"), "templateVersion" },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/product-imports/preview")
        {
            Content = form,
        };
        request.Headers.Add("X-XSRF-TOKEN", await GetAdminAntiforgeryTokenAsync(authenticatedClient));
        using var response = await authenticatedClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Workbook preview failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("publicId").GetGuid();
    }

    // ---- 庫存匯入 -------------------------------------------------------------------------

    public async Task<(string SkuCode, long SkuId)> SeedSkuWithBalanceAsync(int onHand, int reserved)
    {
        await using var context = CreateContext();
        var now = DateTime.UtcNow;
        var brand = new Brand(Guid.CreateVersion7(), UniqueCode("BRAND"), "測試品牌", now);
        var category = new Category(Guid.CreateVersion7(), UniqueCode("CAT"), UniqueCode("slot"), "測試分類", null, now);
        context.AddRange(brand, category);
        await context.SaveChangesAsync();
        var product = new Product(Guid.CreateVersion7(), UniqueCode("PROD"), brand.Id, category.Id, "盤點商品", now);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var sku = new Sku(Guid.CreateVersion7(), UniqueCode("SKU"), product.Id, "盤點 SKU", 1000m, 600m, now);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();
        var balance = new InventoryBalance(Guid.CreateVersion7(), sku.Id, onHandQuantity: onHand, reorderLevel: 0, now);
        balance.ApplyQuantities(onHand, reserved, now);
        context.InventoryBalances.Add(balance);
        await context.SaveChangesAsync();
        return (sku.SkuCode, sku.Id);
    }

    public async Task<Guid> StageInventoryPreviewBatchAsync(HttpClient authenticatedClient, string dataRows)
    {
        var csv = "sku_code,target_on_hand,reason_code,note\r\n" + dataRows;
        using var form = new MultipartFormDataContent
        {
            { CsvContent(csv), "adjustmentsFile", "stock.csv" },
            { new StringContent("1"), "templateVersion" },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/inventory-imports/preview")
        {
            Content = form,
        };
        request.Headers.Add("X-XSRF-TOKEN", await GetAdminAntiforgeryTokenAsync(authenticatedClient));
        using var response = await authenticatedClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Inventory preview failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("publicId").GetGuid();
    }

    public static async Task<string> ReadBatchRowVersionAsync(HttpClient client, string batchUrl)
    {
        using var response = await client.GetAsync(batchUrl);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("rowVersion").GetString()!;
    }

    /// <summary>值不變、RowVersion 變——「Preview 之後被別的交易碰過」的最小版本。</summary>
    public async Task TouchBalanceAsync(long skuId)
    {
        await using var context = CreateContext();
        var balance = await context.InventoryBalances.SingleAsync(candidate => candidate.SkuId == skuId);
        balance.ApplyQuantities(balance.OnHandQuantity, balance.ReservedQuantity, DateTime.UtcNow);
        await context.SaveChangesAsync();
    }

    public async Task<int> ReadOnHandAsync(long skuId)
    {
        await using var context = CreateContext();
        return await context.InventoryBalances.AsNoTracking()
            .Where(candidate => candidate.SkuId == skuId)
            .Select(candidate => candidate.OnHandQuantity)
            .SingleAsync();
    }

    public async Task<int> CountMovementsAsync(long skuId)
    {
        await using var context = CreateContext();
        return await context.InventoryMovements.CountAsync(candidate => candidate.SkuId == skuId);
    }

    public static async Task<string> GetAdminAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add("X-DoSelect-Client", "admin");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("requestToken").GetString()!;
    }

    private static StringContent CsvContent(string csv) =>
        new(csv, Encoding.UTF8, "text/csv");

    private static DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new DoSelectDbContext(options);
    }

    private static async Task ResetDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }
}

[CollectionDefinition(nameof(ProductImportsApiCollection))]
public sealed class ProductImportsApiCollection : ICollectionFixture<ProductImportsApiFixture>;
