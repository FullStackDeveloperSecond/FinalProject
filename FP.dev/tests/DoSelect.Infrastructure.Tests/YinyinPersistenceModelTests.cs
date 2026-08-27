using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Promotions;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DoSelect.Infrastructure.Tests;

public sealed class YinyinPersistenceModelTests
{
    private const string SyntheticConnectionString =
        "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;TrustServerCertificate=True;";

    public static TheoryData<Type, string> Tables => new()
    {
        { typeof(Coupon), "Coupons" },
        { typeof(CouponRedemption), "CouponRedemptions" },
        { typeof(OrderCoupon), "OrderCoupons" },
        { typeof(CouponCategory), "CouponCategories" },
        { typeof(CouponProduct), "CouponProducts" },
        { typeof(CouponExcludedProduct), "CouponExcludedProducts" },
        { typeof(PaymentAttempt), "PaymentAttempts" },
        { typeof(PaymentEvent), "PaymentEvents" },
        { typeof(Refund), "Refunds" },
        { typeof(RefundAllocation), "RefundAllocations" },
        { typeof(SimulatedInvoice), "SimulatedInvoices" },
        { typeof(SimulatedInvoiceItem), "SimulatedInvoiceItems" },
        { typeof(SimulatedInvoiceAllowance), "SimulatedInvoiceAllowances" },
        { typeof(SimulatedInvoiceAllowanceItem), "SimulatedInvoiceAllowanceItems" },
    };

    [Theory]
    [MemberData(nameof(Tables))]
    public void Model_MapsEntityToExpectedTable(Type entityType, string tableName)
    {
        using var context = CreateContext();
        Assert.Equal(tableName, context.Model.FindEntityType(entityType)?.GetTableName());
    }

    [Fact]
    public void CouponRedemption_UsesCompositeOrderUniquenessAndBinaryGuestHash()
    {
        using var context = CreateContext();
        var entity = Assert.IsAssignableFrom<IReadOnlyEntityType>(context.Model.FindEntityType(typeof(CouponRedemption)));

        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.GetDatabaseName() == "UX_CouponRedemptions_CouponId_OrderId" &&
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(CouponRedemption.CouponId), nameof(CouponRedemption.OrderId)]));
        Assert.DoesNotContain(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Count == 1 && index.Properties[0].Name == nameof(CouponRedemption.OrderId));
        Assert.Equal("binary(32)", entity.FindProperty(nameof(CouponRedemption.GuestUsageKeyHash))?.GetColumnType());
    }

    [Fact]
    public void CouponScopeTables_UseCompositeKeysWithoutSyntheticIds()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(CouponCategory))!;

        Assert.Equal([nameof(CouponCategory.CouponId), nameof(CouponCategory.CategoryId)],
            entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Null(entity.FindProperty("Id"));
        Assert.Null(entity.FindProperty("PublicId"));
    }

    [Fact]
    public void PaymentUsesScopedCentralIdempotencyAndFilteredProviderReferenceIndex()
    {
        using var context = CreateContext();
        var payment = context.Model.FindEntityType(typeof(PaymentAttempt))!;
        var refund = context.Model.FindEntityType(typeof(Refund))!;

        Assert.Contains(payment.GetIndexes(), index => !index.IsUnique &&
            index.GetDatabaseName() == "IX_PaymentAttempts_IdempotencyKey");
        Assert.Contains(payment.GetIndexes(), index => index.IsUnique &&
            index.GetDatabaseName() == "UX_PaymentAttempts_ExternalReference" &&
            index.GetFilter() == "[ExternalReference] IS NOT NULL");
        Assert.Contains(refund.GetIndexes(), index => index.IsUnique &&
            index.GetDatabaseName() == "UX_Refunds_IdempotencyKey");
    }

    [Fact]
    public void SimulatedInvoice_EnforcesOneInvoicePerOrderAndDemoMarkerDefault()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(SimulatedInvoice))!;

        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.GetDatabaseName() == "UX_SimulatedInvoices_OrderId");
        Assert.Equal(SimulatedInvoice.RequiredDemoMarker,
            entity.FindProperty(nameof(SimulatedInvoice.DemoMarker))?.GetDefaultValue());
    }

    [Fact]
    public void YinyinForeignKeys_AreRestrictAndMutableRowsUseRowVersion()
    {
        using var context = CreateContext();
        var entityTypes = Tables.Select(row => row[0]).Cast<Type>()
            .Select(type => context.Model.FindEntityType(type)!)
            .ToArray();

        Assert.All(entityTypes.SelectMany(entity => entity.GetForeignKeys()), foreignKey =>
            Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));

        foreach (var mutableType in new[] { typeof(Coupon), typeof(CouponRedemption), typeof(PaymentAttempt), typeof(Refund), typeof(SimulatedInvoice) })
        {
            var property = context.Model.FindEntityType(mutableType)?.FindProperty("RowVersion");
            Assert.NotNull(property);
            Assert.True(property.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        }
    }

    private static DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(SyntheticConnectionString).Options;
        return new DoSelectDbContext(options);
    }
}
