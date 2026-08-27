using System.Reflection;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Shopping;
using DoSelect.Domain.Shipping;

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

    [Fact]
    public void ShippingMethod_RequiresAndPreservesProviderCode()
    {
        var method = new ShippingMethod(
            Guid.NewGuid(),
            "CVS_PICKUP",
            "超商取貨",
            "ConvenienceStore",
            60m,
            1_000m,
            allowsCod: true,
            requiresPrepayment: false,
            providerCode: "CVS_DEMO",
            CreatedAtUtc);

        Assert.Equal("CVS_DEMO", method.ProviderCode);
        Assert.Throws<ArgumentException>(() => new ShippingMethod(
            Guid.NewGuid(),
            "HOME",
            "宅配",
            "HomeDelivery",
            150m,
            5_000m,
            allowsCod: false,
            requiresPrepayment: true,
            providerCode: " ",
            CreatedAtUtc));
    }

    [Fact]
    public void SpecificationDefinition_AllowsMultipleOnlyForOptionValues()
    {
        var definition = new SpecificationDefinition(
            Guid.NewGuid(), 1, "CPU_SOCKET", "支援 Socket", SpecificationValueType.Option,
            null, true, true, 1, CreatedAtUtc, allowsMultiple: true);

        Assert.True(definition.AllowsMultiple);
        Assert.Throws<ArgumentException>(() => new SpecificationDefinition(
            Guid.NewGuid(), 1, "POWER_DRAW_WATTS", "功耗", SpecificationValueType.Decimal,
            1, true, true, 1, CreatedAtUtc, allowsMultiple: true));
    }

    [Fact]
    public void SkuSpecificationOptionSelection_RequiresPositiveReferences()
    {
        var selection = new SkuSpecificationOptionSelection(1, 2, CreatedAtUtc);

        Assert.Equal(1, selection.SkuId);
        Assert.Equal(2, selection.SpecificationOptionId);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SkuSpecificationOptionSelection(0, 2, CreatedAtUtc));
    }

    [Fact]
    public void CompatibilityCatalogContract_UsesApprovedCategoryAndMultiValueKeys()
    {
        Assert.Equal("CPU", CompatibilityCatalogContract.Categories.Cpu);
        Assert.Equal("CPU_COOLER", CompatibilityCatalogContract.Categories.CpuCooler);
        Assert.Equal("CPU_SOCKET", CompatibilityCatalogContract.SemanticKeys.CpuSocket);
        Assert.Equal(
            [
                CompatibilityCatalogContract.SemanticKeys.CpuSocket,
                CompatibilityCatalogContract.SemanticKeys.CaseSupportedMotherboardFormFactor,
                CompatibilityCatalogContract.SemanticKeys.CaseSupportedPsuFormFactor,
            ],
            CompatibilityCatalogContract.MultiValueSemanticKeys);
    }
}
