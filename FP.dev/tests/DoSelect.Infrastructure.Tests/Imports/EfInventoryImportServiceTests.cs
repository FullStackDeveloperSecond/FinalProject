using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Imports;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Imports;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Imports;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Imports;

/// <summary>
/// UC-ADM-INV-01 匯入（匯入暫存與庫存調整設計.md「庫存匯入確認」）。每支測試都先跑真正的 Preview，
/// Confirm 吃到的就是生產環境會吃到的東西：帶 Before／Reserved 快照與 Balance RowVersion 的暫存列。
/// 對真實 SQL Server 跑，因為要證明的正是「RowVersion 條件有沒有真的送到資料庫」。
/// </summary>
[Collection(nameof(ImportServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfInventoryImportServiceTests
{
    private const string Header = "sku_code,target_on_hand,reason_code,note\r\n";

    private static readonly AuditRequestContext TestAuditContext =
        new("test-correlation", "0123456789abcdef0123456789abcdef", null);

    /// <summary>組長 PR #89 item 2：預覽列要有 Before／Delta／After／Reason／Note，而且是明確型別。</summary>
    [Fact]
    public async Task PreviewAsync_ComputesBeforeDeltaAndAfterForEveryRow()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var adminId = await SeedInventoryManagerAsync(context);
        var moving = await SeedSkuWithBalanceAsync(context, onHand: 5, reserved: 1);
        var unchanged = await SeedSkuWithBalanceAsync(context, onHand: 4, reserved: 0);
        var service = CreateService(context);

        var preview = await service.PreviewAsync(Request(
            Header +
            $"{moving.SkuCode},8,StocktakeDifference,\\N\r\n" +
            $"{unchanged.SkuCode},4,DataCorrection,重新盤點後一致\r\n"), adminId, CancellationToken.None);

        Assert.Equal(ImportBatchStatus.Ready.ToString(), preview.Status);
        Assert.Equal(1, preview.UpdatedCount);
        Assert.Equal(1, preview.UnchangedCount);

        var rows = await service.GetRowsAsync(
            preview.PublicId, new ImportRowsQuery(null, false, null, 50), CancellationToken.None);
        var movingRow = Assert.Single(rows.Items, row => row.SkuCode == moving.SkuCode);
        Assert.Equal("Update", movingRow.Action);
        Assert.Equal(5, movingRow.BeforeOnHand);
        Assert.Equal(1, movingRow.ReservedQuantity);
        Assert.Equal(8, movingRow.TargetOnHand);
        Assert.Equal(3, movingRow.Delta);
        Assert.Equal("StocktakeDifference", movingRow.ReasonCode);
        Assert.Null(movingRow.Note);

        var unchangedRow = Assert.Single(rows.Items, row => row.SkuCode == unchanged.SkuCode);
        Assert.Equal("NoChange", unchangedRow.Action);
        Assert.Equal(0, unchangedRow.Delta);
        Assert.Equal("重新盤點後一致", unchangedRow.Note);
    }

    /// <summary>
    /// 組長 PR #89 item 1 的核心：NoChange 列也必須驗 Balance RowVersion。Preview 之後別的交易把
    /// 那個 SKU 改過（改到的值剛好等於目標值），Confirm 仍然要整批拒絕——「任一 SKU 已變動就整批
    /// 拒絕並重新 Preview」沒有把 Delta 為 0 的列排除在外。
    ///
    /// 只設 EF OriginalValue 而不真的寫 Balance 的話，這一列根本不會產生 UPDATE，RowVersion 條件不會
    /// 送到 SQL，這支測試就會變成綠的。
    /// </summary>
    [Fact]
    public async Task ConfirmAsync_WhenAnUnchangedRowsBalanceMovedAfterPreview_RejectsTheWholeBatch()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var adminId = await SeedInventoryManagerAsync(context);
        var moving = await SeedSkuWithBalanceAsync(context, onHand: 5, reserved: 0);
        var unchanged = await SeedSkuWithBalanceAsync(context, onHand: 4, reserved: 0);
        var service = CreateService(context);

        var preview = await service.PreviewAsync(Request(
            Header +
            $"{moving.SkuCode},7,StocktakeDifference,\\N\r\n" +
            $"{unchanged.SkuCode},4,DataCorrection,\\N\r\n"), adminId, CancellationToken.None);
        Assert.Equal(ImportBatchStatus.Ready.ToString(), preview.Status);

        // 別的交易動了 NoChange 那一列的 Balance。數值沒變（4→4），但 RowVersion 變了——這正是
        // 「數字剛好等於目標值」的情況。
        await using (var other = ImportServiceFixture.CreateContext())
        {
            var balance = await other.InventoryBalances.SingleAsync(candidate => candidate.SkuId == unchanged.Id);
            balance.ApplyQuantities(4, 0, DateTime.UtcNow);
            await other.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            service.ConfirmAsync(preview.PublicId, adminId, preview.RowVersion, TestAuditContext, CancellationToken.None));
        Assert.Equal(DomainErrorCodes.ConcurrencyConflict, exception.Code);

        // 整批回滾：會動的那一列也沒有被套用，沒有任何 Movement，批次仍是 Ready。
        await using var verify = ImportServiceFixture.CreateContext();
        var movingBalance = await verify.InventoryBalances.AsNoTracking().SingleAsync(candidate => candidate.SkuId == moving.Id);
        Assert.Equal(5, movingBalance.OnHandQuantity);
        Assert.False(await verify.InventoryMovements.AnyAsync(candidate => candidate.ReferencePublicId == preview.PublicId));
        var batch = await verify.ImportBatches.AsNoTracking().SingleAsync(candidate => candidate.PublicId == preview.PublicId);
        Assert.Equal(ImportBatchStatus.Ready, batch.Status);
    }

    /// <summary>「所有列都保存 Before、Delta、After、Reason、Actor、Batch PublicId 及時間」——Delta 為 0 的列也是。</summary>
    [Fact]
    public async Task ConfirmAsync_WritesAnAdjustmentMovementForEveryRowIncludingUnchangedOnes()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var adminId = await SeedInventoryManagerAsync(context);
        var moving = await SeedSkuWithBalanceAsync(context, onHand: 5, reserved: 2);
        var unchanged = await SeedSkuWithBalanceAsync(context, onHand: 4, reserved: 0);
        var service = CreateService(context);

        var preview = await service.PreviewAsync(Request(
            Header +
            $"{moving.SkuCode},7,Damaged,\\N\r\n" +
            $"{unchanged.SkuCode},4,DataCorrection,\\N\r\n"), adminId, CancellationToken.None);
        var result = await service.ConfirmAsync(preview.PublicId, adminId, preview.RowVersion, TestAuditContext, CancellationToken.None);
        Assert.Equal(ImportBatchStatus.Committed.ToString(), result.Status);

        await using var verify = ImportServiceFixture.CreateContext();
        var movements = await verify.InventoryMovements.AsNoTracking()
            .Where(candidate => candidate.ReferencePublicId == preview.PublicId)
            .ToListAsync();
        Assert.Equal(2, movements.Count);

        var movingMovement = Assert.Single(movements, candidate => candidate.SkuId == moving.Id);
        Assert.Equal(InventoryMovementTypes.Adjustment, movingMovement.MovementType);
        Assert.Equal(2, movingMovement.OnHandDelta);
        Assert.Equal(5, movingMovement.BeforeOnHand);
        Assert.Equal(7, movingMovement.AfterOnHand);
        Assert.Equal(2, movingMovement.BeforeReserved);
        Assert.Equal("Damaged", movingMovement.ReasonCode);
        Assert.Equal(adminId, movingMovement.ActorUserId);

        var unchangedMovement = Assert.Single(movements, candidate => candidate.SkuId == unchanged.Id);
        Assert.Equal(0, unchangedMovement.OnHandDelta);
        Assert.Equal(4, unchangedMovement.BeforeOnHand);
        Assert.Equal(4, unchangedMovement.AfterOnHand);
        Assert.Equal("DataCorrection", unchangedMovement.ReasonCode);

        var movingBalance = await verify.InventoryBalances.AsNoTracking().SingleAsync(candidate => candidate.SkuId == moving.Id);
        Assert.Equal(7, movingBalance.OnHandQuantity);
        Assert.Equal(2, movingBalance.ReservedQuantity);
    }

    /// <summary>
    /// 組長 PR #89 item 3：Other 的說明要進長期稽核資料。ImportRow 24 小時後會被清掉，Movement 才是
    /// 留得住的地方；只存 ReasonCode 的話「Other」等於沒有原因。
    /// </summary>
    [Fact]
    public async Task ConfirmAsync_PersistsTheOtherReasonNoteOnTheMovement()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var adminId = await SeedInventoryManagerAsync(context);
        var sku = await SeedSkuWithBalanceAsync(context, onHand: 10, reserved: 0);
        var service = CreateService(context);

        var preview = await service.PreviewAsync(Request(
            Header + $"{sku.SkuCode},9,Other,櫃位 B 盤點短少一件\r\n"), adminId, CancellationToken.None);
        await service.ConfirmAsync(preview.PublicId, adminId, preview.RowVersion, TestAuditContext, CancellationToken.None);

        await using var verify = ImportServiceFixture.CreateContext();
        var movement = await verify.InventoryMovements.AsNoTracking()
            .SingleAsync(candidate => candidate.ReferencePublicId == preview.PublicId);
        Assert.Equal("Other", movement.ReasonCode);
        Assert.Equal("櫃位 B 盤點短少一件", movement.AdjustmentNote);
    }

    /// <summary>低於已保留數量是列級錯誤；預覽列仍然給出 Before／Reserved，管理員才看得出為什麼被擋。</summary>
    [Fact]
    public async Task PreviewAsync_WhenTargetIsBelowReserved_FlagsTheRowAndStillShowsBeforeAndReserved()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var adminId = await SeedInventoryManagerAsync(context);
        var sku = await SeedSkuWithBalanceAsync(context, onHand: 5, reserved: 3);
        var service = CreateService(context);

        var preview = await service.PreviewAsync(Request(
            Header + $"{sku.SkuCode},2,Lost,\\N\r\n"), adminId, CancellationToken.None);

        Assert.Equal(ImportBatchStatus.Invalid.ToString(), preview.Status);
        var rows = await service.GetRowsAsync(
            preview.PublicId, new ImportRowsQuery(null, true, null, 50), CancellationToken.None);
        var row = Assert.Single(rows.Items);
        Assert.Equal("Error", row.Action);
        Assert.Contains(DomainErrorCodes.ImportValidationFailed, row.ErrorCodes);
        Assert.Equal(5, row.BeforeOnHand);
        Assert.Equal(3, row.ReservedQuantity);
        Assert.Equal(2, row.TargetOnHand);
    }

    private static EfInventoryImportService CreateService(DoSelectDbContext context) =>
        new(context, new EfAuditWriter(context, TimeProvider.System));

    private static PreviewInventoryImportRequest Request(string csv)
    {
        var bytes = ImportServiceFixture.Utf8(csv);
        return new PreviewInventoryImportRequest(
            new IncomingImportFile("stock.csv", "text/csv", bytes.Length, true, () => new MemoryStream(bytes)),
            TemplateVersion: 1);
    }

    /// <summary>Confirm 的稽核 Actor 要是真的持有 InventoryManager 的管理員。</summary>
    private static async Task<string> SeedInventoryManagerAsync(DoSelectDbContext context)
    {
        var admin = ApplicationUser.CreateAdmin(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
        context.Users.Add(admin);
        await context.SaveChangesAsync();

        var role = await context.Roles.SingleOrDefaultAsync(candidate => candidate.Name == AuditRoleNames.InventoryManager);
        if (role is null)
        {
            role = new IdentityRole(AuditRoleNames.InventoryManager);
            context.Roles.Add(role);
            await context.SaveChangesAsync();
        }

        context.UserRoles.Add(new IdentityUserRole<string> { UserId = admin.Id, RoleId = role.Id });
        await context.SaveChangesAsync();
        return admin.Id;
    }

    private static async Task<Sku> SeedSkuWithBalanceAsync(DoSelectDbContext context, int onHand, int reserved)
    {
        var now = DateTime.UtcNow;
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var product = new Product(Guid.CreateVersion7(), ImportServiceFixture.UniqueCode("PROD"), brand.Id, category.Id, "盤點商品", now);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var sku = new Sku(Guid.CreateVersion7(), ImportServiceFixture.UniqueCode("SKU"), product.Id, "盤點 SKU", 1000m, 600m, now);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        var balance = new InventoryBalance(Guid.CreateVersion7(), sku.Id, onHandQuantity: onHand, reorderLevel: 0, now);
        balance.ApplyQuantities(onHand, reserved, now);
        context.InventoryBalances.Add(balance);
        await context.SaveChangesAsync();
        return sku;
    }
}
