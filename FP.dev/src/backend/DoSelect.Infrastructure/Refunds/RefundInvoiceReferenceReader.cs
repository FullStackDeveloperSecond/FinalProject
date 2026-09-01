using DoSelect.Application.Refunds;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Refunds;

public sealed class RefundInvoiceReferenceReader : IRefundInvoiceReferenceReader
{
    private readonly DoSelectDbContext _context;

    public RefundInvoiceReferenceReader(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<IReadOnlyDictionary<long, Guid>> FindManyAsync(
        IReadOnlyCollection<long> refundIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refundIds);

        var wanted = refundIds.Where(id => id > 0).Distinct().ToArray();
        if (wanted.Length == 0)
        {
            return new Dictionary<long, Guid>();
        }

        return await _context.Refunds.AsNoTracking()
            .Where(refund => wanted.Contains(refund.Id))
            .Select(refund => new { refund.Id, refund.PublicId })
            .ToDictionaryAsync(refund => refund.Id, refund => refund.PublicId, cancellationToken);
    }
}

public sealed class RefundInvoiceVoidReader : IRefundInvoiceVoidReader
{
    private readonly DoSelectDbContext _context;

    public RefundInvoiceVoidReader(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public Task<bool> HasSucceededRefundAsync(
        long orderId,
        CancellationToken cancellationToken = default) =>
        _context.Refunds.AsNoTracking().AnyAsync(
            refund => refund.OrderId == orderId && refund.Status == RefundStatus.Succeeded,
            cancellationToken);
}
