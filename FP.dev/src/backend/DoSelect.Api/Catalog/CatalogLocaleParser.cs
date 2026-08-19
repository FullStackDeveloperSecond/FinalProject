using DoSelect.Domain.Members;

namespace DoSelect.Api.Catalog;

internal static class CatalogLocaleParser
{
    public static SupportedLocale Parse(string? locale) => locale switch
    {
        "ja-JP" => SupportedLocale.JaJp,
        "ko-KR" => SupportedLocale.KoKr,
        _ => SupportedLocale.ZhTw,
    };
}
