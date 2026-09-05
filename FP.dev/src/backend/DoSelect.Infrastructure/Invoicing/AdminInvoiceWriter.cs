using System.Data;
using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Invoicing;
using DoSelect.Application.Orders;
using DoSelect.Application.Refunds;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Invoicing;

/// <summary>
/// 後台人工開立與作廢模擬發票。跨模組事實只透過 Orders／Refunds-owned ports 取得。
/// </summary>
public sealed class AdminInvoiceWriter : IAdminInvoiceWriter
{
    private readonly DoSelectDbContext _context;
    private readonly IssueInvoiceService _issuePlanner;
    private readonly InvoiceQueryService _queries;
    private readonly IOrderInvoiceVoidReader _orders;
    private readonly IRefundInvoiceVoidReader _refunds;
    private readonly IIdempotencyExecutor _idempotencyExecutor;
    private readonly IAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    public AdminInvoiceWriter(
        DoSelectDbContext context,
        IssueInvoiceService issuePlanner,
        InvoiceQueryService queries,
        IOrderInvoiceVoidReader orders,
        IRefundInvoiceVoidReader refunds,
        IIdempotencyExecutor idempotencyExecutor,
        IAuditWriter auditWriter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(issuePlanner);
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(refunds);
        ArgumentNullException.ThrowIfNull(idempotencyExecutor);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _context = context;
        _issuePlanner = issuePlanner;
        _queries = queries;
        _orders = orders;
        _refunds = refunds;
        _idempotencyExecutor = idempotencyExecutor;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
    }

    public async Task<IdempotencyExecutionResult<AdminInvoiceDto>> IssueAsync(
        IssueSimulatedInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        RequireWellFormed(command);
        var actor = await ResolveActorAsync(command.AdminUserId, cancellationToken);
        var idempotencyCommand = IdempotencyCommand.Create(
            IdempotencyActorScope.ForAdmin(actor.PublicId!.Value),
            InvoiceWriteConstants.IssueOperation,
            command.IdempotencyKey,
            new
            {
                command.OrderPublicId,
                OrderRowVersion = Convert.ToBase64String(command.OrderRowVersion),
            });

        try
        {
            return await _idempotencyExecutor.ExecuteAsync(
                idempotencyCommand,
                handler: ct => IssueOnceAsync(command, actor, ct),
                replayFactory: ReplayIssueAsync,
                cancellationToken,
                IsolationLevel.Serializable);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict,
                "The order changed. Reload it and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            throw DomainProblemException.Conflict(
                InvoiceErrorCodes.InvoiceAlreadyExists,
                "The order already has a simulated invoice.");
        }
    }

    public async Task<AdminInvoiceDto> VoidAsync(
        VoidSimulatedInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        RequireWellFormed(command);
        var actor = await ResolveActorAsync(command.AdminUserId, cancellationToken);
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var invoice = await _context.SimulatedInvoices.SingleOrDefaultAsync(
                candidate => candidate.PublicId == command.InvoicePublicId,
                cancellationToken);
            if (invoice is null)
            {
                throw DomainProblemException.NotFound("The simulated invoice was not found.");
            }

            _context.Entry(invoice).Property(entity => entity.RowVersion).OriginalValue =
                command.InvoiceRowVersion.ToArray();

            var order = await _orders.FindVoidSnapshotAsync(invoice.OrderId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Invoice '{invoice.PublicId}' references a missing order.");
            var hasSucceededRefund = await _refunds.HasSucceededRefundAsync(
                invoice.OrderId,
                cancellationToken);
            var rejection = InvoicePolicy.FindVoidRejection(
                invoice.Status,
                order.OrderFullyCancelled,
                hasSucceededRefund);
            if (rejection is not null)
            {
                throw DomainProblemException.Conflict(
                    rejection,
                    rejection == InvoiceErrorCodes.InvoiceAllowanceRequired
                        ? "A succeeded refund must be represented by an allowance."
                        : "The simulated invoice cannot be voided in its current state.");
            }

            var previousStatus = invoice.Status;
            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            invoice.Void(nowUtc);
            _auditWriter.Add(AuditWriteRequest.Create(
                Guid.CreateVersion7(),
                actor,
                AuditActions.InvoiceVoid,
                AuditResourceTypes.SimulatedInvoice,
                invoice.PublicId,
                AuditResult.Success,
                errorCode: null,
                [AuditFieldChange.Code("status", previousStatus.ToString(), invoice.Status.ToString())],
                command.ReasonCode,
                command.CorrelationId,
                command.TraceId,
                jobPublicId: null,
                command.RemoteIpAddress,
                command.Note));

            await _context.SaveChangesAsync(cancellationToken);
            var dto = await _queries.FindAsync(invoice.PublicId, cancellationToken)
                ?? throw new InvalidOperationException("The voided invoice could not be projected.");
            await transaction.CommitAsync(cancellationToken);
            return dto;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict,
                "The invoice changed. Reload it and try again.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<IdempotencyResponse<AdminInvoiceDto>> IssueOnceAsync(
        IssueSimulatedInvoiceCommand command,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        var result = await _issuePlanner.IssueAsync(
            new IssueInvoiceRequest(command.OrderPublicId, command.OrderRowVersion),
            cancellationToken);
        if (result.Plan is not { } plan)
        {
            throw MapIssuanceFailure(result.ErrorCode!);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var invoicePublicId = Guid.CreateVersion7();
        var invoice = new SimulatedInvoice(
            invoicePublicId,
            new SimulatedInvoiceCreation(
                plan.OrderId,
                plan.InvoiceNumber,
                plan.BuyerType,
                plan.BuyerEmail,
                plan.CarrierType,
                plan.CarrierValueMasked,
                plan.CompanyTaxId,
                plan.CompanyName,
                plan.NetAmount,
                plan.TaxAmount,
                plan.IssuedAmount),
            nowUtc);
        _context.SimulatedInvoices.Add(invoice);
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var line in plan.Lines)
        {
            var values = line.Breakdown;
            _context.SimulatedInvoiceItems.Add(new SimulatedInvoiceItem(
                Guid.CreateVersion7(),
                invoice.Id,
                line.OrderItemId,
                values.ProductNameSnapshot,
                values.SkuCodeSnapshot,
                values.Quantity,
                values.UnitPrice,
                values.DiscountAmount,
                values.NetAmount,
                values.TaxAmount,
                values.GrossAmount,
                nowUtc));
        }

        invoice.Issue(nowUtc);
        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            actor,
            AuditActions.InvoiceIssue,
            AuditResourceTypes.SimulatedInvoice,
            invoicePublicId,
            AuditResult.Success,
            errorCode: null,
            [
                AuditFieldChange.Code("status", SimulatedInvoiceStatus.Pending.ToString(), invoice.Status.ToString()),
                AuditFieldChange.Changed("itemCount"),
                AuditFieldChange.Changed("grossAmount"),
            ],
            InvoiceWriteConstants.IssueAuditReason,
            command.CorrelationId,
            command.TraceId,
            jobPublicId: null,
            command.RemoteIpAddress));

        await _context.SaveChangesAsync(cancellationToken);
        var dto = await _queries.FindAsync(invoicePublicId, cancellationToken)
            ?? throw new InvalidOperationException("The issued invoice could not be projected.");
        return new IdempotencyResponse<AdminInvoiceDto>(
            201,
            dto,
            JsonSerializer.Serialize(new InvoiceReplayReceipt(invoicePublicId)));
    }

    private async Task<AdminInvoiceDto> ReplayIssueAsync(
        StoredIdempotencyResponse stored,
        CancellationToken cancellationToken)
    {
        var receipt = JsonSerializer.Deserialize<InvoiceReplayReceipt>(stored.ResponseSummary)
            ?? throw new InvalidOperationException("The stored invoice receipt is invalid.");
        return await _queries.FindAsync(receipt.InvoicePublicId, cancellationToken)
            ?? throw new InvalidOperationException("The stored invoice no longer exists.");
    }

    private async Task<AuditActor> ResolveActorAsync(
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var admin = await _context.Users.AsNoTracking()
            .Where(user => user.Id == adminUserId && user.AccountType == AccountType.Admin)
            .Select(user => new { user.Id, user.PublicId })
            .SingleOrDefaultAsync(cancellationToken);
        if (admin is null)
        {
            throw DomainProblemException.Forbidden("The administrator identity is invalid.");
        }

        var roles = await (
            from userRole in _context.UserRoles.AsNoTracking()
            join role in _context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == admin.Id && role.Name != null
            select role.Name!)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (!roles.Contains(AuditRoleNames.FinanceManager, StringComparer.Ordinal) &&
            !roles.Contains(AuditRoleNames.SuperAdmin, StringComparer.Ordinal))
        {
            throw DomainProblemException.Forbidden(
                "The administrator no longer has permission to manage invoices.");
        }

        return AuditActor.Create(AuditActorType.Admin, admin.PublicId, roles);
    }

    private static DomainProblemException MapIssuanceFailure(string errorCode) => errorCode switch
    {
        InvoiceErrorCodes.ResourceNotFound =>
            DomainProblemException.NotFound("The order was not found."),
        DomainErrorCodes.ConcurrencyConflict =>
            DomainProblemException.Conflict(errorCode, "The order changed. Reload it and try again."),
        InvoiceErrorCodes.InvoiceOrderUnpaid =>
            DomainProblemException.Conflict(errorCode, "The order has not been paid."),
        InvoiceErrorCodes.InvoiceOrderCancelled =>
            DomainProblemException.Conflict(errorCode, "A cancelled order cannot be invoiced."),
        InvoiceErrorCodes.InvoiceAlreadyExists =>
            DomainProblemException.Conflict(errorCode, "The order already has a simulated invoice."),
        _ => DomainProblemException.Conflict(
            InvoiceErrorCodes.InvoiceStateConflict,
            "The simulated invoice cannot be issued."),
    };

    private static void RequireWellFormed(IssueSimulatedInvoiceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OrderPublicId == Guid.Empty ||
            command.OrderRowVersion is not { Length: 8 } ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey) ||
            command.IdempotencyKey.Trim().Length > 128 ||
            string.IsNullOrWhiteSpace(command.AdminUserId))
        {
            throw DomainProblemException.Validation(
                "Order, row version, idempotency key, and administrator are required.");
        }
    }

    private static void RequireWellFormed(VoidSimulatedInvoiceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.InvoicePublicId == Guid.Empty ||
            command.InvoiceRowVersion is not { Length: 8 } ||
            string.IsNullOrWhiteSpace(command.ReasonCode) ||
            command.ReasonCode.Trim().Length > 64 ||
            command.Note?.Length > 1_000 ||
            string.IsNullOrWhiteSpace(command.AdminUserId))
        {
            throw DomainProblemException.Validation(
                "Invoice, reason, row version, and administrator are required.");
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 2601 or 2627 })
            {
                return true;
            }
        }

        return false;
    }

    private sealed record InvoiceReplayReceipt(Guid InvoicePublicId);
}
