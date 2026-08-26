using DoSelect.Application.Returns;
using DoSelect.Domain.Returns;

namespace DoSelect.Application.Tests.Returns;

public sealed class AdminReturnServiceTests
{
    private static readonly DateTimeOffset NowOffset = new(2026, 8, 24, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTime NowUtc = NowOffset.UtcDateTime;

    private static readonly System.Reflection.FieldInfo RequestIdField =
        typeof(ReturnRequest).BaseType!.BaseType!.BaseType!
            .GetField("<Id>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    private static readonly System.Reflection.FieldInfo ItemIdField =
        typeof(ReturnItem).BaseType!.BaseType!
            .GetField("<Id>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    private static (AdminReturnService Service, FakeReturnStore Store, ReturnRequest Request) CreateSutWithRequestedReturn()
    {
        var store = new FakeReturnStore();
        var orderPort = new FakeReturnOrderEligibilityPort();
        var orderPublicId = Guid.NewGuid();
        orderPort.Register(new OrderEligibilitySnapshot(1, orderPublicId, "ORD-1", "member-a", NowUtc.AddDays(-3), 1, [], []));

        var request = new ReturnRequest(Guid.NewGuid(), "RT-1", 1, "member-a", "Defective", "面板有亮點", 1, NowUtc);
        RequestIdField.SetValue(request, 1L);
        store.Requests.Add(request);

        var item = new ReturnItem(Guid.NewGuid(), 1, 10, 1, 0m, "NotInspected", NowUtc);
        ItemIdField.SetValue(item, 100L);
        store.Items.Add(item);

        var service = new AdminReturnService(store, orderPort, new FixedTimeProvider(NowOffset));
        return (service, store, request);
    }

    private static ReturnItem AddSecondItem(FakeReturnStore store)
    {
        var item = new ReturnItem(Guid.NewGuid(), 1, 11, 1, 0m, "NotInspected", NowUtc);
        ItemIdField.SetValue(item, 101L);
        store.Items.Add(item);
        return item;
    }

    [Fact]
    public async Task ReviewAsync_Approve_WithInspectionRequired_MovesToAwaitingShipment()
    {
        var (service, store, request) = CreateSutWithRequestedReturn();
        var approval = new ApproveReturnRequest(
            true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, InspectionRequired: true)], "eligible", null, request.RowVersion);

        var dto = await service.ReviewAsync(request.PublicId, "admin-1", approval, CancellationToken.None);

        Assert.Equal(ReturnRequestStatus.AwaitingShipment, dto.Status);
        Assert.NotNull(dto.ReturnShipmentDueAtUtc);
        Assert.Equal(dto.ApprovedAtUtc!.Value.AddDays(7), dto.ReturnShipmentDueAtUtc);
    }

    [Fact]
    public async Task ReviewAsync_Approve_WithoutInspectionRequired_MovesStraightToAwaitingRefund()
    {
        var (service, store, request) = CreateSutWithRequestedReturn();
        var approval = new ApproveReturnRequest(
            true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, InspectionRequired: false)], "goodwill", null, request.RowVersion);

        var dto = await service.ReviewAsync(request.PublicId, "admin-1", approval, CancellationToken.None);

        Assert.Equal(ReturnRequestStatus.AwaitingRefund, dto.Status);
        Assert.Null(dto.ReturnShipmentDueAtUtc);
    }

    [Fact]
    public async Task ReviewAsync_Reject_MovesToRejectedWithReason()
    {
        var (service, store, request) = CreateSutWithRequestedReturn();
        var rejection = new ApproveReturnRequest(false, [], "not-eligible", "超過期限", request.RowVersion);

        var dto = await service.ReviewAsync(request.PublicId, "admin-1", rejection, CancellationToken.None);

        Assert.Equal(ReturnRequestStatus.Rejected, dto.Status);
        Assert.Single(store.Histories);
        Assert.Equal("not-eligible", store.Histories[0].ReasonCode);
    }

    [Fact]
    public async Task ReviewAsync_PartialQuantityApproval_ThrowsValidationFailed()
    {
        var (service, store, request) = CreateSutWithRequestedReturn();
        var approval = new ApproveReturnRequest(
            true, [new ApproveReturnItemLine(store.Items[0].PublicId, 0, InspectionRequired: true)], "partial", null, request.RowVersion);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.ReviewAsync(request.PublicId, "admin-1", approval, CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task FullLifecycle_ApproveReceiveInspect_ReachesAwaitingRefund()
    {
        var (service, store, request) = CreateSutWithRequestedReturn();
        await service.ReviewAsync(
            request.PublicId, "admin-1",
            new ApproveReturnRequest(true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, true)], "eligible", null, request.RowVersion),
            CancellationToken.None);

        await service.ReceiveAsync(request.PublicId, "admin-1", new ReceiveReturnRequest(null, request.RowVersion), CancellationToken.None);
        Assert.Equal(ReturnRequestStatus.Received, request.Status);

        var inspect = new InspectReturnRequest(
            [new InspectReturnItemLine(store.Items[0].PublicId, "Unopened", RestockDisposition.Resellable, null)], request.RowVersion);
        var dto = await service.InspectAsync(request.PublicId, "admin-1", inspect, CancellationToken.None);

        Assert.Equal(ReturnRequestStatus.AwaitingRefund, dto.Status);
        Assert.Equal(RestockDisposition.Resellable, store.Items[0].RestockDisposition);
        Assert.Single(store.Inspections);
    }

    [Fact]
    public async Task InspectAsync_PassesTheCallersOwnReturnRowVersion_NotTheFreshlyLoadedEntityRowVersion()
    {
        // Regression test for the P1 defect: InspectAsync used to call SaveTransitionAsync with
        // returnRequest.RowVersion (the value just loaded inside this same call) instead of
        // request.ReturnRowVersion (the value the caller actually submitted), which silently
        // defeats optimistic concurrency — a stale caller could never be rejected, because the
        // "expected" version was always re-derived from whatever the server currently holds.
        // Here the two are made deliberately different so only the fix's precise plumbing —
        // not an accidental match — can make the assertion pass.
        var (service, store, request) = CreateSutWithRequestedReturn();
        await service.ReviewAsync(
            request.PublicId, "admin-1",
            new ApproveReturnRequest(true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, true)], "eligible", null, request.RowVersion),
            CancellationToken.None);
        await service.ReceiveAsync(request.PublicId, "admin-1", new ReceiveReturnRequest(null, request.RowVersion), CancellationToken.None);

        var callerSuppliedRowVersion = new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 };
        Assert.NotEqual(callerSuppliedRowVersion, request.RowVersion);
        var inspect = new InspectReturnRequest(
            [new InspectReturnItemLine(store.Items[0].PublicId, "Unopened", RestockDisposition.Resellable, null)],
            callerSuppliedRowVersion);

        await service.InspectAsync(request.PublicId, "admin-1", inspect, CancellationToken.None);

        Assert.Equal(callerSuppliedRowVersion, store.LastSaveTransitionExpectedRowVersion);
    }

    [Fact]
    public async Task ExtendShipmentDeadlineAsync_SecondCall_ThrowsExtensionNotAllowed()
    {
        var (service, store, request) = CreateSutWithRequestedReturn();
        await service.ReviewAsync(
            request.PublicId, "admin-1",
            new ApproveReturnRequest(true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, true)], "eligible", null, request.RowVersion),
            CancellationToken.None);

        var extend = new ExtendShipmentDeadlineRequest("customer-requested", request.RowVersion);
        await service.ExtendShipmentDeadlineAsync(request.PublicId, "admin-1", extend, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.ExtendShipmentDeadlineAsync(request.PublicId, "admin-1", extend, CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ReturnShipmentExtensionNotAllowed, exception.ErrorCode);
    }

    [Fact]
    public async Task ReviewAsync_WhenAlreadyDecided_ThrowsStateConflict()
    {
        var (service, store, request) = CreateSutWithRequestedReturn();
        var approval = new ApproveReturnRequest(
            true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, true)], "eligible", null, request.RowVersion);
        await service.ReviewAsync(request.PublicId, "admin-1", approval, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.ReviewAsync(request.PublicId, "admin-1", approval, CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ReturnStateConflict, exception.ErrorCode);
    }


    [Fact]
    public async Task ReviewAsync_DuplicateItemThatOmitsAnotherItem_ThrowsBeforeMutation()
    {
        var (service, store, request) = CreateSutWithRequestedReturn();
        AddSecondItem(store);
        var duplicatedId = store.Items[0].PublicId;
        var approval = new ApproveReturnRequest(
            true,
            [
                new ApproveReturnItemLine(duplicatedId, 1, true),
                new ApproveReturnItemLine(duplicatedId, 1, true),
            ],
            "eligible",
            null,
            request.RowVersion);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.ReviewAsync(request.PublicId, "admin-1", approval, CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
        Assert.Equal(ReturnRequestStatus.Requested, request.Status);
        Assert.Empty(store.Histories);
    }

    [Fact]
    public async Task InspectAsync_DuplicateItemThatOmitsAnotherItem_ThrowsBeforeInspectionMutation()
    {
        var (service, store, request) = CreateSutWithRequestedReturn();
        var secondItem = AddSecondItem(store);
        await service.ReviewAsync(
            request.PublicId,
            "admin-1",
            new ApproveReturnRequest(
                true,
                [
                    new ApproveReturnItemLine(store.Items[0].PublicId, 1, true),
                    new ApproveReturnItemLine(secondItem.PublicId, 1, true),
                ],
                "eligible",
                null,
                request.RowVersion),
            CancellationToken.None);
        await service.ReceiveAsync(
            request.PublicId,
            "admin-1",
            new ReceiveReturnRequest(null, request.RowVersion),
            CancellationToken.None);

        var duplicatedId = store.Items[0].PublicId;
        var inspection = new InspectReturnRequest(
            [
                new InspectReturnItemLine(duplicatedId, "Unopened", RestockDisposition.Resellable, null),
                new InspectReturnItemLine(duplicatedId, "Unopened", RestockDisposition.Resellable, null),
            ],
            request.RowVersion);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.InspectAsync(request.PublicId, "admin-1", inspection, CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
        Assert.Equal(ReturnRequestStatus.Received, request.Status);
        Assert.Empty(store.Inspections);
    }

    private static async Task<(AdminReturnService Service, FakeReturnStore Store, ReturnRequest Request)> CreateSutWithAwaitingShipmentReturnAsync()
    {
        var (service, store, request) = CreateSutWithRequestedReturn();
        await service.ReviewAsync(
            request.PublicId, "admin-1",
            new ApproveReturnRequest(true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, true)], "eligible", null, request.RowVersion),
            CancellationToken.None);
        await service.CreateShipmentAsync(
            request.PublicId,
            new CreateReturnShipmentRequest(
                ReturnShipmentMethod.SelfShip, null, null, null, null, null, null, null, request.RowVersion),
            CancellationToken.None);
        return (service, store, request);
    }

    [Fact]
    public async Task AppendShipmentEventAsync_DelayedLowerRankEvent_IsPersistedButDoesNotRegressStatus()
    {
        var (service, store, request) = await CreateSutWithAwaitingShipmentReturnAsync();
        await service.AppendShipmentEventAsync(
            request.PublicId,
            new AppendReturnShipmentEventRequest("carrier", "evt-1", "InTransit", NowUtc.AddHours(3), null),
            CancellationToken.None);

        // A different ExternalEventId, naming an earlier main-sequence status, arrives late.
        var dto = await service.AppendShipmentEventAsync(
            request.PublicId,
            new AppendReturnShipmentEventRequest("carrier", "evt-0-delayed", "PickedUp", NowUtc.AddHours(1), null),
            CancellationToken.None);

        Assert.Equal(ReturnShipmentStatus.InTransit, dto.Status);
        Assert.Equal(2, store.ShipmentEvents.Count);
        Assert.Contains(store.ShipmentEvents, e => e.ExternalEventId == "evt-0-delayed");
    }

    [Fact]
    public async Task AppendShipmentEventAsync_EventOlderThanLastApplied_IsPersistedButDoesNotRegressStatus()
    {
        var (service, store, request) = await CreateSutWithAwaitingShipmentReturnAsync();
        await service.AppendShipmentEventAsync(
            request.PublicId,
            new AppendReturnShipmentEventRequest("carrier", "evt-1", "InTransit", NowUtc.AddHours(3), null),
            CancellationToken.None);

        // A HIGHER-rank target status would normally advance the shipment, but its own
        // OccurredAtUtc precedes the last applied event's — the timing guard alone (not rank)
        // must reject it.
        var dto = await service.AppendShipmentEventAsync(
            request.PublicId,
            new AppendReturnShipmentEventRequest("carrier", "evt-2-delayed", "Delivered", NowUtc.AddHours(1), null),
            CancellationToken.None);

        Assert.Equal(ReturnShipmentStatus.InTransit, dto.Status);
        Assert.Equal(2, store.ShipmentEvents.Count);
        Assert.Equal(ReturnRequestStatus.InTransit, request.Status);
    }

    [Fact]
    public async Task AppendShipmentEventAsync_EventAfterTerminal_PersistsEventWithoutReopening()
    {
        var (service, store, request) = await CreateSutWithAwaitingShipmentReturnAsync();
        await service.AppendShipmentEventAsync(
            request.PublicId,
            new AppendReturnShipmentEventRequest("carrier", "evt-1", "InTransit", NowUtc.AddHours(1), null),
            CancellationToken.None);
        await service.AppendShipmentEventAsync(
            request.PublicId,
            new AppendReturnShipmentEventRequest("carrier", "evt-2", "Delivered", NowUtc.AddHours(2), null),
            CancellationToken.None);

        var dto = await service.AppendShipmentEventAsync(
            request.PublicId,
            new AppendReturnShipmentEventRequest("carrier", "evt-3-stale", "InTransit", NowUtc.AddHours(4), null),
            CancellationToken.None);

        Assert.Equal(ReturnShipmentStatus.Delivered, dto.Status);
        Assert.Equal(3, store.ShipmentEvents.Count);
        Assert.Equal(ReturnRequestStatus.Received, request.Status);
    }

    [Fact]
    public async Task AppendShipmentEventAsync_SameExternalEventId_IsIdempotentAndDoesNotDuplicateHistory()
    {
        var (service, store, request) = await CreateSutWithAwaitingShipmentReturnAsync();
        var eventRequest = new AppendReturnShipmentEventRequest("carrier", "evt-1", "InTransit", NowUtc.AddHours(1), null);

        await service.AppendShipmentEventAsync(request.PublicId, eventRequest, CancellationToken.None);
        var historyCountAfterFirst = store.Histories.Count;
        var dto = await service.AppendShipmentEventAsync(request.PublicId, eventRequest, CancellationToken.None);

        Assert.Single(store.ShipmentEvents);
        Assert.Equal(historyCountAfterFirst, store.Histories.Count);
        Assert.Equal(ReturnShipmentStatus.InTransit, dto.Status);
    }

    [Fact]
    public async Task AppendShipmentEventAsync_ForwardEvent_UsesOccurredAtUtcForStatusTimestampsAndAdvancesReturnRequest()
    {
        var (service, store, request) = await CreateSutWithAwaitingShipmentReturnAsync();
        await service.AppendShipmentEventAsync(
            request.PublicId,
            new AppendReturnShipmentEventRequest("carrier", "evt-1", "InTransit", NowUtc.AddDays(-2), null),
            CancellationToken.None);
        Assert.Equal(ReturnRequestStatus.InTransit, request.Status);

        var occurredAtUtc = NowUtc.AddDays(-1);
        var dto = await service.AppendShipmentEventAsync(
            request.PublicId,
            new AppendReturnShipmentEventRequest("carrier", "evt-2", "Delivered", occurredAtUtc, null),
            CancellationToken.None);

        Assert.Equal(occurredAtUtc, dto.ReceivedAtUtc);
        Assert.Equal(ReturnRequestStatus.Received, request.Status);
        Assert.Equal(ReturnRequestStatus.Received, store.Histories[^1].ToStatus);
        Assert.Equal(occurredAtUtc, store.Histories[^1].OccurredAtUtc);
    }
}
