using DoSelect.Domain.Catalog;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Reviews;
using DoSelect.Infrastructure.Persistence.Configurations;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Reviews;

public sealed class ProductReviewConfiguration : IEntityTypeConfiguration<ProductReview>
{
    public void Configure(EntityTypeBuilder<ProductReview> builder)
    {
        builder.ConfigureMutablePublicEntity("ProductReviews");
        builder.Property(entity => entity.MemberUserId).HasMaxLength(450).IsRequired();
        builder.Property(entity => entity.Title).HasMaxLength(160);
        builder.Property(entity => entity.Content).HasMaxLength(2000).IsRequired();
        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .HasDefaultValue(ProductReviewStatus.Draft)
            .IsRequired();
        builder.Property(entity => entity.ReviewedByAdminUserId).HasMaxLength(450);
        builder.Property(entity => entity.ReviewedAtUtc).HasPrecision(3);
        builder.Property(entity => entity.RejectionReason).HasMaxLength(500);
        builder.HasIndex(entity => new { entity.MemberUserId, entity.CreatedAtUtc })
            .HasDatabaseName("IX_ProductReviews_MemberUserId_CreatedAtUtc");
        builder.HasIndex(entity => entity.OrderItemId)
            .IsUnique()
            .HasDatabaseName("UX_ProductReviews_OrderItemId");
        builder.HasIndex(entity => new { entity.ProductId, entity.Status })
            .HasDatabaseName("IX_ProductReviews_ProductId_Status");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.MemberUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.ReviewedByAdminUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderItem>()
            .WithOne()
            .HasForeignKey<ProductReview>(entity => entity.OrderItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("ProductReviews", table =>
        {
            table.HasCheckConstraint("CK_ProductReviews_Rating", "[Rating] >= 1 AND [Rating] <= 5");
            table.HasCheckConstraint(
                "CK_ProductReviews_RejectionReason",
                "[Status] <> 'Rejected' OR [RejectionReason] IS NOT NULL");
        });
    }
}

public sealed class ReviewImageConfiguration : IEntityTypeConfiguration<ReviewImage>
{
    public void Configure(EntityTypeBuilder<ReviewImage> builder)
    {
        builder.ConfigureMutableEntity("ReviewImages");
        builder.Property(entity => entity.StorageKey).HasMaxLength(500).IsRequired();
        builder.HasIndex(entity => entity.StorageKey)
            .IsUnique()
            .HasDatabaseName("UX_ReviewImages_StorageKey");
        builder.Property(entity => entity.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(entity => entity.MediaType).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.Sha256).HasColumnType("binary(32)").IsRequired();
        builder.Property(entity => entity.ScanStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasDefaultValue(ReviewImageScanStatus.Pending)
            .IsRequired();
        builder.Property(entity => entity.SortOrder).HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.DeletedAtUtc).HasPrecision(3);
        builder.HasIndex(entity => entity.ProductReviewId)
            .HasDatabaseName("IX_ReviewImages_ProductReviewId");
        builder.HasOne<ProductReview>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductReviewId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("ReviewImages", table =>
            table.HasCheckConstraint(
                "CK_ReviewImages_FileSize",
                "[FileSizeBytes] >= 1 AND [FileSizeBytes] <= 5242880"));
    }
}

public sealed class ProductReviewRevisionConfiguration
    : IEntityTypeConfiguration<ProductReviewRevision>
{
    public void Configure(EntityTypeBuilder<ProductReviewRevision> builder)
    {
        builder.ToTable("ProductReviewRevisions");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).UseIdentityColumn();
        builder.Property(entity => entity.Title).HasMaxLength(160);
        builder.Property(entity => entity.Content).HasMaxLength(2000).IsRequired();
        builder.Property(entity => entity.PublishedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.SupersededAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.SupersededReason)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.HasIndex(entity => new { entity.ProductReviewId, entity.SupersededAtUtc })
            .HasDatabaseName("IX_ProductReviewRevisions_ProductReviewId_SupersededAtUtc");
        builder.HasOne<ProductReview>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductReviewId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("ProductReviewRevisions", table =>
            table.HasCheckConstraint(
                "CK_ProductReviewRevisions_Rating",
                "[Rating] >= 1 AND [Rating] <= 5"));
    }
}
