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

/// <summary>
/// One line's quantity ceiling for <see cref="IReturnStore.CreateWithItemsAsync"/> to
/// re-verify itself, under lock, inside its own transaction — the Application layer's
/// pre-transaction sum check (against a snapshot read outside any transaction) is only a cheap
/// fast-fail; it is never sufficient on its own to prevent two concurrent creates from both
/// reading the same "remaining" quantity and both succeeding. <see cref="MaximumReturnableQuantity"/>
/// is the static per-OrderItem ceiling (ReturnableQuantity − ReturnedQuantity) from the caller's
/// already-RowVersion-checked Order snapshot; only the *dynamic* part — how much of that ceiling
/// concurrent ReturnRequests have already consumed — needs a fresh, lock-protected re-read.
/// </summary>
public sealed record ReturnItemQuantityBudget(long OrderItemId, int RequestedQuantity, int MaximumReturnableQuantity);

public sealed record ReturnAttachmentAccess(
    long ReturnRequestId,
    string? MemberUserId,
    long OrderId,
    string StorageKey,
    string OriginalFileName,
    string ContentType);

public sealed record AppendShipmentEventResult(ReturnShipment Shipment, bool WasDuplicate);

public interface IReturnStore
{
    Task<bool> ReturnNumberExistsAsync(string returnNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the request and its items atomically (one transaction), assigning the request's
    /// generated Id to the item factories before they run — mirrors
    /// SupportTicketStore.CreateTicketWithArtifactsAsync. Before inserting, locks each distinct
    /// <paramref name="quantityBudgets"/> OrderItem row (ascending by Id, to avoid deadlocking
    /// against another multi-item create touching the same items in a different order) and
    /// re-sums each item's already-active ReturnItem quantity under that lock, throwing
    /// <see cref="ReturnQuantityConflictException"/> if a concurrent create has since consumed
    /// the remaining budget — this is the actual concurrency gate; the Application layer's own
    /// pre-check is only a fast-fail hint. Throws <see cref="ReturnNumberCollisionException"/>
    /// (never a raw DbUpdateException) on a real SQL unique-constraint violation of the
    /// ReturnNumber index.
    /// </summary>
    Task<ReturnCreationResult> CreateWithItemsAsync(
        ReturnRequest request,
        IReadOnlyList<ReturnItemQuantityBudget> quantityBudgets,
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

    /// <summary>Fast-fail hint only, read outside any lock — the real cap enforcement is
    /// <see cref="TryAddAttachmentAsync"/>, which re-counts under a row lock immediately before
    /// inserting.</summary>
    Task<int> CountActiveAttachmentsAsync(long returnRequestId, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the owning ReturnRequest row (UPDLOCK/HOLDLOCK, mirroring
    /// <see cref="CreateWithItemsAsync"/>'s OrderItem locking), re-counts active attachments under
    /// that lock, and only inserts if still under <paramref name="maxActiveAttachments"/> —
    /// closes the race where two concurrent uploads both read "count &lt; cap" before either
    /// commits. Returns false (no row inserted, no exception) when the cap was already reached by
    /// the time the lock was acquired; the caller is responsible for compensating (deleting the
    /// physical file it already stored) in that case.
    /// </summary>
    Task<bool> TryAddAttachmentAsync(
        ReturnAttachment attachment, int maxActiveAttachments, CancellationToken cancellationToken);

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
        IReadOnlyList<ReturnStatusHistory> historiesToAdd,
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
    /// Serializes appends for one shipment in a short database transaction. The store locks and
    /// reloads the latest Shipment/ReturnRequest before invoking <paramref name="applyToLatestState"/>,
    /// so concurrent distinct events are both retained and state advancement is recomputed from
    /// current data. The same (Source, ExternalEventId) is an idempotent no-op.
    /// </summary>
    Task<AppendShipmentEventResult> AppendShipmentEventAsync(
        ReturnShipmentEvent shipmentEvent,
        Func<ReturnShipment, ReturnRequest, IReadOnlyList<ReturnStatusHistory>> applyToLatestState,
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

/// <summary>Signals that a concurrent create consumed the remaining returnable quantity for an
/// OrderItem between the caller's snapshot check and this transaction's lock-protected re-sum —
/// the actual race this whole locking scheme exists to close. An internal store-level signal,
/// mirroring <see cref="ReturnNumberCollisionException"/>; the Application layer maps it to the
/// stable <c>return_quantity_exceeded</c> error, never lets it (or any raw SQL detail) reach the
/// API.</summary>
public sealed class ReturnQuantityConflictException : Exception
{
    public ReturnQuantityConflictException(long orderItemId)
        : base($"OrderItem {orderItemId} no longer has enough remaining returnable quantity.")
    {
        OrderItemId = orderItemId;
    }

    public long OrderItemId { get; }
}

/// <summary>
/// Raised when a return-attachment upload fails to write metadata after the physical file was
/// already committed, and the follow-up compensation delete of that orphaned file also fails (or
/// the delete itself throws) — mirrors Support's SupportAttachmentCompensationException. Carries
/// the compensation failure as <see cref="Exception.InnerException"/> so it is never silently
/// dropped; the API layer maps this to a generic server error and never echoes
/// <see cref="StorageKey"/> or any path back to the client.
/// </summary>
public sealed class ReturnAttachmentCompensationException : Exception
{
    public ReturnAttachmentCompensationException(string storageKey, Exception? cleanupFailure)
        : base(BuildMessage(storageKey, cleanupFailure), cleanupFailure)
    {
        StorageKey = storageKey;
    }

    /// <summary>The opaque, server-generated storage key of the orphaned file. Never a physical path.</summary>
    public string StorageKey { get; }

    private static string BuildMessage(string storageKey, Exception? cleanupFailure) =>
        cleanupFailure is null
            ? $"Failed to compensate orphaned return-attachment storage key '{storageKey}' after a metadata write failure."
            : $"Failed to compensate orphaned return-attachment storage key '{storageKey}' after a metadata write failure: {cleanupFailure.Message}";
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
