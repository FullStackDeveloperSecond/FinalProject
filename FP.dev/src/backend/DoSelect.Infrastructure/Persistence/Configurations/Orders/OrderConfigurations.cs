using DoSelect.Domain.Catalog;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence.Configurations;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Orders;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ConfigureMutablePublicEntity("Orders");
        builder.Property(order => order.OrderNumber).HasMaxLength(32).IsRequired();
        builder.HasIndex(order => order.OrderNumber)
            .IsUnique()
            .HasDatabaseName("UX_Orders_OrderNumber");
        builder.Property(order => order.MemberUserId).HasMaxLength(450);
        builder.Property(order => order.GuestEmailNormalized).HasMaxLength(320);
        ConfigureStatus(builder.Property(order => order.OrderStatus));
        ConfigureStatus(builder.Property(order => order.PaymentStatus));
        ConfigureStatus(builder.Property(order => order.FulfillmentStatus));
        ConfigureStatus(builder.Property(order => order.AssemblyStatus));
        ConfigureStatus(builder.Property(order => order.OrderRefundStatus));
        ConfigureMoney(builder.Property(order => order.MerchandiseSubtotal));
        ConfigureMoney(builder.Property(order => order.ItemDiscountTotal));
        ConfigureMoney(builder.Property(order => order.ShippingFee));
        ConfigureMoney(builder.Property(order => order.AssemblyFee));
        ConfigureMoney(builder.Property(order => order.GrandTotal));
        ConfigureMoney(builder.Property(order => order.PaidAmount));
        ConfigureMoney(builder.Property(order => order.RefundedAmount));
        builder.Property(order => order.ShippingFreeThresholdSnapshot).HasPrecision(18, 2);
        builder.Property(order => order.ShippingMethodBaseFeeSnapshot).HasPrecision(18, 2);
        builder.Property(order => order.Currency)
            .HasColumnType("char(3)")
            .IsUnicode(false)
            .IsRequired();
        builder.Property(order => order.RecipientName).HasMaxLength(100).IsRequired();
        builder.Property(order => order.RecipientPhone).HasMaxLength(32).IsRequired();
        builder.Property(order => order.RecipientEmail).HasMaxLength(320).IsRequired();
        builder.Property(order => order.PostalCode).HasMaxLength(16);
        builder.Property(order => order.RecipientCity).HasMaxLength(50);
        builder.Property(order => order.RecipientDistrict).HasMaxLength(50);
        builder.Property(order => order.AddressLine1).HasMaxLength(300);
        builder.Property(order => order.AddressLine2).HasMaxLength(300);
        builder.Property(order => order.CountryCode)
            .HasColumnType("char(2)")
            .IsUnicode(false);
        builder.Property(order => order.DeliveryNote).HasMaxLength(500);
        builder.Property(order => order.ShippingMethodCode).HasMaxLength(64).IsRequired();
        builder.Property(order => order.StoreCode).HasMaxLength(64);
        builder.Property(order => order.StoreName).HasMaxLength(160);
        builder.Property(order => order.StoreAddress).HasMaxLength(500);
        builder.Property(order => order.PackageWeightKgSnapshot).HasPrecision(10, 3);
        builder.Property(order => order.PackageLengthCmSnapshot).HasPrecision(10, 2);
        builder.Property(order => order.PackageWidthCmSnapshot).HasPrecision(10, 2);
        builder.Property(order => order.PackageHeightCmSnapshot).HasPrecision(10, 2);
        builder.Property(order => order.PackageTotalCmSnapshot).HasPrecision(10, 2);
        builder.Property(order => order.PackageDeclaredValueSnapshot).HasPrecision(18, 2);
        builder.Property(order => order.InvoiceBuyerType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsUnicode(false);
        builder.Property(order => order.InvoiceBuyerEmail).HasMaxLength(320);
        builder.Property(order => order.InvoiceCarrierType)
            .HasMaxLength(30)
            .IsUnicode(false);
        builder.Property(order => order.InvoiceCarrierValueMasked).HasMaxLength(100);
        builder.Property(order => order.InvoiceCompanyTaxId)
            .HasMaxLength(8)
            .IsUnicode(false);
        builder.Property(order => order.InvoiceCompanyName).HasMaxLength(160);
        builder.Property(order => order.PaymentDueAtUtc).HasPrecision(3);
        builder.Property(order => order.ConfirmedAtUtc).HasPrecision(3);
        builder.Property(order => order.PaidAtUtc).HasPrecision(3);
        builder.Property(order => order.ShippedAtUtc).HasPrecision(3);
        builder.Property(order => order.DeliveredAtUtc).HasPrecision(3);
        builder.Property(order => order.CompletedAtUtc).HasPrecision(3);
        builder.Property(order => order.CancelledAtUtc).HasPrecision(3);
        builder.Property(order => order.CheckoutIdempotencyKey).HasMaxLength(128).IsRequired();
        builder.HasIndex(order => order.CheckoutIdempotencyKey)
            .HasDatabaseName("IX_Orders_CheckoutIdempotencyKey");
        builder.HasIndex(order => new { order.MemberUserId, order.CreatedAtUtc })
            .HasDatabaseName("IX_Orders_MemberUserId_CreatedAtUtc");
        builder.HasIndex(order => new { order.OrderStatus, order.PaymentDueAtUtc })
            .HasDatabaseName("IX_Orders_OrderStatus_PaymentDueAtUtc");
        builder.HasIndex(order => order.CompletedAtUtc)
            .HasDatabaseName("IX_Orders_CompletedAtUtc");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(order => order.MemberUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ShippingProviderProfile>()
            .WithMany()
            .HasForeignKey(order => order.ShippingProviderProfileVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PackageLimitVersion>()
            .WithMany()
            .HasForeignKey(order => order.PackageLimitVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("Orders", table =>
        {
            table.HasCheckConstraint(
                "CK_Orders_Owner",
                "[MemberUserId] IS NOT NULL OR [GuestEmailNormalized] IS NOT NULL");
            table.HasCheckConstraint(
                "CK_Orders_Amounts_Nonnegative",
                "[MerchandiseSubtotal] >= 0 AND [ItemDiscountTotal] >= 0 AND [ShippingFee] >= 0 AND [AssemblyFee] >= 0 AND [GrandTotal] >= 0 AND [PaidAmount] >= 0 AND [RefundedAmount] >= 0");
            table.HasCheckConstraint(
                "CK_Orders_GrandTotal",
                "[GrandTotal] = ROUND([MerchandiseSubtotal] - [ItemDiscountTotal] + [ShippingFee] + [AssemblyFee], 0)");
            table.HasCheckConstraint("CK_Orders_PaidAmount", "[PaidAmount] <= [GrandTotal]");
            table.HasCheckConstraint("CK_Orders_RefundedAmount", "[RefundedAmount] <= [PaidAmount]");
            table.HasCheckConstraint("CK_Orders_Currency", "[Currency] = 'TWD'");
            table.HasCheckConstraint(
                "CK_Orders_CountryCode",
                "[CountryCode] IS NULL OR [CountryCode] = 'TW'");
            table.HasCheckConstraint(
                "CK_Orders_PackageSnapshot",
                "([PackageLimitVersionId] IS NULL AND [PackageWeightKgSnapshot] IS NULL AND [PackageLengthCmSnapshot] IS NULL AND [PackageWidthCmSnapshot] IS NULL AND [PackageHeightCmSnapshot] IS NULL AND [PackageTotalCmSnapshot] IS NULL AND [PackageDeclaredValueSnapshot] IS NULL) OR ([PackageLimitVersionId] IS NOT NULL AND [PackageWeightKgSnapshot] > 0 AND [PackageLengthCmSnapshot] > 0 AND [PackageWidthCmSnapshot] > 0 AND [PackageHeightCmSnapshot] > 0 AND [PackageTotalCmSnapshot] = [PackageLengthCmSnapshot] + [PackageWidthCmSnapshot] + [PackageHeightCmSnapshot] AND [PackageDeclaredValueSnapshot] >= 0)");
            table.HasCheckConstraint(
                "CK_Orders_ShippingFreeThresholdSnapshot",
                "[ShippingFreeThresholdSnapshot] IS NULL OR [ShippingFreeThresholdSnapshot] >= 0");
            table.HasCheckConstraint(
                "CK_Orders_ShippingMethodBaseFeeSnapshot",
                "[ShippingMethodBaseFeeSnapshot] IS NULL OR [ShippingMethodBaseFeeSnapshot] >= 0");
            table.HasCheckConstraint(
                "CK_Orders_PolicyVersions",
                "[ShippingConstraintPolicyVersion] > 0 AND [ReturnPolicyVersion] > 0 AND ([TermsPolicyVersion] IS NULL OR [TermsPolicyVersion] > 0) AND ([PrivacyPolicyVersion] IS NULL OR [PrivacyPolicyVersion] > 0) AND ([CouponPolicyVersion] IS NULL OR [CouponPolicyVersion] > 0)");
            table.HasCheckConstraint(
                "CK_Orders_InvoiceCarrier",
                "([InvoiceCarrierType] IS NULL AND [InvoiceCarrierValueMasked] IS NULL) OR ([InvoiceCarrierType] IS NOT NULL AND [InvoiceCarrierValueMasked] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_Orders_InvoiceCompany",
                "([InvoiceBuyerType] IS NULL AND [InvoiceBuyerEmail] IS NULL AND [InvoiceCarrierType] IS NULL AND [InvoiceCarrierValueMasked] IS NULL AND [InvoiceCompanyTaxId] IS NULL AND [InvoiceCompanyName] IS NULL) OR ([InvoiceBuyerType] = 'Company' AND [InvoiceBuyerEmail] IS NOT NULL AND [InvoiceCompanyTaxId] IS NOT NULL AND [InvoiceCompanyName] IS NOT NULL) OR ([InvoiceBuyerType] = 'Individual' AND [InvoiceBuyerEmail] IS NOT NULL AND [InvoiceCompanyTaxId] IS NULL AND [InvoiceCompanyName] IS NULL)");
        });
    }

    private static void ConfigureStatus<TStatus>(PropertyBuilder<TStatus> property)
        where TStatus : struct, Enum =>
        property.HasConversion<string>().HasMaxLength(32).IsUnicode(false).IsRequired();

    private static void ConfigureMoney(PropertyBuilder<decimal> property) =>
        property.HasPrecision(18, 2).IsRequired();
}

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ConfigurePublicEntity("OrderItems");
        builder.Property(item => item.SkuCodeSnapshot).HasMaxLength(64).IsRequired();
        builder.Property(item => item.ProductNameSnapshot).HasMaxLength(160).IsRequired();
        builder.Property(item => item.SkuNameSnapshot).HasMaxLength(160).IsRequired();
        builder.Property(item => item.SpecificationSummarySnapshot).HasMaxLength(1000);
        builder.Property(item => item.SpecificationJsonSnapshot).HasMaxLength(4000);
        builder.Property(item => item.IsCouponEligible).IsRequired();
        ConfigureMoney(builder.Property(item => item.ListUnitPrice));
        ConfigureMoney(builder.Property(item => item.SaleUnitPrice));
        ConfigureMoney(builder.Property(item => item.FinalUnitPrice));
        ConfigureMoney(builder.Property(item => item.UnitCostSnapshot));
        ConfigureMoney(builder.Property(item => item.LineSubtotal));
        ConfigureMoney(builder.Property(item => item.DiscountAllocation));
        ConfigureMoney(builder.Property(item => item.LineTotal));
        builder.HasIndex(item => item.OrderId).HasDatabaseName("IX_OrderItems_OrderId");
        builder.HasIndex(item => item.SkuId).HasDatabaseName("IX_OrderItems_SkuId");
        builder.HasIndex(item => new { item.OrderId, item.AssemblyGroupKey })
            .HasDatabaseName("IX_OrderItems_OrderId_AssemblyGroupKey");
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Sku>()
            .WithMany()
            .HasForeignKey(item => item.SkuId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("OrderItems", table =>
        {
            table.HasCheckConstraint("CK_OrderItems_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint(
                "CK_OrderItems_ReturnedQuantity",
                "[ReturnedQuantity] >= 0 AND [ReturnableQuantity] >= [ReturnedQuantity] AND [Quantity] >= [ReturnableQuantity]");
            table.HasCheckConstraint(
                "CK_OrderItems_Amounts_Nonnegative",
                "[ListUnitPrice] >= 0 AND [SaleUnitPrice] >= 0 AND [FinalUnitPrice] >= 0 AND [UnitCostSnapshot] >= 0 AND [LineSubtotal] >= 0 AND [DiscountAllocation] >= 0 AND [LineTotal] >= 0");
            table.HasCheckConstraint(
                "CK_OrderItems_SpecificationSnapshot",
                "([SpecificationSummarySnapshot] IS NULL AND [SpecificationJsonSnapshot] IS NULL AND [SpecificationSchemaVersion] IS NULL) OR ([SpecificationSummarySnapshot] IS NOT NULL AND [SpecificationJsonSnapshot] IS NOT NULL AND [SpecificationSchemaVersion] > 0)");
        });
    }

    private static void ConfigureMoney(PropertyBuilder<decimal> property) =>
        property.HasPrecision(18, 2).IsRequired();
}

public sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ConfigurePublicEntity("OrderStatusHistories");
        builder.Property(history => history.StateDimension)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(history => history.FromStatus)
            .HasMaxLength(32)
            .IsUnicode(false);
        builder.Property(history => history.ToStatus)
            .HasMaxLength(32)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(history => history.ReasonCode)
            .HasMaxLength(64)
            .IsUnicode(false);
        builder.Property(history => history.ActorUserId).HasMaxLength(450);
        builder.Property(history => history.OccurredAtUtc).HasPrecision(3).IsRequired();
        builder.Property(history => history.TraceId).HasMaxLength(64).IsRequired();
        builder.HasIndex(history => new { history.OrderId, history.OccurredAtUtc })
            .HasDatabaseName("IX_OrderStatusHistories_OrderId_OccurredAtUtc");
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(history => history.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(history => history.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GuestOrderAccessRequestConfiguration
    : IEntityTypeConfiguration<GuestOrderAccessRequest>
{
    public void Configure(EntityTypeBuilder<GuestOrderAccessRequest> builder)
    {
        builder.ConfigureMutablePublicEntity("GuestOrderAccessRequests");
        builder.Property(request => request.CodeHash).HasColumnType("binary(32)");
        builder.Property(request => request.RequesterIpHash)
            .HasColumnType("binary(32)")
            .IsRequired();
        builder.Property(request => request.EmailKeyHash)
            .HasColumnType("binary(32)")
            .IsRequired();
        builder.Property(request => request.OrderLookupKeyHash)
            .HasColumnType("binary(32)")
            .IsRequired();
        builder.Property(request => request.ExpiresAtUtc).HasPrecision(3).IsRequired();
        builder.Property(request => request.AttemptCount).HasDefaultValue(0).IsRequired();
        builder.Property(request => request.SendCount).HasDefaultValue(0).IsRequired();
        builder.Property(request => request.LastSentAtUtc).HasPrecision(3);
        builder.Property(request => request.LockedAtUtc).HasPrecision(3);
        builder.Property(request => request.ConsumedAtUtc).HasPrecision(3);
        builder.Property(request => request.RevokedAtUtc).HasPrecision(3);
        builder.HasIndex(request => request.ExpiresAtUtc)
            .HasDatabaseName("IX_GuestOrderAccessRequests_ExpiresAtUtc");
        builder.HasIndex(request => new { request.RequesterIpHash, request.CreatedAtUtc })
            .HasDatabaseName("IX_GuestOrderAccessRequests_RequesterIpHash_CreatedAtUtc");
        builder.HasIndex(request => new { request.EmailKeyHash, request.CreatedAtUtc })
            .HasDatabaseName("IX_GuestOrderAccessRequests_EmailKeyHash_CreatedAtUtc");
        builder.HasIndex(request => new { request.OrderLookupKeyHash, request.CreatedAtUtc })
            .HasDatabaseName("IX_GuestOrderAccessRequests_OrderLookupKeyHash_CreatedAtUtc");
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(request => request.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("GuestOrderAccessRequests", table =>
        {
            table.HasCheckConstraint(
                "CK_GuestOrderAccessRequests_AttemptCount",
                "[AttemptCount] >= 0 AND [AttemptCount] <= 5");
            table.HasCheckConstraint(
                "CK_GuestOrderAccessRequests_SendCount",
                "[SendCount] >= 0 AND [SendCount] <= 3");
        });
    }

}

public sealed class GuestOrderAccessTokenConfiguration
    : IEntityTypeConfiguration<GuestOrderAccessToken>
{
    public void Configure(EntityTypeBuilder<GuestOrderAccessToken> builder)
    {
        builder.ConfigurePublicEntity("GuestOrderAccessTokens");
        builder.Property(token => token.TokenHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(token => token.ExpiresAtUtc).HasPrecision(3).IsRequired();
        builder.Property(token => token.RevokedAtUtc).HasPrecision(3);
        builder.Property(token => token.ScopeViolationCount).HasDefaultValue(0).IsRequired();
        builder.HasIndex(token => token.RequestId)
            .IsUnique()
            .HasDatabaseName("UX_GuestOrderAccessTokens_RequestId");
        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("UX_GuestOrderAccessTokens_TokenHash");
        builder.HasIndex(token => token.ExpiresAtUtc)
            .HasDatabaseName("IX_GuestOrderAccessTokens_ExpiresAtUtc");
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(token => token.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GuestOrderAccessRequest>()
            .WithOne()
            .HasForeignKey<GuestOrderAccessToken>(token => token.RequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("GuestOrderAccessTokens", table =>
            table.HasCheckConstraint(
                "CK_GuestOrderAccessTokens_ScopeViolationCount",
                "[ScopeViolationCount] >= 0"));
    }
}

public sealed class AssemblyJobConfiguration : IEntityTypeConfiguration<AssemblyJob>
{
    public void Configure(EntityTypeBuilder<AssemblyJob> builder)
    {
        builder.ConfigureMutablePublicEntity("AssemblyJobs");
        builder.Property(job => job.Status)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(job => job.StartedAtUtc).HasPrecision(3);
        builder.Property(job => job.CompletedAtUtc).HasPrecision(3);
        builder.Property(job => job.AssignedAdminUserId).HasMaxLength(450);
        builder.Property(job => job.Note).HasMaxLength(1000);
        builder.HasIndex(job => new { job.OrderId, job.AssemblyGroupKey })
            .IsUnique()
            .HasDatabaseName("UX_AssemblyJobs_OrderId_AssemblyGroupKey");
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(job => job.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(job => job.AssignedAdminUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AssemblyJobStatusHistoryConfiguration
    : IEntityTypeConfiguration<AssemblyJobStatusHistory>
{
    public void Configure(EntityTypeBuilder<AssemblyJobStatusHistory> builder)
    {
        builder.ConfigurePublicEntity("AssemblyJobStatusHistories");
        builder.Property(history => history.FromStatus)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false);
        builder.Property(history => history.ToStatus)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(history => history.ReasonCode)
            .HasMaxLength(64)
            .IsUnicode(false);
        builder.Property(history => history.ActorUserId).HasMaxLength(450);
        builder.Property(history => history.OccurredAtUtc).HasPrecision(3).IsRequired();
        builder.Property(history => history.TraceId).HasMaxLength(64).IsRequired();
        builder.HasIndex(history => new { history.AssemblyJobId, history.OccurredAtUtc })
            .HasDatabaseName("IX_AssemblyJobStatusHistories_AssemblyJobId_OccurredAtUtc");
        builder.HasOne<AssemblyJob>()
            .WithMany()
            .HasForeignKey(history => history.AssemblyJobId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(history => history.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
