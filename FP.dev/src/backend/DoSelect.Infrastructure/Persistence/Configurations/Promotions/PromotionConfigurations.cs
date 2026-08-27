using DoSelect.Domain.Catalog;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Promotions;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Promotions;

public sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ConfigureMutablePublicEntity("Coupons");
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_Coupons_Code");
        builder.Property(x => x.NameZhTw).HasMaxLength(160).IsRequired();
        builder.HasIndex(x => x.NameZhTw).HasDatabaseName("IX_Coupons_NameZhTw");
        ConfigureEnum(builder.Property(x => x.DiscountType), 16);
        Money(builder.Property(x => x.DiscountValue), false);
        Money(builder.Property(x => x.MinimumSpend), false);
        Money(builder.Property(x => x.MaximumDiscount), false);
        builder.Property(x => x.StartsAtUtc).HasPrecision(3).IsRequired();
        builder.Property(x => x.EndsAtUtc).HasPrecision(3).IsRequired();
        builder.HasIndex(x => x.StartsAtUtc).HasDatabaseName("IX_Coupons_StartsAtUtc");
        builder.HasIndex(x => x.EndsAtUtc).HasDatabaseName("IX_Coupons_EndsAtUtc");
        builder.Property(x => x.MemberOnly).HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ExcludeSaleItems).HasDefaultValue(false).IsRequired();
        builder.HasIndex(x => x.MemberOnly).HasDatabaseName("IX_Coupons_MemberOnly");
        ConfigureEnum(builder.Property(x => x.ScopeType), 16, CouponScopeType.All);
        ConfigureEnum(builder.Property(x => x.Status), 16, CouponStatus.Draft);
        builder.Property(x => x.RuleVersion).HasDefaultValue(1).IsRequired();
        builder.ToTable("Coupons", table =>
        {
            table.HasCheckConstraint("CK_Coupons_Period", "[EndsAtUtc] > [StartsAtUtc]");
            table.HasCheckConstraint("CK_Coupons_UsageLimits", "([TotalUsageLimit] IS NULL OR [TotalUsageLimit] > 0) AND ([PerMemberLimit] IS NULL OR [PerMemberLimit] > 0)");
            table.HasCheckConstraint("CK_Coupons_Amounts", "([DiscountValue] IS NULL OR [DiscountValue] >= 0) AND ([MinimumSpend] IS NULL OR [MinimumSpend] >= 0) AND ([MaximumDiscount] IS NULL OR [MaximumDiscount] >= 0)");
            table.HasCheckConstraint("CK_Coupons_Percentage", "[DiscountType] <> 'Percentage' OR ([DiscountValue] >= 0 AND [DiscountValue] <= 1)");
        });
    }

    private static void ConfigureEnum<T>(PropertyBuilder<T> property, int length, T? defaultValue = null) where T : struct, Enum
    {
        property.HasConversion<string>().HasMaxLength(length).IsUnicode(false).IsRequired();
        if (defaultValue.HasValue) property.HasDefaultValue(defaultValue.Value);
    }
    private static void Money(PropertyBuilder<decimal?> property, bool required) { property.HasPrecision(18, 2); if (required) property.IsRequired(); }
}

public sealed class CouponRedemptionConfiguration : IEntityTypeConfiguration<CouponRedemption>
{
    public void Configure(EntityTypeBuilder<CouponRedemption> builder)
    {
        builder.ConfigureMutablePublicEntity("CouponRedemptions");
        builder.Property(x => x.MemberUserId).HasMaxLength(450);
        builder.Property(x => x.GuestUsageKeyHash).HasColumnType("binary(32)");
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false).HasDefaultValue(CouponRedemptionStatus.Reserved).IsRequired();
        builder.Property(x => x.ReservedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(x => x.ReleasedAtUtc).HasPrecision(3);
        builder.Property(x => x.ConsumedAtUtc).HasPrecision(3);
        builder.Property(x => x.ExpiresAtUtc).HasPrecision(3);
        builder.HasIndex(x => new { x.CouponId, x.OrderId }).IsUnique().HasDatabaseName("UX_CouponRedemptions_CouponId_OrderId");
        builder.HasIndex(x => new { x.CouponId, x.Status }).HasDatabaseName("IX_CouponRedemptions_CouponId_Status");
        builder.HasIndex(x => new { x.CouponId, x.MemberUserId, x.Status }).HasDatabaseName("IX_CouponRedemptions_CouponId_MemberUserId_Status");
        builder.HasIndex(x => new { x.CouponId, x.GuestUsageKeyHash, x.Status }).HasDatabaseName("IX_CouponRedemptions_CouponId_GuestUsageKeyHash_Status");
        builder.HasOne<Coupon>().WithMany().HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.MemberUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("CouponRedemptions", table => table.HasCheckConstraint("CK_CouponRedemptions_Owner", "([MemberUserId] IS NOT NULL AND [GuestUsageKeyHash] IS NULL) OR ([MemberUserId] IS NULL AND [GuestUsageKeyHash] IS NOT NULL)"));
    }
}

public sealed class OrderCouponConfiguration : IEntityTypeConfiguration<OrderCoupon>
{
    public void Configure(EntityTypeBuilder<OrderCoupon> builder)
    {
        builder.ConfigurePublicEntity("OrderCoupons");
        builder.Property(x => x.CouponCodeSnapshot).HasMaxLength(64).IsRequired();
        builder.Property(x => x.NameSnapshot).HasMaxLength(160).IsRequired();
        builder.Property(x => x.DiscountType).HasConversion<string>().HasMaxLength(16).IsUnicode(false).IsRequired();
        Money(builder.Property(x => x.DiscountValue));
        Money(builder.Property(x => x.MinimumSpendAmount));
        Money(builder.Property(x => x.AppliedAmount), true);
        Money(builder.Property(x => x.EligibleSubtotal), true);
        builder.Property(x => x.IsFreeShipping).HasDefaultValue(false).IsRequired();
        builder.HasIndex(x => x.OrderId).IsUnique().HasDatabaseName("UX_OrderCoupons_OrderId");
        builder.HasIndex(x => x.RedemptionId).IsUnique().HasFilter("[RedemptionId] IS NOT NULL").HasDatabaseName("UX_OrderCoupons_RedemptionId");
        builder.HasOne<Order>().WithOne().HasForeignKey<OrderCoupon>(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Coupon>().WithMany().HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CouponRedemption>().WithOne().HasForeignKey<OrderCoupon>(x => x.RedemptionId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("OrderCoupons", table => table.HasCheckConstraint("CK_OrderCoupons_Amounts", "([DiscountValue] IS NULL OR [DiscountValue] >= 0) AND ([MinimumSpendAmount] IS NULL OR [MinimumSpendAmount] >= 0) AND [AppliedAmount] >= 0 AND [EligibleSubtotal] >= 0 AND [RuleVersion] > 0"));
    }
    private static void Money(PropertyBuilder<decimal?> property) => property.HasPrecision(18, 2);
    private static void Money(PropertyBuilder<decimal> property, bool required) { property.HasPrecision(18, 2); if (required) property.IsRequired(); }
}

public sealed class CouponCategoryConfiguration : IEntityTypeConfiguration<CouponCategory>
{
    public void Configure(EntityTypeBuilder<CouponCategory> builder)
    {
        builder.ToTable("CouponCategories"); builder.HasKey(x => new { x.CouponId, x.CategoryId }); builder.Property(x => x.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.HasIndex(x => x.CategoryId).HasDatabaseName("IX_CouponCategories_CategoryId");
        builder.HasOne<Coupon>().WithMany().HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CouponProductConfiguration : IEntityTypeConfiguration<CouponProduct>
{
    public void Configure(EntityTypeBuilder<CouponProduct> builder)
    {
        builder.ToTable("CouponProducts"); builder.HasKey(x => new { x.CouponId, x.ProductId }); builder.Property(x => x.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.HasIndex(x => x.ProductId).HasDatabaseName("IX_CouponProducts_ProductId");
        builder.HasOne<Coupon>().WithMany().HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CouponExcludedProductConfiguration : IEntityTypeConfiguration<CouponExcludedProduct>
{
    public void Configure(EntityTypeBuilder<CouponExcludedProduct> builder)
    {
        builder.ToTable("CouponExcludedProducts"); builder.HasKey(x => new { x.CouponId, x.ProductId }); builder.Property(x => x.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.HasIndex(x => x.ProductId).HasDatabaseName("IX_CouponExcludedProducts_ProductId");
        builder.HasOne<Coupon>().WithMany().HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
