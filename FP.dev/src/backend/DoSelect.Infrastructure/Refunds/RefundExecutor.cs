using System.Data;
using System.Globalization;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
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
/// 退款執行：核對狀態與可退款餘額 → 條件更新退款 → 寫入七類分攤 → 寫入稽核。
/// 交易由共用 <c>IIdempotencyExecutor</c> 擁有並以 Serializable 開啟（DEC-BATCH-019 A1），
/// 因此餘額所依據的範圍查詢在交易期間不會被其他交易插入；
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

    private const int StatusCodes200Ok = 200;

    /// <summary>中央 Idempotency 的 Operation 名稱（DEC-BATCH-019）。</summary>
    public const string Operation = "refund.execute";

    private readonly DoSelectDbContext _context;
    private readonly IAuditWriter _auditWriter;
    private readonly IIdempotencyExecutor _idempotencyExecutor;
    private readonly TimeProvider _timeProvider;

    public RefundExecutor(
        DoSelectDbContext context,
        IAuditWriter auditWriter,
        IIdempotencyExecutor idempotencyExecutor,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(idempotencyExecutor);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _context = context;
        _auditWriter = auditWriter;
        _idempotencyExecutor = idempotencyExecutor;
        _timeProvider = timeProvider;
    }

    public async Task<ExecuteRefundResult> ExecuteAsync(
        ExecuteRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        RefundExecutionDecision.RequireWellFormed(request);

        // Actor 必須在交易之前解析：Scope 是冪等鍵的一部分，而共用 Executor
        // 要求由它自己開啟交易。沿用 InvoiceAllowanceWriter 的既有順序。
        var actor = await ResolveActorAsync(request.ExecutedByAdminUserId, cancellationToken);

        // RequestHash 涵蓋 Refund PublicId、RowVersion、ReasonCode 與 Note
        // （DEC-BATCH-019）。同一把金鑰換上不同的版本或理由，是 Payload 衝突
        // 而不是重播。
        var command = IdempotencyCommand.Create(
            IdempotencyActorScope.ForAdmin(actor.PublicId!.Value),
            Operation,
            request.IdempotencyKey,
            new
            {
                request.RefundPublicId,
                RefundRowVersion = Convert.ToBase64String(request.RefundRowVersion),
                request.ReasonCode,
                request.Note,
            });

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                var execution = await _idempotencyExecutor.ExecuteAsync(
                    command,
                    handler: token => ExecuteOnceAsync(request, actor, token),
                    replayFactory: ReplayAsync,
                    cancellationToken,
                    IsolationLevel.Serializable);

                return execution.Body;
            }
            catch (RefundRejectedException exception)
            {
                // 決策拒絕。交易已回滾，沒有留下冪等完成紀錄。
                return ExecuteRefundResult.Failure(exception.ErrorCode);
            }
            catch (IdempotencyConflictException exception)
            {
                return ExecuteRefundResult.Failure(exception.ErrorCode);
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

    /// <summary>
    /// 回放既有結果。重播不再執行任何金流副作用，只把先前保存的金額原樣回傳。
    /// </summary>
    private static Task<ExecuteRefundResult> ReplayAsync(
        StoredIdempotencyResponse stored,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            decimal.TryParse(
                stored.ResponseSummary,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var settledAmount)
                ? ExecuteRefundResult.Replayed(settledAmount)
                : ExecuteRefundResult.Failure(RefundErrorCodes.RefundStateConflict));

    private async Task<IdempotencyResponse<ExecuteRefundResult>> ExecuteOnceAsync(
        ExecuteRefundRequest request,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        // 追蹤查詢：後續要在同一交易內更新這一列。
        var refund = await _context.Refunds
            .SingleOrDefaultAsync(
                candidate => candidate.PublicId == request.RefundPublicId,
                cancellationToken);

        if (refund is null)
        {
            return Rejected(ExecuteRefundResult.Failure(RefundErrorCodes.ResourceNotFound));
        }

        var snapshot = new RefundExecutionSnapshot(
            refund.Id,
            refund.Status,
            refund.ApprovedAmount,
            refund.SucceededAmount,
            await CalculateRefundableBalanceAsync(refund.OrderId, refund.Id, cancellationToken),
            refund.RowVersion,
            await FindTrustedInputsAsync(refund, cancellationToken));

        var decision = RefundExecutionDecision.Evaluate(snapshot, request);
        if (decision.Plan is not { } plan)
        {
            // 拒絕以例外中止交易，不留下冪等完成紀錄。
            return Rejected(decision);
        }

        // 呼叫端持有的版本已在 Evaluate 比對過一次，那是「讀取當下」的比對。
        // 這裡再設成條件更新的原始值，讓 SaveChanges 產生的 UPDATE 帶上 rowversion
        // 條件 —— 封住「讀取完成到寫入之間」那一段。
        _context.Entry(refund).Property(entity => entity.RowVersion).OriginalValue =
            plan.ExpectedRefundRowVersion;

        var occurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var previousStatus = refund.Status;
        refund.BeginProcessing(plan.ExecutedByAdminUserId, occurredAtUtc);
        refund.Complete(plan.Amount, occurredAtUtc);

        // 權威七類分攤與退款狀態同交易寫入。沒有分攤的成功退款，會讓對帳、
        // 發票折讓與稽核的 allocationCount 全部失真（DEC-P287／P289）。
        var allocationCount = await WriteAllocationsAsync(
            refund, plan.Allocations, occurredAtUtc, cancellationToken);

        // 稽核與退款狀態同批提交。Audit 寫入失敗時，下面的 SaveChanges 會整批失敗，
        // 退款狀態也不會留下來（DEC-P289）。
        WriteAudit(refund, request, plan, actor, previousStatus, allocationCount);

        await _context.SaveChangesAsync(cancellationToken);

        // 交易由共用 Executor 擁有並提交；冪等完成紀錄與上面的寫入在同一個交易內。
        // ResponseSummary 保存已結清金額，重播時據此回放同一結果。
        return new IdempotencyResponse<ExecuteRefundResult>(
            StatusCodes200Ok,
            ExecuteRefundResult.Settled(plan.Amount, plan),
            plan.Amount.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 以例外中止交易，把拒絕帶回 <see cref="ExecuteAsync"/>。
    /// </summary>
    /// <remarks>
    /// **這裡必須用拋的，不能回一個帶 4xx 的 <see cref="IdempotencyResponse{T}"/>。**
    /// <c>EfIdempotencyExecutor</c> 不論狀態碼都會 <c>Complete</c> 並提交冪等紀錄，
    /// 只有 handler 拋例外時才回滾。若把拒絕當成完成結果保存，呼叫端修正原因
    /// （例如上游快照終於齊全）後用同一把金鑰重送，會拿回原本那個拒絕的回放，
    /// 而不是真的重試 —— 等於一次暫時性拒絕被永久固化。
    /// </remarks>
    private static IdempotencyResponse<ExecuteRefundResult> Rejected(ExecuteRefundResult result) =>
        throw new RefundRejectedException(result.ErrorCode!);

    /// <summary>
    /// 決策拒絕的內部訊號，只在本類別內使用，不外流到 Application 層。
    /// </summary>
    private sealed class RefundRejectedException : Exception
    {
        public RefundRejectedException(string errorCode)
            : base(errorCode) => ErrorCode = errorCode;

        public string ErrorCode { get; }
    }

    /// <summary>
    /// 讀出後端產生七類分攤所需的完整可信快照，三項輸入齊全時才回傳。
    /// </summary>
    /// <remarks>
    /// **目前一律回 <c>null</c>。這是 E1 裁定要求的行為，不是尚未實作。**
    /// 詳見 <see cref="RefundExecutionReader"/> 上的同名方法 —— 兩條路徑必須用同一個
    /// 判斷，否則會出現「預覽說可以執行、實際執行卻拒絕」的落差。
    /// </remarks>
    private Task<RefundTrustedInputs?> FindTrustedInputsAsync(
        Refund refund,
        CancellationToken cancellationToken) =>
        new RefundTrustedInputsReader(_context)
            .FindAsync(refund.OrderId, refund.ReturnRequestId, cancellationToken);

    /// <summary>
    /// 把後端算出的分攤寫進 <c>RefundAllocations</c>，回傳實際寫入筆數。
    /// </summary>
    /// <remarks>
    /// 草稿以 <c>OrderItemPublicId</c> 表示商品列，內部主鍵只在這個擁有交易的
    /// 寫入端解析（<c>RefundAllocationDraft</c> 的註解已載明）。解析不到任何一個
    /// 品項就整筆拒絕 —— 少寫一列分攤等於讓退款金額與明細對不起來。
    /// </remarks>
    private async Task<int> WriteAllocationsAsync(
        Refund refund,
        IReadOnlyList<RefundAllocationDraft> drafts,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        if (drafts.Count == 0)
        {
            return 0;
        }

        var itemPublicIds = drafts
            .Where(draft => draft.OrderItemPublicId is not null)
            .Select(draft => draft.OrderItemPublicId!.Value)
            .Distinct()
            .ToArray();

        var itemIds = await _context.OrderItems
            .AsNoTracking()
            .Where(item => item.OrderId == refund.OrderId && itemPublicIds.Contains(item.PublicId))
            .ToDictionaryAsync(item => item.PublicId, item => item.Id, cancellationToken);

        if (itemIds.Count != itemPublicIds.Length)
        {
            throw new InvalidOperationException(
                "A refund allocation refers to an order item that is not on this order.");
        }

        foreach (var draft in drafts)
        {
            _context.RefundAllocations.Add(new RefundAllocation(
                Guid.NewGuid(),
                refund.Id,
                draft.OrderItemPublicId is { } publicId ? itemIds[publicId] : null,
                draft.Type,
                draft.Amount,
                draft.OriginalDiscountAllocation,
                occurredAtUtc,
                draft.Quantity));
        }

        return drafts.Count;
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
