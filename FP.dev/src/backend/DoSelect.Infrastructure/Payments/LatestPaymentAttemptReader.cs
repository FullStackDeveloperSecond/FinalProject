using DoSelect.Application.Payments;
using DoSelect.Domain.Payments;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Payments;

/// <summary>
/// <see cref="ILatestPaymentAttemptReader"/> 的實作。
/// </summary>
public sealed class LatestPaymentAttemptReader : ILatestPaymentAttemptReader
{
    private readonly DoSelectDbContext _context;

    public LatestPaymentAttemptReader(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public Task<PaymentAttemptOrderReference?> FindOrderAsync(
        Guid orderPublicId,
        CancellationToken cancellationToken = default) =>
        orderPublicId == Guid.Empty
            ? Task.FromResult<PaymentAttemptOrderReference?>(null)
            : _context.Orders.AsNoTracking()
                .Where(order => order.PublicId == orderPublicId)
                .Select(order => new PaymentAttemptOrderReference(order.Id, order.MemberUserId))
                .SingleOrDefaultAsync(cancellationToken);

    /// <remarks>
    /// <para>
    /// <b>不篩狀態</b>：終態的付款嘗試也要回（Issue #86 A1）。
    /// </para>
    /// <para>
    /// 排序是 <c>CreatedAtUtc</c> 遞減<b>加上 <c>Id</c> 遞減</b>。只靠時間排序，在兩筆
    /// 建立時間相同時結果不確定 —— SQL Server 對沒有完整排序鍵的 <c>TOP 1</c>
    /// 不保證每次回同一列，付款頁就會時好時壞（alex 2026-09-01 Issue #86 A1 要求的穩定次排序）。
    /// <c>Id</c> 是遞增的識別鍵，所以「較大的 Id」就是「較晚建立的那一筆」。
    /// </para>
    /// <para>
    /// <c>IX_PaymentAttempts_OrderId_CreatedAtUtc</c> 這個索引已經在了。
    /// </para>
    /// </remarks>
    public Task<PaymentAttempt?> FindLatestAsync(
        long orderId,
        CancellationToken cancellationToken = default) =>
        orderId <= 0
            ? Task.FromResult<PaymentAttempt?>(null)
            : _context.PaymentAttempts.AsNoTracking()
                .Where(attempt => attempt.OrderId == orderId)
                .OrderByDescending(attempt => attempt.CreatedAtUtc)
                .ThenByDescending(attempt => attempt.Id)
                .FirstOrDefaultAsync(cancellationToken);
}
