using System.Reflection;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Imports;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Common;
using DoSelect.Domain.Members;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Reviews;
using DoSelect.Domain.Shipping;
using DoSelect.Domain.Shopping;

namespace DoSelect.Domain.Tests;

public sealed class HaruEntityTests
{
    private static readonly DateTime CreatedAtUtc =
        new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(typeof(MemberProfile))]
    [InlineData(typeof(AdminProfile))]
    [InlineData(typeof(MemberAddress))]
    [InlineData(typeof(Favorite))]
    [InlineData(typeof(Order))]
    [InlineData(typeof(OrderItem))]
    [InlineData(typeof(OrderStatusHistory))]
    [InlineData(typeof(GuestOrderAccessRequest))]
    [InlineData(typeof(GuestOrderAccessToken))]
    [InlineData(typeof(AssemblyJob))]
    [InlineData(typeof(AssemblyJobStatusHistory))]
    [InlineData(typeof(Brand))]
    [InlineData(typeof(Category))]
    [InlineData(typeof(Product))]
    [InlineData(typeof(Sku))]
    [InlineData(typeof(Tag))]
    [InlineData(typeof(ProductTag))]
    [InlineData(typeof(BrandTranslation))]
    [InlineData(typeof(CategoryTranslation))]
    [InlineData(typeof(ProductTranslation))]
    [InlineData(typeof(SkuTranslation))]
    [InlineData(typeof(SpecificationDefinitionTranslation))]
    [InlineData(typeof(SpecificationOptionTranslation))]
    [InlineData(typeof(ProductImage))]
    [InlineData(typeof(MeasurementUnit))]
    [InlineData(typeof(SpecificationDefinition))]
    [InlineData(typeof(SpecificationOption))]
    [InlineData(typeof(SpecificationSource))]
    [InlineData(typeof(SkuSpecificationValue))]
    [InlineData(typeof(SalePrice))]
    [InlineData(typeof(ImportBatch))]
    [InlineData(typeof(ImportRow))]
    [InlineData(typeof(Cart))]
    [InlineData(typeof(CartItem))]
    [InlineData(typeof(InventoryBalance))]
    [InlineData(typeof(InventoryReservation))]
    [InlineData(typeof(InventoryMovement))]
    [InlineData(typeof(InventoryReconciliationCase))]
    [InlineData(typeof(ShippingMethod))]
    [InlineData(typeof(ShippingProviderProfile))]
    [InlineData(typeof(PackageLimitVersion))]
    [InlineData(typeof(ConvenienceStore))]
    [InlineData(typeof(Shipment))]
    [InlineData(typeof(ShipmentStatusHistory))]
    [InlineData(typeof(BuildList))]
    [InlineData(typeof(BuildListItem))]
    [InlineData(typeof(BuildShareToken))]
    [InlineData(typeof(CompatibilityRuleSetting))]
    [InlineData(typeof(CompatibilityCheckRun))]
    [InlineData(typeof(CompatibilityCheckResult))]
    [InlineData(typeof(ProductReview))]
    [InlineData(typeof(ReviewImage))]
    [InlineData(typeof(ProductReviewRevision))]
    public void Entity_DoesNotExposePublicPropertySetters(Type entityType)
    {
        var publicSetters = entityType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.SetMethod?.IsPublic == true)
            .Select(property => property.Name)
            .ToArray();

        Assert.Empty(publicSetters);
    }

    [Fact]
    public void SkuSpecificationValue_RequiresExactlyOneTypedValue()
    {
        Assert.Throws<ArgumentException>(() => new SkuSpecificationValue(
            1,
            1,
            "   ",
            null,
            null,
            null,
            null,
            CreatedAtUtc));
        Assert.Throws<ArgumentException>(() => new SkuSpecificationValue(
            1,
            1,
            "ATX",
            1m,
            null,
            null,
            null,
            CreatedAtUtc));
    }

    [Fact]
    public void CatalogCodes_AreNormalizedForStableUniqueKeys()
    {
        var brand = new Brand(Guid.NewGuid(), "  asus  ", "華碩", CreatedAtUtc);

        Assert.Equal("ASUS", brand.Code);
    }

    [Fact]
    public void InventoryMovement_RejectsBeforeDeltaAfterMismatch()
    {
        Assert.Throws<ArgumentException>(() => new InventoryMovement(
            Guid.NewGuid(),
            1,
            null,
            "Adjustment",
            2,
            0,
            10,
            11,
            0,
            0,
            "StocktakeDifference",
            "ImportBatch",
            Guid.NewGuid(),
            null,
            CreatedAtUtc));
    }

    [Fact]
    public void Cart_RequiresExactlyOneMemberOrGuestOwner()
    {
        Assert.Throws<ArgumentException>(() => Cart.CreateForGuest(
            Guid.NewGuid(),
            new byte[31],
            CreatedAtUtc.AddDays(1),
            CreatedAtUtc));
    }

    [Fact]
    public void Shipment_UsesFormalFulfillmentTransitionGraph()
    {
        var shipment = new Shipment(Guid.NewGuid(), 1, 1, 1, null, "S0001", 80m, CreatedAtUtc);

        Assert.Throws<InvalidOperationException>(() =>
            shipment.ChangeStatus(FulfillmentStatus.Delivered, CreatedAtUtc.AddHours(1)));
    }

    [Fact]
    public void Order_CreateWithGuestOwner_SetsTwdAndComputedAmounts()
    {
        var order = CreateOrder();

        Assert.Equal("TWD", order.Currency);
        Assert.Equal(1_325m, order.GrandTotal);
        Assert.Equal(OrderRefundStatus.None, order.OrderRefundStatus);
        Assert.Equal(CreatedAtUtc, order.CreatedAtUtc);
        Assert.Equal(CreatedAtUtc, order.UpdatedAtUtc);
        Assert.Equal(2_000m, order.ShippingFreeThresholdSnapshot);
        Assert.Equal(3, order.TermsPolicyVersion);
        Assert.Equal(4, order.PrivacyPolicyVersion);
        Assert.Equal(SimulatedInvoiceBuyerType.Individual, order.InvoiceBuyerType);
        Assert.Equal("guest@example.com", order.InvoiceBuyerEmail);
        Assert.Equal("MobileBarcode", order.InvoiceCarrierType);
        Assert.Equal("***/ABC", order.InvoiceCarrierValueMasked);
        Assert.Equal("TW", order.CountryCode);
        Assert.Equal("Leave with reception", order.DeliveryNote);
        Assert.Equal(7, order.PackageLimitVersionId);
        Assert.Equal(5m, order.PackageWeightKgSnapshot);
        Assert.Equal(125m, order.PackageTotalCmSnapshot);
    }

    [Theory]
    [InlineData(1, "DS202608270001")]
    [InlineData(9999, "DS202608279999")]
    public void OrderNumber_Create_UsesApprovedDailyFormat(int sequence, string expected)
    {
        Assert.Equal(expected, OrderNumber.Create(new DateOnly(2026, 8, 27), sequence));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public void OrderNumber_Create_RejectsSequenceOutsideDailyCapacity(int sequence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OrderNumber.Create(new DateOnly(2026, 8, 27), sequence));
    }

    [Fact]
    public void Order_CreateWithoutMemberOrGuest_RejectsCreation()
    {
        var creation = ValidOrderCreation() with
        {
            MemberUserId = null,
            GuestEmailNormalized = null,
        };

        Assert.Throws<ArgumentException>(() =>
            Order.Create(Guid.NewGuid(), creation, CreatedAtUtc));
    }

    [Fact]
    public void Order_CreateWithMismatchedGrandTotal_RejectsCreation()
    {
        var creation = ValidOrderCreation() with { GrandTotal = 1m };

        Assert.Throws<ArgumentException>(() =>
            Order.Create(Guid.NewGuid(), creation, CreatedAtUtc));
    }

    [Fact]
    public void Order_CreateWithFractionalRawPayable_UsesAwayFromZeroGrandTotal()
    {
        var creation = ValidOrderCreation() with
        {
            ShippingFee = 225.50m,
            GrandTotal = 1_326m,
        };

        var order = Order.Create(Guid.NewGuid(), creation, CreatedAtUtc);

        Assert.Equal(1_326m, order.GrandTotal);
    }

    [Fact]
    public void Order_CreateCompanyInvoiceWithoutCompanyIdentity_RejectsCreation()
    {
        var creation = ValidOrderCreation() with
        {
            InvoicePreference = new OrderInvoicePreference(
                SimulatedInvoiceBuyerType.Company,
                "buyer@example.com",
                null,
                null,
                null,
                null),
        };

        Assert.Throws<ArgumentException>(() =>
            Order.Create(Guid.NewGuid(), creation, CreatedAtUtc));
    }

    [Fact]
    public void Order_CreateCarrierWithOnlyOneCarrierField_RejectsCreation()
    {
        var creation = ValidOrderCreation() with
        {
            InvoicePreference = new OrderInvoicePreference(
                SimulatedInvoiceBuyerType.Individual,
                "buyer@example.com",
                "MobileBarcode",
                null,
                null,
                null),
        };

        Assert.Throws<ArgumentException>(() =>
            Order.Create(Guid.NewGuid(), creation, CreatedAtUtc));
    }

    [Fact]
    public void OrderItem_Create_PreservesSpecificationSnapshot()
    {
        var item = new OrderItem(
            Guid.NewGuid(),
            1,
            2,
            "GPU-001",
            "Graphics card",
            "16 GB",
            1,
            20_000m,
            19_000m,
            19_000m,
            15_000m,
            19_000m,
            0m,
            19_000m,
            null,
            1,
            CreatedAtUtc,
            true,
            new OrderItemSpecificationSnapshot(
                "VRAM: 16 GB",
                "{\"VRAM_GB\":16}",
                1));

        Assert.Equal("VRAM: 16 GB", item.SpecificationSummarySnapshot);
        Assert.Equal("{\"VRAM_GB\":16}", item.SpecificationJsonSnapshot);
        Assert.Equal(1, item.SpecificationSchemaVersion);
    }

    [Fact]
    public void GuestOrderAccessRequest_FifthFailedAttempt_LocksChallenge()
    {
        var request = GuestOrderAccessRequest.CreateValid(
            Guid.NewGuid(),
            1,
            Hash(),
            Hash(),
            Hash(),
            Hash(),
            CreatedAtUtc.AddMinutes(10),
            CreatedAtUtc);

        for (var attempt = 1; attempt <= GuestOrderAccessRequest.MaximumAttempts; attempt++)
        {
            request.RecordFailedAttempt(CreatedAtUtc.AddSeconds(attempt));
        }

        Assert.Equal(GuestOrderAccessRequest.MaximumAttempts, request.AttemptCount);
        Assert.NotNull(request.LockedAtUtc);
        Assert.Throws<InvalidOperationException>(() =>
            request.RecordFailedAttempt(CreatedAtUtc.AddMinutes(1)));
    }

    [Fact]
    public void AssemblyJob_DoesNotAllowSkippingFromPendingToReadyToShip()
    {
        var job = new AssemblyJob(Guid.NewGuid(), 1, Guid.NewGuid(), CreatedAtUtc);

        Assert.Throws<InvalidOperationException>(() =>
            job.ChangeStatus(AssemblyJobStatus.ReadyToShip, CreatedAtUtc.AddMinutes(1)));
    }

    [Fact]
    public void EntityBase_RejectsNonUtcTimestamps()
    {
        var localTime = DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() =>
            new AssemblyJob(Guid.NewGuid(), 1, Guid.NewGuid(), localTime));
    }

    private static Order CreateOrder() =>
        Order.Create(Guid.NewGuid(), ValidOrderCreation(), CreatedAtUtc);

    private static OrderCreation ValidOrderCreation() =>
        new(
            "DS202608180001",
            null,
            "guest@example.com",
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
            "checkout-0001",
            null,
            3,
            4,
            new OrderInvoicePreference(
                SimulatedInvoiceBuyerType.Individual,
                "guest@example.com",
                "MobileBarcode",
                "***/ABC",
                null,
                null),
            2_000m,
            "Leave with reception",
            new OrderPackageSnapshot(7, 5m, 55m, 40m, 30m, 125m, 4_000m));

    private static byte[] Hash() => Enumerable.Repeat((byte)1, 32).ToArray();
}
