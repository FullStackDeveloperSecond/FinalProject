using System.ComponentModel.DataAnnotations;

namespace DoSelect.Application.Auditing;

/// <summary>
/// 驗證一個要寫進中央 <c>AuditLog.Reason</c> 的理由碼。
/// </summary>
/// <remarks>
/// 中央 Audit 的 <c>reason</c> 只接受 safe-code（ASCII 英數與 <c>._-:</c>，長度上限 64），
/// 不合規時 <c>AuditWriteRequest.Create</c> 會丟 <see cref="ArgumentException"/>。那個例外
/// 沒有專屬 handler，會落到 <c>GlobalExceptionHandler</c> 變成 500 —— 但呼叫端只是送了
/// 格式不合的理由，應該得到 400。
/// <para>
/// 放在 DTO 上而不是只在服務內檢查：這是**請求格式**驗證，和
/// <c>RowVersionRequiredAttribute</c> 同一類，由 <c>[ApiController]</c> 在進入 Action
/// 之前就轉成 400 <c>validation_failed</c>。
/// </para>
/// <para>
/// 刻意直接呼叫中央 Audit 的同一份判斷，不複製規則 —— 另寫一份必然漂移。
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class AuditSafeReasonAttribute : ValidationAttribute
{
    private const int MaximumLength = 64;

    public override bool IsValid(object? value)
    {
        if (value is not string reason)
        {
            return false;
        }

        try
        {
            AuditFieldChange.RequireSafeCode(reason, nameof(reason), MaximumLength);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public override string FormatErrorMessage(string name) =>
        $"{name} must be a stable audit code: ASCII letters, digits and . _ - : only, " +
        $"at most {MaximumLength} characters.";
}

/// <summary>
/// 驗證一個要寫進中央 Audit 的自由文字附註。
/// </summary>
/// <remarks>
/// 規則同樣來自中央 Audit：長度上限、禁止 Email 與標記字元、禁止控制字元與敏感詞。
/// <c>null</c> 或空白視為未提供，屬合法。
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class AuditSafeNoteAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is not string note)
        {
            return false;
        }

        try
        {
            AuditWriteRequest.RequireSafeNote(note, allowsNote: true);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public override string FormatErrorMessage(string name) =>
        $"{name} contains characters or terms that cannot be stored in the audit log.";
}
