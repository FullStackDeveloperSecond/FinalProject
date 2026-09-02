using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using DoSelect.Api.Common;
using DoSelect.Api.Orders;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Orders;
using DoSelect.Application.Outbox;
using DoSelect.Application.Payments;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests.Orders;

/// <summary>
/// <c>POST /api/v1/orders/{id}/payment-attempts</c> 的授權矩陣。
/// </summary>
/// <remarks>
/// <para>
/// 這支端點原本帶著 alex 在 #70 finding 3／4 點名、在 Issue #86 C1 又重申的兩個缺陷：
/// 會員不是擁有者時沒有 Guest 回退，以及 Guest 失敗的錯誤語意被折平成 404。
/// </para>
/// <para>
/// 用<b>真的</b> <see cref="GuestOrderAccessScopeAuthorizer"/>，只換掉它的資料來源 ——
/// C1 要求重用既有 authorizer，所以測試也不該繞過它自己判斷 token。
/// </para>
/// </remarks>
public sealed class CreatePaymentAttemptAuthorizationTests
{
    private const string MemberHeader = "X-Test-Payment-Member";
    private const string GuestTokenHeader = "X-Test-Guest-Token";
    private const string OwnerUserId = "create-attempt-owner";
    private const string RawGuestToken = "raw-guest-token";

    private static readonly Guid OrderPublicId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherOrderPublicId = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task TheOwningMemberCreatesTheAttempt()
    {
        var writer = new RecordingWriter();
        using var factory = CreateFactory(writer, new OwnerOrderService(OwnerUserId));
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, memberUserId: OwnerUserId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var actor = Assert.IsType<OrderActor.Member>(writer.Command!.Actor);
        Assert.Equal(OwnerUserId, actor.UserId);
    }

    [Fact]
    public async Task ASignedInMemberWhoIsNotTheOwnerCanStillUseTheirGuestCookie()
    {
        // #70 finding 4 / Issue #86 C1：同一台裝置可以同時有會員 cookie 與某張訪客訂單的
        // 有效 token。這支端點原本在會員驗證成功後就不再試 guest token。
        var writer = new RecordingWriter();
        using var factory = CreateFactory(
            writer,
            new OwnerOrderService(ownerUserId: null),
            new FakeGuestGateway(OrderPublicId));
        using var client = factory.CreateClient();

        using var response = await PostAsync(
            client, memberUserId: "someone-else", guestToken: RawGuestToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.IsType<OrderActor.Guest>(writer.Command!.Actor);
    }

    [Fact]
    public async Task AnOwningMemberIsNotPenalisedForHoldingAStaleGuestCookie()
    {
        // 先驗 Guest 會有副作用：authorizer 對跨訂單存取會記一次違規。擁有者查自己的
        // 訂單不該被記上違規，所以只有在會員不是擁有者時才去驗 token。
        var writer = new RecordingWriter();
        var gateway = new FakeGuestGateway(OtherOrderPublicId);
        using var factory = CreateFactory(writer, new OwnerOrderService(OwnerUserId), gateway);
        using var client = factory.CreateClient();

        using var response = await PostAsync(
            client, memberUserId: OwnerUserId, guestToken: RawGuestToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.IsType<OrderActor.Member>(writer.Command!.Actor);
        Assert.Equal(0, gateway.ScopeViolations);
    }

    [Fact]
    public async Task AVerifiedGuestCreatesTheAttempt()
    {
        var writer = new RecordingWriter();
        using var factory = CreateFactory(
            writer,
            new OwnerOrderService(ownerUserId: null),
            new FakeGuestGateway(OrderPublicId));
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, guestToken: RawGuestToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.IsType<OrderActor.Guest>(writer.Command!.Actor);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AnExpiredOrRevokedGuestTokenIsUnauthorized(bool expired, bool revoked)
    {
        // #70 finding 3：過期／撤銷要回 401，使用者才知道該重新驗證。
        // 這支端點原本把它折成 404。
        var writer = new RecordingWriter();
        using var factory = CreateFactory(
            writer,
            new OwnerOrderService(ownerUserId: null),
            new FakeGuestGateway(OrderPublicId, expired, revoked));
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, guestToken: RawGuestToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(GuestOrderErrorCodes.AccessExpired, await ReadCodeAsync(response));
        Assert.Null(writer.Command);
    }

    [Fact]
    public async Task AGuestTokenForAnotherOrderIsNotFound()
    {
        // Scope 不符仍折成 404 —— 分開回答等於告訴外人這個 id 存在。
        var writer = new RecordingWriter();
        using var factory = CreateFactory(
            writer,
            new OwnerOrderService(ownerUserId: null),
            new FakeGuestGateway(OtherOrderPublicId));
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, guestToken: RawGuestToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(GuestOrderErrorCodes.ScopeMismatch, await ReadCodeAsync(response));
        Assert.Null(writer.Command);
    }

    [Fact]
    public async Task AMemberWhoIsNotTheOwnerAndHasNoGuestCookieIsNotFound()
    {
        var writer = new RecordingWriter();
        using var factory = CreateFactory(writer, new OwnerOrderService(OwnerUserId));
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, memberUserId: "someone-else");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(OrderWriteException.ErrorCodes.ResourceNotFound, await ReadCodeAsync(response));
        Assert.Null(writer.Command);
    }

    [Fact]
    public async Task AnAnonymousCallerIsUnauthorized()
    {
        var writer = new RecordingWriter();
        using var factory = CreateFactory(writer, new OwnerOrderService(OwnerUserId));
        using var client = factory.CreateClient();

        using var response = await PostAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApiErrorCodes.AuthenticationRequired, await ReadCodeAsync(response));
        Assert.Null(writer.Command);
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string? memberUserId = null,
        string? guestToken = null)
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/orders/{OrderPublicId:D}/payment-attempts")
        {
            Content = JsonContent.Create(
                new CreatePaymentAttemptRequest(PaymentMethod.CreditCard, [1, 2, 3, 4, 5, 6, 7, 8]),
                options: jsonOptions),
        };
        request.Headers.Add(OrdersController.IdempotencyKeyHeaderName, "authz-matrix-key");
        // 全域 antiforgery 過濾器會把沒帶 token 的 POST 擋成 400，那樣就測不到授權。
        request.Headers.Add("X-XSRF-TOKEN", await GetAntiforgeryTokenAsync(client, memberUserId));
        request.Headers.Add(SecurityController.ClientHeaderName, DoSelectClaimValues.Member);
        if (memberUserId is not null)
        {
            request.Headers.Add(MemberHeader, memberUserId);
        }

        if (guestToken is not null)
        {
            request.Headers.Add(GuestTokenHeader, guestToken);
        }

        return await client.SendAsync(request);
    }

    /// <remarks>
    /// Token 綁呼叫者的身分，所以取 token 時要帶跟實際請求<b>同一個</b>會員 header ——
    /// 用匿名身分取的 token 配上帶會員身分的請求會驗不過，變成 400 而不是授權結果。
    /// </remarks>
    private static async Task<string> GetAntiforgeryTokenAsync(
        HttpClient client, string? memberUserId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add(SecurityController.ClientHeaderName, DoSelectClaimValues.Member);
        if (memberUserId is not null)
        {
            request.Headers.Add(MemberHeader, memberUserId);
        }

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("requestToken").GetString()!;
    }

    private static WebApplicationFactory<Program> CreateFactory(
        RecordingWriter writer,
        OwnerOrderService orderService,
        FakeGuestGateway? guestGateway = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAuthenticationHandlerProvider, HeaderHandlerProvider>();
                services.RemoveAll<IIdempotencyExecutor>();
                services.AddSingleton<IIdempotencyExecutor, UnusedIdempotencyExecutor>();
                services.RemoveAll<IPaymentAttemptWriter>();
                services.AddSingleton<IPaymentAttemptWriter>(writer);
                services.RemoveAll<IOrderService>();
                services.AddSingleton<IOrderService>(orderService);
                services.RemoveAll<IGuestOrderAccessGateway>();
                services.AddSingleton<IGuestOrderAccessGateway>(
                    guestGateway ?? new FakeGuestGateway(orderPublicId: null));
                services.RemoveAll<IGuestOrderAccessHasher>();
                services.AddSingleton<IGuestOrderAccessHasher, FakeHasher>();
            }));

    /// <summary>只回答擁有者比對；其餘訂單操作不在這個矩陣的範圍。</summary>
    private sealed class OwnerOrderService(string? ownerUserId) : IOrderService
    {
        public Task<bool> IsMemberOwnerAsync(
            string memberUserId, Guid orderPublicId, CancellationToken cancellationToken) =>
            Task.FromResult(ownerUserId is not null && memberUserId == ownerUserId);

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

    private sealed class RecordingWriter : IPaymentAttemptWriter
    {
        public CreatePaymentAttemptCommand? Command { get; private set; }

        public Task<IdempotencyExecutionResult<PaymentAttemptDto>> CreateAsync(
            CreatePaymentAttemptCommand command, CancellationToken cancellationToken = default)
        {
            Command = command;
            var dto = new PaymentAttemptDto(
                Guid.NewGuid(),
                PaymentMethod.CreditCard,
                PaymentAttemptStatus.AwaitingPayment,
                1000m,
                "TWD",
                null,
                DateTime.UtcNow,
                null,
                [1, 2, 3]);
            return Task.FromResult(new IdempotencyExecutionResult<PaymentAttemptDto>(
                201, dto, ResponseHeadersJson: null, IsReplay: false));
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
            throw new NotSupportedException("The writer is faked in this matrix.");
    }

    /// <summary>只換掉授權器的資料來源，判斷仍由真的 authorizer 做。</summary>
    private sealed class FakeGuestGateway(
        Guid? orderPublicId,
        bool expired = false,
        bool revoked = false) : IGuestOrderAccessGateway
    {
        public int ScopeViolations { get; private set; }

        public Task<GuestOrderAccessTokenContext?> FindTokenByHashAsync(
            byte[] tokenHash, CancellationToken cancellationToken = default)
        {
            if (orderPublicId is not { } scope)
            {
                return Task.FromResult<GuestOrderAccessTokenContext?>(null);
            }

            var token = new GuestOrderAccessToken(
                Guid.NewGuid(),
                7L,
                11L,
                new byte[32],
                expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddHours(1),
                DateTime.UtcNow.AddHours(-1));
            if (revoked)
            {
                token.Revoke(DateTime.UtcNow);
            }

            return Task.FromResult<GuestOrderAccessTokenContext?>(
                new GuestOrderAccessTokenContext(token, scope));
        }

        public Task RecordScopeViolationAsync(
            long tokenId, AuditWriteRequest auditRequest, CancellationToken cancellationToken = default)
        {
            ScopeViolations++;
            return Task.CompletedTask;
        }

        public Task<GuestOrderLookup?> FindGuestOrderAsync(
            string orderNumber, string emailNormalized, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GuestOrderLookup?> FindGuestOrderByIdAsync(
            long orderId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryCreateRequestWithinRateLimitAsync(
            GuestOrderAccessRateLimitWindow window,
            GuestOrderAccessRequest newRequest,
            OutboxWriteRequest? notification,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryRecordResendWithinRateLimitAsync(
            GuestOrderAccessRateLimitWindow window,
            GuestOrderAccessRequest request,
            GuestOrderAccessRequest rateLimitEvent,
            byte[]? newCodeHash,
            DateTime sentAtUtc,
            OutboxWriteRequest? notification,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryRecordUnknownResendAttemptAsync(
            byte[] ipHash,
            int ipPermitLimit,
            DateTime windowStartUtc,
            GuestOrderAccessRequest sentinelRequest,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GuestOrderAccessRequest?> FindActiveRequestAsync(
            Guid requestPublicId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddTokenAsync(
            GuestOrderAccessToken token, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> PurgeExpiredAsync(
            DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ReloadRequestAsync(
            GuestOrderAccessRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeHasher : IGuestOrderAccessHasher
    {
        public byte[] HashIp(string ipAddress) => new byte[32];

        public byte[] HashEmail(string emailNormalized) => new byte[32];

        public byte[] HashOrderLookup(string orderNumber, string emailNormalized) => new byte[32];

        public byte[] HashCode(string sixDigitCode) => new byte[32];

        public string DeriveVerificationCode(Guid requestPublicId, int sendNumber) => "000000";

        public byte[] HashToken(string rawToken) => new byte[32];
    }

    private sealed class HeaderHandlerProvider : IAuthenticationHandlerProvider
    {
        public async Task<IAuthenticationHandler?> GetHandlerAsync(
            HttpContext context, string authenticationScheme)
        {
            var handler = new HeaderHandler(authenticationScheme);
            await handler.InitializeAsync(
                new AuthenticationScheme(authenticationScheme, null, typeof(HeaderHandler)),
                context);
            return handler;
        }
    }

    private sealed class HeaderHandler(string scheme) : IAuthenticationHandler
    {
        private HttpContext _context = null!;

        public Task InitializeAsync(AuthenticationScheme authenticationScheme, HttpContext context)
        {
            _context = context;
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync()
        {
            if (scheme == DoSelectAuthenticationSchemes.Member &&
                _context.Request.Headers.TryGetValue(MemberHeader, out var userId) &&
                !string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(Success(
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Member)));
            }

            if (scheme == DoSelectAuthenticationSchemes.GuestOrderAccess &&
                _context.Request.Headers.TryGetValue(GuestTokenHeader, out var token) &&
                !string.IsNullOrWhiteSpace(token))
            {
                return Task.FromResult(Success(
                    new Claim(GuestOrderAccessClaimTypes.TokenValue, token.ToString())));
            }

            return Task.FromResult(AuthenticateResult.NoResult());
        }

        private AuthenticateResult Success(params Claim[] claims)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, scheme));
        }

        public Task ChallengeAsync(AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(AuthenticationProperties? properties) => Task.CompletedTask;
    }
}
