using DoSelect.Application.Common;
using DoSelect.Application.Support;
using DoSelect.Application.Support.Admin;
using DoSelect.Application.Support.Admin.Dtos;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Tests.Support.Admin;

public sealed class M14BReadModelAcceptanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SlaQueue_UsesSuppliedUtcInstantAndStableLastRowForNextPage()
    {
        var firstId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var lastId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var due = Now.UtcDateTime.AddMinutes(30);
        var store = new RecordingSlaStore
        {
            Result = new SupportSlaQueuePage(
                [SlaItem(firstId, true, due.AddMinutes(-1)), SlaItem(lastId, false, due)],
                HasMore: true),
        };
        var service = new SupportSlaQueueService(store, new FixedTimeProvider(Now));

        var first = await service.GetPageAsync(new SupportSlaQueueQuery(2, null), CancellationToken.None);
        var second = await service.GetPageAsync(new SupportSlaQueueQuery(2, first.NextCursor), CancellationToken.None);

        Assert.Equal(Now.UtcDateTime, store.NowUtc);
        Assert.NotNull(first.NextCursor);
        Assert.NotNull(store.After);
        Assert.Equal(lastId, store.After.TicketPublicId);
        Assert.Equal(due, store.After.EffectiveDueAtUtc);
        Assert.False(store.After.IsOverdue);
        Assert.True(second.HasMore);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task SlaQueue_RejectsOutOfRangePageSize(int pageSize)
    {
        var service = new SupportSlaQueueService(new RecordingSlaStore(), new FixedTimeProvider(Now));

        var error = await Assert.ThrowsAsync<DomainProblemException>(() =>
            service.GetPageAsync(new SupportSlaQueueQuery(pageSize, null), CancellationToken.None));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(DomainErrorCodes.ValidationFailed, error.Code);
    }

    [Theory]
    [InlineData("not-base64url")]
    [InlineData("eyJ2Ijo5OTl9")]
    public async Task SlaQueue_RejectsMalformedOrForeignCursor(string cursor)
    {
        var service = new SupportSlaQueueService(new RecordingSlaStore(), new FixedTimeProvider(Now));

        var error = await Assert.ThrowsAsync<DomainProblemException>(() =>
            service.GetPageAsync(new SupportSlaQueueQuery(10, cursor), CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ValidationFailed, error.Code);
    }

    [Fact]
    public void SlaContracts_ExposeOnlyPublicSafeAssigneeIdentity()
    {
        Assert.Equal(
            ["PublicId", "DisplayName"],
            typeof(AdminAssigneeSummaryDto).GetProperties().Select(property => property.Name));
        Assert.Equal(
            ["TicketPublicId", "TicketNumber", "Priority", "Assignee", "Status",
             "FirstResponseDueAtUtc", "ResolutionDueAtUtc", "EffectiveDueAtUtc", "UsageRatio",
             "IsOverdue", "LastActivityAtUtc", "RowVersion"],
            typeof(SupportSlaItemDto).GetProperties().Select(property => property.Name));

        var publicNames = typeof(SupportSlaItemDto).GetProperties()
            .Concat(typeof(AdminAssigneeSummaryDto).GetProperties())
            .Select(property => property.Name)
            .ToArray();
        Assert.DoesNotContain("Email", publicNames);
        Assert.DoesNotContain("UserId", publicNames);
        Assert.DoesNotContain("Id", publicNames);
    }

    [Fact]
    public void WorkbenchContract_IsExactlyTheApprovedTwelveFields()
    {
        Assert.Equal(
            ["CaseType", "CasePublicId", "CaseNumber", "Title", "Status", "Priority",
             "RequesterDisplay", "AssigneePublicId", "CreatedAtUtc", "LastActivityAtUtc",
             "SlaDueAtUtc", "IsOverdue"],
            typeof(CaseWorkbenchItemDto).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public async Task Workbench_EmptyScopeReturnsEmptyWithoutCallingStore()
    {
        var store = new RecordingWorkbenchStore();
        var service = new CaseWorkbenchService(store);

        var result = await service.GetPageAsync(WorkbenchQuery(), [], CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Null(result.NextCursor);
        Assert.False(result.HasMore);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Workbench_MultiRoleScopeIsCanonicalDistinctUnionAndRequestedTypesOnlyNarrowIt()
    {
        var store = new RecordingWorkbenchStore();
        var service = new CaseWorkbenchService(store);
        var query = WorkbenchQuery(caseTypes: [CaseWorkbenchCaseType.Support, CaseWorkbenchCaseType.Report]);

        await service.GetPageAsync(
            query,
            [CaseWorkbenchCaseType.Report, CaseWorkbenchCaseType.Support, CaseWorkbenchCaseType.Report, CaseWorkbenchCaseType.Return],
            CancellationToken.None);

        Assert.Equal([CaseWorkbenchCaseType.Support, CaseWorkbenchCaseType.Report], store.CaseTypes);
    }

    [Fact]
    public async Task Workbench_UnauthorizedRequestedTypeReturnsEmptyWithoutCallingStore()
    {
        var store = new RecordingWorkbenchStore();
        var service = new CaseWorkbenchService(store);

        var result = await service.GetPageAsync(
            WorkbenchQuery(caseTypes: [CaseWorkbenchCaseType.Report]),
            [CaseWorkbenchCaseType.Support],
            CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Workbench_ForwardsAllFiltersAndStableLastRowCursor()
    {
        var assignee = Guid.NewGuid();
        var lastId = Guid.Parse("ffffffff-ffff-ffff-ffff-fffffffffff0");
        var lastActivity = Now.UtcDateTime.AddHours(-2);
        var store = new RecordingWorkbenchStore
        {
            Result = new CaseWorkbenchPage(
                [WorkbenchItem(Guid.NewGuid(), Now.UtcDateTime), WorkbenchItem(lastId, lastActivity)],
                HasMore: true),
        };
        var service = new CaseWorkbenchService(store);
        var query = WorkbenchQuery(
            statuses: ["Open"], priorities: [CasePriority.High], assignee: assignee,
            overdueOnly: true, keyword: " needle ", pageSize: 2);

        var first = await service.GetPageAsync(query, [CaseWorkbenchCaseType.Support], CancellationToken.None);
        await service.GetPageAsync(query with { Cursor = first.NextCursor }, [CaseWorkbenchCaseType.Support], CancellationToken.None);

        Assert.Equal(["Open"], store.Statuses);
        Assert.Equal([CasePriority.High], store.Priorities);
        Assert.Equal(assignee, store.AssigneePublicId);
        Assert.True(store.OverdueOnly);
        Assert.Equal(" needle ", store.Keyword);
        Assert.Equal(2, store.PageSize);
        Assert.Equal(lastActivity, store.After!.LastActivityAtUtc);
        Assert.Equal(lastId, store.After.CasePublicId);
    }

    [Fact]
    public async Task Workbench_CursorAcceptsCanonicalEquivalentOrdering()
    {
        var store = new RecordingWorkbenchStore
        {
            Result = new CaseWorkbenchPage([WorkbenchItem(Guid.NewGuid(), Now.UtcDateTime)], HasMore: true),
        };
        var service = new CaseWorkbenchService(store);
        var firstQuery = WorkbenchQuery(
            caseTypes: [CaseWorkbenchCaseType.Report, CaseWorkbenchCaseType.Support],
            statuses: ["Assigned", "Open"],
            priorities: [CasePriority.High, CasePriority.Normal]);
        var page = await service.GetPageAsync(
            firstQuery,
            [CaseWorkbenchCaseType.Return, CaseWorkbenchCaseType.Support, CaseWorkbenchCaseType.Report],
            CancellationToken.None);

        var equivalent = firstQuery with
        {
            CaseTypes = [CaseWorkbenchCaseType.Support, CaseWorkbenchCaseType.Report],
            Statuses = ["Open", "Assigned"],
            Priorities = [CasePriority.Normal, CasePriority.High],
            Cursor = page.NextCursor,
        };
        await service.GetPageAsync(
            equivalent,
            [CaseWorkbenchCaseType.Report, CaseWorkbenchCaseType.Return, CaseWorkbenchCaseType.Support],
            CancellationToken.None);

        Assert.Equal(2, store.CallCount);
    }

    [Fact]
    public async Task Workbench_RejectsMalformedFilterMismatchedAndScopeMismatchedCursors()
    {
        var store = new RecordingWorkbenchStore
        {
            Result = new CaseWorkbenchPage([WorkbenchItem(Guid.NewGuid(), Now.UtcDateTime)], HasMore: true),
        };
        var service = new CaseWorkbenchService(store);
        var original = WorkbenchQuery(statuses: ["Open"]);
        var page = await service.GetPageAsync(original, [CaseWorkbenchCaseType.Support], CancellationToken.None);

        await AssertValidationAsync(service, original with { Cursor = "not-a-cursor" }, [CaseWorkbenchCaseType.Support]);
        await AssertValidationAsync(service, original with { Statuses = ["Assigned"], Cursor = page.NextCursor }, [CaseWorkbenchCaseType.Support]);
        await AssertValidationAsync(service, original with { Cursor = page.NextCursor }, [CaseWorkbenchCaseType.Support, CaseWorkbenchCaseType.Report]);
    }

    [Fact]
    public async Task QueueCursors_AreBoundToAdminIdentityAndSupervisionScope()
    {
        var slaStore = new RecordingSlaStore
        {
            Result = new SupportSlaQueuePage(
                [SlaItem(Guid.NewGuid(), overdue: false, Now.UtcDateTime.AddHours(1))],
                HasMore: true),
        };
        var slaService = new SupportSlaQueueService(slaStore, new FixedTimeProvider(Now));
        var slaPage = await slaService.GetPageAsync(
            new SupportSlaQueueQuery(1, null), "agent-a", canSupervise: false, CancellationToken.None);
        var slaError = await Assert.ThrowsAsync<DomainProblemException>(() =>
            slaService.GetPageAsync(
                new SupportSlaQueueQuery(1, slaPage.NextCursor),
                "agent-b",
                canSupervise: false,
                CancellationToken.None));

        var workbenchStore = new RecordingWorkbenchStore
        {
            Result = new CaseWorkbenchPage(
                [WorkbenchItem(Guid.NewGuid(), Now.UtcDateTime)],
                HasMore: true),
        };
        var workbenchService = new CaseWorkbenchService(workbenchStore);
        var workbenchPage = await workbenchService.GetPageAsync(
            WorkbenchQuery(pageSize: 1),
            [CaseWorkbenchCaseType.Support],
            "agent-a",
            canSupervise: false,
            CancellationToken.None);
        var workbenchError = await Assert.ThrowsAsync<DomainProblemException>(() =>
            workbenchService.GetPageAsync(
                WorkbenchQuery(pageSize: 1) with { Cursor = workbenchPage.NextCursor },
                [CaseWorkbenchCaseType.Support],
                "agent-a",
                canSupervise: true,
                CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ValidationFailed, slaError.Code);
        Assert.Equal(DomainErrorCodes.ValidationFailed, workbenchError.Code);
    }
    private static async Task AssertValidationAsync(
        CaseWorkbenchService service,
        CaseWorkbenchQuery query,
        IReadOnlyCollection<CaseWorkbenchCaseType> scope)
    {
        var error = await Assert.ThrowsAsync<DomainProblemException>(() =>
            service.GetPageAsync(query, scope, CancellationToken.None));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(DomainErrorCodes.ValidationFailed, error.Code);
    }

    private static SupportSlaItemDto SlaItem(Guid id, bool overdue, DateTime due) => new(
        id, $"SLA-{id:N}", CasePriority.Normal, null, SupportTicketStatus.Open,
        due.AddHours(-1), due.AddDays(1), due, 0.5, overdue, due.AddHours(-2), new byte[8]);

    private static CaseWorkbenchItemDto WorkbenchItem(Guid id, DateTime lastActivity) => new(
        "Support", id, $"CASE-{id:N}", "Subject", "Open", CasePriority.Normal,
        "Member", null, lastActivity.AddDays(-1), lastActivity, lastActivity.AddHours(1), false);

    private static CaseWorkbenchQuery WorkbenchQuery(
        IReadOnlyCollection<CaseWorkbenchCaseType>? caseTypes = null,
        IReadOnlyCollection<string>? statuses = null,
        IReadOnlyCollection<CasePriority>? priorities = null,
        Guid? assignee = null,
        bool? overdueOnly = null,
        string? keyword = null,
        int pageSize = 20) =>
        new(caseTypes, statuses, priorities, assignee, overdueOnly, keyword, null, pageSize);

    private sealed class RecordingSlaStore : ISupportSlaQueueStore
    {
        public SupportSlaQueuePage Result { get; init; } = new([], false);
        public SupportSlaCursorPosition? After { get; private set; }
        public DateTime NowUtc { get; private set; }

        public Task<SupportSlaQueuePage> QueryPageAsync(
            int pageSize, SupportSlaCursorPosition? after, DateTime nowUtc,
            string adminUserId, bool canSupervise, CancellationToken cancellationToken)
        {
            After = after;
            NowUtc = nowUtc;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingWorkbenchStore : ICaseWorkbenchStore
    {
        public CaseWorkbenchPage Result { get; init; } = new([], false);
        public int CallCount { get; private set; }
        public IReadOnlyCollection<CaseWorkbenchCaseType>? CaseTypes { get; private set; }
        public IReadOnlyCollection<string>? Statuses { get; private set; }
        public IReadOnlyCollection<CasePriority>? Priorities { get; private set; }
        public Guid? AssigneePublicId { get; private set; }
        public bool? OverdueOnly { get; private set; }
        public string? Keyword { get; private set; }
        public int PageSize { get; private set; }
        public CaseWorkbenchCursorPosition? After { get; private set; }

        public Task<CaseWorkbenchPage> QueryPageAsync(
            IReadOnlyCollection<CaseWorkbenchCaseType> caseTypes,
            IReadOnlyCollection<string>? statuses,
            IReadOnlyCollection<CasePriority>? priorities,
            Guid? assigneePublicId,
            bool? overdueOnly,
            string? keyword,
            int pageSize,
            CaseWorkbenchCursorPosition? after,
            string adminUserId,
            bool canSupervise,
            CancellationToken cancellationToken)
        {
            CallCount++;
            CaseTypes = caseTypes;
            Statuses = statuses;
            Priorities = priorities;
            AssigneePublicId = assigneePublicId;
            OverdueOnly = overdueOnly;
            Keyword = keyword;
            PageSize = pageSize;
            After = after;
            return Task.FromResult(Result);
        }
    }
}

internal static class M14BServiceTestExtensions
{
    private const string UnitTestAdminUserId = "unit-test-admin";

    public static Task<CursorPage<SupportSlaItemDto>> GetPageAsync(
        this SupportSlaQueueService service,
        SupportSlaQueueQuery query,
        CancellationToken cancellationToken) =>
        service.GetPageAsync(query, UnitTestAdminUserId, canSupervise: false, cancellationToken);

    public static Task<CursorPage<CaseWorkbenchItemDto>> GetPageAsync(
        this CaseWorkbenchService service,
        CaseWorkbenchQuery query,
        IReadOnlyCollection<CaseWorkbenchCaseType> authorizedCaseTypes,
        CancellationToken cancellationToken) =>
        service.GetPageAsync(
            query, authorizedCaseTypes, UnitTestAdminUserId, canSupervise: false, cancellationToken);
}
