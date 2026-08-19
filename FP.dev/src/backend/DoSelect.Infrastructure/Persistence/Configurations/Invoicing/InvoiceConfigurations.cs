using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Refunds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Invoicing;

public sealed class SimulatedInvoiceConfiguration : IEntityTypeConfiguration<SimulatedInvoice>
{
    public void Configure(EntityTypeBuilder<SimulatedInvoice> builder)
    {
        builder.ConfigureMutablePublicEntity("SimulatedInvoices");
        builder.Property(x => x.InvoiceNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.BuyerType).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(x => x.BuyerEmail).HasMaxLength(320);
        builder.Property(x => x.CarrierType).HasMaxLength(30).IsUnicode(false);
        builder.Property(x => x.CarrierValueMasked).HasMaxLength(100);
        builder.Property(x => x.CompanyTaxId).HasMaxLength(20).IsUnicode(false);
        builder.Property(x => x.CompanyName).HasMaxLength(200);
        Money(builder.Property(x => x.NetAmount));
        Money(builder.Property(x => x.TaxAmount));
        Money(builder.Property(x => x.IssuedAmount));
        builder.Property(x => x.Currency).HasColumnType("char(3)").IsUnicode(false).HasDefaultValue("TWD").IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false).HasDefaultValue(SimulatedInvoiceStatus.Pending).IsRequired();
        builder.Property(x => x.IssuedAtUtc).HasPrecision(3);
        builder.Property(x => x.VoidedAtUtc).HasPrecision(3);
        builder.Property(x => x.DemoMarker).HasMaxLength(32).HasDefaultValue(SimulatedInvoice.RequiredDemoMarker).IsRequired();
        builder.HasIndex(x => x.OrderId).IsUnique().HasDatabaseName("UX_SimulatedInvoices_OrderId");
        builder.HasIndex(x => x.InvoiceNumber).IsUnique().HasDatabaseName("UX_SimulatedInvoices_InvoiceNumber");
        builder.HasIndex(x => x.BuyerType).HasDatabaseName("IX_SimulatedInvoices_BuyerType");
        builder.HasIndex(x => x.CompanyTaxId).HasDatabaseName("IX_SimulatedInvoices_CompanyTaxId");
        builder.HasIndex(x => x.Status).HasDatabaseName("IX_SimulatedInvoices_Status");
        builder.HasIndex(x => x.IssuedAtUtc).HasDatabaseName("IX_SimulatedInvoices_IssuedAtUtc");
        builder.HasIndex(x => x.DemoMarker).HasDatabaseName("IX_SimulatedInvoices_DemoMarker");
        builder.HasOne<Order>().WithOne().HasForeignKey<SimulatedInvoice>(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("SimulatedInvoices", table =>
        {
            table.HasCheckConstraint("CK_SimulatedInvoices_Amounts", "[NetAmount] >= 0 AND [TaxAmount] >= 0 AND [IssuedAmount] = [NetAmount] + [TaxAmount]");
            table.HasCheckConstraint("CK_SimulatedInvoices_Currency", "[Currency] = 'TWD'");
            table.HasCheckConstraint("CK_SimulatedInvoices_DemoMarker", "[DemoMarker] = 'DEMO-NOT-A-TAX-INVOICE'");
            table.HasCheckConstraint("CK_SimulatedInvoices_CompanyBuyer", "[BuyerType] <> 'Company' OR ([CompanyTaxId] IS NOT NULL AND [CompanyName] IS NOT NULL)");
        });
    }
    private static void Money(PropertyBuilder<decimal> property) => property.HasPrecision(18, 2).IsRequired();
}

public sealed class SimulatedInvoiceItemConfiguration : IEntityTypeConfiguration<SimulatedInvoiceItem>
{
    public void Configure(EntityTypeBuilder<SimulatedInvoiceItem> builder)
    {
        builder.ConfigurePublicEntity("SimulatedInvoiceItems");
        builder.Property(x => x.ProductNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SkuCodeSnapshot).HasMaxLength(100).IsUnicode(false).IsRequired();
        Money(builder.Property(x => x.UnitPrice)); Money(builder.Property(x => x.DiscountAmount));
        Money(builder.Property(x => x.NetAmount)); Money(builder.Property(x => x.TaxAmount)); Money(builder.Property(x => x.GrossAmount));
        builder.HasIndex(x => x.SimulatedInvoiceId).HasDatabaseName("IX_SimulatedInvoiceItems_SimulatedInvoiceId");
        builder.HasIndex(x => x.OrderItemId).HasDatabaseName("IX_SimulatedInvoiceItems_OrderItemId");
        builder.HasIndex(x => x.SkuCodeSnapshot).HasDatabaseName("IX_SimulatedInvoiceItems_SkuCodeSnapshot");
        builder.HasOne<SimulatedInvoice>().WithMany().HasForeignKey(x => x.SimulatedInvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("SimulatedInvoiceItems", table =>
        {
            table.HasCheckConstraint("CK_SimulatedInvoiceItems_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_SimulatedInvoiceItems_Amounts", "[UnitPrice] >= 0 AND [DiscountAmount] >= 0 AND [NetAmount] >= 0 AND [TaxAmount] >= 0 AND [GrossAmount] = [NetAmount] + [TaxAmount]");
        });
    }
    private static void Money(PropertyBuilder<decimal> property) => property.HasPrecision(18, 2).IsRequired();
}

public sealed class SimulatedInvoiceAllowanceConfiguration : IEntityTypeConfiguration<SimulatedInvoiceAllowance>
{
    public void Configure(EntityTypeBuilder<SimulatedInvoiceAllowance> builder)
    {
        builder.ConfigurePublicEntity("SimulatedInvoiceAllowances");
        builder.Property(x => x.AllowanceNumber).HasMaxLength(32).IsRequired();
        Money(builder.Property(x => x.NetAmount)); Money(builder.Property(x => x.TaxAmount)); Money(builder.Property(x => x.Amount));
        builder.Property(x => x.IssuedAtUtc).HasPrecision(3).IsRequired();
        builder.HasIndex(x => x.SimulatedInvoiceId).HasDatabaseName("IX_SimulatedInvoiceAllowances_SimulatedInvoiceId");
        builder.HasIndex(x => x.RefundId).IsUnique().HasDatabaseName("UX_SimulatedInvoiceAllowances_RefundId");
        builder.HasIndex(x => x.AllowanceNumber).IsUnique().HasDatabaseName("UX_SimulatedInvoiceAllowances_AllowanceNumber");
        builder.HasIndex(x => x.IssuedAtUtc).HasDatabaseName("IX_SimulatedInvoiceAllowances_IssuedAtUtc");
        builder.HasOne<SimulatedInvoice>().WithMany().HasForeignKey(x => x.SimulatedInvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Refund>().WithOne().HasForeignKey<SimulatedInvoiceAllowance>(x => x.RefundId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("SimulatedInvoiceAllowances", table => table.HasCheckConstraint("CK_SimulatedInvoiceAllowances_Amounts", "[NetAmount] >= 0 AND [TaxAmount] >= 0 AND [Amount] > 0 AND [Amount] = [NetAmount] + [TaxAmount]"));
    }
    private static void Money(PropertyBuilder<decimal> property) => property.HasPrecision(18, 2).IsRequired();
}

public sealed class SimulatedInvoiceAllowanceItemConfiguration : IEntityTypeConfiguration<SimulatedInvoiceAllowanceItem>
{
    public void Configure(EntityTypeBuilder<SimulatedInvoiceAllowanceItem> builder)
    {
        builder.ConfigurePublicEntity("SimulatedInvoiceAllowanceItems");
        Money(builder.Property(x => x.NetAmount)); Money(builder.Property(x => x.TaxAmount)); Money(builder.Property(x => x.GrossAmount));
        builder.HasIndex(x => x.AllowanceId).HasDatabaseName("IX_SimulatedInvoiceAllowanceItems_AllowanceId");
        builder.HasIndex(x => x.SimulatedInvoiceItemId).HasDatabaseName("IX_SimulatedInvoiceAllowanceItems_SimulatedInvoiceItemId");
        builder.HasOne<SimulatedInvoiceAllowance>().WithMany().HasForeignKey(x => x.AllowanceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SimulatedInvoiceItem>().WithMany().HasForeignKey(x => x.SimulatedInvoiceItemId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("SimulatedInvoiceAllowanceItems", table =>
        {
            table.HasCheckConstraint("CK_SimulatedInvoiceAllowanceItems_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_SimulatedInvoiceAllowanceItems_Amounts", "[NetAmount] >= 0 AND [TaxAmount] >= 0 AND [GrossAmount] > 0 AND [GrossAmount] = [NetAmount] + [TaxAmount]");
        });
    }
    private static void Money(PropertyBuilder<decimal> property) => property.HasPrecision(18, 2).IsRequired();
}
