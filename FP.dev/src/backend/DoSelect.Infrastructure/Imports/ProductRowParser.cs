using DoSelect.Application.Common;
using DoSelect.Domain.Imports;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// Pure (no database access) parsing/field-validation for the Products dataset of a product
/// import — 匯入暫存與庫存調整設計.md's "商品模板 v1 欄位契約 / Products" table. Lookup
/// resolution (brand_code/category_code existence, Insert-vs-Update against an existing
/// Product) happens later, in EfProductImportService, since it needs the database.
/// </summary>
internal static class ProductRowParser
{
    public static readonly IReadOnlyList<string> Header =
    [
        "product_key", "product_code", "name_zh_tw", "brand_code",
        "category_code", "description_zh_tw", "warranty_months", "status",
    ];

    public static IReadOnlyList<StagedImportRow<ProductPayload>> Parse(IReadOnlyList<string[]> rows)
    {
        var dataRows = ImportHeaderValidator.ValidateAndGetDataRows(rows, Header, "Products");
        var staged = new List<StagedImportRow<ProductPayload>>(dataRows.Count);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < dataRows.Count; i++)
        {
            var sourceRowNumber = i + 2; // +1 for header, +1 for 1-based row numbering
            var fields = dataRows[i];
            if (fields.Length != Header.Count)
            {
                staged.Add(BuildMalformedRow(sourceRowNumber, fields));
                continue;
            }

            var productKey = ImportFieldNormalization.NormalizeKey(fields[0]);
            var productCode = ImportFieldNormalization.NormalizeCode(fields[1]);
            var nameZhTw = ImportFieldNormalization.NormalizeText(fields[2]);
            var brandCode = ImportFieldNormalization.NormalizeCode(fields[3]);
            var categoryCode = ImportFieldNormalization.NormalizeCode(fields[4]);
            var descriptionZhTw = ImportFieldNormalization.NormalizeText(fields[5]);
            var hasWarranty = ImportFieldNormalization.TryParseInt32(fields[6], out var warrantyMonths);
            var status = ImportFieldNormalization.NormalizeKey(fields[7]);

            var payload = new ProductPayload(
                productKey ?? $"__row{sourceRowNumber}",
                productCode,
                nameZhTw,
                brandCode,
                categoryCode,
                descriptionZhTw,
                hasWarranty ? warrantyMonths : null,
                status);

            var row = new StagedImportRow<ProductPayload>
            {
                SourceRowNumber = sourceRowNumber,
                ImportKey = productKey ?? $"__row{sourceRowNumber}",
                Payload = payload,
                RawFields = fields,
            };

            if (productKey is null || productKey.Length > 64)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }
            else if (!seenKeys.Add(productKey))
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (productCode is null || productCode.Length > 64)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (nameZhTw is null || nameZhTw.Length > 160)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (brandCode is null || brandCode.Length > 40)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (categoryCode is null || categoryCode.Length > 40)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (descriptionZhTw is { Length: > 4000 })
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (fields[6] != "\\N" && !hasWarranty)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }
            else if (hasWarranty && warrantyMonths is < 0 or > 120)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (status is null || !Enum.TryParse<ProductImportStatus>(status, ignoreCase: true, out var parsedStatus) ||
                !Enum.IsDefined(parsedStatus))
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            staged.Add(row);
        }

        return staged;
    }

    private static StagedImportRow<ProductPayload> BuildMalformedRow(int sourceRowNumber, string[] fields)
    {
        var row = new StagedImportRow<ProductPayload>
        {
            SourceRowNumber = sourceRowNumber,
            ImportKey = $"__row{sourceRowNumber}",
            Payload = new ProductPayload($"__row{sourceRowNumber}", null, null, null, null, null, null, null),
            RawFields = fields,
        };
        row.AddError(DomainErrorCodes.ImportValidationFailed);
        return row;
    }
}

/// <summary>The three status values a product import row may set — a strict subset of ProductStatus (excludes Discontinued, which the import contract does not offer).</summary>
internal enum ProductImportStatus
{
    Draft,
    Published,
    Unpublished,
}
