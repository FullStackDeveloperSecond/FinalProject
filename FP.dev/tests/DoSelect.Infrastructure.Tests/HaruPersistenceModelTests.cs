using DoSelect.Domain.Members;
using DoSelect.Domain.Orders;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DoSelect.Infrastructure.Tests;

public sealed class HaruPersistenceModelTests
{
    private const string SyntheticConnectionString =
        "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;" +
        "TrustServerCertificate=True;";

    [Theory]
    [InlineData(typeof(MemberProfile), "MemberProfiles")]
    [InlineData(typeof(AdminProfile), "AdminProfiles")]
    [InlineData(typeof(MemberAddress), "MemberAddresses")]
    [InlineData(typeof(Favorite), "Favorites")]
    [InlineData(typeof(Order), "Orders")]
    [InlineData(typeof(OrderItem), "OrderItems")]
    [InlineData(typeof(OrderStatusHistory), "OrderStatusHistories")]
    [InlineData(typeof(GuestOrderAccessRequest), "GuestOrderAccessRequests")]
    [InlineData(typeof(GuestOrderAccessToken), "GuestOrderAccessTokens")]
    [InlineData(typeof(AssemblyJob), "AssemblyJobs")]
    [InlineData(typeof(AssemblyJobStatusHistory), "AssemblyJobStatusHistories")]
    public void Model_MapsHaruEntityToExpectedTable(Type entityType, string tableName)
    {
        using var context = CreateContext();

        Assert.Equal(tableName, context.Model.FindEntityType(entityType)?.GetTableName());
    }

    [Fact]
    public void Order_ModelHasRequiredPrecisionIndexesAndRestrictOwnerForeignKey()
    {
        using var context = CreateContext();
        var entity = Assert.IsAssignableFrom<IReadOnlyEntityType>(
            context.Model.FindEntityType(typeof(Order)));

        Assert.Equal(18, entity.FindProperty(nameof(Order.GrandTotal))?.GetPrecision());
        Assert.Equal(2, entity.FindProperty(nameof(Order.GrandTotal))?.GetScale());
        Assert.True(entity.FindProperty(nameof(Order.TermsPolicyVersion))?.IsNullable);
        Assert.True(entity.FindProperty(nameof(Order.PrivacyPolicyVersion))?.IsNullable);
        Assert.True(entity.FindProperty(nameof(Order.InvoiceBuyerType))?.IsNullable);
        Assert.True(entity.FindProperty(nameof(Order.InvoiceBuyerEmail))?.IsNullable);
        Assert.Equal("varchar(20)", entity.FindProperty(nameof(Order.InvoiceBuyerType))?.GetColumnType());
        Assert.Equal(320, entity.FindProperty(nameof(Order.InvoiceBuyerEmail))?.GetMaxLength());
        Assert.Equal(30, entity.FindProperty(nameof(Order.InvoiceCarrierType))?.GetMaxLength());
        Assert.Equal(100, entity.FindProperty(nameof(Order.InvoiceCarrierValueMasked))?.GetMaxLength());
        Assert.Equal(8, entity.FindProperty(nameof(Order.InvoiceCompanyTaxId))?.GetMaxLength());
        Assert.Equal(160, entity.FindProperty(nameof(Order.InvoiceCompanyName))?.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique && index.GetDatabaseName() == "UX_Orders_OrderNumber");
        Assert.Contains(entity.GetIndexes(), index =>
            !index.IsUnique &&
            index.GetDatabaseName() == "IX_Orders_CheckoutIdempotencyKey");
        Assert.Contains(entity.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Single().Name == nameof(Order.MemberUserId) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
    }

    [Fact]
    public void GuestToken_ModelEnforcesOneTokenPerChallengeAndBinaryHashes()
    {
        using var context = CreateContext();
        var entity = Assert.IsAssignableFrom<IReadOnlyEntityType>(
            context.Model.FindEntityType(typeof(GuestOrderAccessToken)));

        Assert.Equal(
            "binary(32)",
            entity.FindProperty(nameof(GuestOrderAccessToken.TokenHash))?.GetColumnType());
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            index.GetDatabaseName() == "UX_GuestOrderAccessTokens_RequestId");
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            index.GetDatabaseName() == "UX_GuestOrderAccessTokens_TokenHash");
    }

    [Fact]
    public void MutableEntities_UseSqlServerRowVersionConcurrencyTokens()
    {
        using var context = CreateContext();
        var mutableTypes = new[]
        {
            typeof(MemberAddress),
            typeof(Order),
            typeof(GuestOrderAccessRequest),
            typeof(AssemblyJob),
        };

        foreach (var mutableType in mutableTypes)
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
            .UseSqlServer(SyntheticConnectionString)
            .Options;
        return new DoSelectDbContext(options);
    }
}
