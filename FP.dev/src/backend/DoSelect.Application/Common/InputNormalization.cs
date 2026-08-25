using System.Text;

namespace DoSelect.Application.Common;

/// <summary>
/// Canonicalizes free-text input the same way everywhere it is compared or used as a lookup key —
/// Auth request DTOs, the value Identity ends up looking the user up by, and the per-email
/// rate-limit throttle key — so visually-identical but byte-different Unicode variants of the same
/// value (full-width vs half-width characters, compatibility ligatures, etc.) cannot slip past
/// uniqueness checks, throttling, or account lookup as if they were different values (API DTO與
/// Schema契約.md: Request 未知欄位拒絕；字串 Trim＋NFKC; Alex review, 2026-08-25).
/// </summary>
public static class InputNormalization
{
    public static string Canonicalize(string value) =>
        string.IsNullOrEmpty(value) ? value : value.Trim().Normalize(NormalizationForm.FormKC);
}
