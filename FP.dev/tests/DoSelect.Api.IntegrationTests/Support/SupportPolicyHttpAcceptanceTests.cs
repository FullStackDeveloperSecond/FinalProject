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
    // A bare SuperAdmin is admitted to Detail via CanSupervise() (GetDetail's imperative
    // "Handle OR Supervise" gate), consistent with Assign/Transfer already granting SuperAdmin
    // through the SupportTicketSupervise policy — SuperAdmin must be able to view a ticket it can
    // also assign or transfer. Only a bare Member (neither Handle nor Supervise) is rejected.
    [InlineData("SuperAdmin", HttpStatusCode.OK)]
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

    // Claim is a pure Handle-only mutation (strict SupportTicketHandle policy, no imperative
    // Handle-OR-Supervise fallback) — a bare SuperAdmin (no CustomerService/Supervisor role) and a
    // bare Member are both rejected.
    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("Member")]
    public async Task Claim_WhenRoleIsDisallowed_Returns403(string role)
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, role);

        using var claim = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/admin/support-tickets/{Guid.NewGuid()}/actions/claim",
            new { rowVersion = Convert.ToBase64String(new byte[8]) },
            DoSelectClaimValues.Admin);

        Assert.Equal(HttpStatusCode.Forbidden, claim.StatusCode);
        Assert.Equal(0, fakes.ClaimCalls);
    }

    // Detail, SLA and Workbench all admit Handle OR Supervise for the read side. A bare
    // SuperAdmin therefore receives supervisor scope without gaining any Handle write action.
    [Theory]
    [InlineData("SuperAdmin", HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData("Member", HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    public async Task ViewEndpoints_EnforceHandleOrSuperviseBehavior(
        string role, HttpStatusCode expectedSla, HttpStatusCode expectedWorkbench)
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, role);

        using var sla = await client.GetAsync("/api/v1/admin/support-tickets/sla?pageSize=17&cursor=opaque");
        using var workbench = await client.GetAsync("/api/v1/admin/case-workbench?pageSize=19");

        Assert.Equal(expectedSla, sla.StatusCode);
        Assert.Equal(expectedWorkbench, workbench.StatusCode);
        var expectedCalls =
            (expectedSla == HttpStatusCode.OK ? 1 : 0) + (expectedWorkbench == HttpStatusCode.OK ? 1 : 0);
        Assert.Equal(expectedCalls, fakes.TotalCalls);
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

    [Theory]
    [InlineData("CustomerService", HttpStatusCode.Forbidden)]
    [InlineData("Member", HttpStatusCode.Forbidden)]
    [InlineData("CustomerServiceSupervisor", HttpStatusCode.OK)]
    [InlineData("SuperAdmin", HttpStatusCode.OK)]
    public async Task Assign_EnforcesSuperviseRoleMatrix(string role, HttpStatusCode expected)
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, role);

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/admin/support-tickets/{Guid.NewGuid()}/actions/assign",
            new { targetAdminPublicId = Guid.NewGuid(), reason = "supervisor assign", rowVersion = Convert.ToBase64String(new byte[8]) },
            DoSelectClaimValues.Admin);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, fakes.AssignCalls);
    }

    [Theory]
    [InlineData("CustomerService", HttpStatusCode.Forbidden)]
    [InlineData("CustomerServiceSupervisor", HttpStatusCode.OK)]
    [InlineData("SuperAdmin", HttpStatusCode.OK)]
    public async Task Transfer_EnforcesSuperviseRoleMatrix(string role, HttpStatusCode expected)
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, role);

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/admin/support-tickets/{Guid.NewGuid()}/actions/transfer",
            new { targetAdminPublicId = Guid.NewGuid(), reason = "transfer", rowVersion = Convert.ToBase64String(new byte[8]) },
            DoSelectClaimValues.Admin);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, fakes.TransferCalls);
    }

    [Fact]
    public async Task Assign_WhenConflict_ReturnsExactCommon409WithoutAssigneeExtensions()
    {
        var fakes = new SupportHttpFakes { ThrowAssignConflict = true };
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, "CustomerServiceSupervisor");

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/admin/support-tickets/{Guid.NewGuid()}/actions/assign",
            new { targetAdminPublicId = Guid.NewGuid(), reason = "supervisor assign", rowVersion = Convert.ToBase64String(new byte[8]) },
            DoSelectClaimValues.Admin);
        using var json = await ReadJsonAsync(response);
        var root = json.RootElement;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(DomainErrorCodes.SupportTicketAssignmentConflict, root.GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("currentAssigneePublicId", out _));
        Assert.False(root.TryGetProperty("currentAssigneeDisplayName", out _));
        Assert.DoesNotContain(
            root.EnumerateObject(),
            property => property.Name.Contains("assignee", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("CustomerService", HttpStatusCode.OK)]
    [InlineData("CustomerServiceSupervisor", HttpStatusCode.OK)]
    [InlineData("SuperAdmin", HttpStatusCode.OK)]
    [InlineData("Member", HttpStatusCode.Forbidden)]
    public async Task ChangePriority_AdmitsHandleOrSuperviseButNotBareMember(string role, HttpStatusCode expected)
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, role);

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/admin/support-tickets/{Guid.NewGuid()}/actions/change-priority",
            new { priority = "High", reason = "escalate", rowVersion = Convert.ToBase64String(new byte[8]) },
            DoSelectClaimValues.Admin);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, fakes.ChangePriorityCalls);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal(role is "SuperAdmin" or "CustomerServiceSupervisor", fakes.LastContext!.CanSupervise);
        }
    }

    [Fact]
    public async Task ChangePriority_WhenPriorityIsOmitted_Returns400WithoutCallingService()
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, "CustomerService");

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/admin/support-tickets/{Guid.NewGuid()}/actions/change-priority",
            new { reason = "missing priority", rowVersion = Convert.ToBase64String(new byte[8]) },
            DoSelectClaimValues.Admin);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, fakes.ChangePriorityCalls);
    }

    [Theory]
    [InlineData("CustomerService", HttpStatusCode.OK)]
    [InlineData("CustomerServiceSupervisor", HttpStatusCode.OK)]
    [InlineData("SuperAdmin", HttpStatusCode.Forbidden)]
    public async Task ChangeStatus_EnforcesHandleRoleMatrix(string role, HttpStatusCode expected)
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, role);

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/admin/support-tickets/{Guid.NewGuid()}/actions/change-status",
            new { status = "InProgress", rowVersion = Convert.ToBase64String(new byte[8]) },
            DoSelectClaimValues.Admin);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, fakes.ChangeStatusCalls);
    }

    [Theory]
    [InlineData("CustomerService", HttpStatusCode.OK)]
    [InlineData("SuperAdmin", HttpStatusCode.Forbidden)]
    public async Task Cancel_EnforcesHandleRoleMatrix(string role, HttpStatusCode expected)
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, role);

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/admin/support-tickets/{Guid.NewGuid()}/actions/cancel",
            new { reason = "customer requested", rowVersion = Convert.ToBase64String(new byte[8]) },
            DoSelectClaimValues.Admin);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, fakes.CancelCalls);
    }

    [Theory]
    [InlineData("CustomerService", HttpStatusCode.OK)]
    [InlineData("SuperAdmin", HttpStatusCode.Forbidden)]
    public async Task Reopen_EnforcesHandleRoleMatrix(string role, HttpStatusCode expected)
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, role);

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/admin/support-tickets/{Guid.NewGuid()}/actions/reopen",
            new { reason = "customer replied again", rowVersion = Convert.ToBase64String(new byte[8]) },
            DoSelectClaimValues.Admin);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, fakes.ReopenCalls);
    }

    [Theory]
    [InlineData("CustomerService", HttpStatusCode.OK)]
    [InlineData("CustomerServiceSupervisor", HttpStatusCode.OK)]
    [InlineData("SuperAdmin", HttpStatusCode.Forbidden)]
    [InlineData("Member", HttpStatusCode.Forbidden)]
    public async Task AddInternalNote_EnforcesHandleRoleMatrix(string role, HttpStatusCode expected)
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, role);

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/admin/support-tickets/{Guid.NewGuid()}/internal-notes",
            new { body = "internal note body", rowVersion = Convert.ToBase64String(new byte[8]) },
            DoSelectClaimValues.Admin);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, fakes.AddInternalNoteCalls);
    }

    [Fact]
    public async Task AddInternalNote_WhenAnonymous_Returns401WithoutCallingService()
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/admin/support-tickets/{Guid.NewGuid()}/internal-notes",
            new { body = "internal note body", rowVersion = Convert.ToBase64String(new byte[8]) },
            DoSelectClaimValues.Admin);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fakes.AddInternalNoteCalls);
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
    public async Task SuperAdmin_CanReadDetailSlaAndWorkbenchWithSupervisorScope_ButStillCannotHandle()
    {
        var fakes = new SupportHttpFakes();
        using var factory = CreateFactory(fakes);
        using var client = CreateClient(factory, "SuperAdmin");
        var ticketId = Guid.NewGuid();

        using var detail = await client.GetAsync($"/api/v1/admin/support-tickets/{ticketId}");
        using var sla = await client.GetAsync("/api/v1/admin/support-tickets/sla");
        using var workbench = await client.GetAsync("/api/v1/admin/case-workbench");
        using var changeStatus = await client.PostAsJsonWithAntiforgeryAsync(
            $"/api/v1/admin/support-tickets/{ticketId}/actions/change-status",
            new { status = "InProgress", rowVersion = Convert.ToBase64String(new byte[8]) },
            DoSelectClaimValues.Admin);

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sla.StatusCode);
        Assert.Equal(HttpStatusCode.OK, workbench.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, changeStatus.StatusCode);
        Assert.False(fakes.LastDetailCanHandle);
        Assert.True(fakes.LastDetailCanSupervise);
        Assert.True(fakes.LastSlaCanSupervise);
        Assert.True(fakes.LastWorkbenchCanSupervise);
        Assert.Equal(0, fakes.ChangeStatusCalls);
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
        public bool LastDetailCanHandle { get; private set; }
        public bool LastDetailCanSupervise { get; private set; }
        public bool LastSlaCanSupervise { get; private set; }
        public bool LastWorkbenchCanSupervise { get; private set; }
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

        public Task<AdminSupportTicketDetailDto> GetDetailAsync(
            string adminUserId,
            bool canHandle,
            bool canSupervise,
            Guid ticketPublicId,
            CancellationToken cancellationToken)
        {
            DetailCalls++;
            LastDetailCanHandle = canHandle;
            LastDetailCanSupervise = canSupervise;
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
            SupportSlaQueueQuery query, string adminUserId, bool canSupervise,
            CancellationToken cancellationToken)
        {
            SlaQuery = query;
            LastSlaCanSupervise = canSupervise;
            return Task.FromResult(new CursorPage<SupportSlaItemDto>([], null, false));
        }

        public Task<CursorPage<CaseWorkbenchItemDto>> GetPageAsync(CaseWorkbenchQuery query,
            IReadOnlyCollection<CaseWorkbenchCaseType> authorizedCaseTypes,
            string adminUserId, bool canSupervise, CancellationToken cancellationToken)
        {
            WorkbenchQuery = query;
            AuthorizedCaseTypes = authorizedCaseTypes.ToArray();
            LastWorkbenchCanSupervise = canSupervise;
            return Task.FromResult(new CursorPage<CaseWorkbenchItemDto>([], null, false));
        }

        public bool ThrowAssignConflict { get; init; }
        public int AssignCalls { get; private set; }
        public int TransferCalls { get; private set; }
        public int ChangePriorityCalls { get; private set; }
        public int ChangeStatusCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public int ReopenCalls { get; private set; }
        public SupportTicketActionContext? LastContext { get; private set; }

        public AdminSupportTicketDto AssignResult { get; } = new(
            Guid.NewGuid(), "ST-ASSIGN", SupportTicketCategory.Other, "Assigned subject",
            SupportTicketStatus.Assigned, CasePriority.Normal, null,
            new AdminAssigneeSummaryDto(Guid.NewGuid(), "Target Agent"),
            DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(8),
            null, null, null, 0, new byte[8]);

        public Task<AdminSupportTicketDto> AssignAsync(
            SupportTicketActionContext context, Guid ticketPublicId, AssignSupportTicketRequest request,
            CancellationToken cancellationToken)
        {
            AssignCalls++;
            LastContext = context;
            if (ThrowAssignConflict)
            {
                throw DomainProblemException.Conflict(
                    DomainErrorCodes.SupportTicketAssignmentConflict, "The ticket is no longer eligible to assign.");
            }
            return Task.FromResult(AssignResult);
        }

        public Task<AdminSupportTicketDto> TransferAsync(
            SupportTicketActionContext context, Guid ticketPublicId, TransferSupportTicketRequest request,
            CancellationToken cancellationToken)
        {
            TransferCalls++;
            LastContext = context;
            return Task.FromResult(AssignResult);
        }

        public Task<AdminSupportTicketDetailDto> ChangePriorityAsync(
            SupportTicketActionContext context, Guid ticketPublicId, ChangeSupportTicketPriorityRequest request,
            CancellationToken cancellationToken)
        {
            ChangePriorityCalls++;
            LastContext = context;
            return Task.FromResult(DetailResult);
        }

        public Task<AdminSupportTicketDetailDto> ChangeStatusAsync(
            SupportTicketActionContext context, Guid ticketPublicId, ChangeSupportTicketStatusRequest request,
            CancellationToken cancellationToken)
        {
            ChangeStatusCalls++;
            LastContext = context;
            return Task.FromResult(DetailResult);
        }

        public Task<AdminSupportTicketDetailDto> CancelAsync(
            SupportTicketActionContext context, Guid ticketPublicId, CancelSupportTicketByAdminRequest request,
            CancellationToken cancellationToken)
        {
            CancelCalls++;
            LastContext = context;
            return Task.FromResult(DetailResult);
        }

        public Task<AdminSupportTicketDetailDto> ReopenAsync(
            SupportTicketActionContext context, Guid ticketPublicId, ReopenSupportTicketRequest request,
            CancellationToken cancellationToken)
        {
            ReopenCalls++;
            LastContext = context;
            return Task.FromResult(DetailResult);
        }

        public int AddInternalNoteCalls { get; private set; }

        public Task<AdminSupportTicketDetailDto> AddInternalNoteAsync(
            SupportTicketActionContext context, Guid ticketPublicId, CreateInternalNoteRequest request,
            CancellationToken cancellationToken)
        {
            AddInternalNoteCalls++;
            LastContext = context;
            return Task.FromResult(DetailResult);
        }
    }
}
