using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Invoicing;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Invoicing;

/// <summary>
/// Creates an allowance inside the transaction owned by the shared idempotency executor.
/// The API only supplies request and actor context; it never coordinates persistence.
/// </summary>
public sealed class InvoiceAllowanceWriter : IInvoiceAllowanceWriter
{
    private const int CreatedStatusCode = 201;

    private readonly DoSelectDbContext _context;
    private readonly IssueInvoiceAllowanceService _planner;
    private readonly IIdempotencyExecutor _idempotencyExecutor;
    private readonly IAuditWriter _auditWriter;

    public InvoiceAllowanceWriter(
        DoSelectDbContext context,
        IssueInvoiceAllowanceService planner,
        IIdempotencyExecutor idempotencyExecutor,
        IAuditWriter auditWriter)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(idempotencyExecutor);
        ArgumentNullException.ThrowIfNull(auditWriter);

        _context = context;
        _planner = planner;
        _idempotencyExecutor = idempotencyExecutor;
        _auditWriter = auditWriter;
    }

    public async Task<IdempotencyExecutionResult<SimulatedInvoiceAllowanceDto>> CreateAsync(
        CreateInvoiceAllowanceCommand command,
        CancellationToken cancellationToken = default)
    {
        RequireWellFormed(command);
        var actor = await ResolveActorAsync(command.AdminUserId, cancellationToken);
        var idempotencyCommand = IdempotencyCommand.Create(
            IdempotencyActorScope.ForAdmin(actor.PublicId!.Value),
            InvoiceAllowanceWriteConstants.Operation,
            command.IdempotencyKey,
            new
            {
                command.InvoicePublicId,
                command.RefundPublicId,
                InvoiceRowVersion = Convert.ToBase64String(command.InvoiceRowVersion),
            });

        try
        {
            return await _idempotencyExecutor.ExecuteAsync(
                idempotencyCommand,
                handler: ct => CreateOnceAsync(command, actor, ct),
                replayFactory: ReplayAsync,
                cancellationToken);
        }
        catch (InvoiceAllowanceSourceException exception)
        {
            throw DomainProblemException.Conflict(
                InvoiceErrorCodes.InvoiceStateConflict,
                exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict,
                "The invoice changed. Reload it and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            throw DomainProblemException.Conflict(
                InvoiceErrorCodes.InvoiceStateConflict,
                "The refund already has an allowance or the allowance number was already used.");
        }
    }

    private async Task<IdempotencyResponse<SimulatedInvoiceAllowanceDto>> CreateOnceAsync(
        CreateInvoiceAllowanceCommand command,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        var invoice = await _context.SimulatedInvoices.SingleOrDefaultAsync(
            candidate => candidate.PublicId == command.InvoicePublicId,
            cancellationToken);
        if (invoice is null)
        {
            throw DomainProblemException.NotFound("The simulated invoice was not found.");
        }

        var result = await _planner.IssueAsync(
            new IssueInvoiceAllowanceRequest(
                command.RefundPublicId,
                command.IdempotencyKey,
                invoice.Id),
            cancellationToken);
        if (result.Plan is not { } plan)
        {
            throw MapFailure(result.ErrorCode!);
        }

        var itemPublicIds = plan.Lines
            .Select(line => line.SimulatedInvoiceItemPublicId)
            .ToArray();
        var invoiceItems = await _context.SimulatedInvoiceItems
            .Where(item => item.SimulatedInvoiceId == invoice.Id &&
                itemPublicIds.Contains(item.PublicId))
            .Select(item => new
            {
                item.Id,
                item.PublicId,
                item.OrderItemId,
                item.SkuCodeSnapshot,
            })
            .ToArrayAsync(cancellationToken);
        if (invoiceItems.Length != itemPublicIds.Distinct().Count())
        {
            throw DomainProblemException.Conflict(
                InvoiceErrorCodes.InvoiceStateConflict,
                "An allowance line no longer maps to the original invoice.");
        }
        var invoiceItemMappings = invoiceItems.ToDictionary(
            item => item.PublicId,
            item => new InvoiceItemMapping(
                item.Id,
                ResolvePublicKind(item.OrderItemId, item.SkuCodeSnapshot)));

        var allowancePublicId = Guid.CreateVersion7();
        var allowance = new SimulatedInvoiceAllowance(
            allowancePublicId,
            invoice.Id,
            plan.RefundId,
            plan.AllowanceNumber,
            plan.NetAmount,
            plan.TaxAmount,
            plan.Amount,
            plan.IssuedAtUtc,
            plan.IssuedAtUtc);
        _context.SimulatedInvoiceAllowances.Add(allowance);

        // The identity key is needed by allowance items. This save remains inside the
        // idempotency executor's transaction and is rolled back with every later failure.
        await _context.SaveChangesAsync(cancellationToken);

        var itemDtos = new List<SimulatedInvoiceAllowanceItemDto>(plan.Lines.Count);
        foreach (var line in plan.Lines)
        {
            var itemPublicId = Guid.CreateVersion7();
            _context.SimulatedInvoiceAllowanceItems.Add(new SimulatedInvoiceAllowanceItem(
                itemPublicId,
                allowance.Id,
                invoiceItemMappings[line.SimulatedInvoiceItemPublicId].Id,
                line.Quantity,
                line.NetAmount,
                line.TaxAmount,
                line.GrossAmount,
                plan.IssuedAtUtc));
            itemDtos.Add(new SimulatedInvoiceAllowanceItemDto(
                itemPublicId,
                line.SimulatedInvoiceItemPublicId,
                invoiceItemMappings[line.SimulatedInvoiceItemPublicId].Kind,
                line.Quantity,
                line.NetAmount,
                line.TaxAmount,
                line.GrossAmount));
        }

        _context.Entry(invoice).Property(entity => entity.RowVersion).OriginalValue =
            command.InvoiceRowVersion.ToArray();
        var previousStatus = invoice.Status;
        invoice.RecordAllowance(
            plan.ResultingInvoiceStatus == SimulatedInvoiceStatus.FullyAllowed,
            plan.IssuedAtUtc);

        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.CreateVersion7(),
            actor,
            AuditActions.InvoiceAllowanceCreate,
            AuditResourceTypes.SimulatedInvoiceAllowance,
            allowancePublicId,
            AuditResult.Success,
            errorCode: null,
            [
                AuditFieldChange.Code(
                    "status",
                    previousStatus.ToString(),
                    invoice.Status.ToString()),
                AuditFieldChange.Changed("allowanceAmount"),
                AuditFieldChange.Changed("allowanceItemCount"),
            ],
            InvoiceAllowanceWriteConstants.AuditReason,
            command.CorrelationId,
            command.TraceId,
            jobPublicId: null,
            command.RemoteIpAddress));

        var dto = new SimulatedInvoiceAllowanceDto(
            allowancePublicId,
            plan.AllowanceNumber,
            invoice.PublicId,
            command.RefundPublicId,
            plan.NetAmount,
            plan.TaxAmount,
            plan.Amount,
            itemDtos,
            plan.IssuedAtUtc,
            InvoiceAllowanceWriteConstants.DemoMarker);
        return new IdempotencyResponse<SimulatedInvoiceAllowanceDto>(
            CreatedStatusCode,
            dto,
            JsonSerializer.Serialize(new AllowanceReplayReceipt(allowancePublicId)));
    }

    private async Task<SimulatedInvoiceAllowanceDto> ReplayAsync(
        StoredIdempotencyResponse stored,
        CancellationToken cancellationToken)
    {
        var receipt = JsonSerializer.Deserialize<AllowanceReplayReceipt>(stored.ResponseSummary)
            ?? throw new InvalidOperationException("The stored allowance receipt is invalid.");

        var header = await (
            from allowance in _context.SimulatedInvoiceAllowances.AsNoTracking()
            join invoice in _context.SimulatedInvoices.AsNoTracking()
                on allowance.SimulatedInvoiceId equals invoice.Id
            join refund in _context.Refunds.AsNoTracking()
                on allowance.RefundId equals refund.Id
            where allowance.PublicId == receipt.AllowancePublicId
            select new
            {
                allowance.Id,
                allowance.PublicId,
                allowance.AllowanceNumber,
                InvoicePublicId = invoice.PublicId,
                RefundPublicId = refund.PublicId,
                allowance.NetAmount,
                allowance.TaxAmount,
                allowance.Amount,
                allowance.IssuedAtUtc,
            }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The stored allowance no longer exists.");

        var storedItems = await (
            from allowanceItem in _context.SimulatedInvoiceAllowanceItems.AsNoTracking()
            join invoiceItem in _context.SimulatedInvoiceItems.AsNoTracking()
                on allowanceItem.SimulatedInvoiceItemId equals invoiceItem.Id
            where allowanceItem.AllowanceId == header.Id
            orderby allowanceItem.Id
            select new
            {
                allowanceItem.PublicId,
                InvoiceItemPublicId = invoiceItem.PublicId,
                invoiceItem.OrderItemId,
                invoiceItem.SkuCodeSnapshot,
                allowanceItem.Quantity,
                allowanceItem.NetAmount,
                allowanceItem.TaxAmount,
                allowanceItem.GrossAmount,
            })
            .ToArrayAsync(cancellationToken);
        var items = storedItems.Select(item => new SimulatedInvoiceAllowanceItemDto(
            item.PublicId,
            item.InvoiceItemPublicId,
            ResolvePublicKind(item.OrderItemId, item.SkuCodeSnapshot),
            item.Quantity,
            item.NetAmount,
            item.TaxAmount,
            item.GrossAmount)).ToArray();

        return new SimulatedInvoiceAllowanceDto(
            header.PublicId,
            header.AllowanceNumber,
            header.InvoicePublicId,
            header.RefundPublicId,
            header.NetAmount,
            header.TaxAmount,
            header.Amount,
            items,
            header.IssuedAtUtc,
            InvoiceAllowanceWriteConstants.DemoMarker);
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

    private static void RequireWellFormed(CreateInvoiceAllowanceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.InvoicePublicId == Guid.Empty ||
            command.RefundPublicId == Guid.Empty ||
            command.InvoiceRowVersion is not { Length: 8 } ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey) ||
            command.IdempotencyKey.Trim().Length > 128 ||
            string.IsNullOrWhiteSpace(command.AdminUserId))
        {
            throw DomainProblemException.Validation(
                "Invoice, refund, row version, idempotency key, and administrator are required.");
        }
    }

    private static DomainProblemException MapFailure(string errorCode) => errorCode switch
    {
        InvoiceErrorCodes.ResourceNotFound =>
            DomainProblemException.NotFound("The refund or its simulated invoice was not found."),
        RefundErrorCodes.RefundStateConflict =>
            DomainProblemException.Conflict(errorCode, "The refund has not succeeded."),
        _ => DomainProblemException.Conflict(errorCode, "The invoice cannot record this allowance."),
    };

    private static InvoiceLineKind ResolvePublicKind(
        long? orderItemId,
        string skuCodeSnapshot)
    {
        try
        {
            return InvoiceLineSkuCodes.ResolveKind(orderItemId, skuCodeSnapshot);
        }
        catch (ArgumentException exception)
        {
            throw new InvoiceAllowanceSourceException(
                "The original invoice contains an unknown or reserved line identity.",
                exception);
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

    private sealed record AllowanceReplayReceipt(Guid AllowancePublicId);

    private sealed record InvoiceItemMapping(long Id, InvoiceLineKind Kind);
}
