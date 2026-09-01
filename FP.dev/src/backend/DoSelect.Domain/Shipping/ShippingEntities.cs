using DoSelect.Domain.Common;
using DoSelect.Domain.Orders;

namespace DoSelect.Domain.Shipping;

public sealed class ShippingMethod : MutablePublicEntity
{
    private ShippingMethod() { }

    public ShippingMethod(
        Guid publicId,
        string code,
        string nameZhTw,
        string kind,
        decimal baseFee,
        decimal? freeShippingThreshold,
        bool allowsCod,
        bool requiresPrepayment,
        string providerCode,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (baseFee < 0 || freeShippingThreshold < 0 || allowsCod && requiresPrepayment)
        {
            throw new ArgumentOutOfRangeException(nameof(baseFee));
        }

        Code = RequireText(code, nameof(code));
        NameZhTw = RequireText(nameZhTw, nameof(nameZhTw));
        Kind = RequireText(kind, nameof(kind));
        BaseFee = baseFee;
        FreeShippingThreshold = freeShippingThreshold;
        AllowsCod = allowsCod;
        RequiresPrepayment = requiresPrepayment;
        ProviderCode = RequireText(providerCode, nameof(providerCode));
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string NameZhTw { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public decimal BaseFee { get; private set; }
    public decimal? FreeShippingThreshold { get; private set; }
    public bool AllowsCod { get; private set; }
    public bool RequiresPrepayment { get; private set; }
    /// <summary>
    /// Resolves the current published provider profile at Checkout. Null is reserved for rows
    /// created before provider ownership became mandatory; legacy methods cannot be checked out.
    /// </summary>
    public string? ProviderCode { get; private set; }

    public void UpdateFeesAndCapabilities(
        decimal baseFee,
        decimal? freeShippingThreshold,
        bool allowsCod,
        bool requiresPrepayment,
        DateTime updatedAtUtc)
    {
        if (baseFee < 0 || freeShippingThreshold < 0 || allowsCod && requiresPrepayment)
        {
            throw new ArgumentOutOfRangeException(nameof(baseFee));
        }

        BaseFee = baseFee;
        FreeShippingThreshold = freeShippingThreshold;
        AllowsCod = allowsCod;
        RequiresPrepayment = requiresPrepayment;
        MarkUpdated(updatedAtUtc);
    }
}

public sealed class ShippingProviderProfile : MutablePublicEntity
{
    private ShippingProviderProfile() { }

    public ShippingProviderProfile(
        Guid publicId,
        string providerCode,
        int version,
        string status,
        DateTime? effectiveFromUtc,
        DateTime? effectiveToUtc,
        string configurationJson,
        int schemaVersion,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (version <= 0 || schemaVersion <= 0 ||
            effectiveFromUtc.HasValue && effectiveFromUtc.Value.Kind != DateTimeKind.Utc ||
            effectiveToUtc.HasValue && effectiveToUtc.Value.Kind != DateTimeKind.Utc ||
            effectiveFromUtc.HasValue && effectiveToUtc <= effectiveFromUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        ProviderCode = RequireText(providerCode, nameof(providerCode));
        Version = version;
        Status = RequireText(status, nameof(status));
        EffectiveFromUtc = effectiveFromUtc;
        EffectiveToUtc = effectiveToUtc;
        ConfigurationJson = RequireText(configurationJson, nameof(configurationJson));
        SchemaVersion = schemaVersion;
    }

    public string ProviderCode { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTime? EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }
    public string ConfigurationJson { get; private set; } = string.Empty;
    public int SchemaVersion { get; private set; }

    /// <summary>
    /// Draft -> Published. 只有一個 Published／ProviderCode 的不變量由呼叫端在同一交易內
    /// 先 <see cref="Supersede"/> 前一個已發布版本來維持——本方法本身不查詢其他列。
    /// </summary>
    public void Publish(DateTime updatedAtUtc)
    {
        if (Status != ShippingProviderProfileStatuses.Draft)
        {
            throw new InvalidOperationException($"Only a Draft profile can be published; current status is {Status}.");
        }

        Status = ShippingProviderProfileStatuses.Published;
        MarkUpdated(updatedAtUtc);
    }

    /// <summary>
    /// Ends a previously Published version's effective window when a newer one is published.
    /// 組長 PR #73 round-3 裁定 B1：Superseded 只代表「已被接班」，不代表「已失效」——版本是否可用
    /// 由時間窗決定，因此本方法只把窗口收在 cutoff，cutoff 之前這個版本仍是唯一有效版本。
    /// </summary>
    public void Supersede(DateTime effectiveToUtc, DateTime updatedAtUtc)
    {
        if (Status != ShippingProviderProfileStatuses.Published)
        {
            throw new InvalidOperationException($"Only a Published profile can be superseded; current status is {Status}.");
        }

        Status = ShippingProviderProfileStatuses.Superseded;
        EffectiveToUtc = effectiveToUtc;
        MarkUpdated(updatedAtUtc);
    }
}

public static class ShippingProviderProfileStatuses
{
    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Superseded = "Superseded";

    /// <summary>
    /// 組長 PR #73 round-3 裁定 B1：「版本是否可用以有效時間窗為準」。Draft 從未生效；Published 與
    /// Superseded 都是曾經發布過的版本，其可用性只取決於當下是否落在自己的 [From, To) 窗內——
    /// Superseded 的窗口在 Publish 時已被收到 cutoff，所以「Draft 以外 ＋ 窗內」在任一瞬間至多命中
    /// 一個版本。所有解析點（Checkout、Shipping Options）都必須使用同一條件，不可再篩 Published。
    /// </summary>
    public static bool IsNeverEffective(string status) =>
        string.Equals(status, Draft, StringComparison.Ordinal);
}

public sealed class PackageLimitVersion : MutablePublicEntity
{
    private PackageLimitVersion() { }

    public PackageLimitVersion(
        Guid publicId,
        long providerProfileId,
        int version,
        decimal maxWeightKg,
        decimal maxLengthCm,
        decimal maxWidthCm,
        decimal maxHeightCm,
        decimal maxTotalCm,
        decimal maxDeclaredValue,
        DateTime? effectiveFromUtc,
        DateTime? effectiveToUtc,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (providerProfileId <= 0 || version <= 0 ||
            new[]
            {
                maxWeightKg,
                maxLengthCm,
                maxWidthCm,
                maxHeightCm,
                maxTotalCm,
                maxDeclaredValue,
            }.Any(value => value <= 0) ||
            effectiveFromUtc.HasValue && effectiveFromUtc.Value.Kind != DateTimeKind.Utc ||
            effectiveToUtc.HasValue && effectiveToUtc.Value.Kind != DateTimeKind.Utc ||
            effectiveFromUtc.HasValue && effectiveToUtc <= effectiveFromUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        ProviderProfileId = providerProfileId;
        Version = version;
        MaxWeightKg = maxWeightKg;
        MaxLengthCm = maxLengthCm;
        MaxWidthCm = maxWidthCm;
        MaxHeightCm = maxHeightCm;
        MaxTotalCm = maxTotalCm;
        MaxDeclaredValue = maxDeclaredValue;
        EffectiveFromUtc = effectiveFromUtc;
        EffectiveToUtc = effectiveToUtc;
    }

    /// <summary>
    /// 組長 PR #73 round-3, item 2：profile 與 limit 的窗口必須一致。Profile 被 Supersede 收窗時，
    /// 它的限制列也要收在同一個 cutoff，否則舊限制會留下一條比 profile 更長（甚至開放式）的窗口，
    /// 讓「窗內恰好一個有效限制」的不變量在解析時失準。
    /// </summary>
    public void TruncateEffectiveWindow(DateTime effectiveToUtc, DateTime updatedAtUtc)
    {
        if (EffectiveFromUtc.HasValue && effectiveToUtc <= EffectiveFromUtc.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveToUtc),
                "The cutoff must be after this version's own EffectiveFromUtc.");
        }

        EffectiveToUtc = effectiveToUtc;
        MarkUpdated(updatedAtUtc);
    }

    public long ProviderProfileId { get; private set; }
    public int Version { get; private set; }
    public decimal MaxWeightKg { get; private set; }
    public decimal MaxLengthCm { get; private set; }
    public decimal MaxWidthCm { get; private set; }
    public decimal MaxHeightCm { get; private set; }
    public decimal MaxTotalCm { get; private set; }
    public decimal MaxDeclaredValue { get; private set; }
    public DateTime? EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }
}

public sealed class ConvenienceStore : MutablePublicEntity
{
    private ConvenienceStore() { }

    public ConvenienceStore(
        Guid publicId,
        string providerCode,
        string storeCode,
        string storeName,
        string address,
        string city,
        string district,
        bool isDemoData,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        ProviderCode = RequireText(providerCode, nameof(providerCode));
        StoreCode = RequireText(storeCode, nameof(storeCode));
        StoreName = RequireText(storeName, nameof(storeName));
        Address = RequireText(address, nameof(address));
        City = RequireText(city, nameof(city));
        District = RequireText(district, nameof(district));
        IsDemoData = isDemoData;
        IsActive = true;
    }

    public string ProviderCode { get; private set; } = string.Empty;
    public string StoreCode { get; private set; } = string.Empty;
    public string StoreName { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string District { get; private set; } = string.Empty;
    public bool IsDemoData { get; private set; }
    public bool IsActive { get; private set; }

    public void SetActive(bool isActive, DateTime updatedAtUtc)
    {
        IsActive = isActive;
        MarkUpdated(updatedAtUtc);
    }

    /// <summary>ProviderCode/StoreCode are immutable after creation — they're the identity
    /// half of the unique index; only the display/location fields and active state can change.</summary>
    public void UpdateDetails(
        string storeName,
        string address,
        string city,
        string district,
        bool isActive,
        DateTime updatedAtUtc)
    {
        StoreName = RequireText(storeName, nameof(storeName));
        Address = RequireText(address, nameof(address));
        City = RequireText(city, nameof(city));
        District = RequireText(district, nameof(district));
        IsActive = isActive;
        MarkUpdated(updatedAtUtc);
    }
}

public sealed class Shipment : MutablePublicEntity
{
    private static readonly IReadOnlyDictionary<FulfillmentStatus, FulfillmentStatus[]> Transitions =
        new Dictionary<FulfillmentStatus, FulfillmentStatus[]>
        {
            [FulfillmentStatus.Pending] = [FulfillmentStatus.Preparing],
            [FulfillmentStatus.Preparing] = [FulfillmentStatus.Shipped],
            [FulfillmentStatus.Shipped] = [FulfillmentStatus.InTransit],
            [FulfillmentStatus.InTransit] =
                [FulfillmentStatus.Delivered, FulfillmentStatus.PickupReady, FulfillmentStatus.DeliveryFailed],
            [FulfillmentStatus.PickupReady] =
                [FulfillmentStatus.PickedUp, FulfillmentStatus.DeliveryFailed],
            [FulfillmentStatus.DeliveryFailed] =
                [FulfillmentStatus.InTransit, FulfillmentStatus.Returned],
            [FulfillmentStatus.PickedUp] = [],
            [FulfillmentStatus.Delivered] = [],
            [FulfillmentStatus.Returned] = [],
        };

    private Shipment() { }

    public Shipment(
        Guid publicId,
        long orderId,
        long shippingMethodId,
        long providerProfileVersionId,
        long? convenienceStoreId,
        string shipmentNumber,
        decimal feeSnapshot,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (orderId <= 0 || shippingMethodId <= 0 || providerProfileVersionId <= 0 ||
            convenienceStoreId is <= 0 || feeSnapshot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(orderId));
        }

        OrderId = orderId;
        ShippingMethodId = shippingMethodId;
        ProviderProfileVersionId = providerProfileVersionId;
        ConvenienceStoreId = convenienceStoreId;
        ShipmentNumber = RequireText(shipmentNumber, nameof(shipmentNumber));
        Status = FulfillmentStatus.Pending;
        FeeSnapshot = feeSnapshot;
    }

    public long OrderId { get; private set; }
    public long ShippingMethodId { get; private set; }
    public long ProviderProfileVersionId { get; private set; }
    public long? ConvenienceStoreId { get; private set; }
    public string ShipmentNumber { get; private set; } = string.Empty;
    public FulfillmentStatus Status { get; private set; }
    public string? TrackingNumber { get; private set; }
    /// <summary>
    /// Checkout-time shipping method base fee, even when the order actually paid zero shipping.
    /// Refund calculations must not replace this immutable value with the current method fee.
    /// </summary>
    public decimal FeeSnapshot { get; private set; }
    public DateTime? ShippedAtUtc { get; private set; }
    public DateTime? DeliveredAtUtc { get; private set; }

    public void SetTrackingNumber(string trackingNumber, DateTime updatedAtUtc)
    {
        TrackingNumber = RequireText(trackingNumber, nameof(trackingNumber));
        MarkUpdated(updatedAtUtc);
    }

    public void ChangeStatus(FulfillmentStatus status, DateTime occurredAtUtc)
    {
        if (!Transitions[Status].Contains(status))
        {
            throw new InvalidOperationException($"Shipment cannot move from {Status} to {status}.");
        }

        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        Status = status;
        ShippedAtUtc = status == FulfillmentStatus.Shipped ? occurredAtUtc : ShippedAtUtc;
        DeliveredAtUtc = status is FulfillmentStatus.Delivered or FulfillmentStatus.PickedUp
            ? occurredAtUtc
            : DeliveredAtUtc;
        MarkUpdated(occurredAtUtc);
    }
}

public sealed class ShipmentStatusHistory : PublicEntity
{
    private ShipmentStatusHistory() { }

    public ShipmentStatusHistory(
        Guid publicId,
        long shipmentId,
        FulfillmentStatus? fromStatus,
        FulfillmentStatus toStatus,
        string? externalEventId,
        DateTime occurredAtUtc,
        string? actorUserId)
        : base(publicId, occurredAtUtc)
    {
        if (shipmentId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(shipmentId));
        }

        ShipmentId = shipmentId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ExternalEventId = string.IsNullOrWhiteSpace(externalEventId)
            ? null
            : externalEventId.Trim();
        OccurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        ActorUserId = string.IsNullOrWhiteSpace(actorUserId) ? null : actorUserId.Trim();
    }

    public long ShipmentId { get; private set; }
    public FulfillmentStatus? FromStatus { get; private set; }
    public FulfillmentStatus ToStatus { get; private set; }
    public string? ExternalEventId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? ActorUserId { get; private set; }
}
