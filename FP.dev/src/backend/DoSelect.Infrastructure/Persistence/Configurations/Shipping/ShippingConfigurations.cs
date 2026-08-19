using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence.Configurations;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Shipping;

public sealed class ShippingMethodConfiguration : IEntityTypeConfiguration<ShippingMethod>
{
    public void Configure(EntityTypeBuilder<ShippingMethod> builder)
    {
        builder.ConfigureMutablePublicEntity("ShippingMethods");
        builder.Property(entity => entity.Code).HasMaxLength(64).IsRequired();
        builder.HasIndex(entity => entity.Code)
            .IsUnique()
            .HasDatabaseName("UX_ShippingMethods_Code");
        builder.Property(entity => entity.NameZhTw).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(entity => entity.SortOrder).HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.Kind).HasMaxLength(24).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.BaseFee).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.FreeShippingThreshold).HasPrecision(18, 2);
        builder.ToTable("ShippingMethods", table =>
        {
            table.HasCheckConstraint(
                "CK_ShippingMethods_Fees",
                "[BaseFee] >= 0 AND ([FreeShippingThreshold] IS NULL OR [FreeShippingThreshold] >= 0)");
            table.HasCheckConstraint(
                "CK_ShippingMethods_CodCapability",
                "NOT ([AllowsCod] = 1 AND [RequiresPrepayment] = 1)");
        });
    }
}

public sealed class ShippingProviderProfileConfiguration
    : IEntityTypeConfiguration<ShippingProviderProfile>
{
    public void Configure(EntityTypeBuilder<ShippingProviderProfile> builder)
    {
        builder.ConfigureMutablePublicEntity("ShippingProviderProfiles");
        builder.Property(entity => entity.ProviderCode).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(16).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.EffectiveFromUtc).HasPrecision(3);
        builder.Property(entity => entity.EffectiveToUtc).HasPrecision(3);
        builder.Property(entity => entity.ConfigurationJson).HasMaxLength(4000).IsRequired();
        builder.HasIndex(entity => new { entity.ProviderCode, entity.Version })
            .IsUnique()
            .HasDatabaseName("UX_ProviderProfiles_ProviderCode_Version");
        builder.HasIndex(entity => entity.ProviderCode)
            .IsUnique()
            .HasFilter("[Status] = 'Published'")
            .HasDatabaseName("UX_ProviderProfiles_ProviderCode_Published");
        builder.ToTable("ShippingProviderProfiles", table =>
        {
            table.HasCheckConstraint("CK_ShippingProviderProfiles_Version", "[Version] > 0");
            table.HasCheckConstraint(
                "CK_ShippingProviderProfiles_SchemaVersion",
                "[SchemaVersion] > 0");
            table.HasCheckConstraint(
                "CK_ShippingProviderProfiles_Period",
                "[EffectiveFromUtc] IS NULL OR [EffectiveToUtc] IS NULL OR [EffectiveToUtc] > [EffectiveFromUtc]");
        });
    }
}

public sealed class PackageLimitVersionConfiguration
    : IEntityTypeConfiguration<PackageLimitVersion>
{
    public void Configure(EntityTypeBuilder<PackageLimitVersion> builder)
    {
        builder.ConfigureMutablePublicEntity("PackageLimitVersions");
        builder.Property(entity => entity.MaxWeightKg).HasPrecision(10, 3).IsRequired();
        builder.Property(entity => entity.MaxLengthCm).HasPrecision(10, 2).IsRequired();
        builder.Property(entity => entity.MaxWidthCm).HasPrecision(10, 2).IsRequired();
        builder.Property(entity => entity.MaxHeightCm).HasPrecision(10, 2).IsRequired();
        builder.Property(entity => entity.MaxTotalCm).HasPrecision(10, 2).IsRequired();
        builder.Property(entity => entity.MaxDeclaredValue).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.EffectiveFromUtc).HasPrecision(3);
        builder.Property(entity => entity.EffectiveToUtc).HasPrecision(3);
        builder.HasIndex(entity => new { entity.ProviderProfileId, entity.Version })
            .HasDatabaseName("IX_PackageLimitVersions_ProviderProfileId_Version");
        builder.HasOne<ShippingProviderProfile>()
            .WithMany()
            .HasForeignKey(entity => entity.ProviderProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("PackageLimitVersions", table =>
        {
            table.HasCheckConstraint("CK_PackageLimitVersions_Version", "[Version] > 0");
            table.HasCheckConstraint(
                "CK_PackageLimitVersions_Limits",
                "[MaxWeightKg] > 0 AND [MaxLengthCm] > 0 AND [MaxWidthCm] > 0 AND [MaxHeightCm] > 0 AND [MaxTotalCm] > 0 AND [MaxDeclaredValue] > 0");
            table.HasCheckConstraint(
                "CK_PackageLimitVersions_Period",
                "[EffectiveFromUtc] IS NULL OR [EffectiveToUtc] IS NULL OR [EffectiveToUtc] > [EffectiveFromUtc]");
        });
    }
}

public sealed class ConvenienceStoreConfiguration : IEntityTypeConfiguration<ConvenienceStore>
{
    public void Configure(EntityTypeBuilder<ConvenienceStore> builder)
    {
        builder.ConfigureMutablePublicEntity("ConvenienceStores");
        builder.Property(entity => entity.ProviderCode).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.StoreCode).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.StoreName).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.Address).HasMaxLength(500).IsRequired();
        builder.Property(entity => entity.City).HasMaxLength(60).IsRequired();
        builder.Property(entity => entity.District).HasMaxLength(60).IsRequired();
        builder.Property(entity => entity.IsDemoData).HasDefaultValue(false).IsRequired();
        builder.HasIndex(entity => new { entity.ProviderCode, entity.StoreCode })
            .IsUnique()
            .HasDatabaseName("UX_ConvenienceStores_ProviderCode_StoreCode");
        builder.HasIndex(entity => new { entity.City, entity.District, entity.IsActive })
            .HasDatabaseName("IX_ConvenienceStores_City_District_IsActive");
    }
}

public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ConfigureMutablePublicEntity("Shipments");
        builder.Property(entity => entity.ShipmentNumber).HasMaxLength(64).IsRequired();
        builder.HasIndex(entity => entity.ShipmentNumber)
            .IsUnique()
            .HasDatabaseName("UX_Shipments_ShipmentNumber");
        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.TrackingNumber).HasMaxLength(128);
        builder.Property(entity => entity.FeeSnapshot).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.ShippedAtUtc).HasPrecision(3);
        builder.Property(entity => entity.DeliveredAtUtc).HasPrecision(3);
        builder.HasIndex(entity => entity.OrderId).HasDatabaseName("IX_Shipments_OrderId");
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(entity => entity.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ShippingMethod>()
            .WithMany()
            .HasForeignKey(entity => entity.ShippingMethodId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ShippingProviderProfile>()
            .WithMany()
            .HasForeignKey(entity => entity.ProviderProfileVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConvenienceStore>()
            .WithMany()
            .HasForeignKey(entity => entity.ConvenienceStoreId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("Shipments", table =>
            table.HasCheckConstraint("CK_Shipments_FeeSnapshot", "[FeeSnapshot] >= 0"));
    }
}

public sealed class ShipmentStatusHistoryConfiguration
    : IEntityTypeConfiguration<ShipmentStatusHistory>
{
    public void Configure(EntityTypeBuilder<ShipmentStatusHistory> builder)
    {
        builder.ConfigurePublicEntity("ShipmentStatusHistories");
        builder.Property(entity => entity.FromStatus)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false);
        builder.Property(entity => entity.ToStatus)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.ExternalEventId).HasMaxLength(128);
        builder.Property(entity => entity.OccurredAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.ActorUserId).HasMaxLength(450);
        builder.HasIndex(entity => new { entity.ShipmentId, entity.OccurredAtUtc })
            .HasDatabaseName("IX_ShipmentStatusHistories_ShipmentId_OccurredAtUtc");
        builder.HasIndex(entity => entity.ExternalEventId)
            .IsUnique()
            .HasFilter("[ExternalEventId] IS NOT NULL")
            .HasDatabaseName("UX_ShipmentStatusHistories_ExternalEventId");
        builder.HasOne<Shipment>()
            .WithMany()
            .HasForeignKey(entity => entity.ShipmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
