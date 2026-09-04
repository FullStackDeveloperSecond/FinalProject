using System.Data;
using System.Globalization;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Refunds;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Refunds;

/// <summary>
/// 退款核准：核對狀態 → 重新計算可信金額 → 條件更新退款 → 寫入稽核。交易由共用
/// <c>IIdempotencyExecutor</c> 擁有並以 Serializable 開啟，與 <see cref="RefundExecutor"/>
/// 同一個約定（alex 2026-09-04 #98 WP2 裁定）。
/// </summary>
/// <remarks>
/// <para>
/// <b>不建立分攤、不完成退貨</b>——那兩件事分別屬於執行（<see cref="RefundExecutor"/>）與
/// 退款結案（<c>IRefundReturnCompletionPort</c>）。核准只做一件事：把 <c>PendingReview</c>
/// 依可信快照重算出的金額推進 <c>Approved</c>，讓它成為之後可以被執行的退款。
/// </para>
/// <para>
/// 這裡刻意複製 <see cref="RefundExecutor"/> 的身分重查邏輯（<see cref="AuthorizeActorAsync"/>
/// 等），不抽共用方法——與那個類別的 <c>AuthorizeActorAsync</c> 註解同一個立場：
/// 這四項檢查要與管理員登入資格逐項一致，獨立寫一份能讓稽核直接對照，也不會讓
/// 未來任一條寫入路徑的重構意外改到另一條的授權邊界。
/// </para>
/// </remarks>
public sealed class RefundApprover : IRefundApprover
{
    /// <summary>SQL Server 的死結受害者錯誤碼。</summary>
    private const int DeadlockVictimErrorNumber = 1205;

    /// <summary>交易邊界的重試次數，與 <see cref="RefundExecutor"/> 同一個理由。</summary>
    private const int MaximumAttempts = 3;

    private const int StatusCodes200Ok = 200;

    /// <summary>中央 Idempotency 的 Operation 名稱。</summary>
    public const string Operation = "refund.approve";

    private readonly DoSelectDbContext _context;
    private readonly IAuditWriter _auditWriter;
    private readonly IIdempotencyExecutor _idempotencyExecutor;
    private readonly TimeProvider _timeProvider;

    public RefundApprover(
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

    public async Task<ApproveRefundResult> ApproveAsync(
        ApproveRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        RefundApprovalDecision.RequireWellFormed(request);

        var adminPublicId = await ResolveAdminPublicIdAsync(
            request.ApprovedByAdminUserId, cancellationToken);

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
                    handler: token => ApproveOnceAsync(request, token),
                    replayFactory: (stored, token) => ReplayAsync(
                        stored,
                        request.ApprovedByAdminUserId,
                        token),
                    cancellationToken,
                    IsolationLevel.Serializable);

                return execution.Body;
            }
            catch (RefundRejectedException exception)
            {
                return ApproveRefundResult.Failure(exception.ErrorCode);
            }
            catch (Exception exception) when (IsRetryableConflict(exception))
            {
                _context.ChangeTracker.Clear();

                if (attempt == MaximumAttempts)
                {
                    return ApproveRefundResult.Failure(RefundErrorCodes.ConcurrencyConflict);
                }
            }
        }

        return ApproveRefundResult.Failure(RefundErrorCodes.ConcurrencyConflict);
    }

    private async Task<ApproveRefundResult> ReplayAsync(
        StoredIdempotencyResponse stored,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        await AuthorizeActorAsync(adminUserId, cancellationToken);

        return decimal.TryParse(
            stored.ResponseSummary,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var approvedAmount)
            ? ApproveRefundResult.Replayed(approvedAmount)
            : ApproveRefundResult.Failure(RefundErrorCodes.RefundStateConflict);
    }

    private async Task<IdempotencyResponse<ApproveRefundResult>> ApproveOnceAsync(
        ApproveRefundRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthorizeActorAsync(request.ApprovedByAdminUserId, cancellationToken);

        var refund = await _context.Refunds
            .SingleOrDefaultAsync(
                candidate => candidate.PublicId == request.RefundPublicId,
                cancellationToken);

        if (refund is null)
        {
            return Rejected(ApproveRefundResult.Failure(RefundErrorCodes.ResourceNotFound));
        }

        var snapshot = new RefundApprovalSnapshot(
            refund.Id,
            refund.Status,
            refund.RequestedAmount,
            refund.RowVersion,
            await FindTrustedInputsAsync(refund, cancellationToken));

        var decision = RefundApprovalDecision.Evaluate(snapshot, request);
        if (decision.Plan is not { } plan)
        {
            return Rejected(decision);
        }

        _context.Entry(refund).Property(entity => entity.RowVersion).OriginalValue =
            plan.ExpectedRefundRowVersion;

        var occurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var previousStatus = refund.Status;
        refund.Approve(plan.ApprovedAmount, plan.ApprovedByAdminUserId, occurredAtUtc);

        WriteAudit(refund, request, plan, actor, previousStatus);

        await _context.SaveChangesAsync(cancellationToken);

        return new IdempotencyResponse<ApproveRefundResult>(
            StatusCodes200Ok,
            ApproveRefundResult.Settled(plan.ApprovedAmount, plan),
            plan.ApprovedAmount.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>以例外中止交易，把拒絕帶回 <see cref="ApproveAsync"/>。與
    /// <see cref="RefundExecutor"/> 同一個理由：必須用拋的，不能回帶 4xx 的
    /// <see cref="IdempotencyResponse{T}"/>，否則拒絕原因會被永久固化成冪等回放。</summary>
    private static IdempotencyResponse<ApproveRefundResult> Rejected(ApproveRefundResult result) =>
        throw new RefundRejectedException(result.ErrorCode!);

    private sealed class RefundRejectedException : Exception
    {
        public RefundRejectedException(string errorCode)
            : base(errorCode) => ErrorCode = errorCode;

        public string ErrorCode { get; }
    }

    /// <summary>
    /// 讀出後端產生核准金額所需的完整可信快照，與 <see cref="RefundExecutor"/> 的同名方法
    /// 共用同一份 <c>RefundTrustedInputsReader</c>，避免「核准算得出來、執行算不出來」的落差。
    /// </summary>
    private Task<RefundTrustedInputs?> FindTrustedInputsAsync(
        Refund refund,
        CancellationToken cancellationToken) =>
        new RefundTrustedInputsReader(_context)
            .FindAsync(refund.OrderId, refund.Id, refund.ReturnRequestId, cancellationToken);

    /// <summary>把核准理由寫進中央 <c>AuditLog</c>。</summary>
    private void WriteAudit(
        Refund refund,
        ApproveRefundRequest request,
        RefundApprovalPlan plan,
        AuditActor actor,
        RefundStatus previousStatus)
    {
        _auditWriter.Add(AuditWriteRequest.Create(
            Guid.NewGuid(),
            actor,
            AuditActions.RefundApprove,
            AuditResourceTypes.Refund,
            refund.PublicId,
            AuditResult.Success,
            errorCode: null,
            changes:
            [
                AuditFieldChange.Code(
                    "status", previousStatus.ToString(), refund.Status.ToString()),
                AuditFieldChange.Code(
                    "approvedAmount", null, Text(plan.ApprovedAmount)),
            ],
            reason: request.ReasonCode.Trim(),
            correlationId: request.CorrelationId,
            traceId: request.TraceId,
            jobPublicId: null,
            remoteIpAddress: request.RemoteIpAddress,
            note: request.Note));
    }

    private static string Text<T>(T value) where T : IFormattable =>
        value.ToString(null, CultureInfo.InvariantCulture);

    /// <summary>交易外只換出穩定的管理員 PublicId，供組成 Actor Scope 用——與
    /// <see cref="RefundExecutor.ResolveAdminPublicIdAsync"/> 同一個理由。</summary>
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
    /// 逐項一致於 <see cref="RefundExecutor.AuthorizeActorAsync"/>（因此也一致於管理員
    /// 登入資格）：帳號型別、<c>AccountStatus</c>、<c>AdminProfile.IsActive</c>、
    /// 財務角色。核准與執行都是 <c>Refund.Execute</c> Policy 下的動作，用同一組角色。
    /// </summary>
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
                "The administrator no longer has permission to approve refunds.");
        }

        return AuditActor.Create(AuditActorType.Admin, admin.PublicId, roles);
    }

    /// <summary>值得重跑整段交易的並行衝突：SQL Server 死結受害者，或 rowversion 樂觀鎖失敗。</summary>
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
}
