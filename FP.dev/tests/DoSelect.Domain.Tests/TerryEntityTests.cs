using System.Reflection;
using DoSelect.Domain.Shopping;

namespace DoSelect.Domain.Tests;

public sealed class TerryEntityTests
{
    private static readonly DateTime CreatedAtUtc = new(2026, 8, 21, 3, 0, 0, DateTimeKind.Utc);

    public static TheoryData<Type> Types => new() { typeof(Cart), typeof(CartItem) };

    [Theory]
    [MemberData(nameof(Types))]
    public void Entity_DoesNotExposePublicPropertySetters(Type type) =>
        Assert.DoesNotContain(
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => property.SetMethod?.IsPublic == true);

    [Fact]
    public void CreateForMember_And_CreateForGuest_RequireExactlyOneOwner()
    {
        var member = Cart.CreateForMember(
            Guid.NewGuid(),
            "member-1",
            CreatedAtUtc.AddDays(30),
            CreatedAtUtc);
        Assert.Equal("member-1", member.OwnerUserId);
        Assert.Null(member.GuestCartKeyHash);

        var guest = Cart.CreateForGuest(
            Guid.NewGuid(),
            new byte[32],
            CreatedAtUtc.AddDays(30),
            CreatedAtUtc);
        Assert.Null(guest.OwnerUserId);
        Assert.NotNull(guest.GuestCartKeyHash);
    }

    [Fact]
    public void CreateForGuest_RejectsAHashThatIsNot32Bytes() =>
        Assert.Throws<ArgumentException>(() => Cart.CreateForGuest(
            Guid.NewGuid(),
            new byte[16],
            CreatedAtUtc.AddDays(30),
            CreatedAtUtc));

    [Fact]
    public void CreateForMember_RejectsAnExpiryAtOrBeforeCreation() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Cart.CreateForMember(
            Guid.NewGuid(),
            "member-1",
            CreatedAtUtc,
            CreatedAtUtc));

    [Fact]
    public void Touch_AdvancesUpdatedAtUtc()
    {
        var cart = Cart.CreateForMember(Guid.NewGuid(), "member-1", CreatedAtUtc.AddDays(30), CreatedAtUtc);

        cart.Touch(CreatedAtUtc.AddMinutes(5));

        Assert.Equal(CreatedAtUtc.AddMinutes(5), cart.UpdatedAtUtc);
    }

    [Fact]
    public void ExtendExpiry_PushesExpiryOutAndUpdatesTimestamp()
    {
        var cart = Cart.CreateForMember(Guid.NewGuid(), "member-1", CreatedAtUtc.AddDays(30), CreatedAtUtc);

        cart.ExtendExpiry(CreatedAtUtc.AddDays(35), CreatedAtUtc.AddDays(5));

        Assert.Equal(CreatedAtUtc.AddDays(35), cart.ExpiresAtUtc);
        Assert.Equal(CreatedAtUtc.AddDays(5), cart.UpdatedAtUtc);
    }

    [Fact]
    public void ExtendExpiry_RejectsAnExpiryAtOrBeforeTheUpdateTime() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var cart = Cart.CreateForMember(Guid.NewGuid(), "member-1", CreatedAtUtc.AddDays(30), CreatedAtUtc);
            cart.ExtendExpiry(CreatedAtUtc.AddDays(5), CreatedAtUtc.AddDays(5));
        });

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void CartItem_RejectsQuantityOutsideOneToNinetyNine(int quantity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CartItem(
            Guid.NewGuid(),
            cartId: 1,
            skuId: 1,
            quantity,
            assemblyGroupKey: null,
            CreatedAtUtc));

    [Fact]
    public void CartItem_RejectsAnEmptyAssemblyGroupKey() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CartItem(
            Guid.NewGuid(),
            cartId: 1,
            skuId: 1,
            quantity: 1,
            assemblyGroupKey: Guid.Empty,
            CreatedAtUtc));

    [Fact]
    public void ChangeQuantity_ValidatesRangeAndUpdatesTimestamp()
    {
        var item = new CartItem(Guid.NewGuid(), cartId: 1, skuId: 1, quantity: 1, assemblyGroupKey: null, CreatedAtUtc);

        item.ChangeQuantity(50, CreatedAtUtc.AddMinutes(1));

        Assert.Equal(50, item.Quantity);
        Assert.Equal(CreatedAtUtc.AddMinutes(1), item.UpdatedAtUtc);
        Assert.Throws<ArgumentOutOfRangeException>(() => item.ChangeQuantity(100, CreatedAtUtc.AddMinutes(2)));
        Assert.Equal(50, item.Quantity);
    }
}
