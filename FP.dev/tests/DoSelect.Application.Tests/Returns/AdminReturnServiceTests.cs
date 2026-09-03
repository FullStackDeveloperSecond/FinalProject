using DoSelect.Application.Refunds;
using DoSelect.Application.Returns;
using DoSelect.Domain.Refunds;
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

    private static (AdminReturnService Service, FakeReturnStore Store, ReturnRequest Request, RecordingRefundCreationPort Refunds)
        CreateSutWithRequestedReturn()
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

        var refunds = new RecordingRefundCreationPort();
        var service = new AdminReturnService(
            store, orderPort, store, refunds, new FixedTimeProvider(NowOffset));
        return (service, store, request, refunds);
    }

    private static ReturnItem AddSecondItem(FakeReturnStore store)
    {
        var item = new ReturnItem(Guid.NewGuid(), 1, 11, 1, 0m, "NotInspected", NowUtc);
        ItemIdField.SetValue(item, 101L);
        store.Items.Add(item);
        return item;
    }

    // Positional-argument factory helpers — ApproveReturnRequest/ReceiveReturnRequest/
    // ExtendShipmentDeadlineRequest/CreateReturnShipmentRequest/AppendReturnShipmentEventRequest
    // are property-init records (not primary-constructor positional records), since the native
    // OpenApi generator only reads DataAnnotations from properties — see the doc comment on
    // ApproveReturnRequest. These preserve the old positional-call shape for every test below.
    private static ApproveReturnRequest Approve(
        bool approved, IReadOnlyList<ApproveReturnItemLine> items, string reasonCode, string? note, byte[] rowVersion,
        AssemblyFeeDisposition? assemblyFeeDisposition = null, decimal? returnShippingCost = null) =>
        new()
        {
            Approved = approved,
            Items = items,
            ReasonCode = reasonCode,
            Note = note,
            ReturnRowVersion = rowVersion,
            AssemblyFeeDisposition = assemblyFeeDisposition,
            ReturnShippingCost = returnShippingCost,
        };

    private static ReceiveReturnRequest Receive(string? note, byte[] rowVersion) =>
        new() { Note = note, ReturnRowVersion = rowVersion };

    private static ExtendShipmentDeadlineRequest Extend(string reasonCode, byte[] rowVersion) =>
        new() { ReasonCode = reasonCode, ReturnRowVersion = rowVersion };

    private static CreateReturnShipmentRequest CreateShipment(
        ReturnShipmentMethod method, string? carrierCode, string? recipientName, string? recipientPhone,
        string? postalCode, string? addressLine, string? storeCode, string? storeName, byte[] rowVersion) =>
        new()
        {
            Method = method,
            CarrierCode = carrierCode,
            RecipientName = recipientName,
            RecipientPhone = recipientPhone,
            PostalCode = postalCode,
            AddressLine = addressLine,
            StoreCode = storeCode,
            StoreName = storeName,
            ReturnRowVersion = rowVersion,
        };

    private static void AssertHistory(
        ReturnStatusHistory history,
        ReturnRequestStatus from,
        ReturnRequestStatus to,
        string actorUserId)
    {
        Assert.Equal(from, history.FromStatus);
        Assert.Equal(to, history.ToStatus);
        Assert.Equal(actorUserId, history.ActorUserId);
        Assert.Equal(NowUtc, history.OccurredAtUtc);
    }
    private static AppendReturnShipmentEventRequest ShipmentEvent(
        string source, string externalEventId, string eventType, DateTime occurredAtUtc, string? description) =>
        new() { Source = source, ExternalEventId = externalEventId, EventType = eventType, OccurredAtUtc = occurredAtUtc, Description = description };

    [Fact]
    public async Task ReviewAsync_Approve_WithInspectionRequired_MovesToAwaitingShipment()
    {
        var (service, store, request, _) = CreateSutWithRequestedReturn();
        var approval = Approve(
            true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, InspectionRequired: true)], "eligible", null, request.RowVersion);

        var dto = await service.ReviewAsync(request.PublicId, "admin-1", approval, CancellationToken.None);

        Assert.Equal(ReturnRequestStatus.AwaitingShipment, dto.Status);
        Assert.NotNull(dto.ReturnShipmentDueAtUtc);
        Assert.Equal(dto.ApprovedAtUtc!.Value.AddDays(7), dto.ReturnShipmentDueAtUtc);
    }

    [Fact]
    public async Task ReviewAsync_Approve_WithoutInspectionRequired_MovesStraightToAwaitingRefund()
    {
        var (service, store, request, _) = CreateSutWithRequestedReturn();
        var approval = Approve(
            true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, InspectionRequired: false)], "goodwill", null, request.RowVersion,
            AssemblyFeeDisposition.NotApplicable, 0m);

        var dto = await service.ReviewAsync(request.PublicId, "admin-1", approval, CancellationToken.None);

        Assert.Equal(ReturnRequestStatus.AwaitingRefund, dto.Status);
        Assert.Null(dto.ReturnShipmentDueAtUtc);
        Assert.Equal(AssemblyFeeDisposition.NotApplicable, request.AssemblyFeeDisposition);
        Assert.Equal(0m, request.ReturnShippingCost);
    }

    [Fact]
    public async Task ReviewAsync_Approve_WithoutInspectionRequired_RejectsMissingRefundTrustedInputsBeforeMutation()
    {
        var (service, store, request, _) = CreateSutWithRequestedReturn();
        var approval = Approve(
            true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, InspectionRequired: false)], "eligible", null, request.RowVersion);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.ReviewAsync(request.PublicId, "admin-1", approval, CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
        Assert.Equal(ReturnRequestStatus.Requested, request.Status);
        Assert.Empty(store.Histories);
    }

    [Fact]
    public async Task ReviewAsync_Reject_MovesToRejectedWithReason()
    {
        var (service, store, request, _) = CreateSutWithRequestedReturn();
        var rejection = Approve(false, [], "not-eligible", "超過期限", request.RowVersion);

        var dto = await service.ReviewAsync(request.PublicId, "admin-1", rejection, CancellationToken.None);

        Assert.Equal(ReturnRequestStatus.Rejected, dto.Status);
        Assert.Collection(
            store.Histories,
            h => AssertHistory(h, ReturnRequestStatus.Requested, ReturnRequestStatus.UnderReview, "admin-1"),
            h => AssertHistory(h, ReturnRequestStatus.UnderReview, ReturnRequestStatus.Rejected, "admin-1"));
        Assert.All(store.Histories, h => Assert.Equal("not-eligible", h.ReasonCode));
    }

    [Fact]
    public async Task ReviewAsync_PartialQuantityApproval_ThrowsValidationFailed()
    {
        var (service, store, request, _) = CreateSutWithRequestedReturn();
        var approval = Approve(
            true, [new ApproveReturnItemLine(store.Items[0].PublicId, 0, InspectionRequired: true)], "partial", null, request.RowVersion);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.ReviewAsync(request.PublicId, "admin-1", approval, CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task FullLifecycle_ApproveReceiveInspect_ReachesAwaitingRefund()
    {
        var (service, store, request, _) = CreateSutWithRequestedReturn();
        await service.ReviewAsync(
            request.PublicId, "admin-1",
            Approve(true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, true)], "eligible", null, request.RowVersion),
            CancellationToken.None);

        await service.ReceiveAsync(request.PublicId, "admin-1", Receive(null, request.RowVersion), CancellationToken.None);
        Assert.Equal(ReturnRequestStatus.Received, request.Status);

        var inspect = new InspectReturnRequest(
            [new InspectReturnItemLine(store.Items[0].PublicId, "Unopened", RestockDisposition.Resellable, null)], request.RowVersion,
            AssemblyFeeDisposition.NotApplicable, 80m);
        var dto = await service.InspectAsync(request.PublicId, "admin-1", inspect, CancellationToken.None);

        Assert.Equal(ReturnRequestStatus.AwaitingRefund, dto.Status);
        Assert.Equal(RestockDisposition.Resellable, store.Items[0].RestockDisposition);
        Assert.Single(store.Inspections);
        var restock = Assert.Single(store.ReturnToStockInstructions);
        Assert.Equal(store.Items[0].OrderItemId, restock.OrderItemId);
        Assert.Equal(store.Items[0].PublicId, restock.ReturnItemPublicId);
        Assert.Equal(store.Items[0].Quantity, restock.Quantity);
        Assert.Equal(request.PublicId, store.ReturnToStockReturnPublicId);
        Assert.Equal("admin-1", store.ReturnToStockAdminUserId);
        Assert.Equal(AssemblyFeeDisposition.NotApplicable, request.AssemblyFeeDisposition);
        Assert.Equal(80m, request.ReturnShippingCost);
        Assert.Collection(
            store.Histories,
            h => AssertHistory(h, ReturnRequestStatus.Requested, ReturnRequestStatus.UnderReview, "admin-1"),
            h => AssertHistory(h, ReturnRequestStatus.UnderReview, ReturnRequestStatus.Approved, "admin-1"),
            h => AssertHistory(h, ReturnRequestStatus.Approved, ReturnRequestStatus.AwaitingShipment, "admin-1"),
            h => AssertHistory(h, ReturnRequestStatus.AwaitingShipment, ReturnRequestStatus.InTransit, "admin-1"),
            h => AssertHistory(h, ReturnRequestStatus.InTransit, ReturnRequestStatus.Received, "admin-1"),
            h => AssertHistory(h, ReturnRequestStatus.Received, ReturnRequestStatus.Inspecting, "admin-1"),
            h => AssertHistory(h, ReturnRequestStatus.Inspecting, ReturnRequestStatus.AwaitingRefund, "admin-1"));
    }

    [Fact]
    public async Task InspectAsync_MissingRefundTrustedInputs_RejectsBeforeInspectionMutation()
    {
        var (service, store, request, _) = CreateSutWithRequestedReturn();
        await service.ReviewAsync(
            request.PublicId, "admin-1",
            Approve(true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, true)], "eligible", null, request.RowVersion),
            CancellationToken.None);
        await service.ReceiveAsync(request.PublicId, "admin-1", Receive(null, request.RowVersion), CancellationToken.None);

        var inspect = new InspectReturnRequest(
            [new InspectReturnItemLine(store.Items[0].PublicId, "Unopened", RestockDisposition.Resellable, null)],
            request.RowVersion);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.InspectAsync(request.PublicId, "admin-1", inspect, CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
        Assert.Equal(ReturnRequestStatus.Received, request.Status);
        Assert.Empty(store.Inspections);
        Assert.Null(store.Items[0].RestockDisposition);
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
        var (service, store, request, _) = CreateSutWithRequestedReturn();
        await service.ReviewAsync(
            request.PublicId, "admin-1",
            Approve(true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, true)], "eligible", null, request.RowVersion),
            CancellationToken.None);
        await service.ReceiveAsync(request.PublicId, "admin-1", Receive(null, request.RowVersion), CancellationToken.None);

        var callerSuppliedRowVersion = new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 };
        Assert.NotEqual(callerSuppliedRowVersion, request.RowVersion);
        var inspect = new InspectReturnRequest(
            [new InspectReturnItemLine(store.Items[0].PublicId, "Unopened", RestockDisposition.Resellable, null)],
            callerSuppliedRowVersion,
            AssemblyFeeDisposition.NotApplicable,
            0m);

        await service.InspectAsync(request.PublicId, "admin-1", inspect, CancellationToken.None);

        Assert.Equal(callerSuppliedRowVersion, store.LastSaveTransitionExpectedRowVersion);
    }

    [Fact]
    public async Task ExtendShipmentDeadlineAsync_SecondCall_ThrowsExtensionNotAllowed()
    {
        var (service, store, request, _) = CreateSutWithRequestedReturn();
        await service.ReviewAsync(
            request.PublicId, "admin-1",
            Approve(true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, true)], "eligible", null, request.RowVersion),
            CancellationToken.None);

        var extend = Extend("customer-requested", request.RowVersion);
        await service.ExtendShipmentDeadlineAsync(request.PublicId, "admin-1", extend, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.ExtendShipmentDeadlineAsync(request.PublicId, "admin-1", extend, CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ReturnShipmentExtensionNotAllowed, exception.ErrorCode);
    }

    [Fact]
    public async Task ReviewAsync_WhenAlreadyDecided_ThrowsStateConflict()
    {
        var (service, store, request, _) = CreateSutWithRequestedReturn();
        var approval = Approve(
            true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, true)], "eligible", null, request.RowVersion);
        await service.ReviewAsync(request.PublicId, "admin-1", approval, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.ReviewAsync(request.PublicId, "admin-1", approval, CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ReturnStateConflict, exception.ErrorCode);
    }


    [Fact]
    public async Task ReviewAsync_DuplicateItemThatOmitsAnotherItem_ThrowsBeforeMutation()
    {
        var (service, store, request, _) = CreateSutWithRequestedReturn();
        AddSecondItem(store);
        var duplicatedId = store.Items[0].PublicId;
        var approval = Approve(
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
        var (service, store, request, _) = CreateSutWithRequestedReturn();
        var secondItem = AddSecondItem(store);
        await service.ReviewAsync(
            request.PublicId,
            "admin-1",
            Approve(
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
            Receive(null, request.RowVersion),
            CancellationToken.None);

        var duplicatedId = store.Items[0].PublicId;
        var inspection = new InspectReturnRequest(
            [
                new InspectReturnItemLine(duplicatedId, "Unopened", RestockDisposition.Resellable, null),
                new InspectReturnItemLine(duplicatedId, "Unopened", RestockDisposition.Resellable, null),
            ],
            request.RowVersion,
            AssemblyFeeDisposition.NotApplicable,
            0m);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.InspectAsync(request.PublicId, "admin-1", inspection, CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
        Assert.Equal(ReturnRequestStatus.Received, request.Status);
        Assert.Empty(store.Inspections);
    }

    private static async Task<(AdminReturnService Service, FakeReturnStore Store, ReturnRequest Request)> CreateSutWithAwaitingShipmentReturnAsync()
    {
        var (service, store, request, _) = CreateSutWithRequestedReturn();
        await service.ReviewAsync(
            request.PublicId, "admin-1",
            Approve(true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, true)], "eligible", null, request.RowVersion),
            CancellationToken.None);
        await service.CreateShipmentAsync(
            request.PublicId,
            CreateShipment(
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
            ShipmentEvent("carrier", "evt-1", "InTransit", NowUtc.AddHours(3), null),
            CancellationToken.None);

        // A different ExternalEventId, naming an earlier main-sequence status, arrives late.
        var dto = await service.AppendShipmentEventAsync(
            request.PublicId,
            ShipmentEvent("carrier", "evt-0-delayed", "PickedUp", NowUtc.AddHours(1), null),
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
            ShipmentEvent("carrier", "evt-1", "InTransit", NowUtc.AddHours(3), null),
            CancellationToken.None);

        // A HIGHER-rank target status would normally advance the shipment, but its own
        // OccurredAtUtc precedes the last applied event's — the timing guard alone (not rank)
        // must reject it.
        var dto = await service.AppendShipmentEventAsync(
            request.PublicId,
            ShipmentEvent("carrier", "evt-2-delayed", "Delivered", NowUtc.AddHours(1), null),
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
            ShipmentEvent("carrier", "evt-1", "InTransit", NowUtc.AddHours(1), null),
            CancellationToken.None);
        await service.AppendShipmentEventAsync(
            request.PublicId,
            ShipmentEvent("carrier", "evt-2", "Delivered", NowUtc.AddHours(2), null),
            CancellationToken.None);

        var dto = await service.AppendShipmentEventAsync(
            request.PublicId,
            ShipmentEvent("carrier", "evt-3-stale", "InTransit", NowUtc.AddHours(4), null),
            CancellationToken.None);

        Assert.Equal(ReturnShipmentStatus.Delivered, dto.Status);
        Assert.Equal(3, store.ShipmentEvents.Count);
        Assert.Equal(ReturnRequestStatus.Received, request.Status);
    }

    [Fact]
    public async Task AppendShipmentEventAsync_SameExternalEventId_IsIdempotentAndDoesNotDuplicateHistory()
    {
        var (service, store, request) = await CreateSutWithAwaitingShipmentReturnAsync();
        var eventRequest = ShipmentEvent("carrier", "evt-1", "InTransit", NowUtc.AddHours(1), null);

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
            ShipmentEvent("carrier", "evt-1", "InTransit", NowUtc.AddDays(-2), null),
            CancellationToken.None);
        Assert.Equal(ReturnRequestStatus.InTransit, request.Status);

        var occurredAtUtc = NowUtc.AddDays(-1);
        var dto = await service.AppendShipmentEventAsync(
            request.PublicId,
            ShipmentEvent("carrier", "evt-2", "Delivered", occurredAtUtc, null),
            CancellationToken.None);

        Assert.Equal(occurredAtUtc, dto.ReceivedAtUtc);
        Assert.Equal(ReturnRequestStatus.Received, request.Status);
        Assert.Equal(ReturnRequestStatus.Received, store.Histories[^1].ToStatus);
        Assert.Equal(occurredAtUtc, store.Histories[^1].OccurredAtUtc);
    }

    [Fact]
    public async Task AppendShipmentEventAsync_FirstEventIsDelivered_CascadesThroughInTransitToReceivedWithTwoHistoryRows()
    {
        var (service, store, request) = await CreateSutWithAwaitingShipmentReturnAsync();
        var historyCountBefore = store.Histories.Count;
        var occurredAtUtc = NowUtc.AddDays(-1);

        // No InTransit/PickedUp event ever preceded this one — the shipment jumps straight from
        // Pending to Delivered, mirroring a carrier that only reports a single terminal webhook.
        var dto = await service.AppendShipmentEventAsync(
            request.PublicId,
            ShipmentEvent("carrier", "evt-delivered-first", "Delivered", occurredAtUtc, null),
            CancellationToken.None);

        Assert.Equal(ReturnShipmentStatus.Delivered, dto.Status);
        Assert.Equal(ReturnRequestStatus.Received, request.Status);

        var newHistories = store.Histories.Skip(historyCountBefore).ToList();
        Assert.Equal(2, newHistories.Count);
        Assert.Equal(ReturnRequestStatus.AwaitingShipment, newHistories[0].FromStatus);
        Assert.Equal(ReturnRequestStatus.InTransit, newHistories[0].ToStatus);
        Assert.Equal(occurredAtUtc, newHistories[0].OccurredAtUtc);
        Assert.Equal(ReturnRequestStatus.InTransit, newHistories[1].FromStatus);
        Assert.Equal(ReturnRequestStatus.Received, newHistories[1].ToStatus);
        Assert.Equal(occurredAtUtc, newHistories[1].OccurredAtUtc);

        Assert.Single(store.ShipmentEvents);
    }
    [Fact]
    public async Task ReviewAsync_Approve_WithoutInspectionRequired_StagesTheRefundWithTheJustCapturedSnapshot()
    {
        // 直接落在 AwaitingRefund 的核准必須在同一筆交易裡建立退款（alex 2026-09-03 #98 A2）。
        // 這一層看得到的「同一筆交易」就是「SaveTransitionAsync 之前呼叫過埠」，
        // 真正的原子性由 ReturnRefundCreationTests 在 SQL Server 上證明。
        var (service, store, request, refunds) = CreateSutWithRequestedReturn();
        var approval = Approve(
            true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, InspectionRequired: false)], "goodwill", null, request.RowVersion,
            AssemblyFeeDisposition.AssemblyFault, 120m);

        await service.ReviewAsync(request.PublicId, "admin-1", approval, CancellationToken.None);

        var command = Assert.Single(refunds.Calls);
        Assert.Equal(request.PublicId, command.ReturnPublicId);
        Assert.Equal("admin-1", command.AdminUserId);
        Assert.Equal(request.ReasonCode, command.ReasonCode);

        // 可信的三項必須是這次核准剛剛驗證下來的值。實作不能自己回頭讀資料庫 ——
        // 此刻 CaptureRefundTrustedInputs 只改了記憶體中的實體，SaveChanges 還沒發生。
        Assert.Equal(AssemblyFeeDisposition.AssemblyFault, command.AssemblyFeeDisposition);
        Assert.Equal(120m, command.ReturnShippingCost);
    }

    [Fact]
    public async Task ReviewAsync_Approve_WithInspectionRequired_DoesNotStageARefundYet()
    {
        // 需要寄回檢查的核准只到 AwaitingShipment。這時候建立退款會讓一張還沒收到貨、
        // 還沒檢查的退貨先佔住可退款餘額。
        var (service, store, request, refunds) = CreateSutWithRequestedReturn();
        var approval = Approve(
            true, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, InspectionRequired: true)], "eligible", null, request.RowVersion);

        await service.ReviewAsync(request.PublicId, "admin-1", approval, CancellationToken.None);

        Assert.Empty(refunds.Calls);
    }

    [Fact]
    public async Task ReviewAsync_Reject_DoesNotStageARefund()
    {
        var (service, store, request, refunds) = CreateSutWithRequestedReturn();
        var rejection = Approve(
            false, [new ApproveReturnItemLine(store.Items[0].PublicId, 1, InspectionRequired: false)], "not-eligible", null, request.RowVersion);

        await service.ReviewAsync(request.PublicId, "admin-1", rejection, CancellationToken.None);

        Assert.Empty(refunds.Calls);
    }

    /// <summary>記下退款建立埠被呼叫的時機與參數。</summary>
    /// <remarks>
    /// 退貨核准要在<b>同一筆交易</b>裡建立退款，而這一層看得到的「同一筆交易」就是
    /// 「SaveTransitionAsync 之前已經呼叫過」。實際的原子性由 SQL 層的測試證明。
    /// </remarks>
    private sealed class RecordingRefundCreationPort : IReturnRefundCreationPort
    {
        public List<ReturnRefundCreationCommand> Calls { get; } = [];

        public Task StagePendingRefundAsync(
            ReturnRefundCreationCommand command,
            CancellationToken cancellationToken)
        {
            Calls.Add(command);
            return Task.CompletedTask;
        }
    }

}
