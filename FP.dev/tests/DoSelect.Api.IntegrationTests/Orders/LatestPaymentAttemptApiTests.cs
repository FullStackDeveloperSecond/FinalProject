using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
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
/// <c>GET /api/v1/orders/{id}/payment-attempts/latest</c> 的授權矩陣，走完整條 HTTP 路徑。
/// </summary>
/// <remarks>
/// <para>
/// <b>用真的 <see cref="GuestOrderAccessScopeAuthorizer"/></b>，只把它的
/// <c>IGuestOrderAccessGateway</c> 換成假的 —— alex Issue #86 C1 要求重用既有 authorizer、
/// 不建立平行 validator，所以測試也不該繞過它自己判斷 token。過期／撤銷／跨訂單三種
/// Failure 都是真的授權器算出來的。
/// </para>
/// <para>
/// 不需要 SQL Server：Reader 與 Gateway 都是介面。排序與資料庫行為由
/// <c>LatestPaymentAttemptReaderSqlServerTests</c> 負責。
/// </para>
/// </remarks>
public sealed class LatestPaymentAttemptApiTests
{
    private const string MemberHeader = "X-Test-Payment-Member";
    private const string GuestTokenHeader = "X-Test-Guest-Token";
    private const string OwnerUserId = "latest-attempt-owner";
    private const string RawGuestToken = "raw-guest-token";

    private static readonly Guid OrderPublicId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherOrderPublicId = new("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime NowUtc = new(2026, 9, 1, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TheOwnerGetsTheLatestAttempt()
    {
        using var factory = CreateFactory(new FakeReader(OwnerUserId, Attempt()));
        using var client = factory.CreateClient();

        using var response = await GetAsync(client, memberUserId: OwnerUserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("awaitingPayment", body.GetProperty("status").GetString());
        Assert.Equal(1000m, body.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task ADeferredAttemptStillCarriesTheInstructionCode()
    {
        // ATM／超商代碼是使用者要拿去繳費的東西，正是重新整理最不能掉的欄位。
        using var factory = CreateFactory(
            new FakeReader(OwnerUserId, Attempt(method: PaymentMethod.ATM)));
        using var client = factory.CreateClient();

        using var response = await GetAsync(client, memberUserId: OwnerUserId);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "SIM-REFERENCE",
            body.GetProperty("instruction").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnOrderWithNoAttemptIsNotFound()
    {
        using var factory = CreateFactory(new FakeReader(OwnerUserId, attempt: null));
        using var client = factory.CreateClient();

        using var response = await GetAsync(client, memberUserId: OwnerUserId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownOrderIsNotFound()
    {
        using var factory = CreateFactory(new FakeReader(OwnerUserId, Attempt(), orderExists: false));
        using var client = factory.CreateClient();

        using var response = await GetAsync(client, memberUserId: OwnerUserId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnotherMemberWithNoGuestCookieIsNotFoundRatherThanForbidden()
    {
        // 「不是你的」與「不存在」對外折成同一個 404。
        using var factory = CreateFactory(new FakeReader(OwnerUserId, Attempt()));
        using var client = factory.CreateClient();

        using var response = await GetAsync(client, memberUserId: "someone-else");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnAnonymousCallerIsUnauthorized()
    {
        using var factory = CreateFactory(new FakeReader(OwnerUserId, Attempt()));
        using var client = factory.CreateClient();

        using var response = await GetAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AVerifiedGuestGetsTheAttempt()
    {
        using var factory = CreateFactory(
            new FakeReader(memberUserId: null, Attempt()),
            new FakeGuestGateway(OrderPublicId));
        using var client = factory.CreateClient();

        using var response = await GetAsync(client, guestToken: RawGuestToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ASignedInMemberWhoIsNotTheOwnerCanStillUseTheirGuestCookie()
    {
        // Issue #86 C1（同 #70 finding 4）：同一台裝置可以同時有會員 cookie 與某張
        // 訪客訂單的有效 token。會員不是擁有者時不能就此拒絕。
        using var factory = CreateFactory(
            new FakeReader(memberUserId: null, Attempt()),
            new FakeGuestGateway(OrderPublicId));
        using var client = factory.CreateClient();

        using var response = await GetAsync(
            client, memberUserId: "someone-else", guestToken: RawGuestToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AGuestTokenForAnotherOrderIsNotFoundWithTheScopeMismatchCode()
    {
        using var factory = CreateFactory(
            new FakeReader(memberUserId: null, Attempt()),
            new FakeGuestGateway(OtherOrderPublicId));
        using var client = factory.CreateClient();

        using var response = await GetAsync(client, guestToken: RawGuestToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(GuestOrderErrorCodes.ScopeMismatch, await ReadCodeAsync(response));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AnExpiredOrRevokedGuestTokenIsUnauthorized(bool expired, bool revoked)
    {
        // 過期／撤銷回 401 而不是 404：使用者要知道該重新驗證，不是以為訂單不見了。
        using var factory = CreateFactory(
            new FakeReader(memberUserId: null, Attempt()),
            new FakeGuestGateway(OrderPublicId, expired, revoked));
        using var client = factory.CreateClient();

        using var response = await GetAsync(client, guestToken: RawGuestToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(GuestOrderErrorCodes.AccessExpired, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task AGuestTokenThatNoLongerExistsIsUnauthorized()
    {
        using var factory = CreateFactory(
            new FakeReader(memberUserId: null, Attempt()),
            new FakeGuestGateway(orderPublicId: null));
        using var client = factory.CreateClient();

        using var response = await GetAsync(client, guestToken: RawGuestToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(GuestOrderErrorCodes.AccessExpired, await ReadCodeAsync(response));
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        string? memberUserId = null,
        string? guestToken = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/orders/{OrderPublicId:D}/payment-attempts/latest");
        if (memberUserId is not null)
        {
            request.Headers.Add(MemberHeader, memberUserId);
        }

        if (guestToken is not null)
        {
            request.Headers.Add(GuestTokenHeader, guestToken);
        }

        return client.SendAsync(request);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeReader reader,
        FakeGuestGateway? guestGateway = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAuthenticationHandlerProvider, HeaderHandlerProvider>();
                // EfIdempotencyExecutor 沒有設定 pepper 就會在建構時丟例外，而
                // OrdersController 需要 IPaymentAttemptWriter，它又需要這個執行器 ——
                // 少了這一行，連匿名請求都會變成 500（同 PaymentAttemptApiTests 的做法）。
                services.RemoveAll<IIdempotencyExecutor>();
                services.AddSingleton<IIdempotencyExecutor, UnusedIdempotencyExecutor>();
                services.RemoveAll<ILatestPaymentAttemptReader>();
                services.AddSingleton<ILatestPaymentAttemptReader>(reader);
                services.RemoveAll<IGuestOrderAccessGateway>();
                services.AddSingleton<IGuestOrderAccessGateway>(
                    guestGateway ?? new FakeGuestGateway(orderPublicId: null));
                services.RemoveAll<IGuestOrderAccessHasher>();
                services.AddSingleton<IGuestOrderAccessHasher, FakeHasher>();
            }));

    private static PaymentAttempt Attempt(PaymentMethod method = PaymentMethod.CreditCard)
    {
        var attempt = new PaymentAttempt(
            Guid.NewGuid(), 7L, method, 1000m, "SIM",
            $"key-{Guid.NewGuid():N}", NowUtc.AddHours(1), NowUtc);
        attempt.SetPaymentInstruction("SIM-REFERENCE", NowUtc);
        return attempt;
    }

    /// <summary>這支端點不寫任何東西，冪等執行器只是相依鏈上的必要品。</summary>
    private sealed class UnusedIdempotencyExecutor : IIdempotencyExecutor
    {
        public Task<IdempotencyExecutionResult<T>> ExecuteAsync<T>(
            IdempotencyCommand command,
            Func<CancellationToken, Task<IdempotencyResponse<T>>> handler,
            Func<StoredIdempotencyResponse, CancellationToken, Task<T>> replayFactory,
            CancellationToken cancellationToken,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted) =>
            throw new NotSupportedException("This endpoint performs no writes.");
    }

    private sealed class FakeReader(
        string? memberUserId,
        PaymentAttempt? attempt,
        bool orderExists = true) : ILatestPaymentAttemptReader
    {
        public Task<PaymentAttemptOrderReference?> FindOrderAsync(
            Guid orderPublicId, CancellationToken cancellationToken = default) =>
            Task.FromResult(orderExists
                ? new PaymentAttemptOrderReference(7L, memberUserId)
                : null);

        public Task<PaymentAttempt?> FindLatestAsync(
            long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(attempt);
    }

    /// <summary>只換掉授權器的資料來源，授權判斷仍由真的 authorizer 做。</summary>
    private sealed class FakeGuestGateway(
        Guid? orderPublicId,
        bool expired = false,
        bool revoked = false) : IGuestOrderAccessGateway
    {
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
            long tokenId, AuditWriteRequest auditRequest, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        // 授權器只會用到 FindTokenByHashAsync 與 RecordScopeViolationAsync。
        // 其餘成員刻意丟例外：如果哪天有人靠這個假物件測別的東西，
        // 要立刻炸開，而不是安靜地回一個假的成功。
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
