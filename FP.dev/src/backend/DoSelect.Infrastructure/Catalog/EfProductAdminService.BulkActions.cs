using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using DoSelect.Application.Auditing;
using DoSelect.Application.Catalog;
using DoSelect.Domain.Auditing;
using DoSelect.Application.OperationalReports;
using DoSelect.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

/// <summary>
/// UC-ADM-PROD-02 批次上架／下架／調價與商品匯出（API Endpoint 目錄「M 商品批次操作」列、
/// A-04 頁）。批次寫入採單一交易，任一筆不合法整批拒絕——商品、組裝與相容性.md 對匯入寫的
/// 「任一筆失敗時整批回滾，不允許部分成功」同樣適用於批次動作：一次改 100 個商品的價格，
/// 讓其中 37 個生效而其餘失敗，管理員無從得知該補做哪些。
/// </summary>
public sealed partial class EfProductAdminService
{
    /// <summary>契約上限：`productPublicIds:uuid[1..100]`。</summary>
    private const int MaximumBulkProductCount = 100;

    /// <summary>
    /// ListPrice 是 decimal(18,2) 且有 CK_Skus_Prices [ListPrice] &gt;= 0。調整後的價格必須先在這裡
    /// 驗證落在範圍內，否則會一路撞到 SQL Server 的 CHECK 或溢位而變成 500——與 CreateSkuRequest
    /// 的 Range 屬性同一個理由、同一組界線。
    /// </summary>
    private const decimal MaximumListPrice = 9999999999999999.99m;

    /// <summary>
    /// 「受控」調價的百分比上下限。規格只寫「受控調價模式與值」而沒有給數字，這組界線是提案值：
    /// 下限 -90% 擋掉打錯字把整批商品變成近乎免費，上限 +100% 擋掉一次漲成兩倍以上。已在 PR 請
    /// 組長裁定。
    /// </summary>
    private const decimal MinimumPercentageAdjustment = -90m;
    private const decimal MaximumPercentageAdjustment = 100m;

    /// <summary>
    /// 匯出整份結果會全部載進記憶體再組成檔案，所以要有上限。沿用報表匯出既有的常數，不另立
    /// 一個只差在名字的數字（OperationalReportExportLimits.MaximumRows）。
    /// </summary>
    private const int MaximumExportRows = OperationalReportExportLimits.MaximumRows;

    public async Task<BulkProductActionResultDto> ApplyBulkActionAsync(
        string action,
        BulkProductActionRequest request,
        AuditRequestContext auditContext,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(auditContext);

        action = NormalizeAction(action);
        var rowVersionsByProduct = ValidateSelection(request);
        var adjustment = action == BulkProductActions.AdjustPrice
            ? ValidatePriceAdjustment(request.PriceAdjustment)
            : null;

        var products = await _dbContext.Products
            .Where(product => request.ProductPublicIds.Contains(product.PublicId))
            .ToListAsync(cancellationToken);

        if (products.Count != rowVersionsByProduct.Count)
        {
            var found = products.Select(product => product.PublicId).ToHashSet();
            var missing = rowVersionsByProduct.Keys.Where(id => !found.Contains(id)).ToArray();
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                $"{missing.Length} of the selected products no longer exist.");
        }

        // Discontinued 是「停用」——不接受上架、下架或調價，正好是 product_unavailable 的語意
        // （API 錯誤碼目錄：商品下架、停用或不接受新交易）。先整批檢查完再動任何一筆，才不會
        // 出現「前 20 筆改好了、第 21 筆才發現不能改」。
        var unavailable = products.Where(product => product.Status == ProductStatus.Discontinued).ToArray();
        if (unavailable.Length > 0)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ProductUnavailable,
                $"{unavailable.Length} of the selected products are discontinued and cannot be changed in bulk.");
        }

        var actor = await ResolveActorAsync(actorUserId, cancellationToken);
        var now = DateTime.UtcNow;

        return action == BulkProductActions.AdjustPrice
            ? await AdjustPricesAsync(products, rowVersionsByProduct, adjustment!, actor, auditContext, now, cancellationToken)
            : await ChangeStatusesAsync(products, rowVersionsByProduct, action, actor, auditContext, now, cancellationToken);
    }

    private async Task<BulkProductActionResultDto> ChangeStatusesAsync(
        IReadOnlyList<Product> products,
        IReadOnlyDictionary<Guid, byte[]> rowVersionsByProduct,
        string action,
        AuditActor actor,
        AuditRequestContext auditContext,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var target = action == BulkProductActions.Publish
            ? ProductStatus.Published
            : ProductStatus.Unpublished;
        var auditAction = action == BulkProductActions.Publish
            ? AuditActions.ProductBulkPublish
            : AuditActions.ProductBulkUnpublish;

        var affected = 0;
        foreach (var product in products)
        {
            // 已經是目標狀態的就整個跳過。若照樣呼叫 ChangeStatus，MarkUpdated 會改 UpdatedAtUtc、
            // EF 就會發出一筆什麼都沒變的 UPDATE 並推進 RowVersion，等於憑空讓別人的畫面過期。
            if (product.Status == target)
            {
                continue;
            }

            var before = product.Status;
            _dbContext.Entry(product).Property(candidate => candidate.RowVersion).OriginalValue =
                rowVersionsByProduct[product.PublicId];
            product.ChangeStatus(target, now);
            affected++;

            _auditWriter.Add(AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                actor,
                auditAction,
                AuditResourceTypes.Product,
                product.PublicId,
                AuditResult.Success,
                errorCode: null,
                [AuditFieldChange.Code("status", before.ToString(), target.ToString())],
                reason: action == BulkProductActions.Publish ? "bulk_publish" : "bulk_unpublish",
                auditContext.CorrelationId,
                auditContext.TraceId,
                jobPublicId: null,
                auditContext.RemoteIpAddress));
        }

        await SaveBulkChangesAsync(cancellationToken);
        return new BulkProductActionResultDto(action, affected, AffectedSkuCount: 0);
    }

    private async Task<BulkProductActionResultDto> AdjustPricesAsync(
        IReadOnlyList<Product> products,
        IReadOnlyDictionary<Guid, byte[]> rowVersionsByProduct,
        BulkPriceAdjustment adjustment,
        AuditActor actor,
        AuditRequestContext auditContext,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var productIds = products.Select(product => product.Id).ToArray();
        var skus = await _dbContext.Skus
            .Where(sku => productIds.Contains(sku.ProductId))
            .ToListAsync(cancellationToken);

        var newPrices = new Dictionary<long, decimal>();
        foreach (var sku in skus)
        {
            var candidate = ApplyAdjustment(sku.ListPrice, adjustment);
            if (candidate < 0m || candidate > MaximumListPrice)
            {
                // 先算完再驗，全部合法才寫。讓其中一筆撞上 CK_Skus_Prices 會是 500，而且前面幾筆
                // 已經在同一個 SaveChanges 裡——整批拒絕才是規格要的行為。
                throw new CatalogWriteException(
                    CatalogWriteException.ErrorCodes.ValidationFailed,
                    $"SKU '{sku.SkuCode}' would end up at {candidate.ToString(CultureInfo.InvariantCulture)}, outside the allowed price range.");
            }

            if (candidate != sku.ListPrice)
            {
                newPrices[sku.Id] = candidate;
            }
        }

        var affectedProducts = 0;
        foreach (var product in products)
        {
            var productSkus = skus.Where(sku => sku.ProductId == product.Id && newPrices.ContainsKey(sku.Id)).ToArray();
            if (productSkus.Length == 0)
            {
                continue;
            }

            _dbContext.Entry(product).Property(candidate => candidate.RowVersion).OriginalValue =
                rowVersionsByProduct[product.PublicId];

            foreach (var sku in productSkus)
            {
                sku.UpdateCommercialDetails(
                    sku.NameZhTw,
                    newPrices[sku.Id],
                    sku.UnitCost,
                    sku.IsDefault,
                    sku.RequiresPrepayment,
                    now);
            }

            // 商品本身沒有價格欄位，但它的 RowVersion 是這一頁樂觀鎖的依據；子 SKU 改了價而父
            // 商品的 RowVersion 不動，別人就會拿著看似新鮮的 RowVersion 覆蓋掉這次調價。
            // Product.Touch 就是為了這種情況而存在的。
            product.Touch(now);
            affectedProducts++;

            _auditWriter.Add(AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                actor,
                AuditActions.ProductBulkAdjustPrice,
                AuditResourceTypes.Product,
                product.PublicId,
                AuditResult.Success,
                errorCode: null,
                [
                    // 一個商品可能有多個 SKU、各自的新舊價都不同，塞不進單一 before/after，
                    // 所以這裡只記「listPrice 有變」，模式與值分開記成可查詢的穩定碼。
                    AuditFieldChange.Changed("listPrice"),
                    AuditFieldChange.Code("adjustmentMode", null, adjustment.Mode),
                    AuditFieldChange.Code("adjustmentValue", null, FormatAdjustmentValue(adjustment.Value)),
                ],
                reason: "bulk_adjust_price",
                auditContext.CorrelationId,
                auditContext.TraceId,
                jobPublicId: null,
                auditContext.RemoteIpAddress,
                note: adjustment.Reason));
        }

        await SaveBulkChangesAsync(cancellationToken);
        return new BulkProductActionResultDto(
            BulkProductActions.AdjustPrice,
            affectedProducts,
            newPrices.Count);
    }

    private async Task SaveBulkChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 一次 SaveChangesAsync 就是一個交易：商品、SKU 與稽核列同生共死。
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ConcurrencyConflict,
                "At least one of the selected products was updated by someone else. Reload the list and try again.");
        }
    }

    private static decimal ApplyAdjustment(decimal current, BulkPriceAdjustment adjustment) =>
        adjustment.Mode switch
        {
            BulkPriceAdjustmentModes.Percentage =>
                // 金額四捨五入到 2 位（欄位精度），並用 AwayFromZero——.NET 預設的 ToEven 會讓
                // 一批 x.x05 的價格一半進、一半退，管理員對不出帳。
                decimal.Round(current * (1m + (adjustment.Value / 100m)), 2, MidpointRounding.AwayFromZero),
            _ => decimal.Round(current + adjustment.Value, 2, MidpointRounding.AwayFromZero),
        };

    private static string FormatAdjustmentValue(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string NormalizeAction(string action)
    {
        var normalized = (action ?? string.Empty).Trim().ToLowerInvariant();
        if (!BulkProductActions.All.Contains(normalized, StringComparer.Ordinal))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                $"'{action}' is not a supported bulk product action.");
        }

        return normalized;
    }

    /// <summary>
    /// 契約同時要求 `productPublicIds` 與 `rowVersions`，所以兩者必須指向同一組商品；只信其中一邊
    /// 會讓「送了 10 個 id 但只附 3 個 RowVersion」的請求安靜地少做事，或反過來改到沒選的商品。
    /// </summary>
    private static Dictionary<Guid, byte[]> ValidateSelection(BulkProductActionRequest request)
    {
        var ids = request.ProductPublicIds ?? [];
        if (ids.Count is < 1 or > MaximumBulkProductCount)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                $"A bulk action accepts between 1 and {MaximumBulkProductCount} products.");
        }

        var distinctIds = new HashSet<Guid>(ids);
        if (distinctIds.Count != ids.Count)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "The selected products contain duplicates.");
        }

        if (distinctIds.Contains(Guid.Empty))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "An empty product identifier is not a valid selection.");
        }

        var rowVersions = request.RowVersions ?? [];
        var byProduct = new Dictionary<Guid, byte[]>();
        foreach (var item in rowVersions)
        {
            if (item.RowVersion is not { Length: > 0 })
            {
                throw new CatalogWriteException(
                    CatalogWriteException.ErrorCodes.ValidationFailed,
                    "Every selected product must carry its RowVersion.");
            }

            if (!byProduct.TryAdd(item.ProductPublicId, item.RowVersion))
            {
                throw new CatalogWriteException(
                    CatalogWriteException.ErrorCodes.ValidationFailed,
                    "The RowVersion list contains the same product twice.");
            }
        }

        if (byProduct.Count != distinctIds.Count || !distinctIds.All(byProduct.ContainsKey))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "The RowVersion list must cover exactly the selected products.");
        }

        return byProduct;
    }

    private static BulkPriceAdjustment ValidatePriceAdjustment(BulkPriceAdjustment? adjustment)
    {
        if (adjustment is null)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "A price adjustment mode, value and reason are required for adjust-price.");
        }

        var mode = (adjustment.Mode ?? string.Empty).Trim().ToLowerInvariant();
        if (!BulkPriceAdjustmentModes.All.Contains(mode, StringComparer.Ordinal))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                $"'{adjustment.Mode}' is not a supported price adjustment mode.");
        }

        if (mode == BulkPriceAdjustmentModes.Percentage &&
            adjustment.Value is < MinimumPercentageAdjustment or > MaximumPercentageAdjustment)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                $"A percentage adjustment must be between {MinimumPercentageAdjustment} and {MaximumPercentageAdjustment}.");
        }

        if (mode == BulkPriceAdjustmentModes.Amount &&
            adjustment.Value is < -MaximumListPrice or > MaximumListPrice)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "The amount adjustment is outside the representable price range.");
        }

        var reason = (adjustment.Reason ?? string.Empty).Trim();
        if (reason.Length is < 1 or > 500)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "A price adjustment reason between 1 and 500 characters is required.");
        }

        // AuditWriteRequest.RequireSafeNote 對這些字元與敏感詞直接丟 ArgumentException，那會變成
        // 500。原因是管理員自己打的自由文字，所以在這裡先擋成 400 validation_failed。
        if (reason.IndexOfAny(['@', '<', '>', '&', '\\', '"', '\'']) >= 0 ||
            reason.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "The price adjustment reason contains a character that cannot be stored in the audit log.");
        }

        string[] forbidden =
            ["password", "token", "cookie", "apikey", "api-key", "totp", "recovery code", "card number", "cvv"];
        if (forbidden.Any(term => reason.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "The price adjustment reason contains a term the audit log refuses to store.");
        }

        return new BulkPriceAdjustment(mode, adjustment.Value, reason);
    }

    /// <summary>Same shape as EfConvenienceStoreAdminService.ResolveActorAsync.</summary>
    private async Task<AuditActor> ResolveActorAsync(string actorUserId, CancellationToken cancellationToken)
    {
        var admin = await _dbContext.Users.AsNoTracking()
            .Where(user => user.Id == actorUserId && user.AccountType == DoSelect.Domain.Members.AccountType.Admin)
            .Select(user => new { user.Id, user.PublicId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "The administrator identity is invalid.");

        var roles = await (
            from userRole in _dbContext.UserRoles.AsNoTracking()
            join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == admin.Id && role.Name != null
            select role.Name!)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (!roles.Contains(AuditRoleNames.CatalogManager, StringComparer.Ordinal) &&
            !roles.Contains(AuditRoleNames.SuperAdmin, StringComparer.Ordinal))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                "The administrator is not allowed to run catalog bulk actions.");
        }

        return AuditActor.Create(AuditActorType.Admin, admin.PublicId, roles);
    }

    public async Task<AdminProductExportDto> ExportAsync(
        AdminProductQuery query,
        string format,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var normalizedFormat = (format ?? string.Empty).Trim().ToLowerInvariant();
        if (!AdminProductExportFormats.All.Contains(normalizedFormat, StringComparer.Ordinal))
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                $"'{format}' is not a supported export format.");
        }

        var rows = await BuildFilteredRows(query)
            .OrderBy(row => row.Product.ProductCode)
            .Take(MaximumExportRows + 1)
            .Select(row => new
            {
                row.Product.Id,
                row.Product.ProductCode,
                row.Product.NameZhTw,
                BrandName = row.Brand.NameZhTw,
                CategoryName = row.Category.NameZhTw,
                row.Product.Status,
                row.Product.UpdatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        if (rows.Count > MaximumExportRows)
        {
            throw new CatalogWriteException(
                CatalogWriteException.ErrorCodes.ValidationFailed,
                $"The export exceeds the {MaximumExportRows:N0}-row limit. Narrow the filters and try again.");
        }

        var productIds = rows.Select(row => row.Id).ToArray();
        var skus = await _dbContext.Skus.AsNoTracking()
            .Where(sku => productIds.Contains(sku.ProductId))
            .Select(sku => new { sku.Id, sku.ProductId, sku.ListPrice })
            .ToListAsync(cancellationToken);
        var skuIds = skus.Select(sku => sku.Id).ToArray();
        var onHandBySku = await _dbContext.InventoryBalances.AsNoTracking()
            .Where(balance => skuIds.Contains(balance.SkuId))
            .ToDictionaryAsync(balance => balance.SkuId, balance => balance.OnHandQuantity, cancellationToken);

        var records = rows.Select(row =>
        {
            var productSkus = skus.Where(sku => sku.ProductId == row.Id).ToArray();
            return new ExportRecord(
                row.ProductCode,
                row.NameZhTw,
                row.BrandName,
                row.CategoryName,
                row.Status.ToString(),
                productSkus.Length,
                productSkus.Length == 0 ? 0m : productSkus.Min(sku => sku.ListPrice),
                productSkus.Length == 0 ? 0m : productSkus.Max(sku => sku.ListPrice),
                productSkus.Sum(sku => onHandBySku.GetValueOrDefault(sku.Id)),
                row.UpdatedAtUtc);
        }).ToList();

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return normalizedFormat == AdminProductExportFormats.Xlsx
            ? new AdminProductExportDto(
                $"products-{stamp}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                BuildXlsx(records))
            : new AdminProductExportDto($"products-{stamp}.csv", "text/csv; charset=utf-8", BuildCsv(records));
    }

    /// <summary>
    /// 匯出欄位刻意與 <see cref="AdminProductSummaryDto"/> 對齊——管理員匯出的就是他當下看到的那張
    /// 表。也因此不含 UnitCost：列表看不到成本，匯出當然也不該把它漏出去。
    /// </summary>
    private static readonly string[] ExportHeaders =
        ["商品代碼", "商品名稱", "品牌", "分類", "狀態", "SKU 數", "最低售價", "最高售價", "現有庫存", "最後更新(UTC)"];

    private static byte[] BuildCsv(IReadOnlyList<ExportRecord> records)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', ExportHeaders.Select(EscapeCsv)));
        foreach (var record in records)
        {
            builder.AppendLine(string.Join(',', new[]
            {
                EscapeCsv(record.ProductCode),
                EscapeCsv(record.NameZhTw),
                EscapeCsv(record.BrandName),
                EscapeCsv(record.CategoryName),
                EscapeCsv(record.Status),
                record.SkuCount.ToString(CultureInfo.InvariantCulture),
                record.MinPrice.ToString("0.00", CultureInfo.InvariantCulture),
                record.MaxPrice.ToString("0.00", CultureInfo.InvariantCulture),
                record.OnHandQuantity.ToString(CultureInfo.InvariantCulture),
                record.UpdatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            }));
        }

        // Excel 只有看到 BOM 才會把 UTF-8 的中文正確解讀；沒有 BOM 的話商品名稱會變亂碼。
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(builder.ToString());
    }

    private static string EscapeCsv(string value)
    {
        // RFC 4180：含逗號、引號或換行就整欄加引號，內部引號成對。此外前置一個單引號會讓 Excel
        // 把 =cmd 之類的內容當成公式執行，所以危險前綴一律加上 Tab 前置字元中和。
        var needsQuotes = value.IndexOfAny([',', '"', '\r', '\n']) >= 0;
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
        {
            value = "\t" + value;
            needsQuotes = true;
        }

        return needsQuotes ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }

    private static byte[] BuildXlsx(IReadOnlyList<ExportRecord> records)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Products");
        for (var column = 0; column < ExportHeaders.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = ExportHeaders[column];
        }

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var row = index + 2;
            // SetText 而不是 Value：商品代碼可能長得像數字或日期，交給 Excel 猜就會被改寫。
            sheet.Cell(row, 1).SetValue(record.ProductCode);
            sheet.Cell(row, 2).SetValue(record.NameZhTw);
            sheet.Cell(row, 3).SetValue(record.BrandName);
            sheet.Cell(row, 4).SetValue(record.CategoryName);
            sheet.Cell(row, 5).SetValue(record.Status);
            sheet.Cell(row, 6).SetValue(record.SkuCount);
            sheet.Cell(row, 7).SetValue(record.MinPrice);
            sheet.Cell(row, 8).SetValue(record.MaxPrice);
            sheet.Cell(row, 9).SetValue(record.OnHandQuantity);
            sheet.Cell(row, 10).SetValue(record.UpdatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private sealed record ExportRecord(
        string ProductCode,
        string NameZhTw,
        string BrandName,
        string CategoryName,
        string Status,
        int SkuCount,
        decimal MinPrice,
        decimal MaxPrice,
        int OnHandQuantity,
        DateTime UpdatedAtUtc);
}
