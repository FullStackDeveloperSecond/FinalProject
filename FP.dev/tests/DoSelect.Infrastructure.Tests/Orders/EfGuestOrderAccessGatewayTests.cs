using DoSelect.Application.Auditing;
using DoSelect.Application.Orders;
using DoSelect.Application.Outbox;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Persistence.Orders;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Orders;

public sealed class EfGuestOrderAccessGatewayTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        global::DoSelect.Infrastructure.Tests.SqlServerTestConnection.Build(
            "DoSelectEfGuestOrderAccessGatewayTests");

    private static readonly DateTime CreatedAtUtc = new(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task FindGuestOrderAsync_WhenGuestOrderMatches_ReturnsLookup()
    {
        var orderPublicId = Guid.CreateVersion7();
        await SeedOrderAsync(orderPublicId, "DS-0001", "GUEST@EXAMPLE.COM");

        await using var context = CreateContext();
        var gateway = new EfGuestOrderAccessGateway(context);

        var lookup = await gateway.FindGuestOrderAsync("DS-0001", "GUEST@EXAMPLE.COM");

        Assert.NotNull(lookup);
        Assert.Equal(orderPublicId, lookup!.OrderPublicId);
    }

    [Fact]
    public async Task FindGuestOrderAsync_WhenOrderIsMemberOwned_ReturnsNull()
    {
        // 會員訂單（GuestEmailNormalized 為 null）不得透過訪客查單流程比對到——即使
        // 訂單編號相符，會員應改用登入身分查詢。
        await SeedMemberOrderAsync("DS-0002");

        await using var context = CreateContext();
        var gateway = new EfGuestOrderAccessGateway(context);

        var lookup = await gateway.FindGuestOrderAsync("DS-0002", "GUEST@EXAMPLE.COM");

        Assert.Null(lookup);
    }

    [Fact]
    public async Task FindGuestOrderAsync_WhenEmailDoesNotMatch_ReturnsNull()
    {
        await SeedOrderAsync(Guid.CreateVersion7(), "DS-0003", "GUEST@EXAMPLE.COM");

        await using var context = CreateContext();
        var gateway = new EfGuestOrderAccessGateway(context);

        var lookup = await gateway.FindGuestOrderAsync("DS-0003", "SOMEONE-ELSE@EXAMPLE.COM");

        Assert.Null(lookup);
    }

    [Fact]
    public async Task SeededRequest_ThenFindActiveRequestAsync_RoundTrips()
    {
        var orderId = await SeedOrderAsync(Guid.CreateVersion7(), "DS-0004", "GUEST@EXAMPLE.COM");
        var requestPublicId = Guid.CreateVersion7();

        await using (var context = CreateContext())
        {
            var request = GuestOrderAccessRequest.CreateValid(
                requestPublicId, orderId, Hash(1), Hash(2), Hash(3), Hash(4),
                CreatedAtUtc.AddMinutes(10), CreatedAtUtc);
            context.GuestOrderAccessRequests.Add(request);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var gateway = new EfGuestOrderAccessGateway(context);
            var found = await gateway.FindActiveRequestAsync(
                requestPublicId, CreatedAtUtc.AddMinutes(1));

            Assert.NotNull(found);
            Assert.Equal(orderId, found!.OrderId);
        }
    }

    [Fact]
    public async Task FindActiveRequestAsync_WhenExpired_ReturnsNull()
    {
        var orderId = await SeedOrderAsync(Guid.CreateVersion7(), "DS-0005", "GUEST@EXAMPLE.COM");
        var requestPublicId = Guid.CreateVersion7();

        await using (var context = CreateContext())
        {
            var request = GuestOrderAccessRequest.CreateValid(
                requestPublicId, orderId, Hash(1), Hash(2), Hash(3), Hash(4),
                CreatedAtUtc.AddMinutes(10), CreatedAtUtc);
            context.GuestOrderAccessRequests.Add(request);
            await context.SaveChangesAsync();
        }

        await using var verifyContext = CreateContext();
        var verifyGateway = new EfGuestOrderAccessGateway(verifyContext);
        var found = await verifyGateway.FindActiveRequestAsync(
            requestPublicId, CreatedAtUtc.AddMinutes(11));

        Assert.Null(found);
    }

    [Fact]
    public async Task TryCreateRequestWithinRateLimitAsync_WhenWithinLimit_CreatesInitialRequest()
    {
        var orderId = await SeedOrderAsync(Guid.CreateVersion7(), "DS-0009", "GUEST@EXAMPLE.COM");
        var requestPublicId = Guid.CreateVersion7();

        await using var context = CreateContext();
        var gateway = new EfGuestOrderAccessGateway(context);
        var request = GuestOrderAccessRequest.CreateValid(
            requestPublicId, orderId, Hash(1), Hash(2), Hash(3), Hash(4),
            CreatedAtUtc.AddMinutes(10), CreatedAtUtc);
        request.RecordSend(CreatedAtUtc);

        var created = await gateway.TryCreateRequestWithinRateLimitAsync(
            DefaultWindow(Hash(2), Hash(3), Hash(4), CreatedAtUtc.AddMinutes(-14)),
            request,
            CreateNotification(requestPublicId, sendNumber: 1));

        Assert.True(created);

        await using var verifyContext = CreateContext();
        var persisted = await verifyContext.GuestOrderAccessRequests.SingleAsync();
        Assert.Equal(requestPublicId, persisted.PublicId);
        Assert.Equal(1, persisted.SendCount);
        var outbox = await verifyContext.OutboxMessages.SingleAsync();
        Assert.Equal(requestPublicId, outbox.AggregatePublicId);
    }

    [Fact]
    public async Task TryCreateRequestWithinRateLimitAsync_WhenScopeExceedsLimit_DoesNotCreateRequest()
    {
        var orderId = await SeedOrderAsync(Guid.CreateVersion7(), "DS-0010", "GUEST@EXAMPLE.COM");

        await using var context = CreateContext();
        var gateway = new EfGuestOrderAccessGateway(context);
        var existing = GuestOrderAccessRequest.CreateValid(
            Guid.CreateVersion7(), orderId, Hash(1), Hash(2), Hash(3), Hash(4),
            CreatedAtUtc.AddMinutes(10), CreatedAtUtc);
        context.GuestOrderAccessRequests.Add(existing);
        await context.SaveChangesAsync();

        var newRequest = GuestOrderAccessRequest.CreateValid(
            Guid.CreateVersion7(), orderId, Hash(5), Hash(2), Hash(3), Hash(4),
            CreatedAtUtc.AddMinutes(10), CreatedAtUtc.AddMinutes(1));

        // IP 上限設成 0；任一 Scope 超限就不建立新 Row。
        var window = DefaultWindow(Hash(2), Hash(3), Hash(4), CreatedAtUtc.AddMinutes(-14)) with
        {
            IpPermitLimit = 0,
        };

        var created = await gateway.TryCreateRequestWithinRateLimitAsync(
            window, newRequest, CreateNotification(newRequest.PublicId, sendNumber: 1));

        Assert.False(created);

        await using var verifyContext = CreateContext();
        Assert.Equal(1, await verifyContext.GuestOrderAccessRequests.CountAsync());
        Assert.Empty(await verifyContext.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task TryRecordResendWithinRateLimitAsync_UpdatesStableRequestAndAddsRateLimitEvent()
    {
        var orderId = await SeedOrderAsync(Guid.CreateVersion7(), "DS-0011", "GUEST@EXAMPLE.COM");
        var requestPublicId = Guid.CreateVersion7();
        var eventPublicId = Guid.CreateVersion7();
        var expiresAtUtc = CreatedAtUtc.AddMinutes(10);

        await using var context = CreateContext();
        var gateway = new EfGuestOrderAccessGateway(context);
        var request = GuestOrderAccessRequest.CreateValid(
            requestPublicId, orderId, Hash(1), Hash(2), Hash(3), Hash(4),
            expiresAtUtc, CreatedAtUtc);
        request.RecordSend(CreatedAtUtc);
        context.GuestOrderAccessRequests.Add(request);
        await context.SaveChangesAsync();

        var sentAtUtc = CreatedAtUtc.AddMinutes(1);
        var rateLimitEvent = GuestOrderAccessRequest.CreateResendRateLimitEvent(
            eventPublicId, Hash(9), Hash(3), Hash(4), expiresAtUtc, sentAtUtc);
        var recorded = await gateway.TryRecordResendWithinRateLimitAsync(
            DefaultWindow(Hash(9), Hash(3), Hash(4), CreatedAtUtc.AddMinutes(-14)),
            request,
            rateLimitEvent,
            Hash(5),
            sentAtUtc,
            CreateNotification(requestPublicId, sendNumber: 2));

        Assert.True(recorded);

        await using var verifyContext = CreateContext();
        var persistedRequest = await verifyContext.GuestOrderAccessRequests
            .SingleAsync(r => r.PublicId == requestPublicId);
        Assert.Equal(Hash(5), persistedRequest.CodeHash);
        var outbox = await verifyContext.OutboxMessages.SingleAsync();
        Assert.Equal(requestPublicId, outbox.AggregatePublicId);
        Assert.Equal(2, persistedRequest.SendCount);
        Assert.Null(persistedRequest.RevokedAtUtc);

        var persistedEvent = await verifyContext.GuestOrderAccessRequests
            .SingleAsync(r => r.PublicId == eventPublicId);
        Assert.Null(persistedEvent.OrderId);
        Assert.Null(persistedEvent.CodeHash);
        Assert.NotNull(persistedEvent.RevokedAtUtc);
        Assert.Equal(Hash(9), persistedEvent.RequesterIpHash);
        Assert.Equal(Hash(3), persistedEvent.EmailKeyHash);
        Assert.Equal(Hash(4), persistedEvent.OrderLookupKeyHash);
    }

    [Fact]
    public async Task TryRecordResendWithinRateLimitAsync_WhenScopeExceedsLimit_ChangesNothing()
    {
        var orderId = await SeedOrderAsync(Guid.CreateVersion7(), "DS-0012", "GUEST@EXAMPLE.COM");
        var requestPublicId = Guid.CreateVersion7();
        var expiresAtUtc = CreatedAtUtc.AddMinutes(10);

        await using var context = CreateContext();
        var gateway = new EfGuestOrderAccessGateway(context);
        var request = GuestOrderAccessRequest.CreateValid(
            requestPublicId, orderId, Hash(1), Hash(2), Hash(3), Hash(4),
            expiresAtUtc, CreatedAtUtc);
        request.RecordSend(CreatedAtUtc);
        context.GuestOrderAccessRequests.Add(request);
        await context.SaveChangesAsync();

        var sentAtUtc = CreatedAtUtc.AddMinutes(1);
        var eventPublicId = Guid.CreateVersion7();
        var rateLimitEvent = GuestOrderAccessRequest.CreateResendRateLimitEvent(
            eventPublicId, Hash(9), Hash(3), Hash(4), expiresAtUtc, sentAtUtc);
        var window = DefaultWindow(Hash(9), Hash(3), Hash(4), CreatedAtUtc.AddMinutes(-14)) with
        {
            EmailPermitLimit = 0,
        };

        var recorded = await gateway.TryRecordResendWithinRateLimitAsync(
            window, request, rateLimitEvent, Hash(5), sentAtUtc, notification: null);

        Assert.False(recorded);

        await using var verifyContext = CreateContext();
        var persistedRequest = await verifyContext.GuestOrderAccessRequests.SingleAsync();
        Assert.Equal(requestPublicId, persistedRequest.PublicId);
        Assert.Equal(Hash(1), persistedRequest.CodeHash);
        Assert.Equal(1, persistedRequest.SendCount);
        Assert.False(await verifyContext.GuestOrderAccessRequests.AnyAsync(r => r.PublicId == eventPublicId));
    }

    [Fact]
    public async Task TryRecordResendWithinRateLimitAsync_WhenEventInsertFails_RollsBackRequestUpdate()
    {
        var orderId = await SeedOrderAsync(Guid.CreateVersion7(), "DS-0013", "GUEST@EXAMPLE.COM");
        var requestPublicId = Guid.CreateVersion7();
        var expiresAtUtc = CreatedAtUtc.AddMinutes(10);

        await using var context = CreateContext();
        var gateway = new EfGuestOrderAccessGateway(context);
        var request = GuestOrderAccessRequest.CreateValid(
            requestPublicId, orderId, Hash(1), Hash(2), Hash(3), Hash(4),
            expiresAtUtc, CreatedAtUtc);
        request.RecordSend(CreatedAtUtc);
        context.GuestOrderAccessRequests.Add(request);
        await context.SaveChangesAsync();

        var sentAtUtc = CreatedAtUtc.AddMinutes(1);
        var duplicatePublicIdEvent = GuestOrderAccessRequest.CreateResendRateLimitEvent(
            requestPublicId, Hash(9), Hash(3), Hash(4), expiresAtUtc, sentAtUtc);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            gateway.TryRecordResendWithinRateLimitAsync(
                DefaultWindow(Hash(9), Hash(3), Hash(4), CreatedAtUtc.AddMinutes(-14)),
                request,
                duplicatePublicIdEvent,
                Hash(5),
                sentAtUtc,
                CreateNotification(requestPublicId, sendNumber: 2)));

        await using var verifyContext = CreateContext();
        var persistedRequest = await verifyContext.GuestOrderAccessRequests.SingleAsync();
        Assert.Equal(requestPublicId, persistedRequest.PublicId);
        Assert.Equal(Hash(1), persistedRequest.CodeHash);
        Assert.Equal(1, persistedRequest.SendCount);
        Assert.Empty(await verifyContext.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task TryRecordUnknownResendAttemptAsync_WhenWithinLimit_PersistsSentinelRowForFutureCalls()
    {
        // review #1：查無 PublicId／已完全失效的 Resend 呼叫必須「持久消耗」IP Scope，
        // 不能只唯讀計數——這裡驗證寫入的哨兵 Row 之後真的會被下一次呼叫數到。
        await using var context = CreateContext();
        var gateway = new EfGuestOrderAccessGateway(context);
        var windowStartUtc = CreatedAtUtc.AddMinutes(-14);

        var firstSentinel = GuestOrderAccessRequest.CreateUnknownResendAttempt(
            Guid.CreateVersion7(), Hash(7), CreatedAtUtc.AddMinutes(10), CreatedAtUtc);
        var firstAllowed = await gateway.TryRecordUnknownResendAttemptAsync(
            Hash(7), ipPermitLimit: 1, windowStartUtc, firstSentinel);

        Assert.True(firstAllowed);

        var secondSentinel = GuestOrderAccessRequest.CreateUnknownResendAttempt(
            Guid.CreateVersion7(), Hash(7), CreatedAtUtc.AddMinutes(10), CreatedAtUtc.AddSeconds(1));
        var secondAllowed = await gateway.TryRecordUnknownResendAttemptAsync(
            Hash(7), ipPermitLimit: 1, windowStartUtc, secondSentinel);

        // 上限 1，第一筆已經用掉唯一名額——第二筆同一個 IP Hash 必須被擋下，
        // 不是像唯讀計數那樣永遠通過。
        Assert.False(secondAllowed);

        await using var verifyContext = CreateContext();
        Assert.Equal(1, await verifyContext.GuestOrderAccessRequests.CountAsync());
    }

    [Fact]
    public async Task TryRecordUnknownResendAttemptAsync_UsesUnknownScopeHash_NotRealEmailOrOrderLookupHash()
    {
        // 哨兵 Row 沒有真實 Email／OrderLookup Hash 可用，必須填固定哨兵值——不能拿目前呼叫者
        // IP 之外的任何真實資料湊數，否則會污染其他訪客的 Email／OrderLookup 視窗計數。
        await using var context = CreateContext();
        var gateway = new EfGuestOrderAccessGateway(context);
        var sentinel = GuestOrderAccessRequest.CreateUnknownResendAttempt(
            Guid.CreateVersion7(), Hash(7), CreatedAtUtc.AddMinutes(10), CreatedAtUtc);

        await gateway.TryRecordUnknownResendAttemptAsync(
            Hash(7), ipPermitLimit: 10, CreatedAtUtc.AddMinutes(-14), sentinel);

        await using var verifyContext = CreateContext();
        var persisted = await verifyContext.GuestOrderAccessRequests.SingleAsync();
        Assert.Equal(GuestOrderAccessRequest.UnknownScopeHash, persisted.EmailKeyHash);
        Assert.Equal(GuestOrderAccessRequest.UnknownScopeHash, persisted.OrderLookupKeyHash);
        Assert.Null(persisted.OrderId);
        Assert.Null(persisted.CodeHash);
    }

    private static GuestOrderAccessRateLimitWindow DefaultWindow(
        byte[] ipHash, byte[] emailHash, byte[] orderLookupHash, DateTime windowStartUtc) =>
        new(ipHash, 10, emailHash, 5, orderLookupHash, 5, windowStartUtc);

    [Fact]
    public async Task AddTokenAsync_ThenFindTokenByHashAsync_ReturnsContextWithOrderPublicId()
    {
        var orderPublicId = Guid.CreateVersion7();
        var orderId = await SeedOrderAsync(orderPublicId, "DS-0006", "GUEST@EXAMPLE.COM");
        var tokenHash = Hash(9);

        await using (var context = CreateContext())
        {
            var gateway = new EfGuestOrderAccessGateway(context);
            var request = GuestOrderAccessRequest.CreateValid(
                Guid.CreateVersion7(), orderId, Hash(1), Hash(2), Hash(3), Hash(4),
                CreatedAtUtc.AddMinutes(10), CreatedAtUtc);
            context.GuestOrderAccessRequests.Add(request);
            await context.SaveChangesAsync();

            var token = new GuestOrderAccessToken(
                Guid.CreateVersion7(), orderId, request.Id, tokenHash,
                CreatedAtUtc.AddMinutes(30), CreatedAtUtc);
            await gateway.AddTokenAsync(token);
            await gateway.SaveChangesAsync();
        }

        await using var verifyContext = CreateContext();
        var verifyGateway = new EfGuestOrderAccessGateway(verifyContext);
        var found = await verifyGateway.FindTokenByHashAsync(tokenHash);

        Assert.NotNull(found);
        Assert.Equal(orderPublicId, found!.OrderPublicId);
    }

    [Fact]
    public async Task FindTokenByHashAsync_WhenHashDoesNotMatch_ReturnsNull()
    {
        await using var context = CreateContext();
        var gateway = new EfGuestOrderAccessGateway(context);

        var found = await gateway.FindTokenByHashAsync(Hash(255));

        Assert.Null(found);
    }

    [Fact]
    public async Task RecordScopeViolationAsync_PersistsCounterAndAuditInOneTransaction()
    {
        var orderPublicId = Guid.CreateVersion7();
        var orderId = await SeedOrderAsync(orderPublicId, "DS-0009", "GUEST@EXAMPLE.COM");
        var (tokenId, tokenPublicId) = await SeedTokenAsync(orderId);
        var targetOrderPublicId = Guid.CreateVersion7();

        await using (var context = CreateContext())
        {
            var gateway = new EfGuestOrderAccessGateway(
                context,
                new EfAuditWriter(context, TimeProvider.System));

            await gateway.RecordScopeViolationAsync(
                tokenId,
                CreateScopeViolationAudit(tokenPublicId, targetOrderPublicId));
        }

        await using var verifyContext = CreateContext();
        var token = await verifyContext.GuestOrderAccessTokens.SingleAsync(t => t.Id == tokenId);
        var audit = await verifyContext.AuditLogs.SingleAsync();
        Assert.Equal(1, token.ScopeViolationCount);
        Assert.Equal(AuditActions.GuestOrderScopeViolation, audit.Action);
        Assert.Equal(tokenPublicId, audit.ActorPublicId);
        Assert.Equal(targetOrderPublicId, audit.ResourcePublicId);
    }

    [Fact]
    public async Task RecordScopeViolationAsync_WhenAuditWriterFails_RollsBackCounter()
    {
        var orderPublicId = Guid.CreateVersion7();
        var orderId = await SeedOrderAsync(orderPublicId, "DS-0010", "GUEST@EXAMPLE.COM");
        var (tokenId, tokenPublicId) = await SeedTokenAsync(orderId);

        await using (var context = CreateContext())
        {
            var gateway = new EfGuestOrderAccessGateway(context, new ThrowingAuditWriter());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                gateway.RecordScopeViolationAsync(
                    tokenId,
                    CreateScopeViolationAudit(tokenPublicId, Guid.CreateVersion7())));
        }

        await using var verifyContext = CreateContext();
        var token = await verifyContext.GuestOrderAccessTokens.SingleAsync(t => t.Id == tokenId);
        Assert.Equal(0, token.ScopeViolationCount);
        Assert.Empty(verifyContext.AuditLogs);
    }

    [Fact]
    public async Task PurgeExpiredAsync_OnlyDeletesRowsPastCutoffAndRespectsBatchSize()
    {
        var orderId = await SeedOrderAsync(Guid.CreateVersion7(), "DS-0007", "GUEST@EXAMPLE.COM");

        await using (var context = CreateContext())
        {
            for (var i = 0; i < 3; i++)
            {
                var expired = GuestOrderAccessRequest.CreateDecoy(
                    Guid.CreateVersion7(), Hash(1), Hash(2), Hash(3),
                    CreatedAtUtc.AddMinutes(10), CreatedAtUtc);
                context.GuestOrderAccessRequests.Add(expired);
            }

            var stillFresh = GuestOrderAccessRequest.CreateValid(
                Guid.CreateVersion7(), orderId, Hash(1), Hash(2), Hash(3), Hash(4),
                CreatedAtUtc.AddDays(60), CreatedAtUtc.AddDays(59));
            context.GuestOrderAccessRequests.Add(stillFresh);
            await context.SaveChangesAsync();
        }

        var cutoffUtc = CreatedAtUtc.AddMinutes(10).AddDays(30).AddSeconds(1);

        await using (var context = CreateContext())
        {
            var gateway = new EfGuestOrderAccessGateway(context);
            var firstBatchDeleted = await gateway.PurgeExpiredAsync(cutoffUtc, batchSize: 2);
            Assert.Equal(2, firstBatchDeleted);

            var secondBatchDeleted = await gateway.PurgeExpiredAsync(cutoffUtc, batchSize: 2);
            Assert.Equal(1, secondBatchDeleted);

            var thirdBatchDeleted = await gateway.PurgeExpiredAsync(cutoffUtc, batchSize: 2);
            Assert.Equal(0, thirdBatchDeleted);
        }

        await using var verifyContext = CreateContext();
        Assert.Equal(1, await verifyContext.GuestOrderAccessRequests.CountAsync());
    }

    [Fact]
    public async Task PurgeExpiredAsync_WhenRequestsAndTokensAreBothExpired_CapsCombinedDeletesAtBatchSize()
    {
        // DEC-P267：每批最多 500 筆，是 Request＋Token「合計」的上限，不是各自 500。
        // 這裡用 batchSize=3、2 個過期 Token＋2 個（沒有 Token 的）過期 Request，
        // 驗證第一批只花掉 Token 的 2 筆額度後,剩下 1 筆額度只夠刪 1 個 Request，
        // 不會變成 2+2=4 筆。
        var orderId = await SeedOrderAsync(Guid.CreateVersion7(), "DS-0008", "GUEST@EXAMPLE.COM");

        await using (var context = CreateContext())
        {
            var gateway = new EfGuestOrderAccessGateway(context);

            for (var i = 0; i < 2; i++)
            {
                var expiredWithToken = GuestOrderAccessRequest.CreateValid(
                    Guid.CreateVersion7(), orderId, Hash(1), Hash(2), Hash(3), Hash(4),
                    CreatedAtUtc.AddMinutes(10), CreatedAtUtc);
                context.GuestOrderAccessRequests.Add(expiredWithToken);
                await context.SaveChangesAsync();

                var expiredToken = new GuestOrderAccessToken(
                    Guid.CreateVersion7(), orderId, expiredWithToken.Id, Hash((byte)(10 + i)),
                    CreatedAtUtc.AddMinutes(30), CreatedAtUtc);
                await gateway.AddTokenAsync(expiredToken);
                await gateway.SaveChangesAsync();
            }

            for (var i = 0; i < 2; i++)
            {
                var expiredWithoutToken = GuestOrderAccessRequest.CreateDecoy(
                    Guid.CreateVersion7(), Hash(1), Hash(2), Hash(3),
                    CreatedAtUtc.AddMinutes(10), CreatedAtUtc);
                context.GuestOrderAccessRequests.Add(expiredWithoutToken);
            }

            await context.SaveChangesAsync();
        }

        var cutoffUtc = CreatedAtUtc.AddDays(31);

        await using (var context = CreateContext())
        {
            var gateway = new EfGuestOrderAccessGateway(context);

            var firstBatchDeleted = await gateway.PurgeExpiredAsync(cutoffUtc, batchSize: 3);
            Assert.Equal(3, firstBatchDeleted);

            var secondBatchDeleted = await gateway.PurgeExpiredAsync(cutoffUtc, batchSize: 3);
            Assert.Equal(3, secondBatchDeleted);

            var thirdBatchDeleted = await gateway.PurgeExpiredAsync(cutoffUtc, batchSize: 3);
            Assert.Equal(0, thirdBatchDeleted);
        }

        await using var verifyContext = CreateContext();
        Assert.Equal(0, await verifyContext.GuestOrderAccessTokens.CountAsync());
        Assert.Equal(0, await verifyContext.GuestOrderAccessRequests.CountAsync());
    }

    private async Task<long> SeedOrderAsync(Guid publicId, string orderNumber, string guestEmailNormalized)
    {
        await using var context = CreateContext();
        var (shippingProfileId, packageLimitId) =
            await SeedShippingProviderProfileAsync(context, orderNumber);
        var order = Order.Create(
            publicId,
            ValidOrderCreation(orderNumber, guestEmailNormalized) with
            {
                ShippingProviderProfileVersionId = shippingProfileId,
                PackageSnapshot = TestPackageSnapshot(packageLimitId),
            },
            CreatedAtUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private static async Task<(long TokenId, Guid TokenPublicId)> SeedTokenAsync(long orderId)
    {
        await using var context = CreateContext();
        var request = GuestOrderAccessRequest.CreateValid(
            Guid.CreateVersion7(), orderId, Hash(1), Hash(2), Hash(3), Hash(4),
            CreatedAtUtc.AddMinutes(10), CreatedAtUtc);
        context.GuestOrderAccessRequests.Add(request);
        await context.SaveChangesAsync();

        var tokenPublicId = Guid.CreateVersion7();
        var token = new GuestOrderAccessToken(
            tokenPublicId,
            orderId,
            request.Id,
            Hash(9),
            CreatedAtUtc.AddMinutes(30),
            CreatedAtUtc);
        context.GuestOrderAccessTokens.Add(token);
        await context.SaveChangesAsync();
        return (token.Id, tokenPublicId);
    }

    private static AuditWriteRequest CreateScopeViolationAudit(
        Guid tokenPublicId,
        Guid targetOrderPublicId) =>
        AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            AuditActor.Create(AuditActorType.Guest, tokenPublicId, roles: []),
            AuditActions.GuestOrderScopeViolation,
            AuditResourceTypes.Order,
            targetOrderPublicId,
            AuditResult.Rejected,
            "guest_order_scope_mismatch",
            [AuditFieldChange.Changed("scopeViolationCount")],
            "cross_order_access_rejected",
            "guest-order-test",
            "0123456789abcdef0123456789abcdef",
            jobPublicId: null,
            remoteIpAddress: null);

    private async Task SeedMemberOrderAsync(string orderNumber)
    {
        await using var context = CreateContext();
        var (shippingProfileId, packageLimitId) =
            await SeedShippingProviderProfileAsync(context, orderNumber);
        var member = ApplicationUser.CreateMember(Guid.CreateVersion7(), $"{orderNumber}@example.com", CreatedAtUtc);
        context.Users.Add(member);
        await context.SaveChangesAsync();

        var creation = ValidOrderCreation(orderNumber, null) with
        {
            MemberUserId = member.Id,
            ShippingProviderProfileVersionId = shippingProfileId,
            PackageSnapshot = TestPackageSnapshot(packageLimitId),
        };
        var order = Order.Create(Guid.CreateVersion7(), creation, CreatedAtUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
    }

    private static async Task<(long ProfileId, long PackageLimitId)> SeedShippingProviderProfileAsync(
        DoSelectDbContext context, string discriminator)
    {
        var profile = new ShippingProviderProfile(
            Guid.CreateVersion7(), $"SHIP-{discriminator}", 1, "Active", null, null, "{}", 1, CreatedAtUtc);
        context.ShippingProviderProfiles.Add(profile);
        await context.SaveChangesAsync();
        var packageLimit = new PackageLimitVersion(
            Guid.CreateVersion7(), profile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m,
            null, null, CreatedAtUtc);
        context.PackageLimitVersions.Add(packageLimit);
        await context.SaveChangesAsync();
        return (profile.Id, packageLimit.Id);
    }

    private static OrderCreation ValidOrderCreation(string orderNumber, string? guestEmailNormalized) =>
        new(
            orderNumber,
            null,
            guestEmailNormalized,
            OrderStatus.PendingPayment,
            PaymentStatus.AwaitingPayment,
            FulfillmentStatus.Pending,
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
            1,
            null,
            null,
            null,
            1,
            1,
            null,
            CreatedAtUtc.AddDays(3),
            $"checkout-{orderNumber}",
            null,
            1,
            1,
            new OrderInvoicePreference(
                SimulatedInvoiceBuyerType.Individual,
                "guest@example.com",
                null,
                null,
                null,
                null),
            null,
            null,
            TestPackageSnapshot(1));

    private static OrderPackageSnapshot TestPackageSnapshot(long packageLimitId) =>
        new(packageLimitId, 1m, 40m, 30m, 20m, 90m, 1_200m);

    private static OutboxWriteRequest CreateNotification(Guid requestPublicId, int sendNumber)
    {
        var notificationPublicId = Guid.CreateVersion7();
        return OutboxWriteRequest.Create(
            notificationPublicId,
            GuestOrderAccessNotificationContract.ResourceType,
            requestPublicId,
            new EmailNotificationRequestedV1(
                notificationPublicId,
                GuestOrderAccessNotificationContract.TemplateKey,
                GuestOrderAccessNotificationContract.RecipientPurpose,
                GuestOrderAccessNotificationContract.ResourceType,
                requestPublicId,
                GuestOrderAccessNotificationContract.Locale,
                sendNumber),
            CreatedAtUtc,
            CreatedAtUtc,
            requestPublicId.ToString("N"));
    }

    private static byte[] Hash(byte fill) => Enumerable.Repeat(fill, 32).ToArray();

    private static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>().UseSqlServer(ConnectionString).Options);

    private sealed class ThrowingAuditWriter : IAuditWriter
    {
        public AuditLog Add(AuditWriteRequest request) =>
            throw new InvalidOperationException("Synthetic audit failure.");
    }
}
