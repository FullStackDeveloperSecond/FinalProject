using DoSelect.Application.Files;
using DoSelect.Application.Returns;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Files;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Returns;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Returns;

/// <summary>
/// Regression coverage for the P1 concurrency defect Codex flagged in
/// ReturnStore.CreateWithItemsAsync: SumActiveRequestedQuantityAsync used to run outside any
/// transaction, so two concurrent creates for the same OrderItem could both read the same
/// "remaining" quantity and both succeed, letting the active ReturnItem total exceed
/// OrderItem.ReturnableQuantity. These tests exercise the store directly against a real SQL
/// Server (two independent DbContext/Store instances, mirroring two concurrent requests) rather
/// than through ReturnService, to isolate the transaction/locking behaviour itself from
/// Application-layer orchestration (which DoSelect.Application.Tests already covers with a fake
/// store — see ReturnServiceTests.CreateAsync_WhenStoreDetectsConcurrentQuantityConflictUnderLock_MapsToStableQuantityExceededError).
/// </summary>
[Collection(nameof(ReturnStoreConcurrencyCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ReturnStoreConcurrencyTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 25, 3, 0, 0, DateTimeKind.Utc);

    [SqlServerFact]
    public async Task CreateWithItemsAsync_WhenTwoConcurrentRequestsTargetTheSameOrderItemWithOnlyOneRemaining_OnlyOneSucceeds()
    {
        long orderId;
        long orderItemId;
        await using (var seed = ReturnStoreConcurrencyFixture.CreateContext())
        {
            (orderId, orderItemId) = await SeedOrderWithItemAsync(seed, returnableQuantity: 1);
        }

        await using var contextA = ReturnStoreConcurrencyFixture.CreateContext();
        await using var contextB = ReturnStoreConcurrencyFixture.CreateContext();
        var storeA = new ReturnStore(contextA);
        var storeB = new ReturnStore(contextB);

        var budgets = new[] { new ReturnItemQuantityBudget(orderItemId, RequestedQuantity: 1, MaximumReturnableQuantity: 1) };

        var taskA = RunCreateAsync(storeA, orderId, orderItemId, "RT-CONC-A0001", budgets);
        var taskB = RunCreateAsync(storeB, orderId, orderItemId, "RT-CONC-B0001", budgets);
        var (successA, errorA) = await taskA;
        var (successB, errorB) = await taskB;

        var successes = new[] { successA, successB }.Count(s => s);
        Assert.Equal(1, successes);

        var loserError = successA ? errorB : errorA;
        var loser = Assert.IsType<ReturnQuantityConflictException>(loserError);
        Assert.Equal(orderItemId, loser.OrderItemId);

        await using var verify = ReturnStoreConcurrencyFixture.CreateContext();
        var totalActiveQuantity = await verify.ReturnItems
            .Where(i => i.OrderItemId == orderItemId)
            .Join(
                verify.ReturnRequests.Where(r =>
                    r.Status != ReturnRequestStatus.Rejected && r.Status != ReturnRequestStatus.Cancelled),
                i => i.ReturnRequestId,
                r => r.Id,
                (i, r) => i.Quantity)
            .SumAsync();
        Assert.Equal(1, totalActiveQuantity);
    }

    [SqlServerFact]
    public async Task CreateWithItemsAsync_WhenTwoConcurrentRequestsTargetDifferentOrderItems_NeitherBlocksOnTheOther()
    {
        long orderId;
        long orderItemX;
        long orderItemY;
        await using (var seed = ReturnStoreConcurrencyFixture.CreateContext())
        {
            (orderId, orderItemX) = await SeedOrderWithItemAsync(seed, returnableQuantity: 5);
            orderItemY = await SeedAdditionalItemAsync(seed, orderId, returnableQuantity: 5);
        }

        await using var contextA = ReturnStoreConcurrencyFixture.CreateContext();
        await using var contextB = ReturnStoreConcurrencyFixture.CreateContext();
        var storeA = new ReturnStore(contextA);
        var storeB = new ReturnStore(contextB);

        // A holds its transaction (and the row lock on OrderItem X) open, synchronously blocked
        // inside itemsFactory, until the test explicitly releases it. If locking were scoped too
        // broadly (e.g. a Serializable scan with no supporting index), B's create against the
        // unrelated OrderItem Y would queue behind A's open lock and this would time out.
        var lockAcquiredByA = new ManualResetEventSlim(false);
        var releaseA = new ManualResetEventSlim(false);
        var budgetsX = new[] { new ReturnItemQuantityBudget(orderItemX, RequestedQuantity: 1, MaximumReturnableQuantity: 5) };
        var budgetsY = new[] { new ReturnItemQuantityBudget(orderItemY, RequestedQuantity: 1, MaximumReturnableQuantity: 5) };

        var taskA = storeA.CreateWithItemsAsync(
            NewRequest(orderId, "RT-CONC-X0001"),
            budgetsX,
            requestId =>
            {
                lockAcquiredByA.Set();
                Assert.True(releaseA.Wait(TimeSpan.FromSeconds(10)), "Test setup failure: A was never released.");
                return [new ReturnItem(Guid.CreateVersion7(), requestId, orderItemX, 1, 0m, "NotInspected", NowUtc)];
            },
            CancellationToken.None);

        Assert.True(lockAcquiredByA.Wait(TimeSpan.FromSeconds(5)), "Task A never reached its locked section.");

        var taskB = storeB.CreateWithItemsAsync(
            NewRequest(orderId, "RT-CONC-Y0001"),
            budgetsY,
            requestId => [new ReturnItem(Guid.CreateVersion7(), requestId, orderItemY, 1, 0m, "NotInspected", NowUtc)],
            CancellationToken.None);

        var completedFirst = await Task.WhenAny(taskB, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(taskB, completedFirst);
        var resultB = await taskB;
        Assert.Single(resultB.Items);

        releaseA.Set();
        var resultA = await taskA;
        Assert.Single(resultA.Items);
    }
    [SqlServerFact]
    public async Task CreateWithItemsAsync_PersistsMaxLengthAndNullDescriptions_AndReadModelRoundTripsBoth()
    {
        long orderId;
        long describedItemId;
        long nullItemId;
        await using (var seed = ReturnStoreConcurrencyFixture.CreateContext())
        {
            (orderId, describedItemId) = await SeedOrderWithItemAsync(seed, returnableQuantity: 1);
            nullItemId = await SeedAdditionalItemAsync(seed, orderId, returnableQuantity: 1);
        }

        await using var context = ReturnStoreConcurrencyFixture.CreateContext();
        var store = new ReturnStore(context);
        var maximumDescription = new string('說', 500);
        var budgets = new[]
        {
            new ReturnItemQuantityBudget(describedItemId, RequestedQuantity: 1, MaximumReturnableQuantity: 1),
            new ReturnItemQuantityBudget(nullItemId, RequestedQuantity: 1, MaximumReturnableQuantity: 1),
        };

        var creation = await store.CreateWithItemsAsync(
            NewRequest(orderId, "RT-DESC-0001"),
            budgets,
            requestId =>
            [
                new ReturnItem(Guid.CreateVersion7(), requestId, describedItemId, 1, 0m, "NotInspected", NowUtc, maximumDescription),
                new ReturnItem(Guid.CreateVersion7(), requestId, nullItemId, 1, 0m, "NotInspected", NowUtc, description: null),
            ],
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var persisted = await context.ReturnItems
            .AsNoTracking()
            .Where(item => item.ReturnRequestId == creation.Request.Id)
            .OrderBy(item => item.OrderItemId)
            .ToListAsync();

        Assert.Equal(2, persisted.Count);
        Assert.Equal(maximumDescription, persisted.Single(item => item.OrderItemId == describedItemId).Description);
        Assert.Null(persisted.Single(item => item.OrderItemId == nullItemId).Description);

        var summaries = await store.ListItemSummariesAsync(creation.Request.Id, CancellationToken.None);
        Assert.Equal(maximumDescription, summaries.Single(item => item.Description is not null).Description);
        Assert.Null(summaries.Single(item => item.Description is null).Description);
    }

    [SqlServerFact]
    public async Task AddAttachmentAsync_GuestUploader_PersistsOrderForeignKeyWithoutApplicationUser()
    {
        long orderId;
        long orderItemId;
        await using (var seed = ReturnStoreConcurrencyFixture.CreateContext())
        {
            (orderId, orderItemId) = await SeedOrderWithItemAsync(seed, returnableQuantity: 1);
        }

        await using var context = ReturnStoreConcurrencyFixture.CreateContext();
        var store = new ReturnStore(context);
        var creation = await store.CreateWithItemsAsync(
            NewRequest(orderId, "RT-GUEST-UP01"),
            [new ReturnItemQuantityBudget(orderItemId, RequestedQuantity: 1, MaximumReturnableQuantity: 1)],
            requestId => [new ReturnItem(Guid.CreateVersion7(), requestId, orderItemId, 1, 0m, "NotInspected", NowUtc)],
            CancellationToken.None);
        var attachment = new ReturnAttachment(
            Guid.CreateVersion7(),
            creation.Request.Id,
            uploadedByUserId: null,
            uploadedByGuestOrderId: orderId,
            "guest-proof.pdf",
            $"private-files/returns/{Guid.NewGuid():N}.blob",
            "pdf",
            "application/pdf",
            3,
            new byte[32],
            NowUtc);
        attachment.RecordScan(DoSelect.Domain.Support.PrivateAttachmentScanStatus.Clean, NowUtc);

        var inserted = await store.TryAddAttachmentAsync(attachment, maxActiveAttachments: 3, CancellationToken.None);
        Assert.True(inserted);

        context.ChangeTracker.Clear();
        var persisted = await context.ReturnAttachments
            .AsNoTracking()
            .SingleAsync(candidate => candidate.PublicId == attachment.PublicId);
        Assert.Null(persisted.UploadedByUserId);
        Assert.Equal(orderId, persisted.UploadedByGuestOrderId);
    }

    /// <summary>
    /// B1 review finding: the pre-Codex-review version of this test only swapped the Store call
    /// name and asserted a bool. This exercises the real ReturnService (real disk-backed
    /// LocalPrivateFileStorage, two independent DbContext/ReturnStore/ReturnService instances —
    /// mirroring two concurrent HTTP requests), so it also proves the file-compensation path
    /// (the loser's own newly-stored blob is deleted) actually fires end to end, not just that
    /// TryAddAttachmentAsync returns the right bool.
    /// </summary>
    // Real PNG magic bytes — LocalPrivateFileStorage.StoreAsync runs a genuine format validator,
    // so an arbitrary byte array with an "image/png" content-type would be rejected as
    // FormatInvalid before ever reaching the attachment-count logic under test here.
    private static readonly byte[] ValidPngBytes = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 17, 255];

    [SqlServerFact]
    public async Task UploadAttachmentAsync_TwoConcurrentUploadsForTheLastSlot_OnlyOneSucceedsAndTheLoserFileIsDeleted()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"doselect-return-attach-race-{Guid.NewGuid():N}");
        try
        {
            await RunConcurrentAttachmentUploadRaceAsync(dataRoot);
        }
        finally
        {
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    private async Task RunConcurrentAttachmentUploadRaceAsync(string dataRoot)
    {
        const int maxAttachments = 3;
        var scanner = new AlwaysCleanFileScanner();
        var seedStorage = new LocalPrivateFileStorage(dataRoot, scanner);

        long orderId;
        long orderItemId;
        long returnRequestId;
        Guid returnPublicId;
        await using (var seed = ReturnStoreConcurrencyFixture.CreateContext())
        {
            (orderId, orderItemId) = await SeedOrderWithItemAsync(seed, returnableQuantity: 5);
            var seedStore = new ReturnStore(seed);
            var creation = await seedStore.CreateWithItemsAsync(
                NewRequest(orderId, "RT-ATTACH-RACE1"),
                [new ReturnItemQuantityBudget(orderItemId, RequestedQuantity: 1, MaximumReturnableQuantity: 5)],
                requestId => [new ReturnItem(Guid.CreateVersion7(), requestId, orderItemId, 1, 0m, "NotInspected", NowUtc)],
                CancellationToken.None);
            returnRequestId = creation.Request.Id;
            returnPublicId = creation.Request.PublicId;

            // Pre-fill two of the three slots with real, already-committed files on disk.
            for (var i = 0; i < maxAttachments - 1; i++)
            {
                var stored = await seedStorage.StoreAsync(
                    new PrivateFileUpload(new MemoryStream(ValidPngBytes), $"existing-{i}.png", "image/png"),
                    CancellationToken.None);
                Assert.True(stored.IsStored);
                var existingAttachment = new ReturnAttachment(
                    Guid.CreateVersion7(), returnRequestId, uploadedByUserId: null, uploadedByGuestOrderId: orderId,
                    stored.File!.OriginalFileName, stored.File.StorageKey, stored.File.Extension, stored.File.ContentType,
                    stored.File.FileSizeBytes, stored.File.Sha256, NowUtc);
                existingAttachment.RecordScan(DoSelect.Domain.Support.PrivateAttachmentScanStatus.Clean, NowUtc);
                // A generous cap here only seeds the fixture — it is not the cap under test.
                Assert.True(await seedStore.TryAddAttachmentAsync(existingAttachment, maxActiveAttachments: 999, CancellationToken.None));
            }
        }

        var actor = new ReturnActor(MemberUserId: null, GuestOrderId: orderId);
        await using var contextA = ReturnStoreConcurrencyFixture.CreateContext();
        await using var contextB = ReturnStoreConcurrencyFixture.CreateContext();
        var serviceA = new ReturnService(
            new ReturnStore(contextA), new ThrowingOrderEligibilityPort(), new LocalPrivateFileStorage(dataRoot, scanner),
            TimeProvider.System);
        var serviceB = new ReturnService(
            new ReturnStore(contextB), new ThrowingOrderEligibilityPort(), new LocalPrivateFileStorage(dataRoot, scanner),
            TimeProvider.System);

        var taskA = RunUploadAsync(serviceA, actor, returnPublicId, "race-a.png");
        var taskB = RunUploadAsync(serviceB, actor, returnPublicId, "race-b.png");
        var (successA, errorA) = await taskA;
        var (successB, errorB) = await taskB;

        var successes = new[] { successA, successB }.Count(s => s);
        Assert.Equal(1, successes);
        var loserError = successA ? errorB : errorA;
        var loserException = Assert.IsType<ReturnsWriteException>(loserError);
        Assert.Equal(ReturnsWriteException.ErrorCodes.FileCountExceeded, loserException.ErrorCode);

        await using var verify = ReturnStoreConcurrencyFixture.CreateContext();
        var activeCount = await verify.ReturnAttachments
            .CountAsync(a => a.ReturnRequestId == returnRequestId && a.DeletedAtUtc == null);
        Assert.Equal(maxAttachments, activeCount);

        // The loser's own newly-stored blob must be compensation-deleted: exactly `maxAttachments`
        // .blob files remain on disk (2 pre-existing + 1 winner), even though StoreAsync
        // physically wrote a file for BOTH the winner and the loser before the lock was checked.
        var remainingBlobFiles = Directory.GetFiles(dataRoot, "*.blob", SearchOption.AllDirectories);
        Assert.Equal(maxAttachments, remainingBlobFiles.Length);
    }

    private static async Task<(bool Success, Exception? Error)> RunUploadAsync(
        IReturnService service, ReturnActor actor, Guid returnPublicId, string fileName)
    {
        try
        {
            await service.UploadAttachmentAsync(
                actor, returnPublicId,
                new PrivateFileUpload(new MemoryStream(ValidPngBytes), fileName, "image/png"),
                CancellationToken.None);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex);
        }
    }

    private sealed class AlwaysCleanFileScanner : IFileScanner
    {
        public Task<FileScanResult> ScanAsync(string quarantinedFilePath, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new FileScanResult(FileScanOutcome.Clean, "test-fake", now, now));
        }
    }

    private sealed class ThrowingOrderEligibilityPort : IReturnOrderEligibilityPort
    {
        public Task<OrderEligibilitySnapshot?> FindByPublicIdAsync(Guid orderPublicId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("UploadAttachmentAsync never consults order eligibility.");

        public Task<OrderEligibilitySnapshot?> FindByIdAsync(long orderId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("UploadAttachmentAsync never consults order eligibility.");
    }


    private static async Task<(bool Success, Exception? Error)> RunCreateAsync(
        ReturnStore store, long orderId, long orderItemId, string returnNumber, IReadOnlyList<ReturnItemQuantityBudget> budgets)
    {
        try
        {
            await store.CreateWithItemsAsync(
                NewRequest(orderId, returnNumber),
                budgets,
                requestId => [new ReturnItem(Guid.CreateVersion7(), requestId, orderItemId, 1, 0m, "NotInspected", NowUtc)],
                CancellationToken.None);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex);
        }
    }

    private static ReturnRequest NewRequest(long orderId, string returnNumber) =>
        new(Guid.CreateVersion7(), returnNumber, orderId, requesterUserId: null, "Defective", "面板有亮點", policyVersion: 1, NowUtc);

    private static async Task<(long OrderId, long OrderItemId)> SeedOrderWithItemAsync(DoSelectDbContext context, int returnableQuantity)
    {
        var shippingProfile = new ShippingProviderProfile(
            Guid.CreateVersion7(), $"HOME-{Guid.NewGuid():N}"[..20], 1, "Active",
            null, null, "{}", 1, NowUtc);
        context.Set<ShippingProviderProfile>().Add(shippingProfile);
        await context.SaveChangesAsync();

        var order = Order.Create(Guid.CreateVersion7(), ValidOrderCreation(shippingProfile.Id), NowUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var item = new OrderItem(
            Guid.CreateVersion7(), order.Id, skuId: null, "SKU-1", "27型螢幕", "27型螢幕 White",
            quantity: returnableQuantity, listUnitPrice: 100m, saleUnitPrice: 100m, finalUnitPrice: 100m,
            unitCostSnapshot: 60m, lineSubtotal: 100m * returnableQuantity, discountAllocation: 0m,
            lineTotal: 100m * returnableQuantity, assemblyGroupKey: null, returnableQuantity: returnableQuantity, NowUtc, isCouponEligible: true);
        context.OrderItems.Add(item);
        await context.SaveChangesAsync();

        return (order.Id, item.Id);
    }

    private static async Task<long> SeedAdditionalItemAsync(DoSelectDbContext context, long orderId, int returnableQuantity)
    {
        var item = new OrderItem(
            Guid.CreateVersion7(), orderId, skuId: null, "SKU-2", "機械式鍵盤", "機械式鍵盤 87key",
            quantity: returnableQuantity, listUnitPrice: 80m, saleUnitPrice: 80m, finalUnitPrice: 80m,
            unitCostSnapshot: 40m, lineSubtotal: 80m * returnableQuantity, discountAllocation: 0m,
            lineTotal: 80m * returnableQuantity, assemblyGroupKey: null, returnableQuantity: returnableQuantity, NowUtc, isCouponEligible: true);
        context.OrderItems.Add(item);
        await context.SaveChangesAsync();
        return item.Id;
    }

    private static OrderCreation ValidOrderCreation(long shippingProviderProfileId) =>
        new(
            $"DS{Guid.NewGuid():N}"[..15],
            null,
            $"{Guid.NewGuid():N}@doselect.test",
            OrderStatus.Processing,
            PaymentStatus.Paid,
            FulfillmentStatus.Delivered,
            AssemblyStatus.NotRequired,
            1_200m,
            100m,
            225m,
            0m,
            1_325m,
            "Guest",
            "0912345678",
            "guest@example.com",
            "100",
            "Taipei",
            "Zhongzheng",
            "No. 1",
            null,
            "HOME_DELIVERY",
            shippingProviderProfileId,
            null,
            null,
            null,
            1,
            1,
            null,
            null,
            $"checkout-{Guid.NewGuid():N}",
            null);
}

public sealed class ReturnStoreConcurrencyFixture
{
    public const string ConnectionStringEnvironmentVariable = "DOSELECT_SQLSERVER_TEST_CONNECTION";
    private const string LocalConnectionString =
        "Server=.\\SQL2025;Database=DoSelectReturnConcurrencyTests;Trusted_Connection=True;TrustServerCertificate=True;";
    private static readonly string IsolatedConnectionString = BuildIsolatedConnectionString();

    public static bool IsEnabled =>
        !string.IsNullOrWhiteSpace(GetConfiguredConnectionString()) ||
        (OperatingSystem.IsWindows() &&
         !string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase));

    public static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(IsolatedConnectionString)
            .Options);

    private static string BuildIsolatedConnectionString() => new SqlConnectionStringBuilder(
        GetConfiguredConnectionString() ?? LocalConnectionString)
    {
        InitialCatalog = $"DoSelectReturnConcurrencyTests_{Guid.NewGuid():N}",
    }.ConnectionString;

    private static string? GetConfiguredConnectionString() =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
}

/// <summary>Ensures a freshly (re)created database exists before the first test runs and is
/// dropped afterward — a lightweight collection fixture rather than IdempotencyExecutorFixture's
/// per-class one, since every test in this file shares the same disposable database.</summary>
[CollectionDefinition(nameof(ReturnStoreConcurrencyCollection))]
public sealed class ReturnStoreConcurrencyCollection : ICollectionFixture<ReturnStoreConcurrencyDatabaseLifetime>;

public sealed class ReturnStoreConcurrencyDatabaseLifetime : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        if (!ReturnStoreConcurrencyFixture.IsEnabled)
        {
            return;
        }

        await using var context = ReturnStoreConcurrencyFixture.CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (!ReturnStoreConcurrencyFixture.IsEnabled)
        {
            return;
        }

        await using var context = ReturnStoreConcurrencyFixture.CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!ReturnStoreConcurrencyFixture.IsEnabled)
        {
            Skip = $"Set {ReturnStoreConcurrencyFixture.ConnectionStringEnvironmentVariable} to run SQL Server integration tests.";
        }
    }
}
