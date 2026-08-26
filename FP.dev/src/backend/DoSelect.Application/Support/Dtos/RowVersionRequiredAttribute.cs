using System.ComponentModel.DataAnnotations;

namespace DoSelect.Application.Support.Dtos;

/// <summary>
/// Validates a SQL Server `rowversion` request field: must be present and decode to exactly
/// 8 bytes. [Required] alone is not enough here — the record's `byte[] = []` default is a
/// non-null empty array, so a caller that omits the field entirely still passes [Required]
/// and would otherwise reach the optimistic-concurrency check with a value that can never
/// match, surfacing as a misleading 409 instead of a 400 that tells the caller the request
/// itself was malformed. A value that fails JSON Base64 decoding never reaches IsValid at
/// all — the SystemTextJsonInputFormatter already rejects it during model binding as a
/// separate ModelState error, which [ApiController] turns into the same 400 validation_failed
/// response before the action runs.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class RowVersionRequiredAttribute : ValidationAttribute
{
    private const int ExpectedByteLength = 8;

    public RowVersionRequiredAttribute()
        : base("The RowVersion field is required and must be the 8-byte value returned by the server.")
    {
    }

    public override bool IsValid(object? value) => value is byte[] { Length: ExpectedByteLength };
}
