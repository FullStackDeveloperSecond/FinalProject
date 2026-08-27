using System.ComponentModel.DataAnnotations;

namespace DoSelect.Application.Common;

/// <summary>Rejects a non-UTC DateTime (Unspecified/Local) at the ModelState layer, before it
/// ever reaches a Domain constructor's own DateTimeKind.Utc guard — keeps that failure mode a
/// uniform 400 validation_failed instead of an unhandled ArgumentException.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class UtcDateTimeAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is not DateTime dateTime || dateTime.Kind == DateTimeKind.Utc;
}
