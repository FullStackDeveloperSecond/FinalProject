using DoSelect.Domain.Common;

namespace DoSelect.Domain.Shopping;

public enum CartStatus
{
    Active,
    Converted,
    Expired,
    Abandoned,
}

public sealed class Cart : MutablePublicEntity
{
    private Cart() { }

    private Cart(
        Guid publicId,
        string? ownerUserId,
        byte[]? guestCartKeyHash,
        DateTime expiresAtUtc,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if ((ownerUserId is null) == (guestCartKeyHash is null))
        {
            throw new ArgumentException("Exactly one cart owner is required.");
        }

        if (guestCartKeyHash is not null && guestCartKeyHash.Length != 32)
        {
            throw new ArgumentException("The hash must contain 32 bytes.", nameof(guestCartKeyHash));
        }

        expiresAtUtc = RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        OwnerUserId = ownerUserId;
        GuestCartKeyHash = guestCartKeyHash?.ToArray();
        Status = CartStatus.Active;
        ExpiresAtUtc = expiresAtUtc;
    }

    public string? OwnerUserId { get; private set; }
    public byte[]? GuestCartKeyHash { get; private set; }
    public CartStatus Status { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    public static Cart CreateForMember(
        Guid publicId,
        string ownerUserId,
        DateTime expiresAtUtc,
        DateTime createdAtUtc) =>
        new(publicId, RequireOwner(ownerUserId), null, expiresAtUtc, createdAtUtc);

    public static Cart CreateForGuest(
        Guid publicId,
        byte[] guestCartKeyHash,
        DateTime expiresAtUtc,
        DateTime createdAtUtc) =>
        new(publicId, null, guestCartKeyHash, expiresAtUtc, createdAtUtc);

    public void ChangeStatus(CartStatus status, DateTime updatedAtUtc)
    {
        Status = status;
        MarkUpdated(updatedAtUtc);
    }

    private static string RequireOwner(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The owner is required.", nameof(value))
            : value.Trim();
}

public sealed class CartItem : MutablePublicEntity
{
    private CartItem() { }

    public CartItem(
        Guid publicId,
        long cartId,
        long skuId,
        int quantity,
        Guid? assemblyGroupKey,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (cartId <= 0 || skuId <= 0 || quantity is < 1 or > 99 ||
            assemblyGroupKey == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        CartId = cartId;
        SkuId = skuId;
        Quantity = quantity;
        AssemblyGroupKey = assemblyGroupKey;
    }

    public long CartId { get; private set; }
    public long SkuId { get; private set; }
    public int Quantity { get; private set; }
    public Guid? AssemblyGroupKey { get; private set; }

    public void ChangeQuantity(int quantity, DateTime updatedAtUtc)
    {
        if (quantity is < 1 or > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        Quantity = quantity;
        MarkUpdated(updatedAtUtc);
    }
}
