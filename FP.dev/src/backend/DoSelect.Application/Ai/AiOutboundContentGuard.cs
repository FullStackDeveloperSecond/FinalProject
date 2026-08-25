using System.Text.RegularExpressions;

namespace DoSelect.Application.Ai;

public sealed record AiOutboundContentInspection(
    bool IsAllowed,
    AiSafetyReason Reason);

public static partial class AiOutboundContentGuard
{
    public static AiOutboundContentInspection Inspect(params string[] contentItems)
    {
        ArgumentNullException.ThrowIfNull(contentItems);

        foreach (var content in contentItems)
        {
            if (string.IsNullOrEmpty(content))
            {
                continue;
            }

            if (SecretPattern().IsMatch(content))
            {
                return new AiOutboundContentInspection(
                    IsAllowed: false,
                    AiSafetyReason.SecretDetected);
            }

            if (PersonalDataPattern().IsMatch(content))
            {
                return new AiOutboundContentInspection(
                    IsAllowed: false,
                    AiSafetyReason.PersonalDataDetected);
            }
        }

        return new AiOutboundContentInspection(
            IsAllowed: true,
            AiSafetyReason.None);
    }

    [GeneratedRegex(
        @"(?ix)(?:\[\[synthetic_(?:access|refresh)_token\]\]|\[\[synthetic_api_key\]\]|\bbearer\s+[a-z0-9._~+\-/]+=*|\bsk-[a-z0-9_-]{16,}|\b[a-z0-9_-]{10,}\.[a-z0-9_-]{10,}\.[a-z0-9_-]{10,}|(?:access[_ -]?token|refresh[_ -]?token|api[_ -]?key|cookie|password)\s*[:=])",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();

    [GeneratedRegex(
        @"(?ix)(?:\[\[synthetic_(?:name|email|phone|address)\]\]|\b[a-z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-z0-9-]+(?:\.[a-z0-9-]+)+\b|(?:\+?886[-\s]?)?0?9\d{2}[-\s]?\d{3}[-\s]?\d{3}|(?:姓名|收件人|地址|電話|手機|e-?mail)\s*[:：])",
        RegexOptions.CultureInvariant)]
    private static partial Regex PersonalDataPattern();
}
