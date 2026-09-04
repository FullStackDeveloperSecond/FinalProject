using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Application.Orders;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DoSelect.Api.IntegrationTests.Payments;

/// <summary>
/// 模擬付款端點的 Demo／隔離 E2E Profile 關卡，以及走完整條 HTTP 路徑的成功案例。
/// </summary>
/// <remarks>
/// <para>
/// <c>Demo:SimulationEndpointsEnabled</c> 在 <c>Development</c>／<c>Production</c> 上不得開啟；
/// 隔離 E2E 只為了重用正式 HTTP 契約建立可重跑的跨層證據。
/// 原先這個設定在 <c>dev</c> 上<b>沒有任何程式讀過</b> ——
/// 它有驗證器、每個 fixture 都設成 false，但沒有端點依賴它。這支端點是第一個使用者，
/// 所以那個旗標的行為到現在為止是沒有被證明過的。
/// </para>
/// <para>
/// 兩個方向都要測：只測「關著時打不到」的話，一個永遠 404 的實作也會過。
/// </para>
/// </remarks>
[Trait("Category", "RequiresSqlServer")]
public sealed class SimulatedPaymentDemoGateTests
{
    private static readonly string ConnectionString =
        SqlServerTestConnection.Build("DoSelectSimPaymentGateTests");

    [Theory]
    [InlineData("Demo")]
    [InlineData("E2E")]
    public async Task AnAllowedSimulationProfileCompletesThePaymentAndPaysTheOrder(string environment)
    {
        await using var harness = await Harness.StartAsync(
            ConnectionString, simulationEnabled: true, environment);
        var seeded = await harness.SeedPaymentAsync();
        var client = await harness.CreateMemberClientAsync(seeded.MemberUserId!);

        using var response = await harness.CompleteAsync(client, seeded.AttemptPublicId);

        await AssertStatusAsync(response, HttpStatusCode.OK, harness);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // 契約規定 enum 一律是 camelCase token，不是 .NET 的名稱。
        Assert.Equal("paid", body.GetProperty("status").GetString());

        var order = await harness.ReloadOrderAsync(seeded.AttemptPublicId);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(order.GrandTotal, order.PaidAmount);
    }

    [Fact]
    public async Task AGuestOrderAccessCookieCanCompleteItsOwnPayment()
    {
        await using var harness = await Harness.StartAsync(
            ConnectionString, simulationEnabled: true, environment: "Demo");
        var seeded = await harness.SeedPaymentAsync(guest: true);
        var client = await harness.CreateGuestClientAsync(seeded.RawGuestToken!);

        using var response = await harness.CompleteAsync(client, seeded.AttemptPublicId);

        await AssertStatusAsync(response, HttpStatusCode.OK, harness);
        Assert.Equal(PaymentStatus.Paid, (await harness.ReloadOrderAsync(
            seeded.AttemptPublicId)).PaymentStatus);
    }

    [Fact]
    public async Task AGuestCookieForAnotherOrderReturnsNotFound()
    {
        await using var harness = await Harness.StartAsync(
            ConnectionString, simulationEnabled: true, environment: "Demo");
        var own = await harness.SeedPaymentAsync(guest: true);
        var other = await harness.SeedPaymentAsync(guest: true);
        var client = await harness.CreateGuestClientAsync(own.RawGuestToken!);

        using var response = await harness.CompleteAsync(client, other.AttemptPublicId);

        await AssertStatusAsync(response, HttpStatusCode.NotFound, harness);
        Assert.Equal(GuestOrderErrorCodes.ScopeMismatch, await ReadProblemCodeAsync(response));
        Assert.Equal(PaymentStatus.AwaitingPayment, (await harness.ReloadOrderAsync(
            other.AttemptPublicId)).PaymentStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnExpiredOrRevokedGuestCookieReturnsUnauthorized(bool revoked)
    {
        await using var harness = await Harness.StartAsync(
            ConnectionString, simulationEnabled: true, environment: "Demo");
        var seeded = await harness.SeedPaymentAsync(
            guest: true,
            expiredGuestToken: !revoked,
            revokedGuestToken: revoked);
        var client = await harness.CreateGuestClientAsync(seeded.RawGuestToken!);

        using var response = await harness.CompleteAsync(client, seeded.AttemptPublicId);

        await AssertStatusAsync(response, HttpStatusCode.Unauthorized, harness);
        Assert.Equal(GuestOrderErrorCodes.AccessExpired, await ReadProblemCodeAsync(response));
        Assert.Equal(PaymentStatus.AwaitingPayment, (await harness.ReloadOrderAsync(
            seeded.AttemptPublicId)).PaymentStatus);
    }

    [Fact]
    public async Task TheSameRequestIsNotAvailableWhenTheSimulationFlagIsDisabled()
    {
        // 與上一條完全相同的請求，只差模擬端點旗標。這是關卡真正被證明的地方 ——
        // 少了上面那條對照，一個永遠回 404 的實作也會讓這條過。
        await using var harness = await Harness.StartAsync(
            ConnectionString, simulationEnabled: false, environment: "Development");
        var seeded = await harness.SeedPaymentAsync();
        var client = await harness.CreateMemberClientAsync(seeded.MemberUserId!);

        using var response = await harness.CompleteAsync(client, seeded.AttemptPublicId);

        await AssertStatusAsync(response, HttpStatusCode.NotFound, harness);

        // 而且真的什麼都沒改。
        var order = await harness.ReloadOrderAsync(seeded.AttemptPublicId);
        Assert.Equal(PaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Equal(0m, order.PaidAmount);
    }

    [Fact]
    public async Task TurningTheFlagOnOutsideTheAllowedProfilesFailsFastAtStartup()
    {
        // DemoOptionsValidator 真正的價值：不小心在正式設定裡打開這個旗標時，
        // 應用程式要拒絕啟動，而不是安靜地把模擬端點暴露出去。
        await Assert.ThrowsAnyAsync<OptionsValidationException>(async () =>
        {
            await using var harness = await Harness.StartAsync(
                ConnectionString, simulationEnabled: true, environment: "Development");
        });
    }

    /// <summary>比對狀態碼，失敗時把回應內容一起帶出來。</summary>
    /// <remarks>
    /// 光看「Expected OK, Actual InternalServerError」查不出原因，
    /// 而整合測試的失敗訊息是唯一看得到伺服器怎麼想的地方。
    /// </remarks>
    private static async Task AssertStatusAsync(
        HttpResponseMessage response, HttpStatusCode expected, Harness? harness = null)
    {
        var body = await response.Content.ReadAsStringAsync();
        var serverErrors = harness is null
            ? string.Empty
            : string.Join(Environment.NewLine, harness.Failures);
        Assert.True(
            expected == response.StatusCode,
            $"Expected {expected} but got {response.StatusCode}. Body: {body} Server: {serverErrors}");
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>把例外抄下來，然後讓真正的處理器接手。</summary>
    private sealed class RecordingExceptionHandler(List<string> sink) : IExceptionHandler
    {
        public ValueTask<bool> TryHandleAsync(
            HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            sink.Add(exception.ToString());
            return ValueTask.FromResult(false);
        }
    }

    private sealed record SeededPayment(
        Guid AttemptPublicId,
        string? MemberUserId,
        string? RawGuestToken);

    /// <summary>一個設定好的主機加上它自己的資料庫。</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly string _connectionString;

        public List<string> Failures { get; init; } = [];

        private Harness(WebApplicationFactory<Program> factory, string connectionString)
        {
            _factory = factory;
            _connectionString = connectionString;
        }

        public static async Task<Harness> StartAsync(
            string connectionString,
            bool simulationEnabled,
            string environment)
        {
            await using (var context = CreateContext(connectionString))
            {
                await context.Database.EnsureDeletedAsync();
                await context.Database.EnsureCreatedAsync();
            }

            var overrides = new Dictionary<string, string>
            {
                ["ConnectionStrings__DefaultConnection"] = connectionString,
                ["Observability__FileLoggingEnabled"] = "false",
                ["Features__AiEnabled"] = "false",
                ["Features__EmailEnabled"] = "false",
                ["Idempotency__ActorScopePepper"] = "sim-payment-api-tests-actor-scope-pepper",
                ["Demo__SimulationEndpointsEnabled"] = simulationEnabled ? "true" : "false",
            };

            // Program.cs 在 WithWebHostBuilder 的 hook 之前就急著讀這些鍵，
            // 所以只有環境變數這條路進得去（同 CatalogAdminApiFixture 的說明）。
            var failures = new List<string>();
            WebApplicationFactory<Program> factory;
            using (new EnvironmentOverrideScope(overrides))
            {
                factory = new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(builder =>
                    {
                        builder.UseEnvironment(environment);

                        builder.ConfigureServices(services =>
                        {
                            // 測試主機裡沒有正式的持久化金鑰環，Cookie 驗證與 antiforgery
                            // 需要一組臨時金鑰才會動（同 CatalogAdminApiFixture）。
                            // Serilog 會清掉 logging provider，所以錄不到 ——
                            // 改成插一個排在最前面的 IExceptionHandler，它只記錄後回 false，
                            // 真正的處理仍然交給 GlobalExceptionHandler。
                            services.Insert(0, ServiceDescriptor.Singleton<IExceptionHandler>(
                                new RecordingExceptionHandler(failures)));
                            services.AddSingleton<IDataProtectionProvider>(
                                new EphemeralDataProtectionProvider());
                            // 測試專用的 /__tests/security/sign-in/{accountType} 定義在
                            // 測試組件裡，要掛成 application part 才找得到。
                            services
                                .AddControllers()
                                .AddApplicationPart(typeof(SecurityFoundationTestController).Assembly);
                        });
                    });

                // 設定驗證要在這裡就跑出來，所以在 scope 還活著的時候先建一個 Client。
                factory.CreateClient().Dispose();
            }

            return new Harness(factory, connectionString) { Failures = failures };
        }

        public async ValueTask DisposeAsync()
        {
            await _factory.DisposeAsync();
            await using var context = CreateContext(_connectionString);
            await context.Database.EnsureDeletedAsync();
        }

        public async Task<HttpResponseMessage> CompleteAsync(
            HttpClient client, Guid attemptPublicId)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/simulated-payments/{attemptPublicId:D}/actions/complete")
            {
                Content = JsonContent.Create(new
                {
                    outcome = "succeeded",
                    simulationKey = $"sim-{Guid.NewGuid():N}",
                }),
            };
            // 全域 antiforgery 過濾器會擋掉沒帶 token 的 POST，變成 400 ——
            // 那樣就永遠測不到 Demo 關卡是不是有作用。
            request.Headers.Add("X-XSRF-TOKEN", await GetAntiforgeryTokenAsync(client));
            return await client.SendAsync(request);
        }

        public async Task<HttpClient> CreateMemberClientAsync(string memberUserId)
        {
            var client = CreateHttpsClient();
            var token = await GetAntiforgeryTokenAsync(client);
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "/__tests/security/sign-in/member")
            {
                Content = JsonContent.Create(new
                {
                    includeMfa = false,
                    roles = Array.Empty<string>(),
                    userId = memberUserId,
                }),
            };
            request.Headers.Add("X-XSRF-TOKEN", token);
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return client;
        }

        public async Task<HttpClient> CreateGuestClientAsync(string rawToken)
        {
            var client = CreateHttpsClient();
            var cookieOptions = _factory.Services
                .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(DoSelectAuthenticationSchemes.GuestOrderAccess);
            var identity = new ClaimsIdentity(DoSelectAuthenticationSchemes.GuestOrderAccess);
            identity.AddClaim(new Claim(GuestOrderAccessClaimTypes.TokenValue, rawToken));
            var protectedTicket = cookieOptions.TicketDataFormat.Protect(new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                DoSelectAuthenticationSchemes.GuestOrderAccess));
            client.DefaultRequestHeaders.Add(
                "Cookie",
                $"{cookieOptions.Cookie.Name}={protectedTicket}");
            await GetAntiforgeryTokenAsync(client);
            return client;
        }

        /// <remarks>
        /// 非 Development 環境會套用 <c>UseHttpsRedirection</c>，http 的請求會被轉址，
        /// 測試主機沒有 https 監聽端可以接。TestServer 不做真的 TLS，只看 scheme，
        /// 所以把 BaseAddress 換成 https 就繞過轉址而不必改 production 的 pipeline。
        /// </remarks>
        private HttpClient CreateHttpsClient() =>
            _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });

        public void AssertCanBuildController()
        {
            using var scope = _factory.Services.CreateScope();
            Microsoft.Extensions.DependencyInjection.ActivatorUtilities
                .CreateInstance<DoSelect.Api.Payments.SimulatedPaymentsController>(
                    scope.ServiceProvider);
        }

        public async Task<Order> ReloadOrderAsync(Guid attemptPublicId)
        {
            await using var context = CreateContext(_connectionString);
            var attempt = await context.PaymentAttempts.AsNoTracking()
                .SingleAsync(candidate => candidate.PublicId == attemptPublicId);
            return await context.Orders.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == attempt.OrderId);
        }

        public async Task<SeededPayment> SeedPaymentAsync(
            bool guest = false,
            bool expiredGuestToken = false,
            bool revokedGuestToken = false)
        {
            await using var context = CreateContext(_connectionString);

            ApplicationUser? member = null;
            if (!guest)
            {
                member = ApplicationUser.CreateMember(
                    Guid.CreateVersion7(),
                    $"{Guid.NewGuid():N}@doselect.test",
                    DateTime.UtcNow);
                context.Users.Add(member);
                await context.SaveChangesAsync();
            }

            var nowUtc = new DateTime(2026, 8, 30, 4, 0, 0, DateTimeKind.Utc);
            var profile = new ShippingProviderProfile(
                Guid.NewGuid(), $"INV{Guid.NewGuid():N}"[..16], 1, "Active",
                null, null, "{}", 1, nowUtc);
            context.ShippingProviderProfiles.Add(profile);
            await context.SaveChangesAsync();

            var packageLimit = new PackageLimitVersion(
                Guid.NewGuid(), profile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m,
                null, null, nowUtc);
            context.PackageLimitVersions.Add(packageLimit);
            await context.SaveChangesAsync();

            var order = Order.Create(
                Guid.NewGuid(),
                new OrderCreation(
                    $"INV-{Guid.NewGuid():N}"[..32],
                    member?.Id,
                    guest ? $"guest-{Guid.NewGuid():N}@doselect.test" : null,
                    OrderStatus.PendingPayment,
                    PaymentStatus.AwaitingPayment,
                    FulfillmentStatus.Preparing,
                    AssemblyStatus.NotRequired,
                    1000m, 0m, 0m, 0m, 1000m,
                    "[[SYNTHETIC_NAME]]", "0912345678", "recipient@example.test",
                    "100", "Taipei", "Zhongzheng", "[[SYNTHETIC_ADDRESS]]", null, "HOME",
                    profile.Id, null, null, null, 1, 1, null, null,
                    $"inv-{Guid.NewGuid():N}", null, 1, 1,
                    new OrderInvoicePreference(
                        SimulatedInvoiceBuyerType.Individual,
                        "buyer@example.test", null, null, null, null),
                    1_000m, null,
                    new OrderPackageSnapshot(packageLimit.Id, 1m, 40m, 30m, 20m, 90m, 100m)),
                nowUtc);
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var attempt = new PaymentAttempt(
                Guid.CreateVersion7(),
                order.Id,
                PaymentMethod.CreditCard,
                order.GrandTotal,
                "SIM",
                $"checkout-{Guid.NewGuid():N}:initial-payment",
                // 期限要相對於「現在」，因為端點用的是真實時鐘。
                DateTime.UtcNow.AddHours(4),
                nowUtc);
            attempt.SetPaymentInstruction("SIM-" + attempt.PublicId.ToString("N"), nowUtc);
            context.PaymentAttempts.Add(attempt);
            await context.SaveChangesAsync();

            string? rawGuestToken = null;
            if (guest)
            {
                rawGuestToken = $"guest-{Guid.NewGuid():N}";
                var tokenCreatedAt = expiredGuestToken
                    ? DateTime.UtcNow.AddHours(-2)
                    : DateTime.UtcNow;
                var tokenExpiresAt = expiredGuestToken
                    ? DateTime.UtcNow.AddHours(-1)
                    : DateTime.UtcNow.AddHours(1);
                var hash = new byte[32];
                Random.Shared.NextBytes(hash);
                var requestRow = GuestOrderAccessRequest.CreateValid(
                    Guid.CreateVersion7(),
                    order.Id,
                    hash,
                    hash,
                    hash,
                    hash,
                    tokenExpiresAt,
                    tokenCreatedAt);
                context.GuestOrderAccessRequests.Add(requestRow);
                await context.SaveChangesAsync();

                var hasher = _factory.Services.GetRequiredService<IGuestOrderAccessHasher>();
                var token = new GuestOrderAccessToken(
                    Guid.CreateVersion7(),
                    order.Id,
                    requestRow.Id,
                    hasher.HashToken(rawGuestToken),
                    tokenExpiresAt,
                    tokenCreatedAt);
                if (revokedGuestToken)
                {
                    token.Revoke(DateTime.UtcNow);
                }

                context.GuestOrderAccessTokens.Add(token);
                await context.SaveChangesAsync();
            }

            return new SeededPayment(attempt.PublicId, member?.Id, rawGuestToken);
        }

        private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, "/api/v1/security/antiforgery-token");
            request.Headers.Add("X-DoSelect-Client", "member");
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            return body.GetProperty("requestToken").GetString()!;
        }

        private static DoSelectDbContext CreateContext(string connectionString) =>
            new(new DbContextOptionsBuilder<DoSelectDbContext>()
                .UseSqlServer(connectionString)
                .Options);
    }
}
