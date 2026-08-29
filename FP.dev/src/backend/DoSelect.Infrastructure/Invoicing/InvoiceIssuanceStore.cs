using DoSelect.Application.Invoicing;
using DoSelect.Domain.Invoicing;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Invoicing;

/// <summary>
/// <see cref="IInvoiceExistenceReader"/> 的實作：只讀 Invoicing 自己的
/// <c>SimulatedInvoices</c>，不碰 <c>Orders</c>／<c>OrderItems</c>。
/// </summary>
public sealed class InvoiceExistenceReader : IInvoiceExistenceReader
{
    private readonly DoSelectDbContext _context;

    public InvoiceExistenceReader(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public Task<bool> HasInvoiceAsync(long orderId, CancellationToken cancellationToken = default) =>
        orderId <= 0
            ? Task.FromResult(false)
            : _context.SimulatedInvoices.AsNoTracking()
                .AnyAsync(invoice => invoice.OrderId == orderId, cancellationToken);
}

/// <summary>
/// <see cref="IInvoiceNumberSequence"/> 的實作。
/// </summary>
/// <remarks>
/// <para>
/// 取當月已用的最大序號加一。<b>不是取 <c>Count</c></b>：作廢的發票仍然佔用號碼，
/// 用筆數會在有作廢紀錄之後開始重複發號，最後撞上
/// <c>UX_SimulatedInvoices_InvoiceNumber</c>。
/// </para>
/// <para>
/// 這個查詢本身沒有鎖。號碼的唯一性靠<b>兩件事</b>：呼叫端把取號與寫入放在同一個
/// Serializable 交易內（開票的 idempotency executor 負責），以及唯一索引作為
/// 最後一道防線。這裡刻意不自己開交易 —— 自己開會讓取號落在別的交易，
/// 反而失去它要保護的那個不變量。
/// </para>
/// <para>
/// 月份用 <c>InvoiceNumber</c> 的前綴比對，不用 <c>IssuedAtUtc</c> 區間：
/// 號碼格式是 <c>DEMO-yyyyMM-NNNNNN</c>，序號的作用域就是號碼裡的那個月份，
/// 拿另一個欄位去界定作用域，只要兩者不一致就會發出重複號碼。
/// </para>
/// </remarks>
public sealed class InvoiceNumberSequence : IInvoiceNumberSequence
{
    private readonly DoSelectDbContext _context;

    public InvoiceNumberSequence(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<int> NextAsync(
        DateTime issuedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (issuedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The value must use UTC.", nameof(issuedAtUtc));
        }

        var prefix = $"{DemoInvoiceNumber.Prefix}-{issuedAtUtc:yyyyMM}-";

        var numbers = await _context.SimulatedInvoices.AsNoTracking()
            .Where(invoice => invoice.InvoiceNumber.StartsWith(prefix))
            .Select(invoice => invoice.InvoiceNumber)
            .ToArrayAsync(cancellationToken);

        var used = 0;
        foreach (var number in numbers)
        {
            // 解析不出來的號碼不能當成 0 靜默略過 —— 那會讓下一次發出已經用掉的號碼。
            if (!int.TryParse(number[prefix.Length..], out var sequence))
            {
                throw new InvalidOperationException(
                    $"The invoice number '{number}' does not follow the DEMO-yyyyMM-NNNNNN format.");
            }

            used = Math.Max(used, sequence);
        }

        return used + 1;
    }
}
