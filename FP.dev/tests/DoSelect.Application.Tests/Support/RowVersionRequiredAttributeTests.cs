using DoSelect.Application.Support.Dtos;

namespace DoSelect.Application.Tests.Support;

public sealed class RowVersionRequiredAttributeTests
{
    private static readonly RowVersionRequiredAttribute Attribute = new();

    [Fact]
    public void IsValid_WhenExactlyEightBytes_ReturnsTrue()
    {
        Assert.True(Attribute.IsValid(new byte[8]));
    }

    [Fact]
    public void IsValid_WhenNull_ReturnsFalse()
    {
        Assert.False(Attribute.IsValid(null));
    }

    [Fact]
    public void IsValid_WhenEmptyArray_ReturnsFalse()
    {
        // This is the record's default value ([]) when the field is omitted from the request
        // body entirely — [Required] alone does not catch this because an empty array is not
        // null. This is the exact gap the attribute exists to close.
        Assert.False(Attribute.IsValid(Array.Empty<byte>()));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(16)]
    public void IsValid_WhenLengthIsNotEight_ReturnsFalse(int length)
    {
        Assert.False(Attribute.IsValid(new byte[length]));
    }

    [Fact]
    public void IsValid_WhenValueIsNotByteArray_ReturnsFalse()
    {
        Assert.False(Attribute.IsValid("AAAAAAAAAAE="));
    }
}
