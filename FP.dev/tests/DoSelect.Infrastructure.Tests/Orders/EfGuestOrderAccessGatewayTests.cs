using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Persistence.Orders;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Orders;

public sealed class EfGuestOrderAccessGatewayTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=.\\SQL2025;Database=DoSelectEfGuestOrderAccessGatewayTests;Trusted_Connection=True;" +
        "TrustServerCertificate=True;";

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
    public async Task AddRequestAsync_ThenFindActiveRequestAsync_RoundTrips()
    {
        var orderId = await SeedOrderAsync(Guid.CreateVersion7(), "DS-0004", "GUEST@EXAMPLE.COM");
        var requestPublicId = Guid.CreateVersion7();

        await using (var context = CreateContext())
        {
            var gateway = new EfGuestOrderAccessGateway(context);
            var request = GuestOrderAccessRequest.CreateValid(
                requestPublicId, orderId, Hash(1), Hash(2), Hash(3), Hash(4),
                CreatedAtUtc.AddMinutes(10), CreatedAtUtc);
            await gateway.AddRequestAsync(request);
            await gateway.SaveChangesAsync();
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
            var gateway = new EfGuestOrderAccessGateway(context);
            var request = GuestOrderAccessRequest.CreateValid(
                requestPublicId, orderId, Hash(1), Hash(2), Hash(3), Hash(4),
                CreatedAtUtc.AddMinutes(10), CreatedAtUtc);
            await gateway.AddRequestAsync(request);
            await gateway.SaveChangesAsync();
        }

        await using var verifyContext = CreateContext();
        var verifyGateway = new EfGuestOrderAccessGateway(verifyContext);
        var found = await verifyGateway.FindActiveRequestAsync(
            requestPublicId, CreatedAtUtc.AddMinutes(11));

        Assert.Null(found);
    }

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
            await gateway.AddRequestAsync(request);
            await gateway.SaveChangesAsync();

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
    public async Task PurgeExpiredAsync_OnlyDeletesRowsPastCutoffAndRespectsBatchSize()
    {
        var orderId = await SeedOrderAsync(Guid.CreateVersion7(), "DS-0007", "GUEST@EXAMPLE.COM");

        await using (var context = CreateContext())
        {
            var gateway = new EfGuestOrderAccessGateway(context);
            for (var i = 0; i < 3; i++)
            {
                var expired = GuestOrderAccessRequest.CreateDecoy(
                    Guid.CreateVersion7(), Hash(1), Hash(2), Hash(3),
                    CreatedAtUtc.AddMinutes(10), CreatedAtUtc);
                await gateway.AddRequestAsync(expired);
            }

            var stillFresh = GuestOrderAccessRequest.CreateValid(
                Guid.CreateVersion7(), orderId, Hash(1), Hash(2), Hash(3), Hash(4),
                CreatedAtUtc.AddDays(60), CreatedAtUtc.AddDays(59));
            await gateway.AddRequestAsync(stillFresh);
            await gateway.SaveChangesAsync();
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
                await gateway.AddRequestAsync(expiredWithToken);
                await gateway.SaveChangesAsync();

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
                await gateway.AddRequestAsync(expiredWithoutToken);
            }

            await gateway.SaveChangesAsync();
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
        var shippingProfileId = await SeedShippingProviderProfileAsync(context, orderNumber);
        var order = Order.Create(
            publicId,
            ValidOrderCreation(orderNumber, guestEmailNormalized) with
            {
                ShippingProviderProfileVersionId = shippingProfileId,
            },
            CreatedAtUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private async Task SeedMemberOrderAsync(string orderNumber)
    {
        await using var context = CreateContext();
        var shippingProfileId = await SeedShippingProviderProfileAsync(context, orderNumber);
        var member = ApplicationUser.CreateMember(Guid.CreateVersion7(), $"{orderNumber}@example.com", CreatedAtUtc);
        context.Users.Add(member);
        await context.SaveChangesAsync();

        var creation = ValidOrderCreation(orderNumber, null) with
        {
            MemberUserId = member.Id,
            ShippingProviderProfileVersionId = shippingProfileId,
        };
        var order = Order.Create(Guid.CreateVersion7(), creation, CreatedAtUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
    }

    private static async Task<long> SeedShippingProviderProfileAsync(
        DoSelectDbContext context, string discriminator)
    {
        var profile = new ShippingProviderProfile(
            Guid.CreateVersion7(), $"SHIP-{discriminator}", 1, "Active", null, null, "{}", 1, CreatedAtUtc);
        context.ShippingProviderProfiles.Add(profile);
        await context.SaveChangesAsync();
        return profile.Id;
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
            null);

    private static byte[] Hash(byte fill) => Enumerable.Repeat(fill, 32).ToArray();

    private static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>().UseSqlServer(ConnectionString).Options);
}
