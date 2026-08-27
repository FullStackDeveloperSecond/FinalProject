using DoSelect.Application.Common;

namespace DoSelect.Application.Tests.Common;

public sealed class UtcDateTimeAttributeTests
{
    private static readonly UtcDateTimeAttribute Attribute = new();

    [Fact]
    public void IsValid_WhenUtcKind_ReturnsTrue()
    {
        Assert.True(Attribute.IsValid(DateTime.UtcNow));
    }

    [Fact]
    public void IsValid_WhenUnspecifiedKind_ReturnsFalse()
    {
        // Matches what System.Text.Json produces for an ISO-8601 string with no 'Z'/offset
        // suffix — the exact shape D1's ShipmentEvent HTTP test sends.
        Assert.False(Attribute.IsValid(DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void IsValid_WhenLocalKind_ReturnsFalse()
    {
        Assert.False(Attribute.IsValid(DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Local)));
    }

    [Fact]
    public void IsValid_WhenNull_ReturnsTrue()
    {
        // Nullability (e.g. an optional DateTime?) is [Required]'s job, not this attribute's —
        // matches ValidationAttribute convention (null is skipped unless [Required] is also present).
        Assert.True(Attribute.IsValid(null));
    }

    [Fact]
    public void IsValid_WhenValueIsNotADateTime_ReturnsTrue()
    {
        Assert.True(Attribute.IsValid("2026-01-01"));
    }
}
