using DoSelect.Domain.Members;

namespace DoSelect.Application.Security;

/// <summary>
/// <see cref="SupportedLocale"/> 對外顯示碼，與資料字典
/// <c>PreferredLocale varchar(10) zh-TW/ja-JP/ko-KR</c> 一致。
/// </summary>
public static class LocaleCodes
{
    public static string ToCode(SupportedLocale locale) => locale switch
    {
        SupportedLocale.ZhTw => "zh-TW",
        SupportedLocale.JaJp => "ja-JP",
        SupportedLocale.KoKr => "ko-KR",
        _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, null),
    };
}
