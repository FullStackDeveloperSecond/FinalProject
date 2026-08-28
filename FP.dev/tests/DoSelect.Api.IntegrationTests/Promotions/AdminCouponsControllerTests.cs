using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.Json;
using DoSelect.Api.IntegrationTests.Support;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Promotions;
using DoSelect.Domain.Promotions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests.Promotions;

/// <summary>
/// `/api/v1/admin/coupons*` 的授權與錯誤映射。
/// </summary>
/// <remarks>
/// 這一層負責的是授權、路由繫結與 <see cref="DomainProblemException"/> 到 Problem Details
/// 的映射；狀態機、RowVersion 與唯一索引由 <c>AdminCouponServiceSqlServerTests</c>
/// 對真實 SQL Server 驗證。
/// </remarks>
public sealed class AdminCouponsControllerTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminId = "coupon-admin-id";
    private const string BasePath = "/api/v1/admin/coupons";

    private readonly WebApplicationFactory<Program> _baseFactory;

    public AdminCouponsControllerTests(WebApplicationFactory<Program> baseFactory) =>
        _baseFactory = baseFactory;

    [Fact]
    public async Task AnAnonymousRequestIsRejectedBeforeReachingTheService()
    {
        var fake = new FakeAdminCouponService();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(BasePath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fake.Calls);
    }

    [Theory]
    [InlineData(DoSelectRoles.OrderManager, HttpStatusCode.Forbidden)]
    [InlineData(DoSelectRoles.CatalogManager, HttpStatusCode.Forbidden)]
    [InlineData(DoSelectRoles.CustomerService, HttpStatusCode.Forbidden)]
    [InlineData(DoSelectRoles.FinanceManager, HttpStatusCode.OK)]
    [InlineData(DoSelectRoles.MarketingAnalyst, HttpStatusCode.OK)]
    [InlineData(DoSelectRoles.SuperAdmin, HttpStatusCode.OK)]
    public async Task TheCouponManageRoleMatrixIsEnforced(string role, HttpStatusCode expected)
    {
        // MarketingAnalyst 是 Coupon.Manage 與 Invoice.Manage 的唯一差別（DEC-P284）。
        var fake = new FakeAdminCouponService();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, role);

        using var response = await client.GetAsync(BasePath);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, fake.Calls);
    }

    [Theory]
    [InlineData(DoSelectRoles.FinanceManager)]
    [InlineData(DoSelectRoles.MarketingAnalyst)]
    [InlineData(DoSelectRoles.SuperAdmin)]
    public async Task AnAllowedRoleWithoutMfaIsStillRejected(string role)
    {
        var fake = new FakeAdminCouponService();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, role);
        client.DefaultRequestHeaders.Add(TestAuthHandler.WithoutMfaHeaderName, "true");

        using var response = await client.GetAsync(BasePath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task EveryWriteRouteAlsoRefusesAnAnonymousCaller()
    {
        // 授權在 Controller 層級，但每一條路由都必須實際被覆蓋 ——
        // 少掛一個 [Authorize] 不會被列表的測試抓到。
        var fake = new FakeAdminCouponService();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        using var get = await client.GetAsync($"{BasePath}/{id}");
        using var create = await PostJsonAsync(client, BasePath, CreateRequest());
        using var update = await PutJsonAsync(client, $"{BasePath}/{id}", UpdateRequest());
        using var action = await PostJsonAsync(client,
            $"{BasePath}/{id}/actions/pause", ActionRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, update.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, action.StatusCode);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task AWriteWithoutAnAntiforgeryTokenIsRejectedBeforeTheService()
    {
        // 已通過授權的管理員仍必須帶 Token；否則一個外部網站就能代替他送出停用請求。
        var fake = new FakeAdminCouponService();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.SuperAdmin);

        using var response = await client.PostAsJsonAsync(
            $"{BasePath}/{Guid.NewGuid()}/actions/disable", ActionRequest());

        await AssertProblemAsync(
            response, HttpStatusCode.BadRequest, "antiforgery_validation_failed");
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task TheListQueryStringBindsIntoTheApplicationQuery()
    {
        var fake = new FakeAdminCouponService();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.MarketingAnalyst);

        using var response = await client.GetAsync(
            $"{BasePath}?q=WELCOME&statuses=Active&statuses=Paused&sort=codeAsc&pageNumber=2&pageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("WELCOME", fake.LastQuery!.Q);
        Assert.Equal([CouponStatus.Active, CouponStatus.Paused], fake.LastQuery.Statuses);
        Assert.Equal(AdminCouponSortOptions.CodeAsc, fake.LastQuery.Sort);
        Assert.Equal(2, fake.LastQuery.PageNumber);
        Assert.Equal(5, fake.LastQuery.PageSize);
    }

    [Fact]
    public async Task TheListDefaultsToTheFirstPageWhenNothingIsSupplied()
    {
        var fake = new FakeAdminCouponService();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.FinanceManager);

        using var response = await client.GetAsync(BasePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fake.LastQuery!.PageNumber);
        Assert.Equal(20, fake.LastQuery.PageSize);
        Assert.Null(fake.LastQuery.Statuses);
    }

    [Fact]
    public async Task AnOutOfRangePageSizeSurfacesAsValidationFailed()
    {
        // API共通規範第 88 行：回 400，不自動修正。
        var fake = new FakeAdminCouponService
        {
            OnList = _ => throw DomainProblemException.Validation("pageSize must be between 1 and 100."),
        };
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.FinanceManager);

        using var response = await client.GetAsync($"{BasePath}?pageSize=500");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task AnUnknownCouponReturnsResourceNotFound()
    {
        var fake = new FakeAdminCouponService { Coupon = null };
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.FinanceManager);

        using var response = await client.GetAsync($"{BasePath}/{Guid.NewGuid()}");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "resource_not_found");
    }

    [Fact]
    public async Task CreatingReturnsCreatedWithALocationHeader()
    {
        var fake = new FakeAdminCouponService();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.MarketingAnalyst);

        using var response = await PostJsonAsync(client, BasePath, CreateRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Contains(fake.Coupon!.PublicId.ToString(), response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task ADuplicateCodeSurfacesAsCouponCodeDuplicate()
    {
        var fake = new FakeAdminCouponService
        {
            OnCreate = _ => throw DomainProblemException.Conflict(
                CouponCalculationErrorCodes.CouponCodeDuplicate, "duplicate"),
        };
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.FinanceManager);

        using var response = await PostJsonAsync(client, BasePath, CreateRequest());

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "coupon_code_duplicate");
    }

    [Fact]
    public async Task AnIllegalTransitionSurfacesAsCouponStateConflict()
    {
        var fake = new FakeAdminCouponService
        {
            OnAction = (_, _) => throw DomainProblemException.Conflict(
                CouponCalculationErrorCodes.CouponStateConflict, "state"),
        };
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.SuperAdmin);

        using var response = await PostJsonAsync(client,
            $"{BasePath}/{Guid.NewGuid()}/actions/activate", ActionRequest());

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "coupon_state_conflict");
    }

    [Fact]
    public async Task AStaleRowVersionSurfacesAsConcurrencyConflict()
    {
        var fake = new FakeAdminCouponService
        {
            OnUpdate = (_, _) => throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict, "stale"),
        };
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.FinanceManager);

        using var response = await PutJsonAsync(client,
            $"{BasePath}/{Guid.NewGuid()}", UpdateRequest());

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "concurrency_conflict");
    }

    [Theory]
    [InlineData("activate")]
    [InlineData("pause")]
    [InlineData("disable")]
    public async Task TheActionSegmentReachesTheServiceVerbatim(string action)
    {
        var fake = new FakeAdminCouponService();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.SuperAdmin);

        using var response = await PostJsonAsync(client,
            $"{BasePath}/{Guid.NewGuid()}/actions/{action}", ActionRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(action, fake.LastAction);
    }

    [Fact]
    public async Task AnActionOutsideTheWhitelistIsRoutedAndRejectedByTheService()
    {
        // 白名單判斷屬 Use Case，不是路由限制；未知動作必須回 404 而不是靜默成功。
        var fake = new FakeAdminCouponService
        {
            OnAction = (_, _) => throw DomainProblemException.NotFound("unsupported"),
        };
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.SuperAdmin);

        using var response = await PostJsonAsync(client,
            $"{BasePath}/{Guid.NewGuid()}/actions/expire", ActionRequest());

        Assert.Equal("expire", fake.LastAction);
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "resource_not_found");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(16)]
    public async Task AnActionRowVersionOfTheWrongLengthIsRejectedBeforeTheService(int length)
    {
        // SQL Server 的 rowversion 一律 8 bytes。長度不對是**請求格式**錯誤，
        // 應回 400；先前只有 [Required]，空陣列會一路走到 RequireCurrentRowVersion
        // 被當成過期 token 回 409 concurrency_conflict —— 那個錯誤碼會讓管理員
        // 以為有人同時改了資料，實際上是他自己送錯格式。
        var fake = new FakeAdminCouponService();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.SuperAdmin);

        using var response = await PostJsonAsync(client,
            $"{BasePath}/{Guid.NewGuid()}/actions/pause",
            new
            {
                reasonCode = "admin_request",
                note = (string?)null,
                rowVersion = Convert.ToBase64String(new byte[length]),
            });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(0, fake.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(16)]
    public async Task AnUpdateRowVersionOfTheWrongLengthIsRejectedBeforeTheService(int length)
    {
        var fake = new FakeAdminCouponService();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.SuperAdmin);

        var request = UpdateRequest();
        using var response = await PutJsonAsync(client,
            $"{BasePath}/{Guid.NewGuid()}",
            new
            {
                code = request.Code,
                nameZhTw = request.NameZhTw,
                discountType = "fixedAmount",
                discountValue = request.DiscountValue,
                minimumSpend = request.MinimumSpend,
                maximumDiscount = request.MaximumDiscount,
                startsAtUtc = request.StartsAtUtc,
                endsAtUtc = request.EndsAtUtc,
                totalUsageLimit = request.TotalUsageLimit,
                perMemberLimit = request.PerMemberLimit,
                memberOnly = request.MemberOnly,
                excludeSaleItems = request.ExcludeSaleItems,
                scopeType = "all",
                categoryPublicIds = (Guid[]?)null,
                productPublicIds = (Guid[]?)null,
                excludedProductPublicIds = (Guid[]?)null,
                rowVersion = Convert.ToBase64String(new byte[length]),
            });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(0, fake.Calls);
    }

    [Theory]
    [InlineData("contact me@example.com")]
    [InlineData("see <b>here</b>")]
    public async Task AnUnsafeActionNoteIsRejectedBeforeTheService(string note)
    {
        // note 一路傳給中央 Audit，那裡會拒絕 Email 與標記字元並丟 ArgumentException。
        // 該例外沒有專屬 handler，會落到 GlobalExceptionHandler 變成 500 —— 但呼叫端
        // 只是送了不合規的文字，應該得到 400，而且不得留下任何寫入。
        var fake = new FakeAdminCouponService();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.SuperAdmin);

        using var response = await PostJsonAsync(client,
            $"{BasePath}/{Guid.NewGuid()}/actions/pause",
            new { reasonCode = "admin_request", note, rowVersion = Convert.ToBase64String(RowVersion) });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task AnUnsafeActionReasonCodeIsRejectedBeforeTheService()
    {
        // reason 只收 safe-code。長度合法但含空白與中文，寫稽核時才會失敗。
        var fake = new FakeAdminCouponService();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.SuperAdmin);

        using var response = await PostJsonAsync(client,
            $"{BasePath}/{Guid.NewGuid()}/actions/pause",
            new
            {
                reasonCode = "管理員 要求 暫停",
                note = (string?)null,
                rowVersion = Convert.ToBase64String(RowVersion),
            });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task ARequestBodyMissingItsReasonCodeIsRejectedBeforeTheService()
    {
        // reasonCode 是契約必填（API DTO與Schema契約第 126 行）。
        var fake = new FakeAdminCouponService();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.SuperAdmin);

        using var response = await PostJsonAsync(client,
            $"{BasePath}/{Guid.NewGuid()}/actions/pause",
            new { note = (string?)null, rowVersion = Convert.ToBase64String(RowVersion) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task TheResponseSerializesEnumsAsCamelCaseStrings()
    {
        var fake = new FakeAdminCouponService();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.FinanceManager);

        using var response = await client.GetAsync($"{BasePath}/{Guid.NewGuid()}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal("fixedAmount", json.RootElement.GetProperty("discountType").GetString());
        Assert.Equal("draft", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "all",
            json.RootElement.GetProperty("scope").GetProperty("scopeType").GetString());
    }

    [Fact]
    public async Task TheResponseTimestampsCarryTheUtcSuffix()
    {
        // SQL Server 的 datetime2 讀回來是 Unspecified；沒有標記成 UTC 的話
        // 序列化出來不帶 Z，客戶端只能猜，而且原樣送回 PUT 會被擋成 400。
        var fake = new FakeAdminCouponService();
        using var factory = CreateFactory(fake);
        using var client = CreateAdminClient(factory, DoSelectRoles.FinanceManager);

        using var response = await client.GetAsync($"{BasePath}/{Guid.NewGuid()}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.EndsWith("Z", json.RootElement.GetProperty("startsAtUtc").GetString()!);
        Assert.EndsWith("Z", json.RootElement.GetProperty("endsAtUtc").GetString()!);
    }

    /// <summary>
    /// 用與 API 相同的 JSON 慣例送出請求。
    /// </summary>
    /// <remarks>
    /// <c>PostAsJsonAsync</c> 的預設會把列舉序列化成**數字**，而 API 設定的是
    /// <c>JsonStringEnumConverter(camelCase, allowIntegerValues: false)</c> ——
    /// 數字會被拒絕成 400。這裡刻意沿用同一組轉換器，讓測試送的是真實客戶端會送的內容。
    /// </remarks>
    private static readonly JsonSerializerOptions ClientJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static Task<HttpResponseMessage> PostJsonAsync<T>(
        HttpClient client,
        string requestUri,
        T value) =>
        SendJsonAsync(client, HttpMethod.Post, requestUri, value);

    private static Task<HttpResponseMessage> PutJsonAsync<T>(
        HttpClient client,
        string requestUri,
        T value) =>
        SendJsonAsync(client, HttpMethod.Put, requestUri, value);

    /// <summary>
    /// 送出一個帶有效 Antiforgery Token 的寫入請求。
    /// </summary>
    /// <remarks>
    /// <c>GlobalAntiforgeryFilter</c> 對每一個非安全方法都要求 Token，缺少時回 400
    /// <c>antiforgery_validation_failed</c>。測試若不帶 Token，每一條寫入路由都會停在
    /// 那個 400，永遠測不到後面的授權與錯誤映射。
    /// </remarks>
    private static async Task<HttpResponseMessage> SendJsonAsync<T>(
        HttpClient client,
        HttpMethod method,
        string requestUri,
        T value)
    {
        using var tokenRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/security/antiforgery-token");
        tokenRequest.Headers.Add(SecurityController.ClientHeaderName, DoSelectClaimValues.Admin);
        using var tokenResponse = await client.SendAsync(tokenRequest);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());

        var request = new HttpRequestMessage(method, requestUri)
        {
            Content = JsonContent.Create(value, options: ClientJsonOptions),
        };
        request.Headers.Add(
            "X-XSRF-TOKEN",
            tokenJson.RootElement.GetProperty("requestToken").GetString());
        if (tokenResponse.Headers.TryGetValues("Set-Cookie", out var values))
        {
            var cookie = values.Select(value => value.Split(';', 2)[0])
                .Single(value => value.StartsWith(".DoSelect.Antiforgery=", StringComparison.Ordinal));
            request.Headers.Add("Cookie", cookie);
        }

        return await client.SendAsync(request);
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedCode, json.RootElement.GetProperty("code").GetString());
    }

    private WebApplicationFactory<Program> CreateFactory(FakeAdminCouponService fake) =>
        _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            TestAuthHandler.Configure(services);
            services.RemoveAll<IAdminCouponService>();
            services.AddSingleton<IAdminCouponService>(fake);
        }));

    private static HttpClient CreateAdminClient(
        WebApplicationFactory<Program> factory,
        string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.MemberHeaderName, AdminId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeaderName, role);
        return client;
    }

    private static readonly byte[] RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];

    private static CreateCouponRequest CreateRequest() =>
        new(
            "WELCOME300",
            "新會員",
            CouponDiscountType.FixedAmount,
            300m,
            3000m,
            null,
            new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
            100,
            1,
            false,
            false,
            CouponScopeType.All,
            null,
            null,
            null);

    private static UpdateCouponRequest UpdateRequest() =>
        new(
            "WELCOME300",
            "新會員",
            CouponDiscountType.FixedAmount,
            300m,
            3000m,
            null,
            new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
            100,
            1,
            false,
            false,
            CouponScopeType.All,
            null,
            null,
            null,
            RowVersion);

    private static CouponActionRequest ActionRequest() =>
        new("admin_request", null, RowVersion);

    private sealed class FakeAdminCouponService : IAdminCouponService
    {
        public int Calls { get; private set; }

        public AdminCouponQuery? LastQuery { get; private set; }

        public string? LastAction { get; private set; }

        public Func<AdminCouponQuery, PageResult<CouponDto>>? OnList { get; init; }

        public Func<CreateCouponRequest, CouponDto>? OnCreate { get; init; }

        public Func<Guid, UpdateCouponRequest, CouponDto>? OnUpdate { get; init; }

        public Func<Guid, CouponActionRequest, CouponDto>? OnAction { get; init; }

        public CouponDto? Coupon { get; init; } = Sample;

        public Task<PageResult<CouponDto>> ListAsync(
            AdminCouponQuery query,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastQuery = query;
            return Task.FromResult(
                OnList?.Invoke(query) ??
                new PageResult<CouponDto>([Sample], query.PageNumber, query.PageSize, 1));
        }

        public Task<CouponDto?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Coupon);
        }

        public Task<CouponDto> CreateAsync(
            CreateCouponRequest request,
            AdminCouponActorContext actor,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(OnCreate?.Invoke(request) ?? Coupon!);
        }

        public Task<CouponDto> UpdateAsync(
            Guid publicId,
            UpdateCouponRequest request,
            AdminCouponActorContext actor,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(OnUpdate?.Invoke(publicId, request) ?? Coupon!);
        }

        public Task<CouponDto> ExecuteActionAsync(
            Guid publicId,
            string action,
            CouponActionRequest request,
            AdminCouponActorContext actor,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastAction = action;
            return Task.FromResult(OnAction?.Invoke(publicId, request) ?? Coupon!);
        }

        private static CouponDto Sample { get; } = new(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "WELCOME300",
            "新會員",
            CouponDiscountType.FixedAmount,
            CouponStatus.Draft,
            300m,
            3000m,
            null,
            new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
            false,
            false,
            new CouponScopeDto(CouponScopeType.All, [], [], []),
            new CouponUsageDto(0, 100, 1, 100),
            1,
            new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc),
            RowVersion);
    }
}
