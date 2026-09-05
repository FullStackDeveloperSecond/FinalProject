using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Orders;
using DoSelect.Infrastructure.Persistence.Configurations;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Inventory;

public sealed class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> builder)
    {
        builder.ConfigureMutablePublicEntity("InventoryBalances");
        builder.Property(entity => entity.OnHandQuantity).HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.ReservedQuantity).HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.AvailableQuantity)
            .HasComputedColumnSql("[OnHandQuantity] - [ReservedQuantity]", stored: true);
        builder.Property(entity => entity.ReorderLevel).HasDefaultValue(0).IsRequired();
        builder.HasIndex(entity => entity.SkuId)
            .IsUnique()
            .HasDatabaseName("UX_InventoryBalances_SkuId");
        builder.HasIndex(entity => entity.AvailableQuantity)
            .HasDatabaseName("IX_InventoryBalances_AvailableQuantity");
        builder.HasOne<Sku>()
            .WithOne()
            .HasForeignKey<InventoryBalance>(entity => entity.SkuId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("InventoryBalances", table =>
        {
            table.HasCheckConstraint("CK_InventoryBalances_OnHand", "[OnHandQuantity] >= 0");
            table.HasCheckConstraint(
                "CK_InventoryBalances_Reserved",
                "[ReservedQuantity] >= 0 AND [ReservedQuantity] <= [OnHandQuantity]");
            table.HasCheckConstraint("CK_InventoryBalances_ReorderLevel", "[ReorderLevel] >= 0");
        });
    }
}

public sealed class InventoryReservationConfiguration
    : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ConfigureMutablePublicEntity("InventoryReservations");
        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.ExpiresAtUtc).HasPrecision(3);
        builder.Property(entity => entity.ReleasedAtUtc).HasPrecision(3);
        builder.Property(entity => entity.ReleaseReason).HasMaxLength(32).IsUnicode(false);
        builder.HasIndex(entity => new { entity.OrderId, entity.SkuId })
            .HasDatabaseName("IX_InventoryReservations_OrderId_SkuId");
        builder.HasIndex(entity => new { entity.Status, entity.ExpiresAtUtc })
            .HasDatabaseName("IX_InventoryReservations_Status_ExpiresAtUtc");
        builder.HasOne<Sku>()
            .WithMany()
            .HasForeignKey(entity => entity.SkuId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(entity => entity.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("InventoryReservations", table =>
            table.HasCheckConstraint("CK_InventoryReservations_Quantity", "[Quantity] > 0"));
    }
}

public sealed class InventoryMovementConfiguration
    : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.ConfigurePublicEntity("InventoryMovements");
        builder.Property(entity => entity.MovementType).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.ReasonCode).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.ReferenceType).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.ActorUserId).HasMaxLength(450);
        builder.Property(entity => entity.OccurredAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.UnitCostSnapshot).HasPrecision(18, 2);
        builder.Property(entity => entity.AdjustmentNote).HasMaxLength(InventoryMovement.MaxAdjustmentNoteLength);
        builder.HasIndex(entity => new { entity.SkuId, entity.OccurredAtUtc })
            .HasDatabaseName("IX_InventoryMovements_SkuId_OccurredAtUtc");
        builder.HasOne<Sku>()
            .WithMany()
            .HasForeignKey(entity => entity.SkuId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryReservation>()
            .WithMany()
            .HasForeignKey(entity => entity.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("InventoryMovements", table =>
        {
            table.HasCheckConstraint(
                "CK_InventoryMovements_OnHand",
                "[BeforeOnHand] + [OnHandDelta] = [AfterOnHand]");
            table.HasCheckConstraint(
                "CK_InventoryMovements_Reserved",
                "[BeforeReserved] + [ReservedDelta] = [AfterReserved]");
        });
    }
}

public sealed class InventoryReconciliationCaseConfiguration
    : IEntityTypeConfiguration<InventoryReconciliationCase>
{
    public void Configure(EntityTypeBuilder<InventoryReconciliationCase> builder)
    {
        builder.ConfigureMutablePublicEntity("InventoryReconciliationCases");
        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.DetectedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.AcknowledgedBy).HasMaxLength(450);
        builder.Property(entity => entity.ResolvedByAdminUserId).HasMaxLength(450);
        builder.Property(entity => entity.ResolutionReason).HasMaxLength(1000);
        builder.Property(entity => entity.ResolvedAtUtc).HasPrecision(3);
        builder.HasIndex(entity => entity.SkuId)
            .IsUnique()
            .HasFilter("[Status] = 'Open'")
            .HasDatabaseName("UX_InventoryReconciliationCases_SkuId_Open");
        builder.HasIndex(entity => new { entity.Status, entity.DetectedAtUtc })
            .HasDatabaseName("IX_InventoryReconciliationCases_Status_DetectedAtUtc");
        builder.HasOne<Sku>()
            .WithMany()
            .HasForeignKey(entity => entity.SkuId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryMovement>()
            .WithMany()
            .HasForeignKey(entity => entity.ResolutionMovementId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.AcknowledgedBy)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.ResolvedByAdminUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("InventoryReconciliationCases", table =>
        {
            table.HasCheckConstraint(
                "CK_InventoryReconciliationCases_Quantities",
                "[ExpectedOnHand] >= 0 AND [ActualOnHand] >= 0 AND [ExpectedReserved] >= 0 AND [ActualReserved] >= 0");
            table.HasCheckConstraint(
                "CK_InventoryReconciliationCases_Resolution",
                "([Status] = 'Resolved' AND [ResolutionMovementId] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL) OR ([Status] = 'Dismissed' AND [ResolutionMovementId] IS NULL AND [ResolutionReason] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL) OR [Status] IN ('Open','Acknowledged')");
        });
    }
}
