using System.Data;
using System.Globalization;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Refunds;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Refunds;

/// <summary>
/// 在單一交易內完成退款執行：核對狀態與可退款餘額 → 條件更新退款 → 寫入稽核 → 提交。
/// 隔離等級為 Serializable，因此餘額所依據的範圍查詢在交易期間不會被其他交易插入；
/// 退款列本身另有 rowversion 樂觀鎖，兩者共同保證成功退款累計不超過已收款金額。
/// 稽核與退款狀態同批提交，任一寫入失敗即整體回滾（DEC-P289）。
/// </summary>
public sealed class RefundExecutor : IRefundExecutor
{
    /// <summary>SQL Server 的死結受害者錯誤碼。</summary>
    private const int DeadlockVictimErrorNumber = 1205;

    /// <summary>
    /// 交易邊界的重試次數。並行退款在 Serializable 下會互相死結，
    /// 重跑整個「重新讀取 → 重新判斷 → 寫入」才安全。
    /// </summary>
    private const int MaximumAttempts = 3;

    /// <summary>稽核理由中 note 的長度上限。</summary>
    private const int MaximumNoteLength = 1000;

    private readonly DoSelectDbContext _context;
    private readonly IAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    public RefundExecutor(
        DoSelectDbContext context,
        IAuditWriter auditWriter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _context = context;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
    }

    public async Task<ExecuteRefundResult> ExecuteAsync(
        ExecuteRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        RefundExecutionDecision.RequireWellFormed(request);

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                return await ExecuteOnceAsync(request, cancellationToken);
            }
            catch (Exception exception) when (IsRetryableConflict(exception))
            {
                // 整個交易作廢，連同讀到的餘額一起丟掉。
                // 只重試 SaveChanges 會沿用死結前的舊餘額，因此必須重跑整段。
                _context.ChangeTracker.Clear();

                if (attempt == MaximumAttempts)
                {
                    return ExecuteRefundResult.Failure(RefundErrorCodes.ConcurrencyConflict);
                }
            }
        }

        return ExecuteRefundResult.Failure(RefundErrorCodes.ConcurrencyConflict);
    }

    private async Task<ExecuteRefundResult> ExecuteOnceAsync(
        ExecuteRefundRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        // 追蹤查詢：後續要在同一交易內更新這一列。
        var refund = await _context.Refunds
            .SingleOrDefaultAsync(
                candidate => candidate.PublicId == request.RefundPublicId,
                cancellationToken);

        if (refund is null)
        {
            return ExecuteRefundResult.Failure(RefundErrorCodes.ResourceNotFound);
        }

        var snapshot = new RefundExecutionSnapshot(
            refund.Id,
            refund.Status,
            refund.ApprovedAmount,
            refund.SucceededAmount,
            await CalculateRefundableBalanceAsync(refund.OrderId, refund.Id, cancellationToken),
            refund.IdempotencyKey);

        var decision = RefundExecutionDecision.Evaluate(snapshot, request);
        if (decision.Plan is not { } plan)
        {
            // 拒絕或重播都不寫入，直接結束交易。
            return decision;
        }

        var allocationCount = await _context.RefundAllocations
            .CountAsync(allocation => allocation.RefundId == refund.Id, cancellationToken);

        var occurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var previousStatus = refund.Status;
        refund.BeginProcessing(plan.ExecutedByAdminUserId, occurredAtUtc);
        refund.Complete(plan.Amount, occurredAtUtc);

        // 稽核與退款狀態同批提交。Audit 寫入失敗時，下面的 SaveChanges 會整批失敗，
        // 退款狀態也不會留下來（DEC-P289）。
        var actor = await ResolveActorAsync(plan.ExecutedByAdminUserId, cancellationToken);
        WriteAudit(refund, request, plan, actor, previousStatus, allocationCount);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ExecuteRefundResult.Settled(plan.Amount, plan);
    }

    /// <summary>
    /// 把執行理由寫進中央 <c>AuditLog</c>。理由不在 <c>Refund</c> 重複保存，
    /// 中央稽核是唯一的事實來源（DEC-P289）。
    /// </summary>
    private void WriteAudit(
        Refund refund,
        ExecuteRefundRequest request,
        RefundExecutionPlan plan,
        AuditActor actor,
        RefundStatus previousStatus,
        int allocationCount)
    {
        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.NewGuid(),
            actor,
            AuditActions.RefundExecute,
            AuditResourceTypes.Refund,
            refund.PublicId,
            AuditResult.Success,
            errorCode: null,
            changes:
            [
                AuditFieldChange.Code(
                    "status", previousStatus.ToString(), refund.Status.ToString()),
                AuditFieldChange.Code(
                    "succeededAmount", null, Text(plan.Amount)),
                AuditFieldChange.Code(
                    "allocationCount", null, Text(allocationCount)),
            ],
            reason: BuildReason(request),
            correlationId: request.CorrelationId,
            traceId: request.TraceId,
            jobPublicId: null,
            remoteIpAddress: null));
    }

    private static string Text<T>(T value) where T : IFormattable =>
        value.ToString(null, CultureInfo.InvariantCulture);

    /// <summary>
    /// 在同一交易內把執行者的 Identity Id 換成管理員 PublicId 與角色快照，
    /// 並確認執行當下仍具備退款權限。沿用 <c>InvoiceAllowanceWriter</c> 的既有做法，
    /// 稽核紀錄因此不會出現內部 Identity Id（DEC-P290）。
    /// </summary>
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
                "The administrator no longer has permission to execute refunds.");
        }

        return AuditActor.Create(AuditActorType.Admin, admin.PublicId, roles);
    }

    /// <summary>
    /// 稽核理由。<c>note</c> 經長度限制後接在 <c>reasonCode</c> 之後，兩者只存在稽核紀錄裡。
    /// </summary>
    private static string BuildReason(ExecuteRefundRequest request)
    {
        var reasonCode = request.ReasonCode.Trim();
        if (string.IsNullOrWhiteSpace(request.Note))
        {
            return reasonCode;
        }

        var note = request.Note.Trim();
        if (note.Length > MaximumNoteLength)
        {
            note = note[..MaximumNoteLength];
        }

        return $"{reasonCode}: {note}";
    }

    /// <summary>
    /// 值得重跑整段交易的並行衝突：SQL Server 死結受害者，或 rowversion 樂觀鎖失敗。
    /// 死結的 <see cref="SqlException"/> 會被層層包裝，因此往內層找。
    /// </summary>
    private static bool IsRetryableConflict(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return true;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: DeadlockVictimErrorNumber })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 可退款餘額 = 該訂單已成功收款金額 - 其他退款已成功的金額累計。
    /// 排除本次退款自身，避免重試時把自己算成已用額度。
    /// </summary>
    private async Task<decimal> CalculateRefundableBalanceAsync(
        long orderId,
        long refundId,
        CancellationToken cancellationToken)
    {
        var paidTotal = await _context.PaymentAttempts
            .Where(attempt =>
                attempt.OrderId == orderId &&
                attempt.Status == PaymentAttemptStatus.Paid)
            .SumAsync(attempt => attempt.Amount, cancellationToken);

        var settledTotal = await _context.Refunds
            .Where(candidate =>
                candidate.OrderId == orderId &&
                candidate.Id != refundId &&
                candidate.Status == RefundStatus.Succeeded &&
                candidate.SucceededAmount != null)
            .SumAsync(candidate => candidate.SucceededAmount!.Value, cancellationToken);

        var balance = paidTotal - settledTotal;
        return balance > 0m ? balance : 0m;
    }
}
