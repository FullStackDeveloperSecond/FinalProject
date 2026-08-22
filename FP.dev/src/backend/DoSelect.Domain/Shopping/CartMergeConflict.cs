using DoSelect.Domain.Common;

namespace DoSelect.Domain.Shopping;

public sealed class CartMergeConflict : MutablePublicEntity
{
    private CartMergeConflict()
    {
    }

    public CartMergeConflict(
        Guid publicId,
        long memberCartId,
        long guestCartId,
        Guid guestItemPublicId,
        Guid skuPublicId,
        int guestQuantity,
        int memberQuantity,
        int acceptedQuantity,
        string reason,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (memberCartId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memberCartId));
        }

        if (guestCartId <= 0 || guestCartId == memberCartId)
        {
            throw new ArgumentOutOfRangeException(nameof(guestCartId));
        }

        if (guestItemPublicId == Guid.Empty)
        {
            throw new ArgumentException("Guest item PublicId is required.", nameof(guestItemPublicId));
        }

        if (skuPublicId == Guid.Empty)
        {
            throw new ArgumentException("SKU PublicId is required.", nameof(skuPublicId));
        }

        if (guestQuantity is < 1 or > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(guestQuantity));
        }

        if (memberQuantity is < 0 or > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(memberQuantity));
        }

        if (acceptedQuantity is < 0 or > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(acceptedQuantity));
        }

        MemberCartId = memberCartId;
        GuestCartId = guestCartId;
        GuestItemPublicId = guestItemPublicId;
        SkuPublicId = skuPublicId;
        GuestQuantity = guestQuantity;
        MemberQuantity = memberQuantity;
        AcceptedQuantity = acceptedQuantity;
        Reason = RequireCode(reason, nameof(reason));
    }

    public long MemberCartId { get; private set; }
    public long GuestCartId { get; private set; }
    public Guid GuestItemPublicId { get; private set; }
    public Guid SkuPublicId { get; private set; }
    public int GuestQuantity { get; private set; }
    public int MemberQuantity { get; private set; }
    public int AcceptedQuantity { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTime? ResolvedAtUtc { get; private set; }
    public string? ResolutionCode { get; private set; }
    public bool IsBlocking => ResolvedAtUtc is null;

    public void Resolve(string resolutionCode, DateTime resolvedAtUtc)
    {
        if (ResolvedAtUtc is not null)
        {
            throw new InvalidOperationException("The cart merge conflict is already resolved.");
        }

        ResolutionCode = RequireCode(resolutionCode, nameof(resolutionCode));
        ResolvedAtUtc = RequireUtc(resolvedAtUtc, nameof(resolvedAtUtc));
        MarkUpdated(resolvedAtUtc);
    }

    private static string RequireCode(string value, string parameterName)
    {
        value = RequireText(value, parameterName);
        if (value.Length > 64)
        {
            throw new ArgumentException("The value cannot exceed 64 characters.", parameterName);
        }

        return value;
    }
}
