using System.Diagnostics.CodeAnalysis;
using DoSelect.Application.Common;
using DoSelect.Domain.Orders;

namespace DoSelect.Application.OperationalReports;

public static class OperationalReportQueryValidator
{
    public const string SupportedTimeZone = "Asia/Taipei";
    public const int MaximumDimensionCodeLength = 64;
    public const int MaximumOrderStatuses = 10;
    public const int MaximumCursorLength = 512;
    public const int MaximumPageSize = 100;

    public static ValidatedReportQuery Normalize(ReportQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.FromDate >= query.ToDate)
        {
            ThrowInvalid("fromDate must be earlier than the exclusive toDate boundary.");
        }

        if (!string.Equals(query.TimeZone, SupportedTimeZone, StringComparison.Ordinal))
        {
            ThrowInvalid($"timeZone must be {SupportedTimeZone}.");
        }

        var categoryCode = NormalizeDimensionCode(query.CategoryCode, nameof(query.CategoryCode));
        var brandCode = NormalizeDimensionCode(query.BrandCode, nameof(query.BrandCode));
        var orderStatuses = NormalizeOrderStatuses(query.OrderStatuses);
        var granularity = NormalizeGranularity(query.Granularity);
        var cursor = string.IsNullOrWhiteSpace(query.Cursor) ? null : query.Cursor.Trim();

        if (cursor?.Length > MaximumCursorLength)
        {
            ThrowInvalid($"cursor cannot exceed {MaximumCursorLength} characters.");
        }

        if (query.PageSize is < 1 or > MaximumPageSize)
        {
            ThrowInvalid($"pageSize must be between 1 and {MaximumPageSize}.");
        }

        return new ValidatedReportQuery(
            query.FromDate,
            query.ToDate,
            SupportedTimeZone,
            categoryCode,
            brandCode,
            orderStatuses,
            granularity,
            cursor,
            query.PageSize);
    }

    private static string? NormalizeDimensionCode(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > MaximumDimensionCodeLength)
        {
            ThrowInvalid($"{parameterName} cannot exceed {MaximumDimensionCodeLength} characters.");
        }

        return normalized;
    }

    private static IReadOnlyList<string> NormalizeOrderStatuses(IReadOnlyList<string>? statuses)
    {
        if (statuses is not { Count: > 0 })
        {
            return [];
        }

        if (statuses.Count > MaximumOrderStatuses)
        {
            ThrowInvalid($"orderStatuses cannot contain more than {MaximumOrderStatuses} entries.");
        }

        var normalized = new List<string>(statuses.Count);
        var seen = new HashSet<OrderStatus>();
        foreach (var status in statuses)
        {
            if (string.IsNullOrWhiteSpace(status) || long.TryParse(status, out _))
            {
                ThrowInvalid($"The order status '{status}' is not supported.");
            }

            if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed) ||
                !Enum.IsDefined(parsed))
            {
                ThrowInvalid($"The order status '{status}' is not supported.");
            }

            if (seen.Add(parsed))
            {
                normalized.Add(parsed.ToString());
            }
        }

        return normalized;
    }

    private static string NormalizeGranularity(string granularity)
    {
        if (string.IsNullOrWhiteSpace(granularity))
        {
            ThrowInvalid("granularity is required.");
        }

        return granularity.Trim().ToLowerInvariant() switch
        {
            ReportGranularities.Day => ReportGranularities.Day,
            ReportGranularities.Week => ReportGranularities.Week,
            ReportGranularities.Month => ReportGranularities.Month,
            _ => throw DomainProblemException.BadRequest(
                OperationalReportErrorCodes.ReportRangeInvalid,
                $"The granularity '{granularity}' is not supported."),
        };
    }

    [DoesNotReturn]
    private static void ThrowInvalid(string message) =>
        throw DomainProblemException.BadRequest(
            OperationalReportErrorCodes.ReportRangeInvalid,
            message);
}
