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
/// 退款執行：核對狀態與可退款餘額 → 條件更新退款 → 完成關聯退貨 → 寫入七類分攤 →
/// 寫入稽核。交易由共用 <c>IIdempotencyExecutor</c> 擁有並以 Serializable 開啟
/// （DEC-BATCH-019 A1），因此餘額所依據的範圍查詢在交易期間不會被其他交易插入；
/// 退款列本身另有 rowversion 樂觀鎖，兩者共同保證成功退款累計不超過已收款金額。
/// 稽核、退款狀態與（有關聯退貨時）退貨結案同批提交，任一寫入失敗即整體回滾
/// （DEC-P289；退貨結案是 #98 追蹤、接續 #99 A1 補上的一段）。
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


    private const int StatusCodes200Ok = 200;

    /// <summary>中央 Idempotency 的 Operation 名稱（DEC-BATCH-019）。</summary>
    public const string Operation = "refund.execute";

    /// <summary>正常退款執行成功結案的原因碼——與核准時重算後無款可退的
    /// <c>RefundApprover.ZeroNetApprovalReasonCode</c> 區分。</summary>
    private const string RefundSucceededReasonCode = "refund-succeeded";

    private readonly DoSelectDbContext _context;
    private readonly IAuditWriter _auditWriter;
    private readonly IIdempotencyExecutor _idempotencyExecutor;
    private readonly IRefundReturnCompletionPort _returnCompletionPort;
    private readonly TimeProvider _timeProvider;

    public RefundExecutor(
        DoSelectDbContext context,
        IAuditWriter auditWriter,
        IIdempotencyExecutor idempotencyExecutor,
        IRefundReturnCompletionPort returnCompletionPort,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(idempotencyExecutor);
        ArgumentNullException.ThrowIfNull(returnCompletionPort);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _context = context;
        _auditWriter = auditWriter;
        _idempotencyExecutor = idempotencyExecutor;
        _returnCompletionPort = returnCompletionPort;
        _timeProvider = timeProvider;
    }

    public async Task<ExecuteRefundResult> ExecuteAsync(
        ExecuteRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        RefundExecutionDecision.RequireWellFormed(request);

        // Actor Scope 是冪等鍵的一部分，而共用 Executor 要求由它自己開啟交易，
        // 所以身分解析必須早於交易。這裡只換出**穩定的** PublicId。
        //
        // 角色不在這裡查：那是「執行當下的授權」，在交易內重查（AuthorizeActorAsync）。
        // 兩者一起放在交易外的話，管理員在解析通過到交易開啟之間被撤權，
        // 這筆退款仍會用舊的角色快照完成。
        var adminPublicId = await ResolveAdminPublicIdAsync(
            request.ExecutedByAdminUserId, cancellationToken);

        // RequestHash 涵蓋 Refund PublicId、RowVersion、ReasonCode 與 Note
        // （DEC-BATCH-019）。同一把金鑰換上不同的版本或理由，是 Payload 衝突
        // 而不是重播。
        var command = IdempotencyCommand.Create(
            IdempotencyActorScope.ForAdmin(adminPublicId),
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
                    handler: token => ExecuteOnceAsync(request, token),
                    replayFactory: (stored, token) => ReplayAsync(
                        stored,
                        request.ExecutedByAdminUserId,
                        token),
                    cancellationToken,
                    IsolationLevel.Serializable);

                return execution.Body;
            }
            catch (RefundRejectedException exception)
            {
                // 決策拒絕。交易已回滾，沒有留下冪等完成紀錄。
                return ExecuteRefundResult.Failure(exception.ErrorCode);
            }
            // IdempotencyConflictException 刻意不攔：GlobalExceptionHandler 會把它轉成
            // 409 並帶上 Retry-After（錯誤碼目錄第 36 行要求呼叫端依該標頭等待後重試）。
            // 在這裡抓下來只留 ErrorCode，等於把 RetryAfterSeconds 丟掉，呼叫端不知道
            // 該等多久。
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
    private async Task<ExecuteRefundResult> ReplayAsync(
        StoredIdempotencyResponse stored,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        // A replay still returns finance-only refund data. The controller's cookie claims may be
        // stale after a role or account change, so enforce the same current eligibility used by
        // the first execution before releasing the stored result.
        await AuthorizeActorAsync(adminUserId, cancellationToken);

        return decimal.TryParse(
            stored.ResponseSummary,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var settledAmount)
            ? ExecuteRefundResult.Replayed(settledAmount)
            : ExecuteRefundResult.Failure(RefundErrorCodes.RefundStateConflict);
    }

    private async Task<IdempotencyResponse<ExecuteRefundResult>> ExecuteOnceAsync(
        ExecuteRefundRequest request,
        CancellationToken cancellationToken)
    {
        // 在交易內重查授權：撤權必須在這裡擋下來，不能沿用交易外的角色快照。
        var actor = await AuthorizeActorAsync(request.ExecutedByAdminUserId, cancellationToken);

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

        // 退款結案時一併把對應退貨從 AwaitingRefund 推到 Completed（#98 追蹤、接續
        // #99 A1）。沒有關聯退貨（例如未來非退貨來源的退款）就沒有東西可結案。
        if (refund.ReturnRequestId is { } returnRequestId)
        {
            await _returnCompletionPort.CompleteReturnAsync(
                new RefundReturnCompletionCommand(
                    returnRequestId, plan.ExecutedByAdminUserId, occurredAtUtc, RefundSucceededReasonCode),
                cancellationToken);
        }

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
    /// 與 <see cref="RefundExecutionReader"/> 的同名方法**必須用同一個判斷**，否則會出現
    /// 「預覽說可以執行、實際執行卻拒絕」的落差，而管理員只看得到後者。
    /// <para>
    /// 傳入 <c>refund.Id</c> 是為了讓歷史已退數量排除本次退款自身；這個查詢在共用
    /// Executor 擁有的 Serializable 交易內執行，與後續寫入看到的是同一份快照。
    /// </para>
    /// </remarks>
    private Task<RefundTrustedInputs?> FindTrustedInputsAsync(
        Refund refund,
        CancellationToken cancellationToken) =>
        new RefundTrustedInputsReader(_context)
            .FindAsync(refund.OrderId, refund.Id, refund.ReturnRequestId, cancellationToken);

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
            // reason 只接受 safe-code；note 走中央 Audit 的獨立欄位。
            // 先前把兩者串成 `reasonCode: note` 塞進 reason，任何含空白或中文的
            // note 都會讓 reason 驗證失敗，把一次正常退款變成 500。
            reason: request.ReasonCode.Trim(),
            correlationId: request.CorrelationId,
            traceId: request.TraceId,
            jobPublicId: null,
            remoteIpAddress: request.RemoteIpAddress,
            note: request.Note));
    }

    private static string Text<T>(T value) where T : IFormattable =>
        value.ToString(null, CultureInfo.InvariantCulture);

    /// <summary>
    /// 交易外只換出穩定的管理員 PublicId，供組成 Actor Scope 用。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Actor Scope 是冪等鍵的一部分，而共用 <c>IIdempotencyExecutor</c> 要求由它自己
    /// 開啟交易，所以這一步必須早於交易。
    /// </para>
    /// <para>
    /// **這裡刻意不查角色。** 角色是「執行當下的授權」，必須在交易內重查 ——
    /// 見 <see cref="AuthorizeActorAsync"/>。PublicId 則是帳號的穩定識別，
    /// 提早解析不會產生授權窗口。
    /// </para>
    /// </remarks>
    private async Task<Guid> ResolveAdminPublicIdAsync(
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var publicId = await _context.Users.AsNoTracking()
            .Where(user => user.Id == adminUserId && user.AccountType == AccountType.Admin)
            .Select(user => (Guid?)user.PublicId)
            .SingleOrDefaultAsync(cancellationToken);

        return publicId ?? throw DomainProblemException.Forbidden(
            "The administrator identity is invalid.");
    }

    /// <summary>
    /// 在退款交易內重查帳號狀態與角色，並組出實際寫進稽核的 <see cref="AuditActor"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **必須在交易內**。先前整個 Actor（含角色快照）都在交易外解析，於是
    /// 「解析通過」到「交易開啟」之間存在一個授權窗口：財務管理員在這段時間被撤銷
    /// 角色，handler 仍會拿交易外的舊快照完成退款並寫稽核。Controller 的 Policy 用的是
    /// 既有登入 Claims，補不掉這個窗口 —— 資料庫角色重查本來就是為了擋舊 Claims。
    /// </para>
    /// <para>
    /// 重查的四項與管理員登入資格**逐項一致**（見
    /// <c>SecurityServiceCollectionExtensions</c> 的 Cookie 驗證與
    /// <c>AdminLoginUseCase</c>）：帳號型別、<c>AccountStatus</c>、
    /// <c>AdminProfile.IsActive</c>、退款角色。少查任何一項，就會出現
    /// 「登入時擋得住、執行退款時擋不住」的落差 —— 帳號被停權或 AdminProfile
    /// 被停用但角色列還在時，退款仍會完成。
    /// </para>
    /// <para>
    /// 沒有 <c>AdminProfile</c> 一律視為不合格，與登入路徑的
    /// <c>profile?.IsActive ?? false</c> 相同。
    /// </para>
    /// <para>
    /// 這裡丟 <c>Forbidden</c> 會讓共用 Executor 的交易整個回滾：退款狀態、分攤、
    /// 稽核與冪等完成紀錄都不會留下。
    /// </para>
    /// </remarks>
    private async Task<AuditActor> AuthorizeActorAsync(
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var admin = await _context.Users.AsNoTracking()
            .Where(user => user.Id == adminUserId && user.AccountType == AccountType.Admin)
            .Select(user => new { user.Id, user.PublicId, user.AccountStatus })
            .SingleOrDefaultAsync(cancellationToken);
        if (admin is null)
        {
            throw DomainProblemException.Forbidden("The administrator identity is invalid.");
        }

        if (admin.AccountStatus != AccountStatus.Active)
        {
            throw DomainProblemException.Forbidden(
                "The administrator account is not active.");
        }

        var hasActiveProfile = await _context.AdminProfiles.AsNoTracking()
            .AnyAsync(
                profile => profile.UserId == admin.Id && profile.IsActive,
                cancellationToken);
        if (!hasActiveProfile)
        {
            throw DomainProblemException.Forbidden(
                "The administrator profile is not active.");
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
