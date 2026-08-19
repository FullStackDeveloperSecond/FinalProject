namespace DoSelect.Domain.Orders;

public enum OrderStatus
{
    PendingPayment,
    Confirmed,
    Processing,
    Completed,
    Cancelled,
}

public enum PaymentStatus
{
    Pending,
    AwaitingPayment,
    Processing,
    Paid,
    Failed,
    Cancelled,
    Expired,
}

public enum FulfillmentStatus
{
    Pending,
    Preparing,
    Shipped,
    InTransit,
    PickupReady,
    PickedUp,
    Delivered,
    DeliveryFailed,
    Returned,
}

public enum AssemblyStatus
{
    NotRequired,
    Pending,
    Started,
    Testing,
    ReadyToShip,
    Failed,
    Cancelled,
}

public enum OrderRefundStatus
{
    None,
    Pending,
    PartiallyRefunded,
    Refunded,
}

public enum OrderStateDimension
{
    OrderStatus,
    PaymentStatus,
    FulfillmentStatus,
    AssemblyStatus,
    OrderRefundStatus,
}

public enum AssemblyJobStatus
{
    Pending,
    Started,
    Testing,
    ReadyToShip,
    Failed,
    Cancelled,
}
