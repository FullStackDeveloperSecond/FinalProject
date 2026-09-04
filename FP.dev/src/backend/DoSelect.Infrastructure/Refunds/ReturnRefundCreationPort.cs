using System.Diagnostics;
using DoSelect.Application.Common;
using DoSelect.Application.Refunds;
using DoSelect.Application.Returns;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Refunds;

/// <summary>
/// 退貨核准／檢查完成時判斷退款去向：算出淨額 > 0 就暫存唯一一筆 <c>PendingReview</c>
/// 退款，算出淨額 <= 0 就回報無款可退，不建立任何 Refund（#99 A1 裁定）。
/// </summary>
/// <remarks>
/// <para>
/// <b>不呼叫 SaveChanges</b>，也不開交易：只把實體加進目前這個 scoped Unit of Work，
/// 由 <c>ReturnStore.SaveTransitionAsync</c> 與退貨狀態、歷程一起提交。退貨狀態進了
/// 資料庫、退款卻沒有（或反過來）就是一筆對不了帳的財務紀錄。
/// </para>
/// <para>
/// 金額由 <see cref="RefundCalculator"/> 依可信快照算出，與執行階段<b>同一條路徑</b>
/// （<see cref="RefundTrustedInputsReader"/>）。管理端不傳金額，也不傳分攤。
/// </para>
/// <para>
/// <b>B1 具名例外</b>：本元件讀 <c>ReturnRequests</c>（OrderId、PublicId）與
/// <c>PaymentAttempts</c>（挑出這張訂單已付款的嘗試），寫 <c>Refunds</c>。
/// 可信快照本身不在這裡讀 —— 交給白名單內的 <see cref="RefundTrustedInputsReader"/>，
/// 它有自己的資料表與欄位清單。
/// </para>
/// </remarks>
public sealed class ReturnRefundCreationPort : IReturnRefundCreationPort
{
    /// <summary>
    /// 由退貨對外識別推導的決定性冪等金鑰前綴。
    /// </summary>
    /// <remarks>
    /// 唯一性靠既有的 <c>UX_Refunds_IdempotencyKey</c>，不新增索引或 Migration。
    /// 並行的兩次核准會產生同一把金鑰，第二次在提交時撞索引，整筆交易回滾。
    /// <c>Refund.IdempotencyKey</c> 本來就是「建立退款」這個操作的金鑰
    /// （見 <c>RefundExecutionSnapshot</c> 的說明），執行階段用的是共用
    /// <c>IIdempotencyExecutor</c>，兩者互不相干。
    /// </remarks>
    internal const string IdempotencyKeyPrefix = "return-refund:";

    private readonly DoSelectDbContext _context;
    private readonly IRefundOrderProjectionPort _orderProjectionPort;

    public ReturnRefundCreationPort(
        DoSelectDbContext context,
        IRefundOrderProjectionPort orderProjectionPort)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(orderProjectionPort);
        _context = context;
        _orderProjectionPort = orderProjectionPort;
    }

    /// <summary>這張退貨對應的決定性建立金鑰。</summary>
    internal static string IdempotencyKeyFor(Guid returnPublicId) =>
        $"{IdempotencyKeyPrefix}{returnPublicId:D}";

    public async Task<ReturnRefundCreationOutcome> StagePendingRefundAsync(
        ReturnRefundCreationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.AdminUserId);

        var returnPublicId = command.ReturnPublicId;
        var returnRequest = await _context.ReturnRequests
            .AsNoTracking()
            .Where(request => request.PublicId == returnPublicId)
            .Select(request => new { request.Id, request.OrderId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw DomainProblemException.NotFound("The return request was not found.");

        // 退款一定要掛在一筆真的收過款的嘗試上：退款金額最終要退回那次付款。
        // 一張訂單只會有一筆 Paid（付款狀態機不允許兩次成功），這裡仍取最早那筆並
        // 明確排序，讓結果不依賴資料庫回傳順序。
        var paymentAttemptId = await _context.PaymentAttempts
            .AsNoTracking()
            .Where(attempt =>
                attempt.OrderId == returnRequest.OrderId &&
                attempt.Status == PaymentAttemptStatus.Paid)
            .OrderBy(attempt => attempt.Id)
            .Select(attempt => (long?)attempt.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw DomainProblemException.Conflict(
                RefundErrorCodes.RefundStateConflict,
                "The order has no paid payment attempt to refund against.");

        // 退貨原因必須映射得出退款原因，否則退貨運費由誰負擔是不確定的 —— 不得猜。
        if (!ReturnEligibilityPolicy.TryMapRefundReason(command.ReasonCode, out var reason))
        {
            throw DomainProblemException.Conflict(
                RefundErrorCodes.RefundSnapshotUnavailable,
                "The return reason does not map to a refund reason.");
        }

        // 可信的三項由呼叫端傳入：此刻 CaptureRefundTrustedInputs 只改了記憶體中的
        // 退貨實體，SaveChanges 還沒發生，回頭讀資料庫只會讀到舊值。
        //
        // refundId 0：這一刻退款還不存在。那個參數只用來把「目前這筆退款」排除在
        // 「先前已成功退款」的累計之外，而 Id 0 不存在，因此不會誤排除任何一筆。
        var trustedInputs = await new RefundTrustedInputsReader(_context)
            .FindAsync(
                returnRequest.OrderId,
                0,
                returnRequest.Id,
                reason,
                command.AssemblyFeeDisposition,
                command.ReturnShippingCost,
                cancellationToken)
            ?? throw DomainProblemException.Conflict(
                RefundErrorCodes.RefundSnapshotUnavailable,
                "The approved return does not yet carry the trusted inputs a refund needs.");

        var calculation = RefundCalculator.Calculate(new RefundCalculationRequest(
            trustedInputs.Order,
            trustedInputs.Lines,
            trustedInputs.Reason,
            trustedInputs.AssemblyDisposition,
            trustedInputs.ReturnShippingCost));

        // 淨額為 0 或負數（扣回蓋過退款，例如退貨後不再符合優惠門檻）已經是
        // RefundCalculator 自己決定並測過的行為 —— 它在那種情況下回
        // Failure(RefundAmountExceeded)，不是 Success（見 RefundCalculatorTests
        // .WhenTheClawbackSwallowsTheWholeRefund_TheAmountIsRejected）。這是一個合法的
        // 業務結果，不是錯誤：#99 A1 裁定回具名結果讓呼叫端走 Completed，不得讓退貨
        // 卡在 409 重試迴圈，也不能用例外表達正常業務分支（#99 review 對此的意見）。
        // 其餘計算失敗（找不到品項、退貨數量超過等）仍然是真正的錯誤，照原本方式丟。
        if (!calculation.IsSuccess)
        {
            if (calculation.ErrorCode == RefundErrorCodes.RefundAmountExceeded)
            {
                return new ReturnRefundCreationOutcome.NoRefundDue();
            }

            throw DomainProblemException.Conflict(
                calculation.ErrorCode!,
                "The approved return does not produce a refundable amount.");
        }

        var refund = new Refund(
            Guid.CreateVersion7(),
            returnRequest.OrderId,
            returnRequest.Id,
            paymentAttemptId,
            RefundNumberFor(returnPublicId),
            calculation.NetRefundAmount,
            trustedInputs.Reason.ToString(),
            command.AdminUserId,
            IdempotencyKeyFor(returnPublicId),
            command.OccurredAtUtc);
        _context.Refunds.Add(refund);

        await new RefundOrderProjectionStager(_context, _orderProjectionPort).StageAsync(
            refund,
            "refund-pending-review",
            command.AdminUserId,
            command.OccurredAtUtc,
            Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(),
            cancellationToken);

        return new ReturnRefundCreationOutcome.PendingRefundStaged();
    }

    /// <summary>
    /// 退款編號同樣由退貨對外識別推導。
    /// </summary>
    /// <remarks>
    /// 隨機編號在撞上 <c>UX_Refunds_IdempotencyKey</c> 時會先耗掉一個號碼；推導的編號讓
    /// 重試得到同一個值，<c>UX_Refunds_RefundNumber</c> 於是與冪等金鑰指向同一件事實。
    /// 欄位上限 32 字元，取 GUID 的 <c>N</c> 格式（32）再留前綴空間 —— 取 29 碼，
    /// 前綴 "RF-" 共 32。
    /// </remarks>
    internal static string RefundNumberFor(Guid returnPublicId) =>
        $"RF-{returnPublicId:N}"[..32];
}
