using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Domain.Catalog;
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

    public async Task<HttpClient> CreateAuthenticatedCatalogManagerClientAsync()
    {
        var roles = new[] { DoSelectRoles.CatalogManager };
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
