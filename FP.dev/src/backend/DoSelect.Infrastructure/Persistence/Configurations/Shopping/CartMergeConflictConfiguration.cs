using DoSelect.Domain.Shopping;
using DoSelect.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Shopping;

public sealed class CartMergeConflictConfiguration
    : IEntityTypeConfiguration<CartMergeConflict>
{
    public void Configure(EntityTypeBuilder<CartMergeConflict> builder)
    {
        builder.ConfigureMutablePublicEntity("CartMergeConflicts");
        builder.Property(entity => entity.Reason).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.ResolvedAtUtc).HasPrecision(3);
        builder.Property(entity => entity.ResolutionCode).HasMaxLength(64).IsUnicode(false);
        builder.Ignore(entity => entity.IsBlocking);
        builder.HasIndex(entity => new { entity.MemberCartId, entity.GuestItemPublicId })
            .IsUnique()
            .HasFilter("[ResolvedAtUtc] IS NULL")
            .HasDatabaseName("UX_CartMergeConflicts_MemberCart_GuestItem_Unresolved");
        builder.HasIndex(entity => new { entity.MemberCartId, entity.ResolvedAtUtc })
            .HasDatabaseName("IX_CartMergeConflicts_MemberCart_ResolvedAtUtc");
        builder.HasOne<Cart>()
            .WithMany()
            .HasForeignKey(entity => entity.MemberCartId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Cart>()
            .WithMany()
            .HasForeignKey(entity => entity.GuestCartId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("CartMergeConflicts", table =>
        {
            table.HasCheckConstraint(
                "CK_CartMergeConflicts_DifferentCarts",
                "[MemberCartId] <> [GuestCartId]");
            table.HasCheckConstraint(
                "CK_CartMergeConflicts_Quantities",
                "[GuestQuantity] >= 1 AND [GuestQuantity] <= 99 AND [MemberQuantity] >= 0 AND [MemberQuantity] <= 99 AND [AcceptedQuantity] >= 0 AND [AcceptedQuantity] <= 99");
            table.HasCheckConstraint(
                "CK_CartMergeConflicts_Resolution",
                "([ResolvedAtUtc] IS NULL AND [ResolutionCode] IS NULL) OR ([ResolvedAtUtc] IS NOT NULL AND [ResolutionCode] IS NOT NULL)");
        });
    }
}
