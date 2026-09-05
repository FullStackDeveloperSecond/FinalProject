using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using DoSelect.Api.Orders;
using DoSelect.Api.Security;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Common;
using DoSelect.Application.Orders;
using DoSelect.Application.Payments;
using DoSelect.Domain.Payments;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests.Orders;

public sealed class PaymentAttemptApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string MemberUserId = "payment-attempt-member";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public PaymentAttemptApiTests(WebApplicationFactory<Program> baseFactory) =>
        _baseFactory = baseFactory;

    [Fact]
    public async Task CreatePaymentAttempt_AsOrderMember_ForwardsOnlyTrustedActorAndRouteOrder()
    {
        var writer = new FakeWriter();
        using var factory = CreateFactory(writer);
        using var client = factory.CreateClient();
        var orderPublicId = Guid.NewGuid();

        using var response = await PostAsync(client, orderPublicId, includeIdempotencyKey: true);
        var responseText = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(responseText);

        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Expected 201 but received {(int)response.StatusCode}: {responseText}");
        Assert.Equal(writer.Result.PublicId, body.RootElement.GetProperty("publicId").GetGuid());
        Assert.Equal("awaitingPayment", body.RootElement.GetProperty("status").GetString());
        Assert.NotNull(writer.Command);
        Assert.Equal(orderPublicId, writer.Command.OrderPublicId);
        Assert.Equal("payment-attempt-key", writer.Command.IdempotencyKey);
        var actor = Assert.IsType<OrderActor.Member>(writer.Command.Actor);
        Assert.Equal(MemberUserId, actor.UserId);
    }

    [Fact]
    public async Task CreatePaymentAttempt_WithoutIdempotencyKey_ReturnsValidationProblem()
    {
        var writer = new FakeWriter();
        using var factory = CreateFactory(writer);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, Guid.NewGuid(), includeIdempotencyKey: false);
        var responseText = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(responseText);

        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 400 but received {(int)response.StatusCode}: {responseText}");
        Assert.Equal("validation_failed", body.RootElement.GetProperty("code").GetString());
        Assert.Null(writer.Command);
    }

    private WebApplicationFactory<Program> CreateFactory(FakeWriter writer) =>
        _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<IAuthenticationHandlerProvider>();
            services.AddSingleton<IAuthenticationHandlerProvider, HeaderAuthenticationHandlerProvider>();
            services.RemoveAll<IIdempotencyExecutor>();
            services.AddSingleton<IIdempotencyExecutor, UnusedIdempotencyExecutor>();
            services.RemoveAll<IPaymentAttemptWriter>();
            services.AddSingleton<IPaymentAttemptWriter>(writer);
            // 擁有者比對現在在寫入之前做（Issue #86 C1 的 Member → Guest 回退需要它），
            // 所以這個以假 writer 為主的測試也要有一個會說「是擁有者」的 IOrderService。
            services.RemoveAll<IOrderService>();
            services.AddSingleton<IOrderService, OwnerOrderService>();
        }));

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        Guid orderPublicId,
        bool includeIdempotencyKey)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/orders/{orderPublicId}/payment-attempts")
        {
            Content = JsonContent.Create(
                new CreatePaymentAttemptRequest(PaymentMethod.CreditCard, [1, 2, 3, 4, 5, 6, 7, 8]),
                options: jsonOptions),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        request.Headers.Add(SecurityController.ClientHeaderName, DoSelectClaimValues.Member);
        request.Headers.Add("X-Test-Payment-Member", MemberUserId);
        if (includeIdempotencyKey)
        {
            request.Headers.Add(OrdersController.IdempotencyKeyHeaderName, "payment-attempt-key");
        }

        return await client.SendAsync(request);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/security/antiforgery-token");
        request.Headers.Add(SecurityController.ClientHeaderName, DoSelectClaimValues.Member);
        request.Headers.Add("X-Test-Payment-Member", MemberUserId);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("requestToken").GetString()!;
    }

    /// <summary>只回答擁有者比對；這個測試不碰其他訂單操作。</summary>
    private sealed class OwnerOrderService : IOrderService
    {
        public Task<bool> IsMemberOwnerAsync(
            string memberUserId, Guid orderPublicId, CancellationToken cancellationToken) =>
            Task.FromResult(memberUserId == MemberUserId);

        public Task<PageResult<OrderSummaryDto>> GetOrdersAsync(
            string memberUserId, OrderQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OrderDto> GetOrderAsync(
            OrderActor actor, Guid orderPublicId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OrderDto> CancelOrderAsync(
            OrderActor actor,
            Guid orderPublicId,
            CancelOrderRequest request,
            OrderCancellationAuditContext auditContext,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeWriter : IPaymentAttemptWriter
    {
        public PaymentAttemptDto Result { get; } = new(
            Guid.NewGuid(),
            PaymentMethod.CreditCard,
            PaymentAttemptStatus.AwaitingPayment,
            1_000m,
            "TWD",
            null,
            DateTime.UtcNow,
            null,
            [1, 2, 3, 4, 5, 6, 7, 8]);

        public CreatePaymentAttemptCommand? Command { get; private set; }

        public Task<IdempotencyExecutionResult<PaymentAttemptDto>> CreateAsync(
            CreatePaymentAttemptCommand command,
            CancellationToken cancellationToken = default)
        {
            Command = command;
            return Task.FromResult(new IdempotencyExecutionResult<PaymentAttemptDto>(
                201,
                Result,
                "{}",
                IsReplay: false));
        }
    }

    private sealed class UnusedIdempotencyExecutor : IIdempotencyExecutor
    {
        public Task<IdempotencyExecutionResult<T>> ExecuteAsync<T>(
            IdempotencyCommand command,
            Func<CancellationToken, Task<IdempotencyResponse<T>>> handler,
            Func<StoredIdempotencyResponse, CancellationToken, Task<T>> replayFactory,
            CancellationToken cancellationToken,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted) =>
            throw new NotSupportedException("Checkout is outside this endpoint test.");
    }

    private sealed class HeaderAuthenticationHandlerProvider : IAuthenticationHandlerProvider
    {
        public async Task<IAuthenticationHandler?> GetHandlerAsync(
            HttpContext context,
            string authenticationScheme)
        {
            var handler = new HeaderAuthenticationHandler(authenticationScheme);
            var scheme = new AuthenticationScheme(
                authenticationScheme,
                displayName: null,
                typeof(HeaderAuthenticationHandler));
            await handler.InitializeAsync(scheme, context);
            return handler;
        }
    }

    private sealed class HeaderAuthenticationHandler(string scheme) : IAuthenticationHandler
    {
        private HttpContext _context = null!;

        public Task InitializeAsync(AuthenticationScheme authenticationScheme, HttpContext context)
        {
            _context = context;
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync()
        {
            if (scheme != DoSelectAuthenticationSchemes.Member ||
                !_context.Request.Headers.TryGetValue("X-Test-Payment-Member", out var userId) ||
                string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Member),
                ],
                scheme));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, scheme)));
        }

        public Task ChallengeAsync(AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(AuthenticationProperties? properties) => Task.CompletedTask;
    }
}
