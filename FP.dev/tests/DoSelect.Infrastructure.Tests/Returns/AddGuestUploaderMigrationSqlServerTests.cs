using DoSelect.Domain.Orders;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Shipping;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DoSelect.Infrastructure.Tests.Returns;

/// <summary>
/// E1 review finding: the AddGuestUploaderToReturnAttachments migration's THROW-based Down()
/// guard was only verified by reading the migration file, never by actually applying and rolling
/// back a migration against a real SQL Server database. These tests run the real EF Core
/// migrator (IMigrator.MigrateAsync targeting a specific migration id) against a
/// throwaway database for all three scenarios the review asked for.
/// </summary>
[Trait("Category", "RequiresSqlServer")]
public sealed class AddGuestUploaderMigrationSqlServerTests
{
    private const string ConnectionStringEnvironmentVariable = "DOSELECT_SQLSERVER_TEST_CONNECTION";
    private const string LocalConnectionString =
        "Server=.\\SQL2025;Database=DoSelect;Trusted_Connection=True;Encrypt=False;";
    private const string TargetMigrationId = "20260826012917_AddGuestUploaderToReturnAttachments";
    private const string PriorMigrationId = "20260825174929_AddCentralAuditLogs";
    private static readonly DateTime NowUtc = new(2026, 8, 26, 4, 0, 0, DateTimeKind.Utc);

    [SqlServerFact]
    public async Task Up_AddsGuestUploaderColumnAndConstraintsWhilePreservingMemberUploader()
    {
        await RunAgainstFreshDatabaseAsync(async context =>
        {
            await context.GetService<IMigrator>().MigrateAsync(TargetMigrationId);

            var (memberUserId, returnRequestId, orderId) = await SeedOrderAndReturnAsync(context);

            // Member uploader still works (UploadedByUserId is now nullable, but a real value is
            // still accepted and persists exactly as before).
            var memberAttachment = new ReturnAttachment(
                Guid.CreateVersion7(), returnRequestId, uploadedByUserId: memberUserId, uploadedByGuestOrderId: null,
                "member-proof.png", $"private-files/aa/{Guid.NewGuid():N}.blob", "png", "image/png", 11, new byte[32], NowUtc);
            memberAttachment.RecordScan(PrivateAttachmentScanStatus.Clean, NowUtc);
            context.ReturnAttachments.Add(memberAttachment);
            await context.SaveChangesAsync();

            // Guest uploader can now be added (the whole point of this migration).
            var guestAttachment = new ReturnAttachment(
                Guid.CreateVersion7(), returnRequestId, uploadedByUserId: null, uploadedByGuestOrderId: orderId,
                "guest-proof.png", $"private-files/bb/{Guid.NewGuid():N}.blob", "png", "image/png", 11, new byte[32], NowUtc);
            guestAttachment.RecordScan(PrivateAttachmentScanStatus.Clean, NowUtc);
            context.ReturnAttachments.Add(guestAttachment);
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();
            Assert.Equal(memberUserId, (await context.ReturnAttachments.SingleAsync(a => a.PublicId == memberAttachment.PublicId)).UploadedByUserId);
            Assert.Equal(orderId, (await context.ReturnAttachments.SingleAsync(a => a.PublicId == guestAttachment.PublicId)).UploadedByGuestOrderId);
        });
    }

    [SqlServerFact]
    public async Task Down_WithNoGuestUploaderRows_SucceedsAndRestoresOriginalSchema()
    {
        await RunAgainstFreshDatabaseAsync(async context =>
        {
            await context.GetService<IMigrator>().MigrateAsync(TargetMigrationId);
            var (memberUserId, returnRequestId, _) = await SeedOrderAndReturnAsync(context);

            var memberAttachment = new ReturnAttachment(
                Guid.CreateVersion7(), returnRequestId, uploadedByUserId: memberUserId, uploadedByGuestOrderId: null,
                "member-proof.png", $"private-files/aa/{Guid.NewGuid():N}.blob", "png", "image/png", 11, new byte[32], NowUtc);
            memberAttachment.RecordScan(PrivateAttachmentScanStatus.Clean, NowUtc);
            context.ReturnAttachments.Add(memberAttachment);
            await context.SaveChangesAsync();

            await context.GetService<IMigrator>().MigrateAsync(PriorMigrationId);

            Assert.False(await ColumnExistsAsync(context, "ReturnAttachments", "UploadedByGuestOrderId"));
            var isNullable = await IsColumnNullableAsync(context, "ReturnAttachments", "UploadedByUserId");
            Assert.False(isNullable);
        });
    }

    [SqlServerFact]
    public async Task Down_WithGuestUploaderRows_FailsFastBeforeAnyColumnOrDataMutation()
    {
        await RunAgainstFreshDatabaseAsync(async context =>
        {
            await context.GetService<IMigrator>().MigrateAsync(TargetMigrationId);
            var (memberUserId, returnRequestId, orderId) = await SeedOrderAndReturnAsync(context);

            var memberAttachment = new ReturnAttachment(
                Guid.CreateVersion7(), returnRequestId, uploadedByUserId: memberUserId, uploadedByGuestOrderId: null,
                "member-proof.png", $"private-files/aa/{Guid.NewGuid():N}.blob", "png", "image/png", 11, new byte[32], NowUtc);
            memberAttachment.RecordScan(PrivateAttachmentScanStatus.Clean, NowUtc);
            context.ReturnAttachments.Add(memberAttachment);

            var guestAttachment = new ReturnAttachment(
                Guid.CreateVersion7(), returnRequestId, uploadedByUserId: null, uploadedByGuestOrderId: orderId,
                "guest-proof.png", $"private-files/bb/{Guid.NewGuid():N}.blob", "png", "image/png", 11, new byte[32], NowUtc);
            guestAttachment.RecordScan(PrivateAttachmentScanStatus.Clean, NowUtc);
            context.ReturnAttachments.Add(guestAttachment);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // Asserting on the specific error (not just ThrowsAnyAsync<Exception>) matters here:
            // even a Down() with NO fail-fast guard at all would still throw at its LAST
            // statement (ALTER COLUMN UploadedByUserId ... NOT NULL fails because the guest row's
            // UploadedByUserId is NULL) and the ambient migration transaction would still roll
            // the schema back — so a generic "did it throw" check would pass regardless of
            // whether the deliberate fail-fast guard exists. Only the message check proves THIS
            // specific guard is what fired, before any Drop/Alter ran, rather than an accidental
            // late-stage constraint violation.
            var exception = await Assert.ThrowsAnyAsync<Exception>(
                () => context.GetService<IMigrator>().MigrateAsync(PriorMigrationId));
            Assert.Contains("Cannot roll back AddGuestUploaderToReturnAttachments", exception.ToString(), StringComparison.Ordinal);

            // The failure must have happened before any DropColumn/AlterColumn mutation: the
            // guest-uploader column still exists, and both rows' uploader values are untouched.
            Assert.True(await ColumnExistsAsync(context, "ReturnAttachments", "UploadedByGuestOrderId"));
            var reloadedGuest = await context.ReturnAttachments.AsNoTracking().SingleAsync(a => a.PublicId == guestAttachment.PublicId);
            Assert.Equal(orderId, reloadedGuest.UploadedByGuestOrderId);
            Assert.Null(reloadedGuest.UploadedByUserId);
            var reloadedMember = await context.ReturnAttachments.AsNoTracking().SingleAsync(a => a.PublicId == memberAttachment.PublicId);
            Assert.Equal(memberUserId, reloadedMember.UploadedByUserId);
        });
    }

    private static async Task<(string MemberUserId, long ReturnRequestId, long OrderId)> SeedOrderAndReturnAsync(DoSelectDbContext context)
    {
        var member = ApplicationUser.CreateMember(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", NowUtc);
        context.Users.Add(member);
        await context.SaveChangesAsync();

        var shippingProfile = new ShippingProviderProfile(
            Guid.CreateVersion7(), $"HOME-{Guid.NewGuid():N}"[..20], 1, "Active", null, null, "{}", 1, NowUtc);
        context.Set<ShippingProviderProfile>().Add(shippingProfile);
        await context.SaveChangesAsync();

        var order = Order.Create(Guid.CreateVersion7(), ValidOrderCreation(member.Id, shippingProfile.Id), NowUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var returnRequest = new ReturnRequest(
            Guid.CreateVersion7(), $"RT-{Guid.NewGuid():N}"[..12], order.Id, member.Id, "Defective", "面板有亮點", 1, NowUtc);
        context.ReturnRequests.Add(returnRequest);
        await context.SaveChangesAsync();

        return (member.Id, returnRequest.Id, order.Id);
    }

    private static OrderCreation ValidOrderCreation(string memberUserId, long shippingProviderProfileId) =>
        new(
            $"DS{Guid.NewGuid():N}"[..15], memberUserId, null,
            OrderStatus.Processing, PaymentStatus.Paid, FulfillmentStatus.Delivered, AssemblyStatus.NotRequired,
            1_200m, 100m, 225m, 0m, 1_325m,
            "Member", "0912345678", "member@example.com",
            "100", "Taipei", "Zhongzheng", "No. 1", null,
            "HOME_DELIVERY", shippingProviderProfileId, null, null, null,
            1, 1, null, null, $"checkout-{Guid.NewGuid():N}", null);

    private static async Task<bool> ColumnExistsAsync(DoSelectDbContext context, string table, string column)
    {
        await using var connection = new SqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table AND COLUMN_NAME = @column";
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        var count = (int)(await command.ExecuteScalarAsync())!;
        return count > 0;
    }

    private static async Task<bool> IsColumnNullableAsync(DoSelectDbContext context, string table, string column)
    {
        await using var connection = new SqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table AND COLUMN_NAME = @column";
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        var result = (string)(await command.ExecuteScalarAsync())!;
        return result == "YES";
    }

    private static async Task RunAgainstFreshDatabaseAsync(Func<DoSelectDbContext, Task> test)
    {
        var connectionString = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ??
            LocalConnectionString)
        {
            InitialCatalog = $"DoSelectGuestUploaderMigration_{Guid.NewGuid():N}",
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<DoSelectDbContext>().UseSqlServer(connectionString).Options;
        await using var context = new DoSelectDbContext(options);
        try
        {
            await test(context);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
            await context.Database.EnsureDeletedAsync();
        }
    }
}
