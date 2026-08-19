using DoSelect.Domain.Common;

namespace DoSelect.Domain.Orders;

public sealed record OrderCreation(
    string OrderNumber,
    string? MemberUserId,
    string? GuestEmailNormalized,
    OrderStatus OrderStatus,
    PaymentStatus PaymentStatus,
    FulfillmentStatus FulfillmentStatus,
    AssemblyStatus AssemblyStatus,
    decimal MerchandiseSubtotal,
    decimal ItemDiscountTotal,
    decimal ShippingFee,
    decimal AssemblyFee,
    decimal GrandTotal,
    string RecipientName,
    string RecipientPhone,
    string RecipientEmail,
    string? PostalCode,
    string? RecipientCity,
    string? RecipientDistrict,
    string? AddressLine1,
    string? AddressLine2,
    string ShippingMethodCode,
    long ShippingProviderProfileVersionId,
    string? StoreCode,
    string? StoreName,
    string? StoreAddress,
    int ShippingConstraintPolicyVersion,
    int ReturnPolicyVersion,
    int? CouponPolicyVersion,
    DateTime? PaymentDueAtUtc,
    string CheckoutIdempotencyKey,
    Guid? SourceCartPublicId);

public sealed class Order : MutablePublicEntity
{
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> AllowedTransitions =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.PendingPayment] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
            [OrderStatus.Confirmed] = [OrderStatus.Processing, OrderStatus.Cancelled],
            [OrderStatus.Processing] = [OrderStatus.Completed, OrderStatus.Cancelled],
            [OrderStatus.Completed] = [],
            [OrderStatus.Cancelled] = [],
        };

    private Order()
    {
    }

    private Order(Guid publicId, OrderCreation creation, DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(creation);
        if (string.IsNullOrWhiteSpace(creation.MemberUserId) &&
            string.IsNullOrWhiteSpace(creation.GuestEmailNormalized))
        {
            throw new ArgumentException(
                "A member user or guest Email is required.",
                nameof(creation));
        }

        ValidateAmounts(creation);

        OrderNumber = RequireText(creation.OrderNumber, nameof(creation.OrderNumber));
        MemberUserId = NormalizeOptional(creation.MemberUserId);
        GuestEmailNormalized = NormalizeOptional(creation.GuestEmailNormalized);
        OrderStatus = creation.OrderStatus;
        PaymentStatus = creation.PaymentStatus;
        FulfillmentStatus = creation.FulfillmentStatus;
        AssemblyStatus = creation.AssemblyStatus;
        OrderRefundStatus = OrderRefundStatus.None;
        MerchandiseSubtotal = creation.MerchandiseSubtotal;
        ItemDiscountTotal = creation.ItemDiscountTotal;
        ShippingFee = creation.ShippingFee;
        AssemblyFee = creation.AssemblyFee;
        GrandTotal = creation.GrandTotal;
        Currency = "TWD";
        RecipientName = RequireText(creation.RecipientName, nameof(creation.RecipientName));
        RecipientPhone = RequireText(creation.RecipientPhone, nameof(creation.RecipientPhone));
        RecipientEmail = RequireText(creation.RecipientEmail, nameof(creation.RecipientEmail));
        PostalCode = NormalizeOptional(creation.PostalCode);
        RecipientCity = NormalizeOptional(creation.RecipientCity);
        RecipientDistrict = NormalizeOptional(creation.RecipientDistrict);
        AddressLine1 = NormalizeOptional(creation.AddressLine1);
        AddressLine2 = NormalizeOptional(creation.AddressLine2);
        ShippingMethodCode = RequireText(
            creation.ShippingMethodCode,
            nameof(creation.ShippingMethodCode));
        ShippingProviderProfileVersionId = creation.ShippingProviderProfileVersionId > 0
            ? creation.ShippingProviderProfileVersionId
            : throw new ArgumentOutOfRangeException(
                nameof(creation.ShippingProviderProfileVersionId));
        StoreCode = NormalizeOptional(creation.StoreCode);
        StoreName = NormalizeOptional(creation.StoreName);
        StoreAddress = NormalizeOptional(creation.StoreAddress);
        ShippingConstraintPolicyVersion = RequirePositive(
            creation.ShippingConstraintPolicyVersion,
            nameof(creation.ShippingConstraintPolicyVersion));
        ReturnPolicyVersion = RequirePositive(
            creation.ReturnPolicyVersion,
            nameof(creation.ReturnPolicyVersion));
        CouponPolicyVersion = creation.CouponPolicyVersion.HasValue
            ? RequirePositive(
                creation.CouponPolicyVersion.Value,
                nameof(creation.CouponPolicyVersion))
            : null;
        PaymentDueAtUtc = RequireOptionalUtc(
            creation.PaymentDueAtUtc,
            nameof(creation.PaymentDueAtUtc));
        CheckoutIdempotencyKey = RequireText(
            creation.CheckoutIdempotencyKey,
            nameof(creation.CheckoutIdempotencyKey));
        SourceCartPublicId = creation.SourceCartPublicId;
    }

    public string OrderNumber { get; private set; } = string.Empty;

    public string? MemberUserId { get; private set; }

    public string? GuestEmailNormalized { get; private set; }

    public OrderStatus OrderStatus { get; private set; }

    public PaymentStatus PaymentStatus { get; private set; }

    public FulfillmentStatus FulfillmentStatus { get; private set; }

    public AssemblyStatus AssemblyStatus { get; private set; }

    public OrderRefundStatus OrderRefundStatus { get; private set; }

    public decimal MerchandiseSubtotal { get; private set; }

    public decimal ItemDiscountTotal { get; private set; }

    public decimal ShippingFee { get; private set; }

    public decimal AssemblyFee { get; private set; }

    public decimal GrandTotal { get; private set; }

    public decimal PaidAmount { get; private set; }

    public decimal RefundedAmount { get; private set; }

    public string Currency { get; private set; } = "TWD";

    public string RecipientName { get; private set; } = string.Empty;

    public string RecipientPhone { get; private set; } = string.Empty;

    public string RecipientEmail { get; private set; } = string.Empty;

    public string? PostalCode { get; private set; }

    public string? RecipientCity { get; private set; }

    public string? RecipientDistrict { get; private set; }

    public string? AddressLine1 { get; private set; }

    public string? AddressLine2 { get; private set; }

    public string ShippingMethodCode { get; private set; } = string.Empty;

    public long ShippingProviderProfileVersionId { get; private set; }

    public string? StoreCode { get; private set; }

    public string? StoreName { get; private set; }

    public string? StoreAddress { get; private set; }

    public int ShippingConstraintPolicyVersion { get; private set; }

    public int ReturnPolicyVersion { get; private set; }

    public int? CouponPolicyVersion { get; private set; }

    public DateTime? PaymentDueAtUtc { get; private set; }

    public DateTime? ConfirmedAtUtc { get; private set; }

    public DateTime? PaidAtUtc { get; private set; }

    public DateTime? ShippedAtUtc { get; private set; }

    public DateTime? DeliveredAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }

    public string CheckoutIdempotencyKey { get; private set; } = string.Empty;

    public Guid? SourceCartPublicId { get; private set; }

    public static Order Create(Guid publicId, OrderCreation creation, DateTime createdAtUtc) =>
        new(publicId, creation, createdAtUtc);

    public void ChangeOrderStatus(OrderStatus nextStatus, DateTime occurredAtUtc)
    {
        if (!AllowedTransitions[OrderStatus].Contains(nextStatus))
        {
            throw new InvalidOperationException(
                $"Order status cannot move from {OrderStatus} to {nextStatus}.");
        }

        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        OrderStatus = nextStatus;
        ConfirmedAtUtc = nextStatus == OrderStatus.Confirmed ? occurredAtUtc : ConfirmedAtUtc;
        CompletedAtUtc = nextStatus == OrderStatus.Completed ? occurredAtUtc : CompletedAtUtc;
        CancelledAtUtc = nextStatus == OrderStatus.Cancelled ? occurredAtUtc : CancelledAtUtc;
        MarkUpdated(occurredAtUtc);
    }

    public void ApplyPaymentProjection(
        PaymentStatus status,
        decimal paidAmount,
        DateTime occurredAtUtc)
    {
        if (paidAmount < 0 || paidAmount > GrandTotal)
        {
            throw new ArgumentOutOfRangeException(nameof(paidAmount));
        }

        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        PaymentStatus = status;
        PaidAmount = paidAmount;
        PaidAtUtc = status == PaymentStatus.Paid ? occurredAtUtc : PaidAtUtc;
        MarkUpdated(occurredAtUtc);
    }

    public void ApplyFulfillmentProjection(
        FulfillmentStatus status,
        DateTime occurredAtUtc)
    {
        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        FulfillmentStatus = status;
        ShippedAtUtc = status == FulfillmentStatus.Shipped ? occurredAtUtc : ShippedAtUtc;
        DeliveredAtUtc = status is FulfillmentStatus.Delivered or FulfillmentStatus.PickedUp
            ? occurredAtUtc
            : DeliveredAtUtc;
        MarkUpdated(occurredAtUtc);
    }

    public void ApplyAssemblyProjection(AssemblyStatus status, DateTime occurredAtUtc)
    {
        AssemblyStatus = status;
        MarkUpdated(occurredAtUtc);
    }

    public void ApplyRefundProjection(
        OrderRefundStatus status,
        decimal refundedAmount,
        DateTime occurredAtUtc)
    {
        if (refundedAmount < 0 || refundedAmount > PaidAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(refundedAmount));
        }

        OrderRefundStatus = status;
        RefundedAmount = refundedAmount;
        MarkUpdated(occurredAtUtc);
    }

    private static void ValidateAmounts(OrderCreation creation)
    {
        var amounts = new[]
        {
            creation.MerchandiseSubtotal,
            creation.ItemDiscountTotal,
            creation.ShippingFee,
            creation.AssemblyFee,
            creation.GrandTotal,
        };
        if (amounts.Any(amount => amount < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(creation), "Amounts cannot be negative.");
        }

        var expectedTotal = creation.MerchandiseSubtotal - creation.ItemDiscountTotal +
            creation.ShippingFee + creation.AssemblyFee;
        if (creation.GrandTotal != expectedTotal)
        {
            throw new ArgumentException("GrandTotal does not match its components.", nameof(creation));
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int RequirePositive(int value, string parameterName) =>
        value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName);

    private static DateTime? RequireOptionalUtc(DateTime? value, string parameterName) =>
        value.HasValue ? RequireUtc(value.Value, parameterName) : null;
}

public sealed class OrderItem : PublicEntity
{
    private OrderItem()
    {
    }

    public OrderItem(
        Guid publicId,
        long orderId,
        long? skuId,
        string skuCodeSnapshot,
        string productNameSnapshot,
        string skuNameSnapshot,
        int quantity,
        decimal listUnitPrice,
        decimal saleUnitPrice,
        decimal finalUnitPrice,
        decimal unitCostSnapshot,
        decimal lineSubtotal,
        decimal discountAllocation,
        decimal lineTotal,
        Guid? assemblyGroupKey,
        int returnableQuantity,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (orderId <= 0 || quantity <= 0 || returnableQuantity < 0 ||
            returnableQuantity > quantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        var amounts = new[]
        {
            listUnitPrice,
            saleUnitPrice,
            finalUnitPrice,
            unitCostSnapshot,
            lineSubtotal,
            discountAllocation,
            lineTotal,
        };
        if (amounts.Any(amount => amount < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(listUnitPrice));
        }

        OrderId = orderId;
        SkuId = skuId;
        SkuCodeSnapshot = RequireText(skuCodeSnapshot, nameof(skuCodeSnapshot));
        ProductNameSnapshot = RequireText(productNameSnapshot, nameof(productNameSnapshot));
        SkuNameSnapshot = RequireText(skuNameSnapshot, nameof(skuNameSnapshot));
        Quantity = quantity;
        ListUnitPrice = listUnitPrice;
        SaleUnitPrice = saleUnitPrice;
        FinalUnitPrice = finalUnitPrice;
        UnitCostSnapshot = unitCostSnapshot;
        LineSubtotal = lineSubtotal;
        DiscountAllocation = discountAllocation;
        LineTotal = lineTotal;
        AssemblyGroupKey = assemblyGroupKey;
        ReturnableQuantity = returnableQuantity;
    }

    public long OrderId { get; private set; }

    public long? SkuId { get; private set; }

    public string SkuCodeSnapshot { get; private set; } = string.Empty;

    public string ProductNameSnapshot { get; private set; } = string.Empty;

    public string SkuNameSnapshot { get; private set; } = string.Empty;

    public int Quantity { get; private set; }

    public decimal ListUnitPrice { get; private set; }

    public decimal SaleUnitPrice { get; private set; }

    public decimal FinalUnitPrice { get; private set; }

    public decimal UnitCostSnapshot { get; private set; }

    public decimal LineSubtotal { get; private set; }

    public decimal DiscountAllocation { get; private set; }

    public decimal LineTotal { get; private set; }

    public Guid? AssemblyGroupKey { get; private set; }

    public int ReturnableQuantity { get; private set; }

    public int ReturnedQuantity { get; private set; }

    public void RecordReturnedQuantity(int quantity)
    {
        if (quantity < ReturnedQuantity || quantity > ReturnableQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        ReturnedQuantity = quantity;
    }
}

public sealed class OrderStatusHistory : PublicEntity
{
    private OrderStatusHistory()
    {
    }

    public OrderStatusHistory(
        Guid publicId,
        long orderId,
        OrderStateDimension stateDimension,
        string? fromStatus,
        string toStatus,
        string? reasonCode,
        string? actorUserId,
        DateTime occurredAtUtc,
        string traceId)
        : base(publicId, occurredAtUtc)
    {
        if (orderId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(orderId));
        }

        OrderId = orderId;
        StateDimension = stateDimension;
        FromStatus = string.IsNullOrWhiteSpace(fromStatus) ? null : fromStatus.Trim();
        ToStatus = RequireText(toStatus, nameof(toStatus));
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? null : reasonCode.Trim();
        ActorUserId = string.IsNullOrWhiteSpace(actorUserId) ? null : actorUserId.Trim();
        OccurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        TraceId = RequireText(traceId, nameof(traceId));
    }

    public long OrderId { get; private set; }

    public OrderStateDimension StateDimension { get; private set; }

    public string? FromStatus { get; private set; }

    public string ToStatus { get; private set; } = string.Empty;

    public string? ReasonCode { get; private set; }

    public string? ActorUserId { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public string TraceId { get; private set; } = string.Empty;
}
