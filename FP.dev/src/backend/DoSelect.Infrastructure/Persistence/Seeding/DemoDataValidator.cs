using System.Data;
using DoSelect.Application.OperationalReports;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Reviews;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.OperationalReports;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Seeding;

public sealed class DemoDataValidator(DoSelectDbContext dbContext)
{
    public async Task<DemoDataValidationResult> ValidateAsync(
        CancellationToken cancellationToken = default)
    {
        DemoDatabaseSafety.EnsureAllowedLocalDatabase(dbContext);
        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The demo database is unavailable. Validation never creates or migrates a database.");
        }

        var pendingMigrations = (await dbContext.Database
            .GetPendingMigrationsAsync(cancellationToken))
            .ToArray();
        var counts = await DemoDataSnapshotReader.ReadCountsAsync(dbContext, cancellationToken);
        var distribution = await DemoDataSnapshotReader.ReadDistributionAsync(
            dbContext,
            cancellationToken);
        var hasExpectedMarker = await dbContext.Brands.AnyAsync(
            brand => brand.Code == DemoSeedManifest.MarkerBrandCode,
            cancellationToken);
        var constraintViolations = await ReadConstraintViolationsAsync(cancellationToken);
        var negativeInventoryRows = await dbContext.InventoryBalances.CountAsync(
            balance => balance.OnHandQuantity < 0 ||
                balance.ReservedQuantity < 0 ||
                balance.AvailableQuantity < 0,
            cancellationToken);
        var invalidEnumRows = await CountInvalidEnumRowsAsync(cancellationToken);
        var invalidWorkflowRows = await CountInvalidWorkflowRowsAsync(cancellationToken);
        var reportBaselines = await ReadReportBaselinesAsync(cancellationToken);

        var checks = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["schemaCurrent"] = pendingMigrations.Length == 0,
            ["featureProfileMarker"] = hasExpectedMarker,
            ["mainBusinessRecordTotal"] =
                counts.Values.Sum() == DemoSeedManifest.MainBusinessRecordTotal,
            ["entityCounts"] = DemoDataSnapshotReader.CountsMatchManifest(counts),
            ["specialDistributions"] =
                DemoDataSnapshotReader.DistributionMatchesManifest(distribution),
            ["orphanRows"] = constraintViolations.ForeignKey == 0,
            ["databaseConstraints"] = constraintViolations.Total == 0,
            ["negativeInventoryRows"] = negativeInventoryRows == 0,
            ["invalidEnumRows"] = invalidEnumRows == 0,
            ["invalidWorkflowRows"] = invalidWorkflowRows == 0,
            ["reportBaselines"] = ReportBaselinesMatchManifest(reportBaselines),
        };

        var failures = checks
            .Where(check => !check.Value)
            .Select(check => check.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        return new DemoDataValidationResult(
            DemoSeedManifest.Version,
            DemoSeedManifest.RandomSeed,
            DemoSeedManifest.PeriodStartUtc,
            DemoSeedManifest.PeriodEndUtc,
            counts.Values.Sum(),
            failures.Length == 0,
            checks,
            failures,
            counts,
            distribution,
            new DemoDataIntegritySummary(
                constraintViolations.Total,
                constraintViolations.ForeignKey,
                negativeInventoryRows,
                invalidEnumRows,
                invalidWorkflowRows),
            reportBaselines);
    }

    private async Task<DemoConstraintViolationCounts> ReadConstraintViolationsAsync(
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var foreignKeyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var foreignKeys = connection.CreateCommand())
            {
                foreignKeys.CommandText = "SELECT [name] FROM sys.foreign_keys";
                await using var reader = await foreignKeys.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    foreignKeyNames.Add(reader.GetString(0));
                }
            }

            var total = 0;
            var foreignKey = 0;
            await using (var constraints = connection.CreateCommand())
            {
                constraints.CommandText = "DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS";
                await using var reader = await constraints.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    total++;
                    var constraintName = reader["Constraint"] as string;
                    if (constraintName is not null && foreignKeyNames.Contains(constraintName))
                    {
                        foreignKey++;
                    }
                }
            }

            return new DemoConstraintViolationCounts(total, foreignKey);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<int> CountInvalidEnumRowsAsync(CancellationToken cancellationToken)
    {
        var orderStatuses = Enum.GetValues<OrderStatus>();
        var paymentStatuses = Enum.GetValues<PaymentStatus>();
        var fulfillmentStatuses = Enum.GetValues<FulfillmentStatus>();
        var attemptStatuses = Enum.GetValues<PaymentAttemptStatus>();
        var refundStatuses = Enum.GetValues<RefundStatus>();
        var returnStatuses = Enum.GetValues<ReturnRequestStatus>();
        var supportStatuses = Enum.GetValues<SupportTicketStatus>();
        var reviewStatuses = Enum.GetValues<ProductReviewStatus>();
        var skuStatuses = Enum.GetValues<SkuStatus>();

        return
            await dbContext.Orders.CountAsync(order =>
                !orderStatuses.Contains(order.OrderStatus) ||
                !paymentStatuses.Contains(order.PaymentStatus) ||
                !fulfillmentStatuses.Contains(order.FulfillmentStatus), cancellationToken) +
            await dbContext.PaymentAttempts.CountAsync(attempt =>
                !attemptStatuses.Contains(attempt.Status), cancellationToken) +
            await dbContext.Shipments.CountAsync(shipment =>
                !fulfillmentStatuses.Contains(shipment.Status), cancellationToken) +
            await dbContext.Refunds.CountAsync(refund =>
                !refundStatuses.Contains(refund.Status), cancellationToken) +
            await dbContext.ReturnRequests.CountAsync(request =>
                !returnStatuses.Contains(request.Status), cancellationToken) +
            await dbContext.SupportTickets.CountAsync(ticket =>
                !supportStatuses.Contains(ticket.Status), cancellationToken) +
            await dbContext.ProductReviews.CountAsync(review =>
                !reviewStatuses.Contains(review.Status), cancellationToken) +
            await dbContext.Skus.CountAsync(sku =>
                !skuStatuses.Contains(sku.Status), cancellationToken);
    }

    private async Task<int> CountInvalidWorkflowRowsAsync(CancellationToken cancellationToken) =>
        await dbContext.Orders.CountAsync(order =>
            (order.OrderStatus == OrderStatus.Completed &&
                (order.PaymentStatus != PaymentStatus.Paid ||
                 order.FulfillmentStatus != FulfillmentStatus.Delivered)) ||
            (order.OrderStatus == OrderStatus.Cancelled && order.PaidAmount != 0m),
            cancellationToken) +
        await dbContext.PaymentAttempts.CountAsync(attempt =>
            (attempt.Status == PaymentAttemptStatus.Paid && attempt.PaidAtUtc == null) ||
            (attempt.Status == PaymentAttemptStatus.Failed &&
                (attempt.FailedAtUtc == null || attempt.FailureCode == null)),
            cancellationToken) +
        await dbContext.ReturnRequests.CountAsync(request =>
            request.Status == ReturnRequestStatus.Completed &&
            !dbContext.Refunds.Any(refund =>
                refund.ReturnRequestId == request.Id && refund.Status == RefundStatus.Succeeded),
            cancellationToken);

    private async Task<IReadOnlyDictionary<string, decimal?>> ReadReportBaselinesAsync(
        CancellationToken cancellationToken)
    {
        var reportQueryService = new EfOperationalReportQueryService(
            dbContext,
            new FixedUtcTimeProvider(DemoSeedManifest.PeriodEndUtc.AddSeconds(1)));
        var query = OperationalReportQueryValidator.Normalize(new ReportQuery(
            DateOnly.FromDateTime(DemoSeedManifest.PeriodStartUtc),
            DateOnly.FromDateTime(DemoSeedManifest.PeriodEndUtc).AddDays(1),
            OperationalReportQueryValidator.SupportedTimeZone,
            CategoryCode: null,
            BrandCode: null,
            OrderStatuses: null,
            ReportGranularities.Day,
            Cursor: null,
            OperationalReportQueryValidator.MaximumPageSize));
        var result = new Dictionary<string, decimal?>(StringComparer.Ordinal);

        foreach (var definition in OperationalReportCatalog.All)
        {
            var report = await reportQueryService.QueryAsync(
                definition,
                query,
                cancellationToken);
            foreach (var metric in report.Summary)
            {
                result[$"{definition.Key}.{metric.MetricKey}"] = metric.Value;
            }
        }

        return result;
    }

    private static bool ReportBaselinesMatchManifest(
        IReadOnlyDictionary<string, decimal?> actual) =>
        DemoSeedManifest.ExpectedReportBaselines.Count > 0 &&
        DemoSeedManifest.ExpectedReportBaselines.Count == actual.Count &&
        DemoSeedManifest.ExpectedReportBaselines.All(expected =>
            actual.TryGetValue(expected.Key, out var value) && value == expected.Value);

    private sealed record DemoConstraintViolationCounts(int Total, int ForeignKey);

    private sealed class FixedUtcTimeProvider(DateTime utcNow) : TimeProvider
    {
        private readonly DateTimeOffset _utcNow = new(utcNow);

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}

internal static class DemoDatabaseSafety
{
    public static void EnsureAllowedLocalDatabase(DoSelectDbContext dbContext)
    {
        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A SQL Server connection string is required.");
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        var validSuffix = databaseName.StartsWith("DoSelectDemo_", StringComparison.Ordinal) &&
            databaseName.Length == "DoSelectDemo_".Length + 32 &&
            databaseName["DoSelectDemo_".Length..].All(Uri.IsHexDigit);
        if (!string.Equals(databaseName, "DoSelectDemo", StringComparison.Ordinal) && !validSuffix)
        {
            throw new InvalidOperationException(
                "Demo commands are restricted to 'DoSelectDemo' or " +
                "'DoSelectDemo_<32 hex>' databases.");
        }

        var dataSource = builder.DataSource.Trim();
        if (dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            dataSource = dataSource[4..];
        }
        var host = dataSource.Split(['\\', ','], 2)[0];
        var isLocalHost = host is "." or "(local)" ||
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        if (!isLocalHost)
        {
            throw new InvalidOperationException(
                "Demo commands are restricted to a local or loopback SQL Server data source.");
        }
    }
}

internal static class DemoDataSnapshotReader
{
    public static async Task<Dictionary<string, int>> ReadCountsAsync(
        DoSelectDbContext dbContext,
        CancellationToken cancellationToken) =>
        new(StringComparer.Ordinal)
        {
            ["members"] = await dbContext.MemberProfiles.CountAsync(cancellationToken),
            ["addresses"] = await dbContext.MemberAddresses.CountAsync(cancellationToken),
            ["products"] = await dbContext.Products.CountAsync(cancellationToken),
            ["skus"] = await dbContext.Skus.CountAsync(cancellationToken),
            ["productSpecificationValues"] = await dbContext.SkuSpecificationValues.CountAsync(cancellationToken),
            ["orders"] = await dbContext.Orders.CountAsync(cancellationToken),
            ["orderItems"] = await dbContext.OrderItems.CountAsync(cancellationToken),
            ["paymentAttempts"] = await dbContext.PaymentAttempts.CountAsync(cancellationToken),
            ["shipments"] = await dbContext.Shipments.CountAsync(cancellationToken),
            ["inventoryMovements"] = await dbContext.InventoryMovements.CountAsync(cancellationToken),
            ["supportTickets"] = await dbContext.SupportTickets.CountAsync(cancellationToken),
            ["supportMessages"] = await dbContext.SupportMessages.CountAsync(cancellationToken),
            ["returnRequests"] = await dbContext.ReturnRequests.CountAsync(cancellationToken),
            ["refunds"] = await dbContext.Refunds.CountAsync(cancellationToken),
            ["productReviews"] = await dbContext.ProductReviews.CountAsync(cancellationToken),
            ["favorites"] = await dbContext.Favorites.CountAsync(cancellationToken),
            ["couponsAndRedemptions"] =
                await dbContext.Coupons.CountAsync(cancellationToken) +
                await dbContext.CouponRedemptions.CountAsync(cancellationToken),
            ["aiSearchFunnelEvents"] = 0,
        };

    public static bool CountsMatchManifest(IReadOnlyDictionary<string, int> actual) =>
        DemoSeedManifest.ExpectedCounts.All(expected =>
            actual.TryGetValue(expected.Key, out var count) && count == expected.Value) &&
        actual.Values.Sum() == DemoSeedManifest.MainBusinessRecordTotal;

    public static async Task<IReadOnlyDictionary<string, int>> ReadDistributionAsync(
        DoSelectDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var lowStockPublishedSkus = await (
            from balance in dbContext.InventoryBalances
            join sku in dbContext.Skus on balance.SkuId equals sku.Id
            where sku.Status == SkuStatus.Published &&
                balance.AvailableQuantity <= balance.ReorderLevel
            select balance.Id).CountAsync(cancellationToken);

        return new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["completedOrders"] = await dbContext.Orders.CountAsync(
                order => order.OrderStatus == OrderStatus.Completed, cancellationToken),
            ["cancelledOrders"] = await dbContext.Orders.CountAsync(
                order => order.OrderStatus == OrderStatus.Cancelled, cancellationToken),
            ["ordersWithExpiredPayment"] = await dbContext.Orders.CountAsync(
                order => order.PaymentStatus == PaymentStatus.Expired, cancellationToken),
            ["cancelledOrExpiredOrders"] = await dbContext.Orders.CountAsync(
                order => order.OrderStatus == OrderStatus.Cancelled ||
                    order.PaymentStatus == PaymentStatus.Expired, cancellationToken),
            ["refundRecords"] = await dbContext.Refunds.CountAsync(cancellationToken),
            ["succeededRefunds"] = await dbContext.Refunds.CountAsync(
                refund => refund.Status == RefundStatus.Succeeded, cancellationToken),
            ["failedPaymentAttempts"] = await dbContext.PaymentAttempts.CountAsync(
                attempt => attempt.Status == PaymentAttemptStatus.Failed, cancellationToken),
            ["expiredPaymentAttempts"] = await dbContext.PaymentAttempts.CountAsync(
                attempt => attempt.Status == PaymentAttemptStatus.Expired, cancellationToken),
            ["lowStockPublishedSkus"] = lowStockPublishedSkus,
            ["pendingShipments"] = await dbContext.Shipments.CountAsync(
                shipment => shipment.Status == FulfillmentStatus.Pending, cancellationToken),
            ["preparingShipments"] = await dbContext.Shipments.CountAsync(
                shipment => shipment.Status == FulfillmentStatus.Preparing, cancellationToken),
            ["inTransitShipments"] = await dbContext.Shipments.CountAsync(
                shipment => shipment.Status == FulfillmentStatus.InTransit, cancellationToken),
            ["deliveredShipments"] = await dbContext.Shipments.CountAsync(
                shipment => shipment.Status == FulfillmentStatus.Delivered, cancellationToken),
            ["openSupportTickets"] = await dbContext.SupportTickets.CountAsync(
                ticket => ticket.Status == SupportTicketStatus.Open, cancellationToken),
            ["inProgressSupportTickets"] = await dbContext.SupportTickets.CountAsync(
                ticket => ticket.Status == SupportTicketStatus.InProgress, cancellationToken),
            ["waitingForCustomerSupportTickets"] = await dbContext.SupportTickets.CountAsync(
                ticket => ticket.Status == SupportTicketStatus.WaitingForCustomer, cancellationToken),
            ["closedSupportTickets"] = await dbContext.SupportTickets.CountAsync(
                ticket => ticket.Status == SupportTicketStatus.Closed, cancellationToken),
            ["awaitingRefundReturnRequests"] = await dbContext.ReturnRequests.CountAsync(
                request => request.Status == ReturnRequestStatus.AwaitingRefund, cancellationToken),
            ["awaitingShipmentReturnRequests"] = await dbContext.ReturnRequests.CountAsync(
                request => request.Status == ReturnRequestStatus.AwaitingShipment, cancellationToken),
            ["completedReturnRequests"] = await dbContext.ReturnRequests.CountAsync(
                request => request.Status == ReturnRequestStatus.Completed, cancellationToken),
            ["pendingReviewProductReviews"] = await dbContext.ProductReviews.CountAsync(
                review => review.Status == ProductReviewStatus.PendingReview, cancellationToken),
            ["approvedProductReviews"] = await dbContext.ProductReviews.CountAsync(
                review => review.Status == ProductReviewStatus.Approved, cancellationToken),
        };
    }

    public static bool DistributionMatchesManifest(IReadOnlyDictionary<string, int> actual) =>
        DemoSeedManifest.ExpectedDistributionCounts.Count == actual.Count &&
        DemoSeedManifest.ExpectedDistributionCounts.All(expected =>
            actual.TryGetValue(expected.Key, out var count) && count == expected.Value);
}

public sealed record DemoDataValidationResult(
    string FeatureProfile,
    int RandomSeed,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    int MainBusinessRecordTotal,
    bool IsValid,
    IReadOnlyDictionary<string, bool> Checks,
    IReadOnlyList<string> Failures,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyDictionary<string, int> Distribution,
    DemoDataIntegritySummary Integrity,
    IReadOnlyDictionary<string, decimal?> ReportBaselines);

public sealed record DemoDataIntegritySummary(
    int ConstraintViolationRows,
    int OrphanedForeignKeyRows,
    int NegativeInventoryRows,
    int InvalidEnumRows,
    int InvalidWorkflowRows);
