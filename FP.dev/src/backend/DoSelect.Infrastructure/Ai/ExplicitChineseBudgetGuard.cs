using System.Globalization;
using System.Text.RegularExpressions;
using DoSelect.Domain.Members;

namespace DoSelect.Infrastructure.Ai;

internal sealed record ExplicitBudgetSignal(
    decimal? Minimum,
    decimal Maximum,
    bool HasConflict);

internal static class ExplicitChineseBudgetGuard
{
    private const string AmountPattern =
        "(?<amount>(?:[0-9０-９][0-9０-９,，]*(?:\\.[0-9０-９]+)?\\s*萬(?:[0-9０-９零〇一二兩三四五六七八九十百千]*)?|[0-9０-９][0-9０-９,，]*|[零〇一二兩三四五六七八九十百千萬]+))";

    private static readonly Regex AmbiguousAmountRegex = new(
        "左右|上下|大約|大概|約莫|差不多|[一二兩三四五六七八九兩][一二兩三四五六七八九兩]萬",
        RegexOptions.CultureInvariant);

    private static readonly Regex MinimumPrefixRegex = new(
        $"(?:(?:預算\\s*)?(?:至少|最低|起碼))\\s*{AmountPattern}\\s*(?:元|塊)?",
        RegexOptions.CultureInvariant);

    private static readonly Regex MinimumSuffixRegex = new(
        $"{AmountPattern}\\s*(?:元|塊)?\\s*(?:以上|起)",
        RegexOptions.CultureInvariant);

    private static readonly Regex MaximumPrefixRegex = new(
        $"(?:最多只能花|最多花|預算\\s*(?:最多|最高|上限)?(?:是|為)?|最多|最高|上限(?:是|為)?|不能超過|不超過|只能花)\\s*{AmountPattern}\\s*(?:元|塊)?",
        RegexOptions.CultureInvariant);

    private static readonly Regex MaximumSuffixRegex = new(
        $"{AmountPattern}\\s*(?:元|塊)?\\s*(?:以內|以下|內)",
        RegexOptions.CultureInvariant);

    private static readonly Regex AnyAmountRegex = new(
        AmountPattern,
        RegexOptions.CultureInvariant);

    public static bool TryParse(
        string message,
        SupportedLocale locale,
        out ExplicitBudgetSignal signal)
    {
        signal = null!;
        if (locale != SupportedLocale.ZhTw ||
            string.IsNullOrWhiteSpace(message) ||
            AmbiguousAmountRegex.IsMatch(message))
        {
            return false;
        }

        var minimums = CollectAmounts(message, MinimumPrefixRegex, MinimumSuffixRegex);
        var maximums = CollectAmounts(message, MaximumPrefixRegex, MaximumSuffixRegex);
        if (minimums.Count > 1 || maximums.Count > 1)
        {
            return false;
        }

        if (maximums.Count == 1)
        {
            var minimum = minimums.Count == 1 ? minimums[0] : (decimal?)null;
            signal = new ExplicitBudgetSignal(
                minimum,
                maximums[0],
                minimum > maximums[0]);
            return true;
        }

        if (minimums.Count > 0)
        {
            return false;
        }

        var unqualified = AnyAmountRegex.Matches(message)
            .Select(match => match.Groups["amount"])
            .Where(group => group.Success && group.Value.Contains('萬'))
            .Select(group => TryParseAmount(group.Value, out var value) ? value : (decimal?)null)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        if (unqualified.Length != 1)
        {
            return false;
        }

        signal = new ExplicitBudgetSignal(null, unqualified[0], HasConflict: false);
        return true;
    }

    private static IReadOnlyList<decimal> CollectAmounts(string message, params Regex[] patterns) =>
        patterns
            .SelectMany(pattern => pattern.Matches(message).Cast<Match>())
            .Where(match => match.Groups["amount"].Success && HasFinancialContext(match))
            .GroupBy(match => match.Groups["amount"].Index)
            .Select(group => group.First().Groups["amount"].Value)
            .Select(value => TryParseAmount(value, out var parsed) ? parsed : (decimal?)null)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();

    private static bool HasFinancialContext(Match match) =>
        match.Groups["amount"].Value.Contains('萬') ||
        match.Value.Contains('元') ||
        match.Value.Contains('塊') ||
        match.Value.Contains("預算", StringComparison.Ordinal) ||
        match.Value.Contains('花');

    private static bool TryParseAmount(string raw, out decimal amount)
    {
        amount = 0;
        var normalized = NormalizeDigits(raw)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("，", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (normalized.Any(char.IsAsciiDigit))
        {
            return TryParseArabicAmount(normalized, out amount);
        }

        var tenThousandsIndex = normalized.IndexOf('萬');
        if (tenThousandsIndex < 0)
        {
            return TryParseChineseSection(normalized, out amount) && IsAllowedAmount(amount);
        }

        if (normalized.LastIndexOf('萬') != tenThousandsIndex ||
            !TryParseChineseSection(normalized[..tenThousandsIndex], out var tenThousands))
        {
            return false;
        }

        var remainderText = normalized[(tenThousandsIndex + 1)..];
        decimal remainder = 0;
        if (remainderText.Length == 1 && TryReadChineseDigit(remainderText[0], out var colloquialDigit))
        {
            remainder = colloquialDigit * 1000m;
        }
        else if (remainderText.Length > 0 && !TryParseChineseSection(remainderText, out remainder))
        {
            return false;
        }

        amount = tenThousands * 10_000m + remainder;
        return IsAllowedAmount(amount);
    }

    private static bool TryParseArabicAmount(string value, out decimal amount)
    {
        amount = 0;
        var tenThousandsIndex = value.IndexOf('萬');
        if (tenThousandsIndex < 0)
        {
            return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) &&
                IsAllowedAmount(amount);
        }

        if (value.LastIndexOf('萬') != tenThousandsIndex ||
            !decimal.TryParse(
                value[..tenThousandsIndex],
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var tenThousands))
        {
            return false;
        }

        var remainderText = value[(tenThousandsIndex + 1)..];
        decimal remainder = 0;
        if (remainderText.Length == 1 && char.IsAsciiDigit(remainderText[0]))
        {
            remainder = (remainderText[0] - '0') * 1000m;
        }
        else if (remainderText.Length > 0 &&
                 !decimal.TryParse(remainderText, NumberStyles.None, CultureInfo.InvariantCulture, out remainder))
        {
            return false;
        }

        amount = tenThousands * 10_000m + remainder;
        return IsAllowedAmount(amount);
    }

    private static bool TryParseChineseSection(string value, out decimal result)
    {
        result = 0;
        if (value.Length == 0)
        {
            return false;
        }

        if (value.Length == 1 && TryReadChineseDigit(value[0], out var singleDigit))
        {
            result = singleDigit;
            return true;
        }

        decimal currentDigit = 0;
        var usedUnit = false;
        foreach (var character in value)
        {
            if (TryReadChineseDigit(character, out var digit))
            {
                currentDigit = digit;
                continue;
            }

            var unit = character switch
            {
                '十' => 10m,
                '百' => 100m,
                '千' => 1000m,
                _ => 0m,
            };
            if (unit == 0)
            {
                return false;
            }

            result += (currentDigit == 0 ? 1 : currentDigit) * unit;
            currentDigit = 0;
            usedUnit = true;
        }

        if (!usedUnit)
        {
            return false;
        }

        result += currentDigit;
        return true;
    }

    private static bool TryReadChineseDigit(char value, out decimal digit)
    {
        digit = value switch
        {
            '零' or '〇' => 0,
            '一' => 1,
            '二' or '兩' => 2,
            '三' => 3,
            '四' => 4,
            '五' => 5,
            '六' => 6,
            '七' => 7,
            '八' => 8,
            '九' => 9,
            _ => -1,
        };
        return digit >= 0;
    }

    private static string NormalizeDigits(string value) =>
        string.Concat(value.Select(character => character is >= '０' and <= '９'
            ? (char)('0' + character - '０')
            : character));

    private static bool IsAllowedAmount(decimal amount) => amount is > 0 and <= 10_000_000m;
}
