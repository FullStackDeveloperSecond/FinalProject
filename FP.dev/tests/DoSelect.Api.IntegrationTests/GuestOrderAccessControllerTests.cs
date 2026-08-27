using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoSelect.Api.Contracts.Orders;
using DoSelect.Application.Auditing;
using DoSelect.Application.Notifications;
using DoSelect.Application.Orders;
using DoSelect.Application.Returns;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Outbox;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Outbox;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests;

[Collection(nameof(GuestOrderAccessApiCollection))]
public sealed class GuestOrderAccessControllerTests(GuestOrderAccessApiFixture fixture)
{
    private static readonly DateTime CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5);

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
        await ConsumeEmailOutboxAsync(factory, body.RequestPublicId);
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
        Assert.Empty(capturingEmailSender.SentMessages);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        Assert.False(await dbContext.OutboxMessages.AnyAsync(candidate =>
            candidate.AggregatePublicId == body.RequestPublicId));
    }

    [Fact]
    public async Task ImmediateResend_ValidAndDecoyRequestsBothKeepTheOriginalRequestId()
    {
        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
        });
        var (_, _, orderNumber, email) = await SeedGuestOrderAsync(factory);
        using var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);

        var validRequestId = await RequestAccessAsync(client, orderNumber, email);
        var decoyRequestId = await RequestAccessAsync(
            client,
            $"NO-SUCH-{Guid.NewGuid():N}"[..20],
            "nobody@example.com");

        using var validResend = await client.PostAsync(
            $"/api/v1/guest-orders/access-requests/{validRequestId}/actions/resend",
            content: null);
        using var decoyResend = await client.PostAsync(
            $"/api/v1/guest-orders/access-requests/{decoyRequestId}/actions/resend",
            content: null);
        var validBody = await validResend.Content.ReadFromJsonAsync<GuestOrderAccessRequestAcceptedDto>();
        var decoyBody = await decoyResend.Content.ReadFromJsonAsync<GuestOrderAccessRequestAcceptedDto>();

        Assert.Equal(HttpStatusCode.Accepted, validResend.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, decoyResend.StatusCode);
        Assert.Equal(validRequestId, validBody!.RequestPublicId);
        Assert.Equal(decoyRequestId, decoyBody!.RequestPublicId);
        Assert.Equal(DateTimeKind.Utc, validBody.ExpiresAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, decoyBody.ExpiresAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, validBody.ResendAvailableAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, decoyBody.ResendAvailableAtUtc.Kind);
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
        await ConsumeEmailOutboxAsync(factory, requestPublicId);
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
        await ConsumeEmailOutboxAsync(factory, requestPublicId);
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
        await ConsumeEmailOutboxAsync(factory, requestPublicId);
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
    public async Task Resend_TwoParallelCallsOnTheSameChallenge_UpdatesStableChallengeOnce()
    {
        // A1：平行重寄只能有一個呼叫成功更新穩定 Challenge，另一個 reload 後會被 60 秒
        // 間隔擋下；兩個回應都維持原 RequestPublicId，只新增一筆 rate-limit event。
        //
        // review #3：GuestOrderAccessRequest.EnsureCanSend 規定同一筆 Row 兩次寄送間隔至少
        // 60 秒，建立 Request 當下已經算一次寄送（SendCount=1）——不推進時間，兩個平行呼叫
        // 都會因為未滿 60 秒被 EnsureCanSend 擋下，DB 只會停在原始那 1 筆，不會有延續 Row。
        // 用可控制的 TimeProvider 先推進 61 秒，兩個平行呼叫才會是「這次寄送已經合法」的
        // 真實競爭，而不是被寄送間隔規則一起擋下的偽陽性。
        var capturingEmailSender = new CapturingEmailSender();
        var timeProvider = new ControllableTimeProvider(DateTimeOffset.UtcNow);
        using var factory = CreateFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(timeProvider));
        });
        var (orderId, _, orderNumber, email) = await SeedGuestOrderAsync(factory);
        using var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);
        var requestPublicId = await RequestAccessAsync(client, orderNumber, email);
        await ConsumeEmailOutboxAsync(factory, requestPublicId);
        await capturingEmailSender.WaitForSingleMessageAsync();

        timeProvider.Advance(TimeSpan.FromSeconds(61));

        var resendUrl = $"/api/v1/guest-orders/access-requests/{requestPublicId}/actions/resend";
        var tasks = new[]
        {
            client.PostAsync(resendUrl, content: null),
            client.PostAsync(resendUrl, content: null),
        };
        var responses = await Task.WhenAll(tasks);
        var responseBodies = new List<GuestOrderAccessRequestAcceptedDto>();
        foreach (var response in responses)
        {
            // 兩邊都維持恆定 202——不能因為輸掉競爭就回不同的狀態碼，那本身會洩漏
            // 「誰先誰後」這種跟訂單存在性無關、但一樣不該外洩的時序資訊。
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            responseBodies.Add((await response.Content.ReadFromJsonAsync<GuestOrderAccessRequestAcceptedDto>())!);
            response.Dispose();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var challenge = await dbContext.GuestOrderAccessRequests
            .Where(r => r.OrderId == orderId)
            .SingleAsync();
        Assert.Equal(requestPublicId, challenge.PublicId);
        Assert.Equal(2, challenge.SendCount);
        Assert.Null(challenge.RevokedAtUtc);

        var rateLimitEvents = await dbContext.GuestOrderAccessRequests
            .Where(r =>
                r.OrderId == null &&
                r.EmailKeyHash == challenge.EmailKeyHash &&
                r.OrderLookupKeyHash == challenge.OrderLookupKeyHash)
            .ToListAsync();
        var rateLimitEvent = Assert.Single(rateLimitEvents);
        Assert.NotNull(rateLimitEvent.RevokedAtUtc);

        Assert.All(responseBodies, body => Assert.Equal(requestPublicId, body.RequestPublicId));
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
        await ConsumeEmailOutboxAsync(factory, requestPublicId);
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

        var audit = await dbContext.AuditLogs
            .SingleAsync(entry => entry.Action == AuditActions.GuestOrderScopeViolation);
        Assert.Equal(AuditActorType.Guest, audit.ActorType);
        Assert.Equal(token.PublicId, audit.ActorPublicId);
        Assert.Equal(AuditResourceTypes.Order, audit.ResourceType);
        Assert.Equal(orderBPublicId, audit.ResourcePublicId);
        Assert.Equal(AuditResult.Rejected, audit.Result);
        Assert.Equal(GuestOrderErrorCodes.ScopeMismatch, audit.ErrorCode);
        Assert.Contains("scopeViolationCount", audit.ChangedFieldsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GuestOrderAccessCookie_MintedByVerify_IsAcceptedByFormalReturnsResolver()
    {
        var capturingEmailSender = new CapturingEmailSender();
        var returnService = new CapturingReturnService();
        using var factory = CreateFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
            services.RemoveAll<IReturnService>();
            services.AddSingleton<IReturnService>(returnService);
        });
        var (orderId, orderPublicId, orderNumber, email) = await SeedGuestOrderAsync(factory);
        var (_, otherOrderPublicId, _, _) = await SeedGuestOrderAsync(factory);
        using var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);
        var requestPublicId = await RequestAccessAsync(client, orderNumber, email);
        await ConsumeEmailOutboxAsync(factory, requestPublicId);
        var code = ExtractCode((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);

        using var verifyResponse = await client.PostAsJsonAsync(
            "/api/v1/guest-orders/access-verifications",
            new { requestPublicId, code });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        using var returnResponse = await client.PostAsJsonAsync(
            $"/api/v1/orders/{orderPublicId:D}/returns",
            new
            {
                items = Array.Empty<object>(),
                requestReason = "test-only",
                orderRowVersion = Array.Empty<byte>(),
            });

        Assert.Equal(HttpStatusCode.BadRequest, returnResponse.StatusCode);
        Assert.NotNull(returnService.Actor);
        Assert.Null(returnService.Actor.MemberUserId);
        Assert.Equal(orderId, returnService.Actor.GuestOrderId);
        Assert.Equal(orderPublicId, returnService.OrderPublicId);

        returnService.Reset();
        using var crossOrderResponse = await client.PostAsJsonAsync(
            $"/api/v1/orders/{otherOrderPublicId:D}/returns",
            new
            {
                items = Array.Empty<object>(),
                requestReason = "test-only",
                orderRowVersion = Array.Empty<byte>(),
            });

        Assert.Equal(HttpStatusCode.NotFound, crossOrderResponse.StatusCode);
        Assert.Null(returnService.Actor);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var token = await dbContext.GuestOrderAccessTokens.SingleAsync(candidate =>
            candidate.OrderId == orderId);
        Assert.Equal(1, token.ScopeViolationCount);
        Assert.True(await dbContext.AuditLogs.AnyAsync(entry =>
            entry.Action == AuditActions.GuestOrderScopeViolation &&
            entry.ResourcePublicId == otherOrderPublicId));
    }

    [Fact]
    public async Task GuestOrderAccessCookie_CanReadAndCancelItsOwnOrder()
    {
        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
        });
        var (orderId, orderPublicId, orderNumber, email) = await SeedGuestOrderAsync(factory);
        using var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);
        var requestPublicId = await RequestAccessAsync(client, orderNumber, email);
        await ConsumeEmailOutboxAsync(factory, requestPublicId);
        var code = ExtractCode((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);

        using var verifyResponse = await client.PostAsJsonAsync(
            "/api/v1/guest-orders/access-verifications",
            new { requestPublicId, code });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        using var getResponse = await client.GetAsync($"/api/v1/orders/{orderPublicId:D}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var orderDocument = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        var order = orderDocument.RootElement;
        Assert.Contains(
            order.GetProperty("availableActions").EnumerateArray(),
            action => action.GetString() == "cancel");
        var orderRowVersion = order.GetProperty("rowVersion").GetString();
        Assert.False(string.IsNullOrWhiteSpace(orderRowVersion));

        using var cancelResponse = await client.PostAsJsonAsync(
            $"/api/v1/orders/{orderPublicId:D}/actions/cancel",
            new
            {
                reasonCode = OrderCancellationReasonCodes.OrderedByMistake,
                note = "guest duplicate order",
                orderRowVersion,
            });
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var persistedOrder = await dbContext.Orders.SingleAsync(candidate => candidate.Id == orderId);
        Assert.Equal(OrderStatus.Cancelled, persistedOrder.OrderStatus);
        var history = await dbContext.OrderStatusHistories.SingleAsync(candidate => candidate.OrderId == orderId);
        Assert.Null(history.ActorUserId);
        var token = await dbContext.GuestOrderAccessTokens.SingleAsync(candidate => candidate.OrderId == orderId);
        var audit = await dbContext.AuditLogs.SingleAsync(candidate =>
            candidate.Action == AuditActions.OrderCancel &&
            candidate.ResourcePublicId == orderPublicId);
        Assert.Equal(AuditActorType.Guest, audit.ActorType);
        Assert.Equal(token.PublicId, audit.ActorPublicId);
    }

    [Fact]
    public async Task GuestOrderAccessCookie_WhenReadingAnotherOrder_ReturnsNotFoundAndAuditsViolation()
    {
        var capturingEmailSender = new CapturingEmailSender();
        using var factory = CreateFactory(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(capturingEmailSender));
        });
        var (orderId, _, orderNumber, email) = await SeedGuestOrderAsync(factory);
        var (_, otherOrderPublicId, _, _) = await SeedGuestOrderAsync(factory);
        using var client = factory.CreateClient();
        await PrimeAntiforgeryAsync(client);
        var requestPublicId = await RequestAccessAsync(client, orderNumber, email);
        await ConsumeEmailOutboxAsync(factory, requestPublicId);
        var code = ExtractCode((await capturingEmailSender.WaitForSingleMessageAsync()).TextBody);

        using var verifyResponse = await client.PostAsJsonAsync(
            "/api/v1/guest-orders/access-verifications",
            new { requestPublicId, code });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        using var response = await client.GetAsync($"/api/v1/orders/{otherOrderPublicId:D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var token = await dbContext.GuestOrderAccessTokens.SingleAsync(candidate => candidate.OrderId == orderId);
        Assert.Equal(1, token.ScopeViolationCount);
        Assert.True(await dbContext.AuditLogs.AnyAsync(entry =>
            entry.Action == AuditActions.GuestOrderScopeViolation &&
            entry.ActorPublicId == token.PublicId &&
            entry.ResourcePublicId == otherOrderPublicId));
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

    private static async Task ConsumeEmailOutboxAsync(
        WebApplicationFactory<Program> factory,
        Guid requestPublicId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var message = await dbContext.OutboxMessages.SingleAsync(candidate =>
            candidate.AggregateType == GuestOrderAccessNotificationContract.ResourceType &&
            candidate.AggregatePublicId == requestPublicId &&
            candidate.Status == OutboxMessageStatus.Pending);
        var consumer = scope.ServiceProvider.GetServices<IOutboxConsumer>()
            .Single(candidate => candidate.EventType == message.Type);

        var result = await consumer.ConsumeAsync(message);

        Assert.True(result.Succeeded, result.ErrorCode);
    }

    private WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection>? configureServices = null) =>
        fixture.CreateFactory(configureServices);

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
        var packageLimit = new PackageLimitVersion(
            Guid.CreateVersion7(), shippingProfile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m,
            null, null, CreatedAtUtc);
        dbContext.PackageLimitVersions.Add(packageLimit);
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
                null,
                1,
                1,
                new OrderInvoicePreference(
                    SimulatedInvoiceBuyerType.Individual,
                    email,
                    null,
                    null,
                    null,
                    null),
                null,
                null,
                new OrderPackageSnapshot(
                    packageLimit.Id, 1m, 40m, 30m, 20m, 90m, 1_200m)),
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

            return Task.FromResult(new EmailDeliveryResult(
                EmailDeliveryStatus.Sent,
                $"test-{Guid.NewGuid():N}"));
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

    private sealed class CapturingReturnService : IReturnService
    {
        public ReturnActor? Actor { get; private set; }
        public Guid? OrderPublicId { get; private set; }

        public void Reset()
        {
            Actor = null;
            OrderPublicId = null;
        }

        public Task<ReturnRequestDto> CreateAsync(
            ReturnActor actor,
            Guid orderPublicId,
            CreateReturnRequest request,
            CancellationToken cancellationToken)
        {
            Actor = actor;
            OrderPublicId = orderPublicId;
            throw new ReturnsWriteException(
                ReturnsWriteException.ErrorCodes.ValidationFailed,
                "Synthetic post-authorization validation failure.");
        }

        public Task<ReturnRequestDto> GetDetailAsync(
            ReturnActor actor,
            Guid returnPublicId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReturnAttachmentDto> UploadAttachmentAsync(
            ReturnActor actor,
            Guid returnPublicId,
            DoSelect.Application.Files.PrivateFileUpload upload,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// 取代 <c>TimeProvider.System</c> 的 Singleton 登錄，讓測試可以在真的平行 HTTP 請求前
    /// 先推進時間（例如跳過 60 秒寄送間隔限制）。用 lock 保護，因為平行請求會在不同執行緒
    /// 同時呼叫 <see cref="GetUtcNow"/>。
    /// </summary>
    private sealed class ControllableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly object _lock = new();
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_lock)
            {
                return _now;
            }
        }

        public void Advance(TimeSpan delta)
        {
            lock (_lock)
            {
                _now += delta;
            }
        }
    }
}
