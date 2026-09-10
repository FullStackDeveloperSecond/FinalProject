using DoSelect.Application.Common;
using DoSelect.Application.Invoicing;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Invoicing;

/// <summary>
/// 後台發票清單依 Orders-owned 訂單號碼排序時使用的窄唯讀投影。
/// </summary>
public sealed class OrderNumberSortedAdminInvoiceReader : IOrderNumberSortedAdminInvoiceReader
{
    private readonly DoSelectDbContext _context;

    public OrderNumberSortedAdminInvoiceReader(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<PageResult<AdminInvoiceSummaryDto>> ListAsync(
        AdminInvoiceQuery query,
        CancellationToken cancellationToken = default)
    {
        AdminInvoiceQueryValidator.RequireValid(query);

        if (query.Sort is not (AdminInvoiceSortOptions.OrderNumberAsc or
            AdminInvoiceSortOptions.OrderNumberDesc))
        {
            throw new ArgumentException(
                "This reader only supports order-number sorting.", nameof(query));
        }

        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var pageNumber = Math.Max(query.PageNumber, 1);

        var filtered =
            from invoice in _context.SimulatedInvoices.AsNoTracking()
            join order in _context.Orders.AsNoTracking()
                on invoice.OrderId equals order.Id
            select new { Invoice = invoice, Order = order };

        if (query.Statuses is { Count: > 0 } requested)
        {
            var statuses = requested.ToArray();
            filtered = filtered.Where(row => statuses.Contains(row.Invoice.Status));
        }

        if (query.FromUtc is { } from)
        {
            filtered = filtered.Where(row => row.Invoice.IssuedAtUtc >= from);
        }

        if (query.ToUtc is { } to)
        {
            filtered = filtered.Where(row => row.Invoice.IssuedAtUtc < to);
        }

        var keyword = query.Q?.Trim();
        if (!string.IsNullOrEmpty(keyword))
        {
            filtered = filtered.Where(row => row.Invoice.InvoiceNumber.Contains(keyword));
        }

        var totalCount = await filtered.CountAsync(cancellationToken);
        var skip = ((long)pageNumber - 1) * pageSize;
        if (skip > int.MaxValue)
        {
            return new PageResult<AdminInvoiceSummaryDto>(
                [], pageNumber, pageSize, totalCount);
        }

        var ordered = query.Sort == AdminInvoiceSortOptions.OrderNumberAsc
            ? filtered.OrderBy(row => row.Order.OrderNumber).ThenBy(row => row.Invoice.Id)
            : filtered.OrderByDescending(row => row.Order.OrderNumber)
                .ThenByDescending(row => row.Invoice.Id);

        var items = await ordered
            .Skip((int)skip)
            .Take(pageSize)
            .Select(row => new AdminInvoiceSummaryDto(
                row.Invoice.PublicId,
                row.Invoice.InvoiceNumber,
                row.Order.PublicId,
                row.Order.OrderNumber,
                row.Invoice.Status,
                row.Invoice.NetAmount,
                row.Invoice.TaxAmount,
                row.Invoice.IssuedAmount,
                row.Invoice.IssuedAtUtc,
                row.Invoice.DemoMarker,
                row.Invoice.RowVersion))
            .ToArrayAsync(cancellationToken);

        return new PageResult<AdminInvoiceSummaryDto>(
            items, pageNumber, pageSize, totalCount);
    }
}
