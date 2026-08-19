using DoSelect.Application.Promotions;
using DoSelect.Domain.Promotions;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Tests;

public sealed class CouponRuleReaderTests
{
    private const string SyntheticConnectionString =
        "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;" +
        "TrustServerCertificate=True;";

    private static readonly DateTime CreatedAtUtc =
        new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(CouponRedemptionStatus.Reserved, true)]
    [InlineData(CouponRedemptionStatus.Consumed, true)]
    [InlineData(CouponRedemptionStatus.Released, false)]
    [InlineData(CouponRedemptionStatus.Expired, false)]
    public void OccupiesUsageSeat_CountsReservedAndConsumedOnly(
        CouponRedemptionStatus status,
        bool expected)
    {
        var redemption = CreateRedemption();
        switch (status)
        {
            case CouponRedemptionStatus.Consumed:
                redemption.Consume(CreatedAtUtc.AddHours(1));
                break;
            case CouponRedemptionStatus.Released:
                redemption.Release(CreatedAtUtc.AddHours(1));
                break;
            case CouponRedemptionStatus.Expired:
                redemption.Expire(CreatedAtUtc.AddHours(1));
                break;
        }

        var occupies = CouponRuleReader.OccupiesUsageSeat.Compile();

        Assert.Equal(status, redemption.Status);
        Assert.Equal(expected, occupies(redemption));
    }

    [Fact]
    public void ReaderQueries_AreCoveredByTheDocumentedIndexes()
    {
        using var context = CreateContext();
        var redemption = context.Model.FindEntityType(typeof(CouponRedemption));
        var coupon = context.Model.FindEntityType(typeof(Coupon));

        var indexNames = redemption!.GetIndexes()
            .Select(index => index.GetDatabaseName())
            .ToArray();

        Assert.Contains("IX_CouponRedemptions_CouponId_Status", indexNames);
        Assert.Contains("IX_CouponRedemptions_CouponId_MemberUserId_Status", indexNames);
        Assert.Contains("IX_CouponRedemptions_CouponId_GuestUsageKeyHash_Status", indexNames);
        Assert.Contains(
            coupon!.GetIndexes(),
            index => index.GetDatabaseName() == "UX_Coupons_Code" && index.IsUnique);
    }

    [Fact]
    public void ScopeLinkTables_AreQueryableByCouponId()
    {
        using var context = CreateContext();

        foreach (var entityType in new[]
        {
            typeof(CouponCategory), typeof(CouponProduct), typeof(CouponExcludedProduct),
        })
        {
            var key = context.Model.FindEntityType(entityType)!.FindPrimaryKey();

            Assert.Equal(
                [nameof(CouponCategory.CouponId)],
                key!.Properties.Take(1).Select(property => property.Name));
        }
    }

    [Fact]
    public void AddDoSelectPromotions_ResolvesTheQuoteServiceAndItsReader()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<ICouponRuleReader>();
        var service = scope.ServiceProvider.GetRequiredService<CouponQuoteService>();

        Assert.IsType<CouponRuleReader>(reader);
        Assert.NotNull(service);
    }

    [Fact]
    public void AddDoSelectPromotions_RegistersTheSystemClockOnlyWhenAbsent()
    {
        var services = new ServiceCollection();
        var replacement = new FixedTimeProvider();
        services.AddSingleton<TimeProvider>(replacement);

        services.AddDoSelectPromotions();

        using var provider = services.BuildServiceProvider();
        Assert.Same(replacement, provider.GetRequiredService<TimeProvider>());
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TimeProvider));
    }

    [Fact]
    public async Task FindByCodeAsync_ReturnsNullForABlankCodeWithoutQuerying()
    {
        await using var context = CreateContext();
        var reader = new CouponRuleReader(context);

        Assert.Null(await reader.FindByCodeAsync("   "));
    }

    private static CouponRedemption CreateRedemption() =>
        new(Guid.NewGuid(), 1, 1, "member-1", null, CreatedAtUtc, null, CreatedAtUtc);

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = SyntheticConnectionString,
            })
            .Build();

        return new ServiceCollection()
            .AddDoSelectPersistence(configuration)
            .AddDoSelectPromotions()
            .BuildServiceProvider();
    }

    private static DoSelectDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(SyntheticConnectionString)
            .Options);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }
}
