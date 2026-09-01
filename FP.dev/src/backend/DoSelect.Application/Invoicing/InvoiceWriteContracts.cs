using System.Net;
using DoSelect.Application.Idempotency;

namespace DoSelect.Application.Invoicing;

public sealed record IssueSimulatedInvoiceCommand(
    Guid OrderPublicId,
    byte[] OrderRowVersion,
    string IdempotencyKey,
    string AdminUserId,
    string CorrelationId,
    string TraceId,
    IPAddress? RemoteIpAddress);

public sealed record VoidSimulatedInvoiceCommand(
    Guid InvoicePublicId,
    string ReasonCode,
    string? Note,
    byte[] InvoiceRowVersion,
    string AdminUserId,
    string CorrelationId,
    string TraceId,
    IPAddress? RemoteIpAddress);

public interface IAdminInvoiceWriter
{
    Task<IdempotencyExecutionResult<AdminInvoiceDto>> IssueAsync(
        IssueSimulatedInvoiceCommand command,
        CancellationToken cancellationToken = default);

    Task<AdminInvoiceDto> VoidAsync(
        VoidSimulatedInvoiceCommand command,
        CancellationToken cancellationToken = default);
}

public static class InvoiceWriteConstants
{
    public const string IssueOperation = "invoice.issue";
    public const string IssueAuditReason = "manual_issue";
}
