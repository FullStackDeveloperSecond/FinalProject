using System.Security.Cryptography;
using System.Text;
using DoSelect.Application.Common;
using DoSelect.Domain.Catalog;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// Pure (no database access) parsing/field-validation for the Specifications dataset of a
/// product import — 匯入暫存與庫存調整設計.md's "商品模板 v1 欄位契約 / Specifications"
/// table. sku_key resolution (against the sibling Skus dataset, never an existing DB SKU per
/// spec) and SpecificationDefinition/Option lookups happen later in EfProductImportService.
/// </summary>
internal static class SpecificationRowParser
{
    public static readonly IReadOnlyList<string> Header =
    [
        "sku_key", "semantic_key", "value_type", "string_value",
        "decimal_value", "boolean_value", "option_code",
    ];

    public static IReadOnlyList<StagedImportRow<SpecificationPayload>> Parse(IReadOnlyList<string[]> rows)
    {
        var dataRows = ImportHeaderValidator.ValidateAndGetDataRows(rows, Header, "Specifications");
        var staged = new List<StagedImportRow<SpecificationPayload>>(dataRows.Count);
        var seenPairs = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < dataRows.Count; i++)
        {
            var sourceRowNumber = i + 2;
            var fields = dataRows[i];
            if (fields.Length != Header.Count)
            {
                staged.Add(BuildMalformedRow(sourceRowNumber, fields));
                continue;
            }

            var skuKey = ImportFieldNormalization.NormalizeKey(fields[0]);
            var semanticKey = ImportFieldNormalization.NormalizeCode(fields[1]);
            var valueType = ImportFieldNormalization.NormalizeKey(fields[2]);
            var stringValue = ImportFieldNormalization.NormalizeText(fields[3]);
            var hasDecimal = ImportFieldNormalization.TryParseDecimal(fields[4], out var decimalValue);
            var hasBoolean = ImportFieldNormalization.TryParseBoolean(fields[5], out var booleanValue);
            var optionCode = ImportFieldNormalization.NormalizeCode(fields[6]);

            var payload = new SpecificationPayload(
                skuKey ?? string.Empty,
                semanticKey,
                valueType,
                stringValue,
                hasDecimal ? decimalValue : null,
                hasBoolean ? booleanValue : null,
                optionCode);

            var importKey = ComputeCompositeKey(skuKey ?? $"row{sourceRowNumber}", semanticKey ?? string.Empty);
            var row = new StagedImportRow<SpecificationPayload>
            {
                SourceRowNumber = sourceRowNumber,
                ImportKey = importKey,
                Payload = payload,
                RawFields = fields,
            };

            if (skuKey is null || skuKey.Length > 64)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (semanticKey is null || semanticKey.Length > 64)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }
            else if (skuKey is not null && !seenPairs.Add($"{skuKey}␟{semanticKey}"))
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (valueType is null ||
                !Enum.TryParse<SpecificationValueType>(valueType, ignoreCase: true, out var parsedValueType) ||
                !Enum.IsDefined(parsedValueType))
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
                staged.Add(row);
                continue;
            }

            ValidateValueColumns(parsedValueType, fields, stringValue, hasDecimal, hasBoolean, optionCode, row);

            staged.Add(row);
        }

        return staged;
    }

    private static void ValidateValueColumns(
        SpecificationValueType valueType,
        string[] fields,
        string? stringValue,
        bool hasDecimal,
        bool hasBoolean,
        string? optionCode,
        StagedImportRow<SpecificationPayload> row)
    {
        var stringRaw = ImportFieldNormalization.RawOrNull(fields[3]);
        var decimalRaw = ImportFieldNormalization.RawOrNull(fields[4]);
        var booleanRaw = ImportFieldNormalization.RawOrNull(fields[5]);
        var optionRaw = ImportFieldNormalization.RawOrNull(fields[6]);

        // Exactly one value column may be non-null, and it must be the one matching value_type;
        // every other column must be \N.
        switch (valueType)
        {
            case SpecificationValueType.String:
                if (stringValue is null || stringValue.Length > 500 ||
                    decimalRaw is not null || booleanRaw is not null || optionRaw is not null)
                {
                    row.AddError(DomainErrorCodes.ImportValidationFailed);
                }

                break;
            case SpecificationValueType.Decimal:
                if (!hasDecimal || decimalRaw != fields[4] ||
                    stringRaw is not null || booleanRaw is not null || optionRaw is not null)
                {
                    row.AddError(DomainErrorCodes.ImportValidationFailed);
                }

                break;
            case SpecificationValueType.Boolean:
                if (!hasBoolean ||
                    stringRaw is not null || decimalRaw is not null || optionRaw is not null)
                {
                    row.AddError(DomainErrorCodes.ImportValidationFailed);
                }

                break;
            case SpecificationValueType.Option:
                if (optionCode is null || optionCode.Length > 64 ||
                    stringRaw is not null || decimalRaw is not null || booleanRaw is not null)
                {
                    row.AddError(DomainErrorCodes.ImportValidationFailed);
                }

                break;
        }
    }

    /// <summary>
    /// ImportRow.ImportKey is capped at 64 chars (UX_ImportRows_ImportBatchId_Dataset_ImportKey),
    /// but sku_key and semantic_key are each up to 64 chars on their own — concatenating them
    /// could exceed the column. A deterministic truncated hash keeps the "same sku_key +
    /// semantic_key only once" uniqueness rule enforceable by the DB index regardless of input
    /// length, at the cost of the stored key no longer being human-readable.
    /// </summary>
    private static string ComputeCompositeKey(string skuKey, string semanticKey)
    {
        var bytes = Encoding.UTF8.GetBytes($"{skuKey}␟{semanticKey}");
        return Convert.ToHexString(SHA256.HashData(bytes))[..48];
    }

    private static StagedImportRow<SpecificationPayload> BuildMalformedRow(int sourceRowNumber, string[] fields)
    {
        var row = new StagedImportRow<SpecificationPayload>
        {
            SourceRowNumber = sourceRowNumber,
            ImportKey = ComputeCompositeKey($"row{sourceRowNumber}", string.Empty),
            Payload = new SpecificationPayload(string.Empty, null, null, null, null, null, null),
            RawFields = fields,
        };
        row.AddError(DomainErrorCodes.ImportValidationFailed);
        return row;
    }
}
