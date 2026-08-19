using DoSelect.Domain.Catalog;
using DoSelect.Domain.Shopping;
using DoSelect.Infrastructure.Persistence.Configurations;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Shopping;

public sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ConfigureMutablePublicEntity("Carts");
        builder.Property(entity => entity.OwnerUserId).HasMaxLength(450);
        builder.Property(entity => entity.GuestCartKeyHash).HasColumnType("binary(32)");
        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.ExpiresAtUtc).HasPrecision(3).IsRequired();
        builder.HasIndex(entity => entity.OwnerUserId)
            .IsUnique()
            .HasFilter("[OwnerUserId] IS NOT NULL AND [Status] = 'Active'")
            .HasDatabaseName("UX_Carts_OwnerUserId_Active");
        builder.HasIndex(entity => entity.GuestCartKeyHash)
            .IsUnique()
            .HasFilter("[GuestCartKeyHash] IS NOT NULL AND [Status] = 'Active'")
            .HasDatabaseName("UX_Carts_GuestCartKeyHash_Active");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("Carts", table =>
            table.HasCheckConstraint(
                "CK_Carts_ExactlyOneOwner",
                "([OwnerUserId] IS NOT NULL AND [GuestCartKeyHash] IS NULL) OR ([OwnerUserId] IS NULL AND [GuestCartKeyHash] IS NOT NULL)"));
    }
}

public sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ConfigureMutablePublicEntity("CartItems");
        builder.HasIndex(entity => new
        {
            entity.CartId,
            entity.SkuId,
            entity.AssemblyGroupKey,
        })
            .IsUnique()
            .HasDatabaseName("UX_CartItems_CartId_SkuId_AssemblyGroupKey");
        builder.HasOne<Cart>()
            .WithMany()
            .HasForeignKey(entity => entity.CartId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Sku>()
            .WithMany()
            .HasForeignKey(entity => entity.SkuId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("CartItems", table =>
            table.HasCheckConstraint(
                "CK_CartItems_Quantity",
                "[Quantity] >= 1 AND [Quantity] <= 99"));
    }
}
