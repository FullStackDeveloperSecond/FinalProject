using DoSelect.Application.Files;
using DoSelect.Application.Returns;
using DoSelect.Domain.Returns;

namespace DoSelect.Application.Tests.Returns;

internal sealed class FakePrivateFileStorage : IPrivateFileStorage
{
    public PrivateFileStoreStatus NextStatus { get; set; } = PrivateFileStoreStatus.Stored;

    public Task<PrivateFileStoreResult> StoreAsync(PrivateFileUpload upload, CancellationToken cancellationToken = default) =>
        Task.FromResult(NextStatus == PrivateFileStoreStatus.Stored
            ? new PrivateFileStoreResult(
                PrivateFileStoreStatus.Stored,
                new StoredPrivateFile($"private-files/ab/{Guid.NewGuid():N}.blob", upload.OriginalFileName, "pdf", "application/pdf", 1024, new byte[32]))
            : new PrivateFileStoreResult(NextStatus));

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(null);

    public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

public sealed class ReturnServiceTests
{
    private static readonly DateTimeOffset NowOffset = new(2026, 8, 24, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTime NowUtc = NowOffset.UtcDateTime;
    private static readonly DateTime DeliveredAtUtc = NowUtc.AddDays(-3); // within the 7-day window

    private static (
        ReturnService Service,
        FakeReturnStore Store,
        FakeReturnOrderEligibilityPort OrderPort,
        Guid OrderPublicId,
        Guid OrderItemPublicId) CreateSut(int returnableQuantity = 2, DateTime? deliveredAtUtc = null)
    {
        var store = new FakeReturnStore();
        var orderPort = new FakeReturnOrderEligibilityPort();
        var fileStorage = new FakePrivateFileStorage();
        var orderPublicId = Guid.NewGuid();
        var orderItemPublicId = Guid.NewGuid();
        orderPort.Register(new OrderEligibilitySnapshot(
            1, orderPublicId, "ORD-1", "member-a", deliveredAtUtc ?? DeliveredAtUtc, 1, [1, 2, 3, 4, 5, 6, 7, 8],
            [new EligibleOrderItem(10, orderItemPublicId, "SKU-1", "27型螢幕", returnableQuantity, 0, null, false, 100m)]));

        var service = new ReturnService(store, orderPort, fileStorage, new FixedTimeProvider(NowOffset));
        return (service, store, orderPort, orderPublicId, orderItemPublicId);
    }

    private static CreateReturnRequest DefectiveRequest(Guid orderItemPublicId, int quantity, byte[] orderRowVersion) =>
        new(
            [new CreateReturnItemLine(orderItemPublicId, quantity, "Defective", "面板有亮點")],
            "商品有瑕疵，申請退貨",
            orderRowVersion);

    [Fact]
    public async Task CreateAsync_MemberOwner_CreatesReturnWithOneItem()
    {
        var (service, store, _, orderPublicId, orderItemPublicId) = CreateSut();
        var actor = new ReturnActor("member-a", null);

        var dto = await service.CreateAsync(actor, orderPublicId, DefectiveRequest(orderItemPublicId, 1, [1, 2, 3, 4, 5, 6, 7, 8]), CancellationToken.None);

        Assert.Equal(ReturnRequestStatus.Requested, dto.Status);
        Assert.Equal("Defective", dto.ReasonCode);
        Assert.Single(dto.Items);
        Assert.Single(store.Requests);
        Assert.StartsWith("RT-20260824-", dto.ReturnNumber);
    }

    [Fact]
    public async Task CreateAsync_WhenOrderBelongsToAnotherMember_ThrowsNotFound()
    {
        var (service, _, _, orderPublicId, orderItemPublicId) = CreateSut();
        var actor = new ReturnActor("member-b", null);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.CreateAsync(actor, orderPublicId, DefectiveRequest(orderItemPublicId, 1, [1, 2, 3, 4, 5, 6, 7, 8]), CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_WhenGuestOrderIdMismatches_ThrowsNotFound()
    {
        var (service, _, _, orderPublicId, orderItemPublicId) = CreateSut();
        var actor = new ReturnActor(null, GuestOrderId: 999); // order.OrderId is 1

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.CreateAsync(actor, orderPublicId, DefectiveRequest(orderItemPublicId, 1, [1, 2, 3, 4, 5, 6, 7, 8]), CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_ValidGuestScope_Succeeds()
    {
        var (service, _, _, orderPublicId, orderItemPublicId) = CreateSut();
        var actor = new ReturnActor(null, GuestOrderId: 1);

        var dto = await service.CreateAsync(actor, orderPublicId, DefectiveRequest(orderItemPublicId, 1, [1, 2, 3, 4, 5, 6, 7, 8]), CancellationToken.None);

        Assert.Equal(ReturnRequestStatus.Requested, dto.Status);
    }

    [Fact]
    public async Task CreateAsync_WhenOrderRowVersionStale_ThrowsConcurrencyConflict()
    {
        var (service, _, _, orderPublicId, orderItemPublicId) = CreateSut();
        var actor = new ReturnActor("member-a", null);
        var staleRowVersion = new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 };

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.CreateAsync(actor, orderPublicId, DefectiveRequest(orderItemPublicId, 1, staleRowVersion), CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_WhenQuantityExceedsReturnable_ThrowsQuantityExceeded()
    {
        var (service, _, _, orderPublicId, orderItemPublicId) = CreateSut(returnableQuantity: 1);
        var actor = new ReturnActor("member-a", null);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.CreateAsync(actor, orderPublicId, DefectiveRequest(orderItemPublicId, 2, [1, 2, 3, 4, 5, 6, 7, 8]), CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ReturnQuantityExceeded, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_SecondRequest_CannotExceedRemainingAfterFirstConsumesQuantity()
    {
        var (service, _, _, orderPublicId, orderItemPublicId) = CreateSut(returnableQuantity: 2);
        var actor = new ReturnActor("member-a", null);
        await service.CreateAsync(actor, orderPublicId, DefectiveRequest(orderItemPublicId, 2, [1, 2, 3, 4, 5, 6, 7, 8]), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.CreateAsync(actor, orderPublicId, DefectiveRequest(orderItemPublicId, 1, [1, 2, 3, 4, 5, 6, 7, 8]), CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ReturnQuantityExceeded, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_WhenStoreDetectsConcurrentQuantityConflictUnderLock_MapsToStableQuantityExceededError()
    {
        // The store's pre-transaction snapshot check (CreateSut's own returnableQuantity) has
        // room, but the lock-protected re-check inside CreateWithItemsAsync represents a
        // concurrent sibling request that committed first and already consumed the budget —
        // FakeReturnStore.SimulateQuantityConflictOnNextCreate stands in for that real-SQL-Server
        // race (see the SqlServer-backed regression tests in DoSelect.Infrastructure.Tests for
        // the actual concurrent-transaction proof). This test only verifies the Application
        // layer maps ReturnQuantityConflictException to the same stable, documented error code a
        // same-request over-quantity failure gets — never a raw/internal exception.
        var (service, store, _, orderPublicId, orderItemPublicId) = CreateSut(returnableQuantity: 2);
        var actor = new ReturnActor("member-a", null);
        store.SimulateQuantityConflictOnNextCreate = true;

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.CreateAsync(actor, orderPublicId, DefectiveRequest(orderItemPublicId, 1, [1, 2, 3, 4, 5, 6, 7, 8]), CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ReturnQuantityExceeded, exception.ErrorCode);
        Assert.Empty(store.Requests);
    }

    [Fact]
    public async Task CreateAsync_CoolingOffPastDeadline_ThrowsDeadlineExpired()
    {
        var (service, _, _, orderPublicId, orderItemPublicId) = CreateSut(deliveredAtUtc: NowUtc.AddDays(-10));
        var actor = new ReturnActor("member-a", null);
        var request = new CreateReturnRequest(
            [new CreateReturnItemLine(orderItemPublicId, 1, "CoolingOff", null)], "不需要了", [1, 2, 3, 4, 5, 6, 7, 8]);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.CreateAsync(actor, orderPublicId, request, CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ReturnDeadlineExpired, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_DefectivePastCoolingOffWindow_StillAllowed()
    {
        // Defective/WrongItem/ShippingDamage/Warranty are not time-boxed by the 7-day window.
        var (service, _, _, orderPublicId, orderItemPublicId) = CreateSut(deliveredAtUtc: NowUtc.AddDays(-30));
        var actor = new ReturnActor("member-a", null);

        var dto = await service.CreateAsync(actor, orderPublicId, DefectiveRequest(orderItemPublicId, 1, [1, 2, 3, 4, 5, 6, 7, 8]), CancellationToken.None);

        Assert.Equal(ReturnRequestStatus.Requested, dto.Status);
    }

    [Fact]
    public async Task CreateAsync_MixedReasonCodesAcrossItems_ThrowsValidationFailed()
    {
        var (service, _, orderPort, orderPublicId, orderItemPublicId) = CreateSut();
        var secondItemPublicId = Guid.NewGuid();
        var order = orderPort.OrdersByPublicId[orderPublicId];
        orderPort.Register(order with
        {
            Items = [.. order.Items, new EligibleOrderItem(11, secondItemPublicId, "SKU-2", "鍵盤", 2, 0, null, false, 50m)],
        });
        var actor = new ReturnActor("member-a", null);
        var request = new CreateReturnRequest(
            [
                new CreateReturnItemLine(orderItemPublicId, 1, "Defective", null),
                new CreateReturnItemLine(secondItemPublicId, 1, "CoolingOff", null),
            ],
            "混合原因",
            [1, 2, 3, 4, 5, 6, 7, 8]);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.CreateAsync(actor, orderPublicId, request, CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_TicketNumberCollidesOnce_RetriesAndSucceeds()
    {
        var (service, store, _, orderPublicId, orderItemPublicId) = CreateSut();
        store.SimulateCollisionsRemaining = 1;
        var actor = new ReturnActor("member-a", null);

        var dto = await service.CreateAsync(actor, orderPublicId, DefectiveRequest(orderItemPublicId, 1, [1, 2, 3, 4, 5, 6, 7, 8]), CancellationToken.None);

        Assert.Single(store.Requests);
        Assert.Equal(ReturnRequestStatus.Requested, dto.Status);
    }

    [Fact]
    public async Task GetDetailAsync_WhenNotOwnedByCaller_ThrowsNotFound()
    {
        var (service, store, _, orderPublicId, orderItemPublicId) = CreateSut();
        var owner = new ReturnActor("member-a", null);
        var created = await service.CreateAsync(owner, orderPublicId, DefectiveRequest(orderItemPublicId, 1, [1, 2, 3, 4, 5, 6, 7, 8]), CancellationToken.None);

        var stranger = new ReturnActor("member-b", null);
        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.GetDetailAsync(stranger, created.PublicId, CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ResourceNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task UploadAttachmentAsync_WhenLimitReached_ThrowsFileCountExceeded()
    {
        var (service, store, _, orderPublicId, orderItemPublicId) = CreateSut();
        var actor = new ReturnActor("member-a", null);
        var created = await service.CreateAsync(actor, orderPublicId, DefectiveRequest(orderItemPublicId, 1, [1, 2, 3, 4, 5, 6, 7, 8]), CancellationToken.None);
        var requestId = store.Requests.Single().Id;
        for (var i = 0; i < 3; i++)
        {
            store.Attachments.Add(new ReturnAttachment(
                Guid.NewGuid(), requestId, "member-a", $"f{i}.pdf", $"private-files/xx/{i}.blob", "pdf", "application/pdf", 100, new byte[32], NowUtc));
        }

        var upload = new PrivateFileUpload(new MemoryStream([1, 2, 3]), "evidence.pdf", "application/pdf");
        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.UploadAttachmentAsync(actor, created.PublicId, upload, CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.FileCountExceeded, exception.ErrorCode);
    }
}
