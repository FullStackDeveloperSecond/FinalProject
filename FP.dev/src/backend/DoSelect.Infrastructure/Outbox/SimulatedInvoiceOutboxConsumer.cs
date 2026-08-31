using System.Data;
using System.Text.Json;
using DoSelect.Application.Invoicing;
using DoSelect.Application.Outbox;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Outbox;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DoSelect.Infrastructure.Outbox;

/// <summary>
/// 付款成功提交後開立模擬發票。Outbox 可能重送，因此以訂單唯一發票作為冪等邊界。
/// </summary>
public sealed class SimulatedInvoiceOutboxConsumer(
    DoSelectDbContext context,
    IssueInvoiceService issueInvoiceService,
    TimeProvider timeProvider) : IOutboxConsumer
{
    public string EventType => OutboxEventTypes.SimulatedInvoiceRequestedV1;

    public async Task<OutboxConsumeResult> ConsumeAsync(
        OutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message.PayloadVersion != 1)
        {
            return OutboxConsumeResult.Failure("outbox_payload_version_unsupported");
        }

        SimulatedInvoiceRequestedV1? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SimulatedInvoiceRequestedV1>(
                message.PayloadJson,
                OutboxJson.Options);
        }
        catch (JsonException)
        {
            return OutboxConsumeResult.Failure("outbox_payload_invalid");
        }

        if (payload is null || payload.OrderPublicId == Guid.Empty)
        {
            return OutboxConsumeResult.Failure("outbox_payload_invalid");
        }

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var result = await issueInvoiceService.IssueAsync(
                new IssueInvoiceRequest(payload.OrderPublicId),
                cancellationToken);
            if (result.ErrorCode == InvoiceErrorCodes.InvoiceAlreadyExists)
            {
                await transaction.RollbackAsync(cancellationToken);
                return OutboxConsumeResult.Success();
            }

            if (result.Plan is not { } plan)
            {
                await transaction.RollbackAsync(cancellationToken);
                return OutboxConsumeResult.Failure(
                    result.ErrorCode ?? InvoiceErrorCodes.InvoiceStateConflict);
            }

            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var invoice = new SimulatedInvoice(
                Guid.CreateVersion7(),
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
            context.SimulatedInvoices.Add(invoice);
            await context.SaveChangesAsync(cancellationToken);

            foreach (var line in plan.Lines)
            {
                var values = line.Breakdown;
                context.SimulatedInvoiceItems.Add(new SimulatedInvoiceItem(
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
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OutboxConsumeResult.Success();
        }
        catch (DbUpdateException exception) when (SqlDuplicateKey.IsViolation(exception))
        {
            await RollbackAndResetTrackingAsync(transaction, message);

            var invoiceExists = await (
                from invoice in context.SimulatedInvoices.AsNoTracking()
                join order in context.Orders.AsNoTracking()
                    on invoice.OrderId equals order.Id
                where order.PublicId == payload.OrderPublicId
                select invoice.Id)
                .AnyAsync(cancellationToken);
            if (invoiceExists)
            {
                return OutboxConsumeResult.Success();
            }

            throw;
        }
        catch
        {
            // 交易回滾不會自動清除 EF 追蹤狀態。若保留已新增的 Invoice／Items，
            // OutboxDispatchJob 標記訊息失敗時的 SaveChanges 會再次嘗試寫入它們。
            await RollbackAndResetTrackingAsync(transaction, message);
            throw;
        }
    }

    private async Task RollbackAndResetTrackingAsync(
        IDbContextTransaction transaction,
        OutboxMessage message)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        finally
        {
            context.ChangeTracker.Clear();
            // OutboxDispatchJob 仍要在 Consumer 回傳後完成或標記同一訊息失敗。
            context.Attach(message);
        }
    }
}
