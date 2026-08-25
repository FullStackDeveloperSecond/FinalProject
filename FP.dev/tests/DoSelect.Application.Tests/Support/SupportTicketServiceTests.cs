using DoSelect.Application.Common;
using DoSelect.Application.Support;
using DoSelect.Application.Support.Dtos;
using DoSelect.Domain.Support;

namespace DoSelect.Application.Tests.Support;

public sealed class SupportTicketServiceTests
{
    private static readonly DateTimeOffset NowOffset = new(2026, 8, 19, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTime NowUtc = NowOffset.UtcDateTime;

    private static (SupportTicketService Service, FakeSupportTicketStore Store, FakeOrderOwnershipLookup Orders) CreateSut()
    {
        var store = new FakeSupportTicketStore();
        var orders = new FakeOrderOwnershipLookup();
        var service = new SupportTicketService(store, orders, new FixedTimeProvider(NowOffset));
        return (service, store, orders);
    }

    [Fact]
    public async Task CreateAsync_ComputesNormalPriorityAndSlaDueDatesFromCreation()
    {
        var (service, _, _) = CreateSut();
        var request = new CreateSupportTicketRequest { Category = SupportTicketCategory.Order, Subject = "訂單延遲", Message = "我的包裹還沒到" };

        var dto = await service.CreateAsync("member-a", request, CancellationToken.None);

        Assert.Equal(CasePriority.Normal, dto.Priority);
        Assert.Equal(NowUtc.AddHours(8), dto.FirstResponseDueAtUtc);
        Assert.Equal(NowUtc.AddDays(3), dto.ResolutionDueAtUtc);
        Assert.Equal(SupportTicketStatus.Open, dto.Status);
        Assert.StartsWith("CS-20260819-", dto.TicketNumber);
        Assert.Single(dto.Messages);
        Assert.Equal("我的包裹還沒到", dto.Messages[0].Body);
        Assert.Contains("cancel", dto.AvailableActions);
        Assert.Contains("addMessage", dto.AvailableActions);
    }

    [Fact]
    public async Task CreateAsync_GeneratesDistinctTicketNumbersForConsecutiveTickets()
    {
        var (service, store, _) = CreateSut();
        var request = new CreateSupportTicketRequest { Category = SupportTicketCategory.Other, Subject = "測試", Message = "測試訊息內容" };

        var first = await service.CreateAsync("member-a", request, CancellationToken.None);
        var second = await service.CreateAsync("member-a", request, CancellationToken.None);

        Assert.NotEqual(first.TicketNumber, second.TicketNumber);
        Assert.Equal(2, store.Tickets.Count);
    }

    [Fact]
    public async Task CreateAsync_WhenTicketNumberCollidesOnce_RetriesAndSucceeds()
    {
        var (service, store, _) = CreateSut();
        store.SimulateTicketNumberCollisionsRemaining = 1;
        var request = new CreateSupportTicketRequest { Category = SupportTicketCategory.Other, Subject = "測試", Message = "測試訊息內容" };

        var dto = await service.CreateAsync("member-a", request, CancellationToken.None);

        Assert.Single(store.Tickets);
        Assert.StartsWith("CS-20260819-", dto.TicketNumber);
    }

    [Fact]
    public async Task CreateAsync_WhenTicketNumberKeepsColliding_ThrowsConflictAfterExhaustingRetries()
    {
        var (service, store, _) = CreateSut();
        store.SimulateTicketNumberCollisionsRemaining = int.MaxValue;
        var request = new CreateSupportTicketRequest { Category = SupportTicketCategory.Other, Subject = "測試", Message = "測試訊息內容" };

        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => service.CreateAsync("member-a", request, CancellationToken.None));

        Assert.Equal(DomainErrorCodes.SupportTicketNumberGenerationFailed, exception.Code);
        Assert.Equal(409, exception.StatusCode);
        Assert.Empty(store.Tickets);
    }

    [Fact]
    public async Task CreateAsync_WhenOrderBelongsToAnotherMember_ThrowsNotFound()
    {
        var (service, _, orders) = CreateSut();
        var orderPublicId = Guid.NewGuid();
        orders.Orders[orderPublicId] = new OrderOwnershipInfo(1, orderPublicId, "someone-else");
        var request = new CreateSupportTicketRequest
        {
            Category = SupportTicketCategory.Order,
            Subject = "訂單問題",
            Message = "這不是我的訂單",
            OrderPublicId = orderPublicId,
        };

        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => service.CreateAsync("member-a", request, CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ResourceNotFound, exception.Code);
        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task GetDetailAsync_WhenTicketBelongsToAnotherMember_ThrowsNotFound_NotForbidden()
    {
        var (service, _, _) = CreateSut();
        var created = await service.CreateAsync(
            "member-a",
            new CreateSupportTicketRequest { Category = SupportTicketCategory.Account, Subject = "帳號問題", Message = "無法登入我的帳號" },
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => service.GetDetailAsync("member-b", created.PublicId, CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ResourceNotFound, exception.Code);
    }

    [Fact]
    public async Task ListMyTicketsAsync_OnlyReturnsCallersOwnTickets()
    {
        var (service, _, _) = CreateSut();
        await service.CreateAsync("member-a", new CreateSupportTicketRequest { Category = SupportTicketCategory.Other, Subject = "A的案件", Message = "member A 的問題內容" }, CancellationToken.None);
        await service.CreateAsync("member-b", new CreateSupportTicketRequest { Category = SupportTicketCategory.Other, Subject = "B的案件", Message = "member B 的問題內容" }, CancellationToken.None);

        var page = await service.ListMyTicketsAsync("member-a", new SupportTicketQuery(), CancellationToken.None);

        var summary = Assert.Single(page.Items);
        Assert.Equal("A的案件", summary.Subject);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task AddMessageAsync_WhenTicketIsClosed_ThrowsStateConflict()
    {
        var (service, store, _) = CreateSut();
        var created = await service.CreateAsync("member-a", new CreateSupportTicketRequest { Category = SupportTicketCategory.Other, Subject = "案件", Message = "初始訊息內容" }, CancellationToken.None);
        var ticket = store.Tickets.Single(t => t.PublicId == created.PublicId);
        ticket.Assign("agent", NowUtc.AddMinutes(1));
        ticket.Transition(SupportTicketStatus.InProgress, NowUtc.AddMinutes(2));
        ticket.Transition(SupportTicketStatus.Resolved, NowUtc.AddMinutes(3));
        ticket.Transition(SupportTicketStatus.Closed, NowUtc.AddMinutes(4));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.AddMessageAsync(
            "member-a",
            created.PublicId,
            new CreateSupportMessageRequest { Body = "還在嗎？", RowVersion = ticket.RowVersion },
            CancellationToken.None));

        Assert.Equal(DomainErrorCodes.SupportTicketStateConflict, exception.Code);
    }

    [Fact]
    public async Task AddMessageAsync_WhenWaitingForCustomer_ResumesStatusPauseAndSlaInOneSave()
    {
        var store = new FakeSupportTicketStore();
        var orders = new FakeOrderOwnershipLookup();
        var createService = new SupportTicketService(
            store,
            orders,
            new FixedTimeProvider(NowOffset.AddHours(-2)));
        var created = await createService.CreateAsync(
            "member-a",
            new CreateSupportTicketRequest
            {
                Category = SupportTicketCategory.Other,
                Subject = "案件",
                Message = "初始訊息內容",
            },
            CancellationToken.None);
        var ticket = store.Tickets.Single(t => t.PublicId == created.PublicId);
        ticket.Assign("agent", NowUtc.AddMinutes(-110));
        ticket.Transition(SupportTicketStatus.InProgress, NowUtc.AddMinutes(-100));
        ticket.AddPausedSeconds(71 * 60 * 60, NowUtc.AddMinutes(-91));
        ticket.Transition(SupportTicketStatus.WaitingForCustomer, NowUtc.AddMinutes(-90));
        var service = new SupportTicketService(store, orders, new FixedTimeProvider(NowOffset));

        var result = await service.AddMessageAsync(
            "member-a",
            created.PublicId,
            new CreateSupportMessageRequest { Body = "已補充資料", RowVersion = ticket.RowVersion },
            CancellationToken.None);

        Assert.Equal(SupportTicketStatus.InProgress, result.Status);
        Assert.Equal(72 * 60 * 60, ticket.PausedSeconds);
        Assert.Null(ticket.WaitingForCustomerStartedAtUtc);
        var history = Assert.Single(store.StatusHistories, h => h.FromStatus == SupportTicketStatus.WaitingForCustomer);
        Assert.Equal(SupportTicketStatus.InProgress, history.ToStatus);
        Assert.Equal("customer-replied", history.ReasonCode);
        var resumed = Assert.Single(store.SlaEvents, e => e.EventType == SupportSlaEventType.Resumed);
        Assert.Equal(SupportSlaTargetType.Resolution, resumed.TargetType);
        Assert.Equal(60 * 60, resumed.DurationSeconds);
        Assert.Equal(ticket.ResolutionDueAtUtc.AddHours(72), resumed.DueAtUtc);
        Assert.Contains(result.Messages, message => message.Body == "已補充資料");
    }

    [Fact]
    public async Task AddMessageAsync_WhenResolved_RejectsWithoutAppendingArtifacts()
    {
        var (service, store, _) = CreateSut();
        var created = await service.CreateAsync(
            "member-a",
            new CreateSupportTicketRequest
            {
                Category = SupportTicketCategory.Other,
                Subject = "案件",
                Message = "初始訊息內容",
            },
            CancellationToken.None);
        var ticket = store.Tickets.Single(t => t.PublicId == created.PublicId);
        ticket.Assign("agent", NowUtc.AddMinutes(1));
        ticket.Transition(SupportTicketStatus.InProgress, NowUtc.AddMinutes(2));
        ticket.Transition(SupportTicketStatus.Resolved, NowUtc.AddMinutes(3));
        var messageCount = store.Messages.Count;
        var historyCount = store.StatusHistories.Count;
        var slaEventCount = store.SlaEvents.Count;

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.AddMessageAsync(
            "member-a",
            created.PublicId,
            new CreateSupportMessageRequest { Body = "問題仍未解決", RowVersion = ticket.RowVersion },
            CancellationToken.None));

        Assert.Equal(DomainErrorCodes.SupportTicketStateConflict, exception.Code);
        Assert.Equal(SupportTicketStatus.Resolved, ticket.Status);
        Assert.Equal(messageCount, store.Messages.Count);
        Assert.Equal(historyCount, store.StatusHistories.Count);
        Assert.Equal(slaEventCount, store.SlaEvents.Count);
    }
    [Fact]
    public async Task AddMessageAsync_WhenSaveReportsConcurrencyConflict_SurfacesAsDomainConflict()
    {
        var (service, store, _) = CreateSut();
        var created = await service.CreateAsync("member-a", new CreateSupportTicketRequest { Category = SupportTicketCategory.Other, Subject = "案件", Message = "初始訊息內容" }, CancellationToken.None);
        store.SimulateConcurrencyConflictOnNextSave = true;

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.AddMessageAsync(
            "member-a",
            created.PublicId,
            new CreateSupportMessageRequest { Body = "追加訊息", RowVersion = created.RowVersion },
            CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ConcurrencyConflict, exception.Code);
        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task CancelAsync_WhenOpenAndNoHumanReplyYet_Succeeds()
    {
        var (service, _, _) = CreateSut();
        var created = await service.CreateAsync("member-a", new CreateSupportTicketRequest { Category = SupportTicketCategory.Other, Subject = "案件", Message = "初始訊息內容" }, CancellationToken.None);

        var cancelled = await service.CancelAsync(
            "member-a",
            created.PublicId,
            new CancelSupportTicketRequest { ReasonCode = "no-longer-needed", RowVersion = created.RowVersion },
            CancellationToken.None);

        Assert.Equal(SupportTicketStatus.Cancelled, cancelled.Status);
        Assert.DoesNotContain("cancel", cancelled.AvailableActions);
    }

    [Fact]
    public async Task CancelAsync_AfterFirstHumanResponse_IsNotAllowed()
    {
        var (service, store, _) = CreateSut();
        var created = await service.CreateAsync("member-a", new CreateSupportTicketRequest { Category = SupportTicketCategory.Other, Subject = "案件", Message = "初始訊息內容" }, CancellationToken.None);
        var ticket = store.Tickets.Single(t => t.PublicId == created.PublicId);
        ticket.RecordFirstHumanResponse(NowUtc.AddMinutes(5));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() => service.CancelAsync(
            "member-a",
            created.PublicId,
            new CancelSupportTicketRequest { ReasonCode = "no-longer-needed", RowVersion = ticket.RowVersion },
            CancellationToken.None));

        Assert.Equal(DomainErrorCodes.SupportTicketCancelNotAllowed, exception.Code);
    }

    [Fact]
    public async Task GetDetailAsync_NeverReturnsInternalMessages()
    {
        var (service, store, _) = CreateSut();
        var created = await service.CreateAsync("member-a", new CreateSupportTicketRequest { Category = SupportTicketCategory.Other, Subject = "案件", Message = "初始訊息內容" }, CancellationToken.None);
        var ticket = store.Tickets.Single(t => t.PublicId == created.PublicId);
        store.Messages.Add(new SupportMessage(Guid.NewGuid(), ticket.Id, SupportSenderType.Admin, "agent", "內部備註", isInternal: true, aiGenerated: false, replyToMessageId: null, language: "zh-TW", sentAtUtc: NowUtc.AddMinutes(1)));

        var detail = await service.GetDetailAsync("member-a", created.PublicId, CancellationToken.None);

        Assert.DoesNotContain(detail.Messages, m => m.Body == "內部備註");
    }
}
