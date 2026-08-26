using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Domain.Returns;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Refunds;

public sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ConfigureMutablePublicEntity("Refunds");
        builder.Property(x => x.RefundNumber).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.RefundNumber).IsUnique().HasDatabaseName("UX_Refunds_RefundNumber");
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsUnicode(false).HasDefaultValue(RefundStatus.PendingReview).IsRequired();
        Money(builder.Property(x => x.RequestedAmount), true);
        Money(builder.Property(x => x.ApprovedAmount));
        Money(builder.Property(x => x.SucceededAmount));
        builder.Property(x => x.ReasonCode).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(x => x.RequestedBy).HasMaxLength(450);
        builder.Property(x => x.ApprovedBy).HasMaxLength(450);
        builder.Property(x => x.ExecutedByAdminUserId).HasMaxLength(450);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("UX_Refunds_IdempotencyKey");
        builder.Property(x => x.SucceededAtUtc).HasPrecision(3);
        builder.HasIndex(x => x.OrderId).HasDatabaseName("IX_Refunds_OrderId");
        builder.HasIndex(x => x.ReturnRequestId).HasDatabaseName("IX_Refunds_ReturnRequestId");
        builder.HasIndex(x => x.PaymentAttemptId).HasDatabaseName("IX_Refunds_PaymentAttemptId");
        builder.HasIndex(x => x.Status).HasDatabaseName("IX_Refunds_Status");
        builder.HasIndex(x => x.ReasonCode).HasDatabaseName("IX_Refunds_ReasonCode");
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaymentAttempt>().WithMany().HasForeignKey(x => x.PaymentAttemptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ReturnRequest>().WithMany().HasForeignKey(x => x.ReturnRequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RequestedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApprovedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ExecutedByAdminUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("Refunds", table =>
        {
            table.HasCheckConstraint("CK_Refunds_Amounts", "[RequestedAmount] > 0 AND ([ApprovedAmount] IS NULL OR ([ApprovedAmount] > 0 AND [ApprovedAmount] <= [RequestedAmount])) AND ([SucceededAmount] IS NULL OR ([SucceededAmount] > 0 AND [SucceededAmount] <= [ApprovedAmount]))");
        });
    }
    private static void Money(PropertyBuilder<decimal> property, bool required) { property.HasPrecision(18, 2); if (required) property.IsRequired(); }
    private static void Money(PropertyBuilder<decimal?> property) => property.HasPrecision(18, 2);
}

public sealed class RefundAllocationConfiguration : IEntityTypeConfiguration<RefundAllocation>
{
    public void Configure(EntityTypeBuilder<RefundAllocation> builder)
    {
        builder.ConfigurePublicEntity("RefundAllocations");
        builder.Property(x => x.AllocationType).HasConversion<string>().HasMaxLength(24).IsUnicode(false).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.OriginalDiscountAllocation).HasPrecision(18, 2).HasDefaultValue(0m).IsRequired();
        builder.Property(x => x.Quantity);
        builder.HasIndex(x => x.RefundId).HasDatabaseName("IX_RefundAllocations_RefundId");
        builder.HasIndex(x => x.OrderItemId).HasDatabaseName("IX_RefundAllocations_OrderItemId");
        builder.HasIndex(x => x.AllocationType).HasDatabaseName("IX_RefundAllocations_AllocationType");
        builder.HasOne<Refund>().WithMany().HasForeignKey(x => x.RefundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("RefundAllocations", table =>
        {
            table.HasCheckConstraint(
                "CK_RefundAllocations_Amounts",
                "[Amount] > 0 AND [OriginalDiscountAllocation] >= 0");
            table.HasCheckConstraint(
                "CK_RefundAllocations_TypeAndShape",
                "[AllocationType] IN ('ItemRefund', 'OriginalShipping', 'ShippingClawback', 'DiscountClawback', 'AssemblyFee', 'ReturnShipping') AND (([AllocationType] = 'ItemRefund' AND [OrderItemId] IS NOT NULL AND [Quantity] > 0) OR ([AllocationType] <> 'ItemRefund' AND [OrderItemId] IS NULL AND [Quantity] IS NULL))");
        });
    }
}
