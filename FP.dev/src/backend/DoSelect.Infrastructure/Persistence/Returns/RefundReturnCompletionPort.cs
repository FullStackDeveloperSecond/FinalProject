using DoSelect.Application.Refunds;
using DoSelect.Domain.Returns;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Returns;

/// <summary>
/// Returns-owner adapter for a successfully executed refund. It stages the transition and
/// history row on the shared scoped DbContext and deliberately leaves SaveChanges to
/// <c>RefundExecutor</c> so the refund's own status/allocations/audit and the return's
/// completion commit atomically — same discipline as <see cref="ReturnInventoryRestockWriter"/>.
/// </summary>
public sealed class RefundReturnCompletionPort : IRefundReturnCompletionPort
{
    /// <summary>正常退款執行成功結案的原因碼——與零淨額結案的 <c>zero-net-refund</c> 區分。</summary>
    private const string RefundSucceededReasonCode = "refund-succeeded";

    private readonly DoSelectDbContext _context;

    public RefundReturnCompletionPort(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task CompleteReturnAsync(
        RefundReturnCompletionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.AdminUserId);

        var returnRequest = await _context.ReturnRequests
            .SingleOrDefaultAsync(request => request.Id == command.ReturnRequestId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Refund execution referenced return request {command.ReturnRequestId}, which no longer exists.");

        // 不在這裡另外檢查 returnRequest.Status == AwaitingRefund：ReturnRequest.Transition
        // 本身已經是那個檢查——Allowed 字典裡只有 AwaitingRefund 才能到 Completed，其餘
        // 任何狀態呼叫這裡都會讓 Transition 自己丟 InvalidOperationException。#98 A2／
        // #99 A1 保證這裡讀到的正常情況下永遠是 AwaitingRefund（一張退貨只有唯一一筆
        // Refund，那筆 Refund 建立的同一筆交易才把退貨推進這個狀態）；多寫一次同樣的
        // 檢查只是複製 Domain 已經做的事。
        var fromStatus = returnRequest.Status;
        returnRequest.Transition(ReturnRequestStatus.Completed, command.OccurredAtUtc);

        _context.ReturnStatusHistories.Add(new ReturnStatusHistory(
            command.ReturnRequestId,
            fromStatus,
            ReturnRequestStatus.Completed,
            RefundSucceededReasonCode,
            note: null,
            command.AdminUserId,
            command.OccurredAtUtc));
    }
}
