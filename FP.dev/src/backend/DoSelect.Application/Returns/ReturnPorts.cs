using DoSelect.Domain.Returns;

namespace DoSelect.Application.Returns;

// ---- Cross-module read ports (Kafen owns these narrow adapters; haru has not published a
// shared Order port yet, so — mirroring Support's IOrderOwnershipLookup precedent — Returns'
// own Infrastructure implements them by reading Order/OrderItem/GuestOrderAccessToken directly.
// Never shares a Repository/DbContext abstraction with another module's Application code. ----

public sealed record EligibleOrderItem(
    long OrderItemId,
    Guid OrderItemPublicId,
    string SkuCodeSnapshot,
    string ProductNameSnapshot,
    int ReturnableQuantity,
    int ReturnedQuantity,
    Guid? AssemblyGroupKey,
    bool AssemblyStarted,
    decimal FinalUnitPrice);

public sealed record OrderEligibilitySnapshot(
    long OrderId,
    Guid OrderPublicId,
    string OrderNumber,
    string? MemberUserId,
    DateTime? DeliveredAtUtc,
    int ReturnPolicyVersion,
    byte[] RowVersion,
    IReadOnlyList<EligibleOrderItem> Items);

public interface IReturnOrderEligibilityPort
{
    Task<OrderEligibilitySnapshot?> FindByPublicIdAsync(Guid orderPublicId, CancellationToken cancellationToken);

    Task<OrderEligibilitySnapshot?> FindByIdAsync(long orderId, CancellationToken cancellationToken);
}

/// <summary>
/// Validates a presented guest-order-access cookie value. The mint flow (C-17
/// /guest-orders/verify) does not exist anywhere in origin/dev yet, so no cookie name or
/// hashing algorithm is fixed by merged code. This reads the already-finalized
/// GuestOrderAccessTokens schema directly (SHA-256 of the raw token) — see the implementation
/// result report for the alignment risk this creates with haru's eventual SH-05 mint flow.
/// </summary>
public interface IGuestOrderAccessValidator
{
    Task<long?> ValidateAsync(
        string rawToken,
        long requestedOrderId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>For routes keyed on a resource (a return) rather than an order — resolves which
    /// order a presented cookie is bound to, without the caller needing to know it in advance.
    /// The caller must still scope every subsequent query to this resolved OrderId; this alone
    /// does not imply access to any particular return.</summary>
    Task<long?> ResolveOrderIdAsync(string rawToken, DateTime nowUtc, CancellationToken cancellationToken);
}

// ---- Persistence port ----

public sealed record ReturnCreationResult(long ReturnRequestId, ReturnRequest Request, IReadOnlyList<ReturnItem> Items);

public sealed record ReturnAttachmentAccess(
    long ReturnRequestId,
    string? MemberUserId,
    long OrderId,
    string StorageKey,
    string OriginalFileName,
    string ContentType);

public interface IReturnStore
{
    Task<bool> ReturnNumberExistsAsync(string returnNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the request and its items atomically (one transaction), assigning the request's
    /// generated Id to the item factories before they run — mirrors
    /// SupportTicketStore.CreateTicketWithArtifactsAsync. Throws
    /// <see cref="ReturnNumberCollisionException"/> (never a raw DbUpdateException) on a real
    /// SQL unique-constraint violation of the ReturnNumber index.
    /// </summary>
    Task<ReturnCreationResult> CreateWithItemsAsync(
        ReturnRequest request,
        Func<long, IReadOnlyList<ReturnItem>> itemsFactory,
        CancellationToken cancellationToken);

    /// <summary>Actor-scoped lookup: memberUserId for a member owner, or guestOrderId for a
    /// validated guest — exactly one must be non-null. Cross-owner/cross-order rows never
    /// match, so callers get the same 404 as a truly missing return.</summary>
    Task<ReturnRequest?> FindOwnedAsync(
        Guid returnPublicId,
        string? memberUserId,
        long? guestOrderId,
        CancellationToken cancellationToken);

    /// <summary>Unscoped lookup for admin surfaces (already gated by Policy at the Controller).</summary>
    Task<ReturnRequest?> FindByPublicIdAsync(Guid returnPublicId, CancellationToken cancellationToken);

    /// <summary>Raw tracked entities — used internally for admin inspection writes.</summary>
    Task<IReadOnlyList<ReturnItem>> ListItemsAsync(long returnRequestId, CancellationToken cancellationToken);

    /// <summary>Read-only projection enriched with the owning OrderItem's SKU/product snapshot
    /// and public id (a SQL join in Infrastructure) — what customer/admin detail views display.</summary>
    Task<IReadOnlyList<ReturnItemDto>> ListItemSummariesAsync(long returnRequestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReturnAttachmentDto>> ListCleanAttachmentSummariesAsync(
        long returnRequestId,
        CancellationToken cancellationToken);

    Task<int> CountActiveAttachmentsAsync(long returnRequestId, CancellationToken cancellationToken);

    Task AddAttachmentAsync(ReturnAttachment attachment, CancellationToken cancellationToken);

    /// <summary>Everything the download endpoint needs in one query: enough to check Actor
    /// Scope (owning member/order) and to stream the file — null for anonymous/wrong-domain
    /// ids, deleted, or non-Clean attachments (all converge on the same 404).</summary>
    Task<ReturnAttachmentAccess?> FindAttachmentAccessAsync(Guid attachmentPublicId, CancellationToken cancellationToken);

    /// <summary>
    /// Sums Quantity across every ReturnItem for this OrderItem whose owning ReturnRequest is
    /// NOT Rejected/Cancelled (i.e. still consumes the item's returnable allowance — including
    /// completed returns, since this module does not maintain OrderItem.ReturnedQuantity
    /// itself; see the implementation report's cross-owner decision on this). Used to compute
    /// remaining-returnable quantity under concurrency without touching haru's OrderItem row.
    /// </summary>
    Task<int> SumActiveRequestedQuantityAsync(long orderItemId, CancellationToken cancellationToken);

    /// <summary>Enriched with each row's own Order PublicId/Number (a SQL join in
    /// Infrastructure) — the admin list page needs an order-safe summary per row.</summary>
    Task<(IReadOnlyList<AdminReturnSummaryDto> Items, int TotalCount)> ListForAdminAsync(
        AdminReturnQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReturnHistoryEntryDto>> ListHistoryAsync(long returnRequestId, CancellationToken cancellationToken);

    /// <summary>Enriched with each row's own ReturnItem PublicId (a SQL join in
    /// Infrastructure) — ReturnInspection only carries the internal ReturnItemId.</summary>
    Task<IReadOnlyList<ReturnInspectionDto>> ListInspectionsAsync(long returnRequestId, CancellationToken cancellationToken);

    Task<ReturnShipment?> FindShipmentAsync(long returnRequestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReturnShipmentEvent>> ListShipmentEventsAsync(long returnShipmentId, CancellationToken cancellationToken);

    Task<bool> ShipmentEventExistsAsync(string source, string externalEventId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a mutation already applied in-memory to <paramref name="request"/> (and any
    /// staged item/inspection/history additions), enforcing <paramref name="expectedRowVersion"/>
    /// as an optimistic-concurrency precondition. One transaction. Covers every admin
    /// state-changing action (approve/reject/receive/inspect/extend) and the background
    /// overdue-cancel path (pass the RowVersion already loaded in that same call).
    /// </summary>
    Task SaveTransitionAsync(
        ReturnRequest request,
        IReadOnlyList<ReturnItem>? itemsToUpdate,
        IReadOnlyList<ReturnInspection>? inspectionsToAdd,
        ReturnStatusHistory? historyToAdd,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken);

    /// <summary>Inserts a new ReturnShipment row (Status=Pending) for a request already
    /// verified to be AwaitingShipment with no existing active shipment, RowVersion-checked
    /// against the request in the same transaction (no request field changes here).</summary>
    Task<ReturnShipment> CreateShipmentAsync(
        ReturnShipment shipment,
        long returnRequestId,
        byte[] expectedReturnRowVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// One transaction: insert the (already deduplication-checked) event, update the shipment's
    /// denormalized status, and optionally transition the owning ReturnRequest
    /// (AwaitingShipment→InTransit / InTransit→Received) with its own history row.
    /// </summary>
    Task AppendShipmentEventAsync(
        ReturnShipmentEvent shipmentEvent,
        ReturnShipment shipment,
        ReturnRequest? requestToTransition,
        ReturnStatusHistory? requestHistory,
        CancellationToken cancellationToken);

    /// <summary>
    /// Idempotent background sweep: cancels every AwaitingShipment request whose
    /// ReturnShipmentDueAtUtc has passed as of <paramref name="nowUtc"/>, each in its own
    /// RowVersion-checked transaction loaded fresh inside this call (a request already
    /// cancelled by a prior run simply no longer matches the query). Returns the PublicIds
    /// actually cancelled by this invocation.
    /// </summary>
    Task<IReadOnlyList<Guid>> CancelOverdueAwaitingShipmentAsync(DateTime nowUtc, CancellationToken cancellationToken);
}

/// <summary>Signals a ReturnNumber unique-constraint collision — mirrors
/// SupportTicketNumberCollisionException so the create Use Case can retry with a fresh
/// candidate instead of this internal signal escaping to the Api layer as a 500.</summary>
public sealed class ReturnNumberCollisionException : Exception
{
    public ReturnNumberCollisionException(string returnNumber, Exception innerException)
        : base($"ReturnNumber '{returnNumber}' collided with an existing return.", innerException)
    {
        ReturnNumber = returnNumber;
    }

    public string ReturnNumber { get; }
}

public sealed class ReturnsWriteException : Exception
{
    public ReturnsWriteException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }

    public static class ErrorCodes
    {
        public const string ValidationFailed = "validation_failed";
        public const string ResourceNotFound = "resource_not_found";
        public const string AuthorizationForbidden = "authorization_forbidden";
        public const string ConcurrencyConflict = "concurrency_conflict";
        public const string ReturnDeadlineExpired = "return_deadline_expired";
        public const string ReturnQuantityExceeded = "return_quantity_exceeded";
        public const string ReturnStateConflict = "return_state_conflict";
        public const string ReturnShipmentDeadlineExpired = "return_shipment_deadline_expired";
        public const string ReturnShipmentExtensionNotAllowed = "return_shipment_extension_not_allowed";
        public const string FileCountExceeded = "file_count_exceeded";
        public const string FileSizeExceeded = "file_size_exceeded";
        public const string FileFormatInvalid = "file_format_invalid";
        public const string FileMalwareDetected = "file_malware_detected";
        public const string FileScanUnavailable = "file_scan_unavailable";
    }
}
