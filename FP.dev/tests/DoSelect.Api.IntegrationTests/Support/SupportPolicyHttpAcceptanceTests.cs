using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Support.Admin;
using DoSelect.Application.Support.Admin.Dtos;
using DoSelect.Domain.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests.Support;

public sealed class SupportPolicyHttpAcceptanceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminId = "acceptance-admin-id";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public SupportPolicyHttpAcceptanceTests(WebApplicationFactory<Program> baseFactory) =>
        _baseFactory = baseFactory;

    public static TheoryData<string, string[], bool> PolicyMatrix => new()
    {
        { DoSelectPolicies.SupportTicketHandle, [DoSelectRoles.CustomerService], true },
        { DoSelectPolicies.SupportTicketHandle, [DoSelectRoles.CustomerServiceSupervisor], true },
        { DoSelectPolicies.SupportTicketHandle, [DoSelectRoles.SuperAdmin], false },
        { DoSelectPolicies.SupportTicketHandle, [DoSelectRoles.SuperAdmin, DoSelectRoles.CustomerService], true },
        { DoSelectPolicies.SupportTicketHandle, [DoSelectRoles.SuperAdmin, DoSelectRoles.CustomerServiceSupervisor], true },
        { DoSelectPolicies.SupportTicketSupervise, [DoSelectRoles.CustomerServiceSupervisor], true },
        { DoSelectPolicies.SupportTicketSupervise, [DoSelectRoles.SuperAdmin], true },
        { DoSelectPolicies.SupportTicketSupervise, [DoSelectRoles.CustomerService], false },
    };

    [Theory]
    [MemberData(nameof(PolicyMatrix))]
    public async Task RegisteredPolicies_EnforceExactRoleMatrix(string policy, string[] roles, bool expected)
    {
        using var scope = _baseFactory.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var claims = roles.Select(role => new Claim(ClaimTypes.Role, role)).ToList();
        claims.Add(new Claim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Admin));
        claims.Add(new Claim(DoSelectClaimTypes.AuthenticationMethod, DoSelectClaimValues.MultiFactor));
        var identity = new ClaimsIdentity(claims, DoSelectAuthenticationSchemes.Admin);

        var result = await authorization.AuthorizeAsync(new ClaimsPrincipal(identity), null, policy);

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task Claim_WhenAnonymous_Returns401()
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/admin/support-tickets/{Guid.NewGuid()}/actions/claim",
            new { rowVersion = Convert.ToBase64String(new byte[8]) },
            DoSelectClaimValues.Admin);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fakes.ClaimCalls);
    }

    [Fact]
    public async Task Detail_WhenAnonymous_Returns401WithoutCallingService()
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/admin/support-tickets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fakes.DetailCalls);
    }

    [Theory]
    [InlineData("SuperAdmin", HttpStatusCode.Forbidden)]
    [InlineData("Member", HttpStatusCode.Forbidden)]
    [InlineData("CustomerService", HttpStatusCode.OK)]
    [InlineData("CustomerServiceSupervisor", HttpStatusCode.OK)]
    [InlineData("SuperAdmin,CustomerService", HttpStatusCode.OK)]
    [InlineData("SuperAdmin,CustomerServiceSupervisor", HttpStatusCode.OK)]
    public async Task Detail_EnforcesHandleRoleMatrix(string rolesCsv, HttpStatusCode expected)
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, rolesCsv.Split(','));

        using var response = await client.GetAsync($"/api/v1/admin/support-tickets/{Guid.NewGuid()}");

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, fakes.DetailCalls);
    }

    [Fact]
    public async Task Detail_WhenAuthorized_ReturnsInternalFlagsWithoutSensitiveNamesOrSentinelValues()
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, "CustomerService");

        using var response = await client.GetAsync($"/api/v1/admin/support-tickets/{fakes.DetailResult.PublicId}");
        var jsonText = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(jsonText);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([false, true], json.RootElement.GetProperty("messages").EnumerateArray().Select(x => x.GetProperty("isInternal").GetBoolean()));
        foreach (var forbidden in new[] { "email", "memberUserId", "assigneeAdminUserId", "senderUserId", "storageKey", "identity-member-sentinel", "identity-admin-sentinel" })
        {
            Assert.DoesNotContain(forbidden, jsonText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Detail_WhenServiceReportsMissing_ReturnsStandard404ProblemDetails()
    {
        var fakes = new SupportHttpFakes { ThrowDetailNotFound = true };
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, "CustomerServiceSupervisor");

        using var response = await client.GetAsync($"/api/v1/admin/support-tickets/{Guid.NewGuid()}");
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(DomainErrorCodes.ResourceNotFound, json.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("Member")]
    public async Task HandleEndpoints_WhenRoleIsDisallowed_Return403(string role)
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, role);

        using var claim = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/admin/support-tickets/{Guid.NewGuid()}/actions/claim",
            new { rowVersion = Convert.ToBase64String(new byte[8]) },
            DoSelectClaimValues.Admin);
        using var sla = await client.GetAsync("/api/v1/admin/support-tickets/sla?pageSize=17&cursor=opaque");
        using var workbench = await client.GetAsync("/api/v1/admin/case-workbench?pageSize=19");

        Assert.Equal(HttpStatusCode.Forbidden, claim.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sla.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, workbench.StatusCode);
        Assert.Equal(0, fakes.TotalCalls);
    }

    [Fact]
    public async Task Claim_WhenHandleAuthorized_UsesNameIdentifierAndReturnsPublicDto()
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, "CustomerService");
        var ticketId = Guid.NewGuid();

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/admin/support-tickets/{ticketId}/actions/claim",
            new { rowVersion = Convert.ToBase64String(new byte[8]) },
            DoSelectClaimValues.Admin);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AdminId, fakes.ClaimAdminId);
        Assert.Equal(ticketId, fakes.ClaimTicketId);
        Assert.Equal(fakes.ClaimResult.PublicId.ToString(), json.RootElement.GetProperty("publicId").GetString());
        Assert.False(json.RootElement.TryGetProperty("adminUserId", out _));
        Assert.False(json.RootElement.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task Claim_WhenAssignmentLost_ReturnsExactCommon409WithoutAssigneeExtensions()
    {
        const string correlationId = "acceptance-correlation-409";
        var fakes = new SupportHttpFakes { ThrowAssignmentConflict = true };
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, "CustomerServiceSupervisor");
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, correlationId);

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/admin/support-tickets/{Guid.NewGuid()}/actions/claim",
            new { rowVersion = Convert.ToBase64String(new byte[8]) },
            DoSelectClaimValues.Admin);
        using var json = await ReadJsonAsync(response);
        var root = json.RootElement;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(DomainErrorCodes.SupportTicketAssignmentConflict, root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.Equal(correlationId, root.GetProperty("correlationId").GetString());
        Assert.False(root.TryGetProperty("currentAssigneePublicId", out _));
        Assert.False(root.TryGetProperty("currentAssigneeDisplayName", out _));
        Assert.DoesNotContain(
            root.EnumerateObject(),
            property => property.Name.Contains("assignee", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Sla_WhenHandleAuthorized_DelegatesBoundPaginationQuery()
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, "CustomerService");

        using var response = await client.GetAsync("/api/v1/admin/support-tickets/sla?pageSize=17&cursor=opaque-cursor");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(17, fakes.SlaQuery?.PageSize);
        Assert.Equal("opaque-cursor", fakes.SlaQuery?.Cursor);
    }

    [Fact]
    public async Task Workbench_WhenHandleAuthorized_DelegatesQueryWithSupportOnlyScope()
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, "CustomerServiceSupervisor");
        var assignee = Guid.NewGuid();

        using var response = await client.GetAsync(
            $"/api/v1/admin/case-workbench?pageSize=19&caseTypes=Return&caseTypes=Support&statuses=open&priorities=High&assigneePublicId={assignee}&overdueOnly=true&keyword=late&cursor=next");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(19, fakes.WorkbenchQuery?.PageSize);
        Assert.Equal([CaseWorkbenchCaseType.Return, CaseWorkbenchCaseType.Support], fakes.WorkbenchQuery?.CaseTypes);
        Assert.Equal([CaseWorkbenchCaseType.Support], fakes.AuthorizedCaseTypes);
        Assert.Equal(assignee, fakes.WorkbenchQuery?.AssigneePublicId);
        Assert.True(fakes.WorkbenchQuery?.OverdueOnly);
        Assert.Equal("late", fakes.WorkbenchQuery?.Keyword);
        Assert.Equal("next", fakes.WorkbenchQuery?.Cursor);
    }

    private WebApplicationFactory<Program> CreateFactory(SupportHttpFakes fakes) =>
        _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            TestAuthHandler.Configure(services);
            services.RemoveAll<IAdminSupportTicketService>();
            services.RemoveAll<ISupportSlaQueueService>();
            services.RemoveAll<ICaseWorkbenchService>();
            services.AddSingleton<IAdminSupportTicketService>(fakes);
            services.AddSingleton<ISupportSlaQueueService>(fakes);
            services.AddSingleton<ICaseWorkbenchService>(fakes);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, params string[] roles)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.MemberHeaderName, AdminId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeaderName, string.Join(',', roles));
        return client;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private sealed class SupportHttpFakes : IAdminSupportTicketService, ISupportSlaQueueService, ICaseWorkbenchService
    {
        public int ClaimCalls { get; private set; }
        public int DetailCalls { get; private set; }
        public int TotalCalls => ClaimCalls + DetailCalls + (SlaQuery is null ? 0 : 1) + (WorkbenchQuery is null ? 0 : 1);
        public string? ClaimAdminId { get; private set; }
        public Guid ClaimTicketId { get; private set; }
        public SupportSlaQueueQuery? SlaQuery { get; private set; }
        public CaseWorkbenchQuery? WorkbenchQuery { get; private set; }
        public IReadOnlyCollection<CaseWorkbenchCaseType>? AuthorizedCaseTypes { get; private set; }
        public bool ThrowAssignmentConflict { get; init; }
        public bool ThrowDetailNotFound { get; init; }
        public AdminSupportTicketDto ClaimResult { get; } = new(
            Guid.NewGuid(), "ST-ACCEPTANCE", SupportTicketCategory.Other, "Public subject",
            SupportTicketStatus.Open, CasePriority.Normal, null,
            new AdminAssigneeSummaryDto(Guid.NewGuid(), "Support Agent"),
            DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(8),
            null, null, null, 0, new byte[8]);
        public AdminSupportTicketDetailDto DetailResult { get; } = new(
            Guid.NewGuid(), "ST-DETAIL", SupportTicketCategory.Other, "Safe subject", SupportTicketStatus.Open,
            CasePriority.High, null, null, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow,
            DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(8), false, null, null, null, 0, ["claim"], new byte[8],
            [
                new(Guid.NewGuid(), SupportSenderType.Member, false, false, "public body", "zh-TW", DateTime.UtcNow.AddMinutes(-2)),
                new(Guid.NewGuid(), SupportSenderType.Admin, false, true, "internal body", "zh-TW", DateTime.UtcNow.AddMinutes(-1)),
            ]);

        public Task<AdminSupportTicketDetailDto> GetDetailAsync(Guid ticketPublicId, CancellationToken cancellationToken)
        {
            DetailCalls++;
            if (ThrowDetailNotFound)
            {
                throw DomainProblemException.NotFound("The support ticket was not found.");
            }
            return Task.FromResult(DetailResult);
        }

        public Task<AdminSupportTicketDto> ClaimAsync(string adminUserId, Guid ticketPublicId,
            ClaimSupportTicketRequest request, CancellationToken cancellationToken)
        {
            ClaimCalls++;
            ClaimAdminId = adminUserId;
            ClaimTicketId = ticketPublicId;
            if (ThrowAssignmentConflict)
            {
                throw DomainProblemException.Conflict(
                    DomainErrorCodes.SupportTicketAssignmentConflict, "The ticket was already claimed.");
            }
            return Task.FromResult(ClaimResult);
        }

        public Task<CursorPage<SupportSlaItemDto>> GetPageAsync(
            SupportSlaQueueQuery query, CancellationToken cancellationToken)
        {
            SlaQuery = query;
            return Task.FromResult(new CursorPage<SupportSlaItemDto>([], null, false));
        }

        public Task<CursorPage<CaseWorkbenchItemDto>> GetPageAsync(CaseWorkbenchQuery query,
            IReadOnlyCollection<CaseWorkbenchCaseType> authorizedCaseTypes, CancellationToken cancellationToken)
        {
            WorkbenchQuery = query;
            AuthorizedCaseTypes = authorizedCaseTypes.ToArray();
            return Task.FromResult(new CursorPage<CaseWorkbenchItemDto>([], null, false));
        }
    }
}
