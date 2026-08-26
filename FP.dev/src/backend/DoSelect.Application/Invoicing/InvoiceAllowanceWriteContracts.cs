using System.Net;
using DoSelect.Application.Idempotency;
using DoSelect.Domain.Invoicing;

namespace DoSelect.Application.Invoicing;

public sealed record CreateInvoiceAllowanceCommand(
    Guid InvoicePublicId,
    Guid RefundPublicId,
    byte[] InvoiceRowVersion,
    string IdempotencyKey,
    string AdminUserId,
    string CorrelationId,
    string TraceId,
    IPAddress? RemoteIpAddress);

public sealed record SimulatedInvoiceAllowanceItemDto(
    Guid PublicId,
    Guid InvoiceItemPublicId,
    InvoiceLineKind Kind,
    int Quantity,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrossAmount);

public sealed record SimulatedInvoiceAllowanceDto(
    Guid PublicId,
    string AllowanceNumber,
    Guid InvoicePublicId,
    Guid RefundPublicId,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrossAmount,
    IReadOnlyList<SimulatedInvoiceAllowanceItemDto> Items,
    DateTime IssuedAtUtc,
    string DemoMarker);

public interface IInvoiceAllowanceWriter
{
    Task<IdempotencyExecutionResult<SimulatedInvoiceAllowanceDto>> CreateAsync(
        CreateInvoiceAllowanceCommand command,
        CancellationToken cancellationToken = default);
}

public static class InvoiceAllowanceWriteConstants
{
    public const string AuditReason = "refund.succeeded";
    public const string Operation = "invoice.allowance.create";
    public const string DemoMarker = SimulatedInvoice.RequiredDemoMarker;
}
