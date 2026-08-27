using DoSelect.Application.Common;
using DoSelect.Application.Support.Admin.Dtos;

namespace DoSelect.Application.Support.Admin;

/// <summary>
/// Admin-facing read use case for the support SLA queue (UC-SLA-01). The caller is responsible
/// for authenticating/authorizing the admin (CustomerService/CustomerServiceSupervisor policy)
/// before invoking this service. The queue is scoped by the acting admin and supervisor flag.
/// </summary>
public interface ISupportSlaQueueService
{
    /// <summary>
    /// Throws DomainProblemException (validation_failed, 400) when PageSize is outside 1..100 or
    /// Cursor is present but malformed/mismatched for this query shape.
    /// </summary>
    Task<CursorPage<SupportSlaItemDto>> GetPageAsync(
        SupportSlaQueueQuery query,
        string adminUserId,
        bool canSupervise,
        CancellationToken cancellationToken);
}
