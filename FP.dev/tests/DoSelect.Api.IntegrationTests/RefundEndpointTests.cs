using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using System.Net.Http.Json;
using DoSelect.Api.Refunds;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Refunds;
using DoSelect.Domain.Refunds;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests;

public sealed class RefundEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid RefundPublicId = new("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private const string ExecuteRoute =
        "/api/v1/admin/refunds/cccccccc-cccc-cccc-cccc-cccccccccccc/actions/execute";
    private const string ApproveRoute =
        "/api/v1/admin/refunds/cccccccc-cccc-cccc-cccc-cccccccccccc/actions/approve";

    private readonly WebApplicationFactory<Program> _factory;

    public RefundEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task AFinanceManagerCanQueryTheRefundList()
    {
        var reader = new FakeRefundReader();
        using var factory = CreateFactory(new FakeRefundExecutor(Settled(500m)), reader);
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await client.GetAsync(
            "/api/v1/admin/refunds?statuses=approved&q=RF-202608&pageNumber=2&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(reader.LastQuery);
        Assert.Equal(2, reader.LastQuery.PageNumber);
        Assert.Equal(10, reader.LastQuery.PageSize);
        Assert.Equal("RF-202608", reader.LastQuery.Q);
        Assert.Equal([RefundStatus.Approved], reader.LastQuery.Statuses);
    }

    [Fact]
    public async Task AFinanceManagerCanReadTheRefundDetail()
    {
        using var factory = CreateFactory(new FakeRefundExecutor(Settled(500m)));
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await client.GetAsync(
            $"/api/v1/admin/refunds/{RefundPublicId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RefundDto>(ResponseJsonOptions);
        Assert.Equal(RefundPublicId, body!.PublicId);
    }

    [Fact]
    public async Task AnonymousRefundQueriesAreChallenged()
    {
        using var factory = CreateFactory(new FakeRefundExecutor(Settled(500m)));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/admin/refunds");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousCallerIsChallenged()
    {
        using var factory = CreateFactory(new FakeRefundExecutor(Settled(500m)));
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, signedIn: false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnAdminWithoutTheFinanceRoleIsForbidden()
    {
        using var factory = CreateFactory(new FakeRefundExecutor(Settled(500m)));
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.CatalogManager);

        using var response = await PostAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AFinanceManagerWithoutMultiFactorIsForbidden()
    {
        // 工程包要求退款執行需 TOTP 二次確認；該條件由 Policy 的 MFA 宣告保證。
        using var factory = CreateFactory(new FakeRefundExecutor(Settled(500m)));
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: false, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AMissingIdempotencyKeyIsRejectedBeforeAnyExecution()
    {
        var executor = new FakeRefundExecutor(Settled(500m));
        using var factory = CreateFactory(executor);
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client, idempotencyKey: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(executor.LastRequest);
    }

    [Fact]
    public async Task AnOversizedIdempotencyKeyReturns400BeforeAnyExecution()
    {
        var executor = new FakeRefundExecutor(Settled(500m));
        using var factory = CreateFactory(executor);
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client, idempotencyKey: new string('k', 129));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(executor.LastRequest);

        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation_failed", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAuthorisedExecutionReturnsTheSettledAmount()
    {
        var executor = new FakeRefundExecutor(Settled(500m));
        using var factory = CreateFactory(executor);
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RefundDto>(ResponseJsonOptions);
        Assert.Equal(RefundPublicId, body!.PublicId);
        Assert.Equal(500m, body.SucceededAmount);
        Assert.Equal(RefundPublicId, executor.LastRequest!.RefundPublicId);
        Assert.Equal("refund-1", executor.LastRequest.IdempotencyKey);
    }

    [Fact]
    public async Task TheRowVersionFromTheBodyReachesTheExecutor()
    {
        // 這個欄位曾經在 Body 上宣告卻從未傳進 Application 契約，
        // 於是拿舊畫面版本執行的請求照樣成功。
        var executor = new FakeRefundExecutor(Settled(500m));
        using var factory = CreateFactory(executor);
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            executor.LastRequest!.RefundRowVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(16)]
    public async Task ARowVersionOfTheWrongLengthReturns400NotAServerError(int length)
    {
        // Application 層的同一條檢查是丟 ArgumentException，而那個例外沒有專屬
        // handler，會落到 GlobalExceptionHandler 變成 500 unexpected_error ——
        // 呼叫端只是送錯了長度，不該看到「伺服器錯誤」。
        var executor = new FakeRefundExecutor(Settled(500m));
        using var factory = CreateFactory(executor);
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client, rowVersion: new byte[length]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(executor.LastRequest);

        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation_failed", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALegalNoteReachesTheExecutorUnmodified()
    {
        // note 走中央 Audit 的獨立欄位；端點不得改寫或截斷它。
        //
        // 請求 IP 的覆蓋在 RefundExecutorSqlServerTests：TestServer 不提供真實連線 IP，
        // 在這一層斷言它只會測到主機環境，測不出程式行為。
        var executor = new FakeRefundExecutor(Settled(500m));
        using var factory = CreateFactory(executor);
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(
            client, note: "Customer confirmed the damaged item by phone");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Customer confirmed the damaged item by phone",
            executor.LastRequest!.Note);
    }

    [Theory]
    [InlineData("contact me@example.com")]
    [InlineData("see <b>here</b>")]
    public async Task AnUnsafeNoteReturns400NotAServerError(string note)
    {
        // 中央 Audit 會拒收這些輸入。不在邊界擋，稽核建構時丟的 ArgumentException
        // 會落到 GlobalExceptionHandler 變成 500，但呼叫端只是送了格式不合的理由。
        var executor = new FakeRefundExecutor(Settled(500m));
        using var factory = CreateFactory(executor);
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client, note: note);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation_failed", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnsafeReasonCodeReturns400NotAServerError()
    {
        // reason 只接受 safe-code（ASCII 英數與 ._-:）。
        var executor = new FakeRefundExecutor(Settled(500m));
        using var factory = CreateFactory(executor);
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client, reasonCode: "客戶要求");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnInProgressIdempotentRequestReturns409WithRetryAfter()
    {
        // 錯誤碼目錄第 36 行要求呼叫端「依 Retry-After: 3 等待後重試」。
        // Executor 若把 IdempotencyConflictException 攔下來只留 ErrorCode，
        // RetryAfterSeconds 就到不了 API，呼叫端不知道該等多久。
        using var factory = CreateFactory(
            new ThrowingRefundExecutor(new IdempotencyConflictException(
                IdempotencyErrorCodes.RequestInProgress, retryAfterSeconds: 3)));
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("3", response.Headers.RetryAfter?.ToString());

        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains(
            IdempotencyErrorCodes.RequestInProgress, problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCorrelationIdAndTraceIdComeFromDifferentSources()
    {
        // 兩者曾經都取自 HttpContext.TraceIdentifier。CorrelationIdMiddleware 會把
        // 合法的 X-Correlation-ID 寫進 TraceIdentifier，而那不是中央 Audit 要求的
        // 32 位 W3C TraceId —— 混用會讓一次正常退款變成 500。
        var executor = new FakeRefundExecutor(Settled(500m));
        using var factory = CreateFactory(executor);
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client, correlationId: "refund-request-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("refund-request-1", executor.LastRequest!.CorrelationId);
        Assert.Equal(32, executor.LastRequest.TraceId.Length);
        Assert.All(
            executor.LastRequest.TraceId,
            character => Assert.True(Uri.IsHexDigit(character)));
    }

    [Fact]
    public async Task AReplayReturnsTheSameRefundWithoutASecondEffect()
    {
        // 重播由共用 IIdempotencyExecutor 判定並回放；端點回同一份 RefundDto，
        // 呼叫端不需要分辨，也不該從回應看出差別。
        using var factory = CreateFactory(
            new FakeRefundExecutor(ExecuteRefundResult.Replayed(500m)));
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RefundDto>(ResponseJsonOptions);
        Assert.Equal(500m, body!.SucceededAmount);
    }

    [Fact]
    public async Task TheResponseNeverExposesAnInternalIdentityId()
    {
        // requestedBy／approvedBy／executedBy 只回 PublicId 與遮蔽標籤（DEC-P290）。
        using var factory = CreateFactory(new FakeRefundExecutor(Settled(500m)));
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("finance-1", payload, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RefundErrorCodes.ResourceNotFound, HttpStatusCode.NotFound)]
    [InlineData(RefundErrorCodes.RefundStateConflict, HttpStatusCode.Conflict)]
    [InlineData(RefundErrorCodes.RefundAmountExceeded, HttpStatusCode.Conflict)]
    [InlineData(RefundErrorCodes.IdempotencyPayloadConflict, HttpStatusCode.Conflict)]
    [InlineData(RefundErrorCodes.RefundSnapshotUnavailable, HttpStatusCode.Conflict)]
    public async Task DomainErrorCodesMapToTheDocumentedStatuses(
        string errorCode,
        HttpStatusCode expected)
    {
        using var factory = CreateFactory(
            new FakeRefundExecutor(ExecuteRefundResult.Failure(errorCode)));
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostAsync(client);

        Assert.Equal(expected, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains(errorCode, problem, StringComparison.Ordinal);
    }

    // ── 退款核准（alex 2026-09-04 #98 WP2 裁定） ──────────────────────────────

    [Fact]
    public async Task AFinanceManagerCanApproveAPendingRefund()
    {
        var approver = new FakeRefundApprover(ApprovedSettled(500m));
        using var factory = CreateFactory(new FakeRefundExecutor(Settled(500m)), approver: approver);
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostApproveAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RefundDto>(ResponseJsonOptions);
        Assert.Equal(RefundPublicId, body!.PublicId);
        Assert.Equal(RefundPublicId, approver.LastRequest!.RefundPublicId);
        Assert.Equal("refund-approve-1", approver.LastRequest.IdempotencyKey);
    }

    [Fact]
    public async Task ApprovalSharesTheSameAuthorizationBoundaryAsExecution()
    {
        // 核准與執行同一個 Policy（Refund.Execute）：同一組角色、同一個 MFA 要求。
        using var factory = CreateFactory(
            new FakeRefundExecutor(Settled(500m)),
            approver: new FakeRefundApprover(ApprovedSettled(500m)));
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: false, DoSelectRoles.FinanceManager);

        using var response = await PostApproveAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousApprovalIsChallenged()
    {
        using var factory = CreateFactory(
            new FakeRefundExecutor(Settled(500m)),
            approver: new FakeRefundApprover(ApprovedSettled(500m)));
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, ApproveRoute)
        {
            Content = JsonContent.Create(new
            {
                reasonCode = "customer_request",
                note = (string?)null,
                refundRowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            }),
        };
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AMissingIdempotencyKeyIsRejectedBeforeAnyApproval()
    {
        var approver = new FakeRefundApprover(ApprovedSettled(500m));
        using var factory = CreateFactory(new FakeRefundExecutor(Settled(500m)), approver: approver);
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostApproveAsync(client, idempotencyKey: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(approver.LastRequest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(16)]
    public async Task ARowVersionOfTheWrongLengthOnApprovalReturns400NotAServerError(int length)
    {
        var approver = new FakeRefundApprover(ApprovedSettled(500m));
        using var factory = CreateFactory(new FakeRefundExecutor(Settled(500m)), approver: approver);
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostApproveAsync(client, rowVersion: new byte[length]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(approver.LastRequest);
    }

    [Theory]
    [InlineData("contact me@example.com")]
    [InlineData("see <b>here</b>")]
    public async Task AnUnsafeNoteOnApprovalReturns400NotAServerError(string note)
    {
        var approver = new FakeRefundApprover(ApprovedSettled(500m));
        using var factory = CreateFactory(new FakeRefundExecutor(Settled(500m)), approver: approver);
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostApproveAsync(client, note: note);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AReplayedApprovalReturnsTheSameRefundWithoutASecondEffect()
    {
        using var factory = CreateFactory(
            new FakeRefundExecutor(Settled(500m)),
            approver: new FakeRefundApprover(ApproveRefundResult.Replayed(500m)));
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostApproveAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RefundDto>(ResponseJsonOptions);
        Assert.Equal(RefundPublicId, body!.PublicId);
    }

    [Theory]
    [InlineData(RefundErrorCodes.ResourceNotFound, HttpStatusCode.NotFound)]
    [InlineData(RefundErrorCodes.RefundStateConflict, HttpStatusCode.Conflict)]
    [InlineData(RefundErrorCodes.RefundAmountExceeded, HttpStatusCode.Conflict)]
    [InlineData(RefundErrorCodes.RefundSnapshotUnavailable, HttpStatusCode.Conflict)]
    [InlineData(RefundErrorCodes.RefundCalculationMismatch, HttpStatusCode.Conflict)]
    public async Task ApprovalErrorCodesMapToTheDocumentedStatuses(
        string errorCode,
        HttpStatusCode expected)
    {
        using var factory = CreateFactory(
            new FakeRefundExecutor(Settled(500m)),
            approver: new FakeRefundApprover(ApproveRefundResult.Failure(errorCode)));
        using var client = factory.CreateClient();
        await SignInAsync(client, includeMfa: true, DoSelectRoles.FinanceManager);

        using var response = await PostApproveAsync(client);

        Assert.Equal(expected, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains(errorCode, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void TheApprovalRequestCarriesNoAllocationsOrAmounts()
    {
        // 契約層級的保證，與 Application 層的同名測試互相獨立驗證（alex #98 WP2）。
        var properties = typeof(ApproveRefundRequestBody).GetProperties();

        Assert.DoesNotContain(properties, property =>
            property.PropertyType == typeof(decimal) ||
            property.PropertyType == typeof(decimal?));
    }

    /// <summary>
    /// 用與 API 相同的 JSON 慣例讀回應。
    /// </summary>
    /// <remarks>
    /// API 設定的是 <c>JsonStringEnumConverter(camelCase, allowIntegerValues: false)</c>，
    /// 因此 <c>status</c> 是 <c>"succeeded"</c> 而不是數字。用預設選項反序列化會失敗 ——
    /// 那是測試的問題，不是回應的問題。
    /// </remarks>
    private static readonly JsonSerializerOptions ResponseJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

    private static ExecuteRefundResult Settled(decimal amount) =>
        ExecuteRefundResult.Settled(
            amount,
            new RefundExecutionPlan(
                11L,
                amount,
                "finance-1",
                "refund-1",
                [1, 2, 3, 4, 5, 6, 7, 8],
                []));

    private static ApproveRefundResult ApprovedSettled(decimal amount) =>
        ApproveRefundResult.Settled(
            amount,
            new RefundApprovalPlan(
                11L,
                amount,
                "finance-1",
                "refund-approve-1",
                [1, 2, 3, 4, 5, 6, 7, 8]));

    private async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string? idempotencyKey = "refund-1",
        bool signedIn = true,
        string? correlationId = null,
        byte[]? rowVersion = null,
        string? note = null,
        string reasonCode = "customer_request")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ExecuteRoute);
        if (idempotencyKey is not null)
        {
            request.Headers.Add(RefundsController.IdempotencyKeyHeaderName, idempotencyKey);
        }

        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        if (signedIn)
        {
            request.Headers.Add("X-XSRF-TOKEN", await GetAntiforgeryTokenAsync(client, "admin"));
        }

        // Body 只帶理由與 RowVersion，沒有 allocations 也沒有金額（DEC-P287）。
        request.Content = JsonContent.Create(new
        {
            reasonCode,
            note,
            refundRowVersion = rowVersion ?? new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
        });

        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostApproveAsync(
        HttpClient client,
        string? idempotencyKey = "refund-approve-1",
        byte[]? rowVersion = null,
        string? note = null,
        string reasonCode = "customer_request")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ApproveRoute);
        if (idempotencyKey is not null)
        {
            request.Headers.Add(RefundsController.IdempotencyKeyHeaderName, idempotencyKey);
        }

        request.Headers.Add("X-XSRF-TOKEN", await GetAntiforgeryTokenAsync(client, "admin"));

        // Body 只帶理由與 RowVersion，沒有 allocations 也沒有金額——與 execute 同一個
        // 契約（alex 2026-09-04 #98 WP2 裁定）。
        request.Content = JsonContent.Create(new
        {
            reasonCode,
            note,
            refundRowVersion = rowVersion ?? new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
        });

        return await client.SendAsync(request);
    }

    private WebApplicationFactory<Program> CreateFactory(
        IRefundExecutor executor,
        IRefundReader? reader = null,
        IRefundApprover? approver = null) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services.AddSingleton(executor);
                services.AddSingleton<IRefundReader>(reader ?? new FakeRefundReader());
                // RefundsController 建構式一次注入全部三個依賴，MVC 會在每個 action
                // 都建構整支 Controller——execute 相關測試即使不打 approve 路由，
                // 真正的 RefundApprover 仍會被解析，而它依賴的 IIdempotencyExecutor
                // 若沒有設定 Idempotency:ActorScopePepper 會在解析當下丟例外，
                // 讓每個 Refund 端點都變成 500（與 IReturnRefundCreationPort 那次
                // 同一個 DI 陷阱）。一律覆寫成假的，不論這條測試會不會真的核准。
                services.AddSingleton(approver ?? new FakeRefundApprover(ApprovedSettled(500m)));

                services
                    .AddControllers()
                    .AddApplicationPart(typeof(SecurityFoundationTestController).Assembly);
            });
        });

    private static async Task SignInAsync(HttpClient client, bool includeMfa, params string[] roles)
    {
        var token = await GetAntiforgeryTokenAsync(client, "admin");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/sign-in/admin")
        {
            Content = JsonContent.Create(new { includeMfa, roles }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string clientType)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/security/antiforgery-token");
        request.Headers.Add(SecurityController.ClientHeaderName, clientType);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await System.Text.Json.JsonDocument.ParseAsync(stream);
        return document.RootElement.GetProperty("requestToken").GetString()!;
    }

    /// <summary>
    /// 回一份形狀正確的 <see cref="RefundDto"/>。
    /// </summary>
    /// <remarks>
    /// 這一層測的是授權、路由繫結與回應形狀；欄位怎麼從資料庫投影出來
    /// （含 Identity Id 換 PublicId 與遮蔽）由 <c>RefundReader</c> 的測試負責。
    /// 管理員摘要刻意帶值，讓「回應不得含內部 Id」那條測試有東西可以檢查。
    /// </remarks>
    private sealed class FakeRefundReader : IRefundReader
    {
        public AdminRefundQuery? LastQuery { get; private set; }

        public Task<PageResult<RefundDto>> ListAsync(
            AdminRefundQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(new PageResult<RefundDto>(
                [CreateRefundDto(RefundPublicId)],
                query.PageNumber,
                query.PageSize,
                1));
        }

        public Task<RefundDto?> FindByPublicIdAsync(
            Guid refundPublicId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RefundDto?>(CreateRefundDto(refundPublicId));

        private static RefundDto CreateRefundDto(Guid refundPublicId) => new(
                refundPublicId,
                "RF-202608-000001",
                new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                ReturnPublicId: null,
                RefundStatus.Succeeded,
                RequestedAmount: 500m,
                ApprovedAmount: 500m,
                SucceededAmount: 500m,
                Allocations: [],
                RequestedBy: null,
                ApprovedBy: null,
                ExecutedBy: new MaskedAdminSummaryDto(
                    new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"), "f*******"),
                CreatedAtUtc: new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc),
                SucceededAtUtc: new DateTime(2026, 8, 27, 1, 0, 0, DateTimeKind.Utc),
                RowVersion: [1, 2, 3, 4, 5, 6, 7, 8]);
    }

    /// <summary>
    /// 讓例外原樣傳出的 fake，用來驗證共用 handler 的行為（狀態碼與 Retry-After）。
    /// </summary>
    private sealed class ThrowingRefundExecutor(Exception exception) : IRefundExecutor
    {
        public Task<ExecuteRefundResult> ExecuteAsync(
            ExecuteRefundRequest request,
            CancellationToken cancellationToken = default) =>
            throw exception;
    }

    private sealed class FakeRefundExecutor(ExecuteRefundResult result) : IRefundExecutor
    {
        public ExecuteRefundRequest? LastRequest { get; private set; }

        public Task<ExecuteRefundResult> ExecuteAsync(
            ExecuteRefundRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingRefundApprover(Exception exception) : IRefundApprover
    {
        public Task<ApproveRefundResult> ApproveAsync(
            ApproveRefundRequest request,
            CancellationToken cancellationToken = default) =>
            throw exception;
    }

    private sealed class FakeRefundApprover(ApproveRefundResult result) : IRefundApprover
    {
        public ApproveRefundRequest? LastRequest { get; private set; }

        public Task<ApproveRefundResult> ApproveAsync(
            ApproveRefundRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }
}
