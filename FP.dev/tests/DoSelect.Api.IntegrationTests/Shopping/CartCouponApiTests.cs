using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Promotions;
using DoSelect.Application.Shopping;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests.Shopping;

/// <summary>
/// <c>POST／DELETE /api/v1/cart/coupon</c>（UC-COUPON-01）。
/// </summary>
/// <remarks>
/// <para>
/// 走完整條 HTTP 路徑。折扣計算已由 <c>ApplyCartCouponServiceTests</c> 覆蓋，這裡驗的是
/// 只有在 HTTP 層才看得到的東西：<b>請求 DTO 綁不綁得起來</b>、身分怎麼解析、
/// 驗證屬性有沒有真的生效。
/// </para>
/// <para>
/// 用<b>真的</b> <see cref="ApplyCartCouponService"/>，只把它的 <c>ICartCouponLineReader</c>
/// 換成回 <c>null</c> 的假物件 —— 服務因此丟 <c>NotFound</c>。這讓「到得了服務」（404）
/// 與「在綁定或驗證就被擋下」（400）能分開，而綁定壞掉會是 500，三者互不混淆。
/// </para>
/// </remarks>
public sealed class CartCouponApiTests
{
    private const string GuestCartKeyHeader = "X-DoSelect-Guest-Cart-Key";

    /// <remarks>CartIdentityResolver 要求 32..256 字元，短的金鑰會被當成沒有身分。</remarks>
    private const string GuestCartKey = "e2e-guest-cart-key-0123456789abcdef";

    [Fact]
    public async Task AValidGuestRequestReachesTheCouponService()
    {
        // 這條同時是請求 DTO 的綁定證據：ApplyCartCouponRequest 是本專案唯一用
        // [property:] 掛驗證屬性的請求 record，而在這支端點出現之前，它從來沒有被
        // 任何 [FromBody] 綁定過。綁定若壞掉，這裡會是 500 而不是 404。
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, "SAVE10", guestCartKey: GuestCartKey);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AMemberlessKeylessRequestIsRejectedBeforeTheServiceRuns()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, "SAVE10", guestCartKey: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ReadCodeAsync(response));
    }

    [Theory]
    [InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task ACodeOutsideTheContractLengthIsRejected(string code)
    {
        // Code 是 StringLength(64, MinimumLength = 1)。驗證屬性若沒有生效，
        // 這些會一路送進服務並得到 404，而不是 400。
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, code, guestCartKey: GuestCartKey);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemovingWithNoIdentityIsAlsoRejected()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/cart/coupon");
        await AddAntiforgeryAsync(client, request);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string code, string? guestCartKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cart/coupon")
        {
            Content = JsonContent.Create(new { code, cartRowVersion = "AAAAAAAAB9E=" }),
        };
        if (guestCartKey is not null)
        {
            request.Headers.Add(GuestCartKeyHeader, guestCartKey);
        }

        await AddAntiforgeryAsync(client, request);
        return await client.SendAsync(request);
    }

    /// <remarks>
    /// 全域 antiforgery 過濾器會把沒帶 token 的不安全請求擋成 400
    /// <c>antiforgery_validation_failed</c>，那樣就測不到綁定與授權。
    /// Token 的 scheme 依 <c>X-DoSelect-Client</c> 決定，與呼叫者最後被解析成會員或
    /// 訪客購物車無關（同 CartApiFixture 的說明）。
    /// </remarks>
    private static async Task AddAntiforgeryAsync(HttpClient client, HttpRequestMessage request)
    {
        using var tokenRequest = new HttpRequestMessage(
            HttpMethod.Get, "/api/v1/security/antiforgery-token");
        tokenRequest.Headers.Add("X-DoSelect-Client", "member");
        using var tokenResponse = await client.SendAsync(tokenRequest);
        tokenResponse.EnsureSuccessStatusCode();
        var body = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        request.Headers.Add("X-XSRF-TOKEN", body.GetProperty("requestToken").GetString());
        request.Headers.Add("X-DoSelect-Client", "member");
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                // ICartService 的實作需要冪等執行器，而它沒有 pepper 就會在建構時丟例外，
                // 讓整個 controller 變成 500。這些測試從來不會走到真的寫入。
                services.RemoveAll<IIdempotencyExecutor>();
                services.AddSingleton<IIdempotencyExecutor, UnusedIdempotencyExecutor>();
                services.RemoveAll<ICartCouponLineReader>();
                services.AddSingleton<ICartCouponLineReader, MissingCartReader>();
            }));

    /// <summary>回 <c>null</c>，讓真的服務走到「購物車不存在」那條路。</summary>
    private sealed class MissingCartReader : ICartCouponLineReader
    {
        public Task<CartCouponLines?> FindAsync(
            CartIdentity identity, CancellationToken cancellationToken = default) =>
            Task.FromResult<CartCouponLines?>(null);
    }

    /// <summary>這兩支端點不寫任何東西，冪等執行器只是相依鏈上的必要品。</summary>
    private sealed class UnusedIdempotencyExecutor : IIdempotencyExecutor
    {
        public Task<IdempotencyExecutionResult<T>> ExecuteAsync<T>(
            IdempotencyCommand command,
            Func<CancellationToken, Task<IdempotencyResponse<T>>> handler,
            Func<StoredIdempotencyResponse, CancellationToken, Task<T>> replayFactory,
            CancellationToken cancellationToken,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted) =>
            throw new NotSupportedException("These endpoints perform no idempotent writes.");
    }

}
