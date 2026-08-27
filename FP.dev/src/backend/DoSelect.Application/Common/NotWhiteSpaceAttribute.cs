using System.ComponentModel.DataAnnotations;

namespace DoSelect.Application.Common;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class NotWhiteSpaceAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is string text && !string.IsNullOrWhiteSpace(text);
}
