using DoSelect.Domain.Auditing;
using DoSelect.Domain.Ai;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Idempotency;
using DoSelect.Domain.Imports;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Notifications;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Outbox;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Promotions;
using DoSelect.Domain.Reports;
using DoSelect.Domain.Reviews;
using DoSelect.Domain.Refunds;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Shopping;
using DoSelect.Domain.Shipping;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence;

public sealed class DoSelectDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole, string>
{
    public DoSelectDbContext(DbContextOptions<DoSelectDbContext> options)
        : base(options)
    {
    }

    public DbSet<MemberProfile> MemberProfiles => Set<MemberProfile>();

    public DbSet<AiConsentRecord> AiConsentRecords => Set<AiConsentRecord>();

    public DbSet<AiUsageLedgerEntry> AiUsageLedger => Set<AiUsageLedgerEntry>();

    public DbSet<AiConversation> AiConversations => Set<AiConversation>();

    public DbSet<AiInteraction> AiInteractions => Set<AiInteraction>();

    public DbSet<AiCitation> AiCitations => Set<AiCitation>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<AdminProfile> AdminProfiles => Set<AdminProfile>();

    public DbSet<MemberAddress> MemberAddresses => Set<MemberAddress>();

    public DbSet<Favorite> Favorites => Set<Favorite>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();

    public DbSet<GuestOrderAccessRequest> GuestOrderAccessRequests =>
        Set<GuestOrderAccessRequest>();

    public DbSet<GuestOrderAccessToken> GuestOrderAccessTokens =>
        Set<GuestOrderAccessToken>();

    public DbSet<AssemblyJob> AssemblyJobs => Set<AssemblyJob>();

    public DbSet<AssemblyJobStatusHistory> AssemblyJobStatusHistories =>
        Set<AssemblyJobStatusHistory>();

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Sku> Skus => Set<Sku>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<ProductTag> ProductTags => Set<ProductTag>();

    public DbSet<BrandTranslation> BrandTranslations => Set<BrandTranslation>();

    public DbSet<CategoryTranslation> CategoryTranslations => Set<CategoryTranslation>();

    public DbSet<ProductTranslation> ProductTranslations => Set<ProductTranslation>();

    public DbSet<SkuTranslation> SkuTranslations => Set<SkuTranslation>();

    public DbSet<SpecificationDefinitionTranslation> SpecificationDefinitionTranslations =>
        Set<SpecificationDefinitionTranslation>();

    public DbSet<SpecificationOptionTranslation> SpecificationOptionTranslations =>
        Set<SpecificationOptionTranslation>();

    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<MeasurementUnit> MeasurementUnits => Set<MeasurementUnit>();

    public DbSet<SpecificationDefinition> SpecificationDefinitions =>
        Set<SpecificationDefinition>();

    public DbSet<SpecificationOption> SpecificationOptions => Set<SpecificationOption>();

    public DbSet<SpecificationSource> SpecificationSources => Set<SpecificationSource>();

    public DbSet<SkuSpecificationValue> SkuSpecificationValues =>
        Set<SkuSpecificationValue>();

    public DbSet<SkuSpecificationOptionSelection> SkuSpecificationOptionSelections =>
        Set<SkuSpecificationOptionSelection>();

    public DbSet<SalePrice> SalePrices => Set<SalePrice>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<ImportRow> ImportRows => Set<ImportRow>();

    public DbSet<Cart> Carts => Set<Cart>();

    public DbSet<CartItem> CartItems => Set<CartItem>();

    public DbSet<CartMergeConflict> CartMergeConflicts => Set<CartMergeConflict>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<EmailDelivery> EmailDeliveries => Set<EmailDelivery>();

    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();

    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();

    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();

    public DbSet<InventoryReconciliationCase> InventoryReconciliationCases =>
        Set<InventoryReconciliationCase>();

    public DbSet<ShippingMethod> ShippingMethods => Set<ShippingMethod>();

    public DbSet<ShippingProviderProfile> ShippingProviderProfiles =>
        Set<ShippingProviderProfile>();

    public DbSet<PackageLimitVersion> PackageLimitVersions => Set<PackageLimitVersion>();

    public DbSet<ConvenienceStore> ConvenienceStores => Set<ConvenienceStore>();

    public DbSet<Shipment> Shipments => Set<Shipment>();

    public DbSet<ShipmentStatusHistory> ShipmentStatusHistories =>
        Set<ShipmentStatusHistory>();

    public DbSet<BuildList> BuildLists => Set<BuildList>();

    public DbSet<BuildListItem> BuildListItems => Set<BuildListItem>();

    public DbSet<BuildShareToken> BuildShareTokens => Set<BuildShareToken>();

    public DbSet<CompatibilityRuleSetting> CompatibilityRuleSettings =>
        Set<CompatibilityRuleSetting>();

    public DbSet<CompatibilityCheckRun> CompatibilityCheckRuns =>
        Set<CompatibilityCheckRun>();

    public DbSet<CompatibilityCheckResult> CompatibilityCheckResults =>
        Set<CompatibilityCheckResult>();

    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();

    public DbSet<ReviewImage> ReviewImages => Set<ReviewImage>();

    public DbSet<ProductReviewRevision> ProductReviewRevisions =>
        Set<ProductReviewRevision>();

    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponRedemption> CouponRedemptions => Set<CouponRedemption>();
    public DbSet<OrderCoupon> OrderCoupons => Set<OrderCoupon>();
    public DbSet<CouponCategory> CouponCategories => Set<CouponCategory>();
    public DbSet<CouponProduct> CouponProducts => Set<CouponProduct>();
    public DbSet<CouponExcludedProduct> CouponExcludedProducts => Set<CouponExcludedProduct>();
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<RefundAllocation> RefundAllocations => Set<RefundAllocation>();
    public DbSet<SimulatedInvoice> SimulatedInvoices => Set<SimulatedInvoice>();
    public DbSet<SimulatedInvoiceItem> SimulatedInvoiceItems => Set<SimulatedInvoiceItem>();
    public DbSet<SimulatedInvoiceAllowance> SimulatedInvoiceAllowances => Set<SimulatedInvoiceAllowance>();
    public DbSet<SimulatedInvoiceAllowanceItem> SimulatedInvoiceAllowanceItems => Set<SimulatedInvoiceAllowanceItem>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<SupportAttachment> SupportAttachments => Set<SupportAttachment>();
    public DbSet<SupportAssignmentHistory> SupportAssignmentHistories => Set<SupportAssignmentHistory>();
    public DbSet<SupportStatusHistory> SupportStatusHistories => Set<SupportStatusHistory>();
    public DbSet<SupportSlaEvent> SupportSlaEvents => Set<SupportSlaEvent>();
    public DbSet<SupportSummary> SupportSummaries => Set<SupportSummary>();
    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();
    public DbSet<ReturnItem> ReturnItems => Set<ReturnItem>();
    public DbSet<ReturnInspection> ReturnInspections => Set<ReturnInspection>();
    public DbSet<ReturnAttachment> ReturnAttachments => Set<ReturnAttachment>();
    public DbSet<ReturnAssignmentHistory> ReturnAssignmentHistories => Set<ReturnAssignmentHistory>();
    public DbSet<ReturnStatusHistory> ReturnStatusHistories => Set<ReturnStatusHistory>();
    public DbSet<ReturnShipment> ReturnShipments => Set<ReturnShipment>();
    public DbSet<ReturnShipmentEvent> ReturnShipmentEvents => Set<ReturnShipmentEvent>();
    public DbSet<ReportCase> ReportCases => Set<ReportCase>();
    public DbSet<ReportAttachment> ReportAttachments => Set<ReportAttachment>();
    public DbSet<ReportAssignmentHistory> ReportAssignmentHistories => Set<ReportAssignmentHistory>();
    public DbSet<ReportStatusHistory> ReportStatusHistories => Set<ReportStatusHistory>();
    public DbSet<CaseWorkbenchRow> CaseWorkbench => Set<CaseWorkbenchRow>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(DoSelectDbContext).Assembly);
    }
}
