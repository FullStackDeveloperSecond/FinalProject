using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoSelect.Api.Contracts.Orders;
using DoSelect.Application.Notifications;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests;

public sealed class GuestOrderAccessControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTime CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5);
    private readonly WebApplicationFactory<Program> _factory;

    static GuestOrderAccessControllerTests()
    {
        // GuestOrderAccessHasher (Scoped) validates this at first resolution — a fresh test
        // run has no appsettings.Development.json, so this must be supplied here rather than
        // relying on the (empty) example config. Set once, before any test in this class
        // resolves it, mirroring CartApiFixture's environment-variable override approach.
        Environment.SetEnvironmentVariable(
            "GuestOrderAccess__Pepper", "guest-order-access-controller-tests-pepper-00000");
    }

    public GuestOrderAccessControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RequestAccess_WhenOrderExists_ReturnsAcceptedAndDeliversSixDigitCode()
    {
        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
        });
        var (_, _, orderNumber, email) = await SeedGuestOrderAsync(factory);
        using var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);

        using var response = await client.PostAsJsonAsync("/api/v1/guest-orders/access-requests", new
        {
            orderNumber,
            email,
        });
        var body = await response.Content.ReadFromJsonAsync<GuestOrderAccessRequestAcceptedDto>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotEqual(Guid.Empty, body!.RequestPublicId);
        var message = await capturingEmailSender.WaitForSingleMessageAsync();
        Assert.Equal(email, message.RecipientAddress);
        Assert.Equal(6, ExtractCode(message.TextBody).Length);
    }

    [Fact]
    public async Task RequestAccess_WhenOrderDoesNotExist_ReturnsTheSameAcceptedShapeWithoutSendingEmail()
    {
        // 相同 202：不論訂單是否存在，回應形狀必須一致（Haru-會員登入訂單與訪客存取最終
        // Schema.md 第 5 節）。
        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
        });
        using var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);

        using var response = await client.PostAsJsonAsync("/api/v1/guest-orders/access-requests", new
        {
            orderNumber = $"NO-SUCH-{Guid.NewGuid():N}"[..20],
            email = "nobody@example.com",
        });
        var body = await response.Content.ReadFromJsonAsync<GuestOrderAccessRequestAcceptedDto>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotEqual(Guid.Empty, body!.RequestPublicId);
        await Task.Delay(200);
        Assert.Empty(capturingEmailSender.SentMessages);
    }

    [Fact]
    public async Task Verify_WhenCodeIsWrong_ReturnsBadRequestAndDoesNotIssueCookie()
    {
        using var factory = CreateFactory();
        var (_, _, orderNumber, email) = await SeedGuestOrderAsync(factory);
        using var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);
        var requestPublicId = await RequestAccessAsync(client, orderNumber, email);

        using var response = await client.PostAsJsonAsync("/api/v1/guest-orders/access-verifications", new
        {
            requestPublicId,
            code = "000000",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await ReadJsonAsync(response);
        Assert.Equal(
            "guest_order_verification_invalid", problem.RootElement.GetProperty("code").GetString());
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Fact]
    public async Task Verify_AfterFiveFailedAttempts_LocksChallengeEvenWithTheCorrectCode()
    {
        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
        });
        var (_, _, orderNumber, email) = await SeedGuestOrderAsync(factory);
        using var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);
        var requestPublicId = await RequestAccessAsync(client, orderNumber, email);
        var correctCode = ExtractCode((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);

        for (var i = 0; i < GuestOrderAccessRequest.MaximumAttempts; i++)
        {
            using var attempt = await client.PostAsJsonAsync("/api/v1/guest-orders/access-verifications", new
            {
                requestPublicId,
                code = "000000",
            });
        }

        using var response = await client.PostAsJsonAsync("/api/v1/guest-orders/access-verifications", new
        {
            requestPublicId,
            code = correctCode,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Verify_FiveOrMoreParallelWrongCodes_AllAreReliablyCountedAndTheChallengeLocks()
    {
        // Alex review #5：Fake gateway 測試無法證明平行 read-modify-write 在真實 SQL Server
        // RowVersion 樂觀鎖下不會漏記。用同一個 HttpClient 真的平行送出多個請求——ASP.NET
        // Core 對每個請求都會建立獨立的 DI Scope／DbContext，形成真實競爭，不是 Fake 預先
        // 安排的例外。
        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
        });
        var (_, _, orderNumber, email) = await SeedGuestOrderAsync(factory);
        using var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);
        var requestPublicId = await RequestAccessAsync(client, orderNumber, email);
        await capturingEmailSender.WaitForSingleMessageAsync();

        const int concurrentAttempts = 6;
        var tasks = Enumerable.Range(0, concurrentAttempts)
            .Select(_ => client.PostAsJsonAsync("/api/v1/guest-orders/access-verifications", new
            {
                requestPublicId,
                code = "000000",
            }))
            .ToArray();
        var responses = await Task.WhenAll(tasks);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var persisted = await dbContext.GuestOrderAccessRequests
            .SingleAsync(r => r.PublicId == requestPublicId);

        // 6 個平行錯碼，但上限是 5——確實計數到 5（不能因為並行衝突悄悄漏記變少），
        // 且第 5 次要可靠地鎖定，不能因為平行而被繞過。
        Assert.Equal(GuestOrderAccessRequest.MaximumAttempts, persisted.AttemptCount);
        Assert.NotNull(persisted.LockedAtUtc);
    }

    [Fact]
    public async Task Verify_TwoParallelCorrectCodes_IssuesExactlyOneToken()
    {
        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
        });
        var (orderId, _, orderNumber, email) = await SeedGuestOrderAsync(factory);
        using var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);
        var requestPublicId = await RequestAccessAsync(client, orderNumber, email);
        var correctCode = ExtractCode((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);

        var tasks = new[]
        {
            client.PostAsJsonAsync(
                "/api/v1/guest-orders/access-verifications", new { requestPublicId, code = correctCode }),
            client.PostAsJsonAsync(
                "/api/v1/guest-orders/access-verifications", new { requestPublicId, code = correctCode }),
        };
        var responses = await Task.WhenAll(tasks);
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var failureCount = responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        // 同一張 Challenge 只能核發一個 Token：兩個平行正確碼，剛好一個 200、一個 400，
        // 不能兩個都成功（DB 留下兩筆 Token），也不能兩個都失敗（Token 憑空消失）。
        Assert.Equal(1, successCount);
        Assert.Equal(1, failureCount);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var tokenCount = await dbContext.GuestOrderAccessTokens.CountAsync(t => t.OrderId == orderId);
        Assert.Equal(1, tokenCount);
    }

    [Fact]
    public async Task Resend_TwoParallelCallsOnTheSameChallenge_OnlyOneSucceedsInCreatingASuccessor()
    {
        // 對應這次 review #4 新加的「Resend 建立延續 Row＋撤銷舊 Row」：平行重寄不能核發出
        // 兩筆同時有效的延續 Row，DB 裡永遠只能有一筆「仍然有效」（RevokedAtUtc is null）。
        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
        });
        var (orderId, _, orderNumber, email) = await SeedGuestOrderAsync(factory);
        using var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);
        var requestPublicId = await RequestAccessAsync(client, orderNumber, email);
        await capturingEmailSender.WaitForSingleMessageAsync();

        var resendUrl = $"/api/v1/guest-orders/access-requests/{requestPublicId}/actions/resend";
        var tasks = new[]
        {
            client.PostAsync(resendUrl, content: null),
            client.PostAsync(resendUrl, content: null),
        };
        var responses = await Task.WhenAll(tasks);
        foreach (var response in responses)
        {
            // 兩邊都維持恆定 202——不能因為輸掉競爭就回不同的狀態碼，那本身會洩漏
            // 「誰先誰後」這種跟訂單存在性無關、但一樣不該外洩的時序資訊。
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            response.Dispose();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var chain = await dbContext.GuestOrderAccessRequests
            .Where(r => r.OrderId == orderId)
            .ToListAsync();

        // 原始 Row＋剛好一筆延續 Row（另一個平行呼叫沒有真的核發出第二筆），
        // 且任何時間點只有一筆是「仍然有效」。
        Assert.Equal(2, chain.Count);
        Assert.Single(chain, r => r.RevokedAtUtc is null);
    }

    [Fact]
    public async Task GuestOrderAccessCookie_CanBeUsedRepeatedlyForItsOwnOrderButRejectedForAnotherOrder()
    {
        // 負面授權測試核心案例：Actor A 的限單 Cookie 不得用來存取 Order B（跨訂單拒絕），
        // 且拒絕後不留下任何資料存取的副作用（ScopeViolationCount 遞增以外沒有其他狀態改變）。
        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
        });
        var (_, orderAPublicId, orderANumber, orderAEmail) = await SeedGuestOrderAsync(factory);
        var (_, orderBPublicId, _, _) = await SeedGuestOrderAsync(factory);
        using var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);
        var requestPublicId = await RequestAccessAsync(client, orderANumber, orderAEmail);
        var code = ExtractCode((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);

        using var verifyResponse = await client.PostAsJsonAsync("/api/v1/guest-orders/access-verifications", new
        {
            requestPublicId,
            code,
        });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        using var ownOrderResponse = await client.GetAsync($"/__tests/security/guest-order/{orderAPublicId:D}");
        using var otherOrderResponse = await client.GetAsync($"/__tests/security/guest-order/{orderBPublicId:D}");
        using var ownOrderResponseAgain = await client.GetAsync($"/__tests/security/guest-order/{orderAPublicId:D}");

        Assert.Equal(HttpStatusCode.OK, ownOrderResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherOrderResponse.StatusCode);
        // 30 分鐘內可重複使用：跨訂單被拒絕後，同一張 Cookie 對自己的訂單仍然有效。
        Assert.Equal(HttpStatusCode.OK, ownOrderResponseAgain.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var token = await dbContext.GuestOrderAccessTokens
            .Where(t => t.OrderId == GetOrderId(orderAPublicId, dbContext))
            .SingleAsync();
        Assert.Equal(1, token.ScopeViolationCount);
    }

    [Fact]
    public async Task GuestOrder_WithoutAnyCookie_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);

        using var response = await client.GetAsync($"/__tests/security/guest-order/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static long GetOrderId(Guid orderPublicId, DoSelectDbContext dbContext) =>
        dbContext.Orders.Single(o => o.PublicId == orderPublicId).Id;

    private static async Task<Guid> RequestAccessAsync(HttpClient client, string orderNumber, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/guest-orders/access-requests", new
        {
            orderNumber,
            email,
        });
        var body = await response.Content.ReadFromJsonAsync<GuestOrderAccessRequestAcceptedDto>();
        return body!.RequestPublicId;
    }

    private WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection>? configureServices = null) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services
                    .AddControllers()
                    .AddApplicationPart(typeof(SecurityFoundationTestController).Assembly);
                configureServices?.Invoke(services);
            });
        });

    private static async Task<(long OrderId, Guid OrderPublicId, string OrderNumber, string Email)>
        SeedGuestOrderAsync(WebApplicationFactory<Program> factory)
    {
        var unique = Guid.NewGuid().ToString("N")[..12];
        var orderNumber = $"GOA-{unique}";
        var email = $"guest-order-{unique}@example.com";

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();

        var shippingProfile = new ShippingProviderProfile(
            Guid.CreateVersion7(), $"SHIP-{unique}", 1, "Active", null, null, "{}", 1, CreatedAtUtc);
        dbContext.ShippingProviderProfiles.Add(shippingProfile);
        await dbContext.SaveChangesAsync();

        var orderPublicId = Guid.CreateVersion7();
        var order = Order.Create(
            orderPublicId,
            new OrderCreation(
                orderNumber,
                null,
                email.ToUpperInvariant(),
                OrderStatus.PendingPayment,
                PaymentStatus.AwaitingPayment,
                FulfillmentStatus.Pending,
                AssemblyStatus.NotRequired,
                1_200m,
                100m,
                225m,
                0m,
                1_325m,
                "Guest",
                "0912345678",
                email,
                "100",
                "Taipei",
                "Zhongzheng",
                "No. 1",
                null,
                "HOME_DELIVERY",
                shippingProfile.Id,
                null,
                null,
                null,
                1,
                1,
                null,
                CreatedAtUtc.AddDays(3),
                $"checkout-{unique}",
                null),
            CreatedAtUtc);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        return (order.Id, orderPublicId, orderNumber, email);
    }

    private static async Task PrimeAntiforgeryAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add("X-DoSelect-Client", "member");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", body.GetProperty("requestToken").GetString());
    }

    private static string ExtractCode(string emailTextBody)
    {
        var match = Regex.Match(emailTextBody, @"驗證碼為：(?<code>\d{6})");
        Assert.True(match.Success, $"No verification code found in email body: {emailTextBody}");
        return match.Groups["code"].Value;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> SentMessages { get; } = [];

        public Task<EmailDeliveryResult> SendAsync(
            EmailMessage message, CancellationToken cancellationToken = default)
        {
            lock (SentMessages)
            {
                SentMessages.Add(message);
            }

            return Task.FromResult(new EmailDeliveryResult(EmailDeliveryStatus.Sent));
        }

        public async Task<EmailMessage> WaitForSingleMessageAsync(TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
            while (DateTime.UtcNow < deadline)
            {
                lock (SentMessages)
                {
                    if (SentMessages.Count > 0)
                    {
                        return Assert.Single(SentMessages);
                    }
                }

                await Task.Delay(20);
            }

            return Assert.Single(SentMessages);
        }
    }
}
