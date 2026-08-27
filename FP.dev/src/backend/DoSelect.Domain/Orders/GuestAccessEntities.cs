using DoSelect.Domain.Common;

namespace DoSelect.Domain.Orders;

public sealed class GuestOrderAccessRequest : MutablePublicEntity
{
    public const int MaximumAttempts = 5;
    public const int MaximumSends = 3;

    private GuestOrderAccessRequest()
    {
    }

    private GuestOrderAccessRequest(
        Guid publicId,
        long? orderId,
        byte[]? codeHash,
        byte[] requesterIpHash,
        byte[] emailKeyHash,
        byte[] orderLookupKeyHash,
        DateTime expiresAtUtc,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (orderId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(orderId));
        }

        OrderId = orderId;
        CodeHash = OptionalHash(codeHash, nameof(codeHash));
        RequesterIpHash = RequireHash(requesterIpHash, nameof(requesterIpHash));
        EmailKeyHash = RequireHash(emailKeyHash, nameof(emailKeyHash));
        OrderLookupKeyHash = RequireHash(orderLookupKeyHash, nameof(orderLookupKeyHash));
        ExpiresAtUtc = RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (ExpiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }
    }

    public long? OrderId { get; private set; }

    public byte[]? CodeHash { get; private set; }

    public byte[] RequesterIpHash { get; private set; } = [];

    public byte[] EmailKeyHash { get; private set; } = [];

    public byte[] OrderLookupKeyHash { get; private set; } = [];

    public DateTime ExpiresAtUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public int SendCount { get; private set; }

    public DateTime? LastSentAtUtc { get; private set; }

    public DateTime? LockedAtUtc { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public static GuestOrderAccessRequest CreateValid(
        Guid publicId,
        long orderId,
        byte[] codeHash,
        byte[] requesterIpHash,
        byte[] emailKeyHash,
        byte[] orderLookupKeyHash,
        DateTime expiresAtUtc,
        DateTime createdAtUtc) =>
        new(
            publicId,
            orderId,
            codeHash,
            requesterIpHash,
            emailKeyHash,
            orderLookupKeyHash,
            expiresAtUtc,
            createdAtUtc);

    public static GuestOrderAccessRequest CreateDecoy(
        Guid publicId,
        byte[] requesterIpHash,
        byte[] emailKeyHash,
        byte[] orderLookupKeyHash,
        DateTime expiresAtUtc,
        DateTime createdAtUtc) =>
        new(
            publicId,
            null,
            null,
            requesterIpHash,
            emailKeyHash,
            orderLookupKeyHash,
            expiresAtUtc,
            createdAtUtc);

    /// <summary>
    /// 固定哨兵值，代表「沒有可信 Email／OrderLookup Hash」——見
    /// <see cref="CreateUnknownResendAttempt"/>。HMAC-SHA256 輸出實務上不可能是全零，
    /// 不會跟任何真實 Email／OrderLookup Hash 碰撞。
    /// </summary>
    public static readonly byte[] UnknownScopeHash = new byte[32];

    /// <summary>
    /// Resend 查無此 PublicId／已完全失效呼叫時，用來「唯一消耗＋持久化」目前呼叫者 IP
    /// 這一個 Scope 的紀錄——沒有真實 Request 可延續，也沒有可信 Email／OrderLookup Hash
    /// 可用，因此兩者一律填 <see cref="UnknownScopeHash"/>，只讓這筆 Row 對 IP Scope 的
    /// (Hash, CreatedAtUtc) 索引可數。<see cref="Entity.PublicId"/> 只是佔位，永遠不會回傳
    /// 給呼叫端，也不會被 <c>FindActiveRequestAsync</c> 需要再利用。
    /// </summary>
    public static GuestOrderAccessRequest CreateUnknownResendAttempt(
        Guid publicId,
        byte[] requesterIpHash,
        DateTime expiresAtUtc,
        DateTime createdAtUtc) =>
        new(
            publicId,
            null,
            null,
            requesterIpHash,
            UnknownScopeHash,
            UnknownScopeHash,
            expiresAtUtc,
            createdAtUtc);

    /// <summary>
    /// 已知 Challenge 成功重寄時的持久化限流事件。它沿用 Challenge 的 Email／OrderLookup
    /// Hash，並保存這次呼叫的 IP Hash，讓三個 Scope 都能透過既有
    /// (Hash, CreatedAtUtc) 索引計數；不保存訂單、驗證碼，也永遠不回傳給呼叫端。
    /// 建立後立即標記撤銷，確保即使 PublicId 被取得也不能被當成可驗證的 Decoy Challenge。
    /// </summary>
    public static GuestOrderAccessRequest CreateResendRateLimitEvent(
        Guid publicId,
        byte[] requesterIpHash,
        byte[] emailKeyHash,
        byte[] orderLookupKeyHash,
        DateTime expiresAtUtc,
        DateTime createdAtUtc)
    {
        var rateLimitEvent = new GuestOrderAccessRequest(
            publicId,
            null,
            null,
            requesterIpHash,
            emailKeyHash,
            orderLookupKeyHash,
            expiresAtUtc,
            createdAtUtc);
        rateLimitEvent.Revoke(createdAtUtc);
        return rateLimitEvent;
    }

    public void RecordSend(DateTime sentAtUtc)
    {
        EnsureCanSend(sentAtUtc);
        SendCount++;
        LastSentAtUtc = sentAtUtc;
        MarkUpdated(sentAtUtc);
    }

    /// <summary>
    /// 重寄時原地更新同一張 Challenge，PublicId 與到期時間不變；有效 Request 以新 CodeHash
    /// 取代舊碼，Decoy 維持 null，並把本輪錯誤嘗試歸零。持久化限流由同一交易新增的
    /// <see cref="CreateResendRateLimitEvent"/> 負責，不能靠建立無鏈識別欄位的 successor Row。
    /// </summary>
    public void RecordResend(
        byte[]? newCodeHash,
        DateTime sentAtUtc)
    {
        EnsureCanSend(sentAtUtc);
        CodeHash = OrderId is null
            ? newCodeHash is null
                ? null
                : throw new ArgumentException("A decoy challenge cannot store a code hash.", nameof(newCodeHash))
            : RequireHash(newCodeHash ?? throw new ArgumentNullException(nameof(newCodeHash)), nameof(newCodeHash));
        AttemptCount = 0;
        SendCount++;
        LastSentAtUtc = sentAtUtc;
        MarkUpdated(sentAtUtc);
    }

    private void EnsureCanSend(DateTime sentAtUtc)
    {
        EnsureActive(sentAtUtc);
        if (SendCount >= MaximumSends)
        {
            throw new InvalidOperationException("The send limit has been reached.");
        }

        if (LastSentAtUtc.HasValue && sentAtUtc - LastSentAtUtc.Value < TimeSpan.FromSeconds(60))
        {
            throw new InvalidOperationException("The resend interval has not elapsed.");
        }
    }

    public void RecordFailedAttempt(DateTime attemptedAtUtc)
    {
        EnsureActive(attemptedAtUtc);
        AttemptCount++;
        if (AttemptCount >= MaximumAttempts)
        {
            LockedAtUtc = attemptedAtUtc;
        }

        MarkUpdated(attemptedAtUtc);
    }

    public void Consume(DateTime consumedAtUtc)
    {
        EnsureActive(consumedAtUtc);
        ConsumedAtUtc = consumedAtUtc;
        MarkUpdated(consumedAtUtc);
    }

    public void Revoke(DateTime revokedAtUtc)
    {
        revokedAtUtc = RequireUtc(revokedAtUtc, nameof(revokedAtUtc));
        if (RevokedAtUtc.HasValue)
        {
            return;
        }

        RevokedAtUtc = revokedAtUtc;
        MarkUpdated(revokedAtUtc);
    }

    private void EnsureActive(DateTime nowUtc)
    {
        nowUtc = RequireUtc(nowUtc, nameof(nowUtc));
        if (nowUtc >= ExpiresAtUtc || ConsumedAtUtc.HasValue || LockedAtUtc.HasValue ||
            RevokedAtUtc.HasValue || AttemptCount >= MaximumAttempts)
        {
            throw new InvalidOperationException("The guest access challenge is not active.");
        }
    }

    private static byte[] RequireHash(byte[] value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length != 32)
        {
            throw new ArgumentException("The hash must contain 32 bytes.", parameterName);
        }

        return value.ToArray();
    }

    private static byte[]? OptionalHash(byte[]? value, string parameterName) =>
        value is null ? null : RequireHash(value, parameterName);
}

public sealed class GuestOrderAccessToken : PublicEntity
{
    private GuestOrderAccessToken()
    {
    }

    public GuestOrderAccessToken(
        Guid publicId,
        long orderId,
        long requestId,
        byte[] tokenHash,
        DateTime expiresAtUtc,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (orderId <= 0 || requestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(orderId));
        }

        if (tokenHash is null || tokenHash.Length != 32)
        {
            throw new ArgumentException("The token hash must contain 32 bytes.", nameof(tokenHash));
        }

        expiresAtUtc = RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        OrderId = orderId;
        RequestId = requestId;
        TokenHash = tokenHash.ToArray();
        ExpiresAtUtc = expiresAtUtc;
    }

    public long OrderId { get; private set; }

    public long RequestId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public int ScopeViolationCount { get; private set; }

    public void Revoke(DateTime revokedAtUtc)
    {
        RevokedAtUtc ??= RequireUtc(revokedAtUtc, nameof(revokedAtUtc));
    }
}

public sealed class AssemblyJob : MutablePublicEntity
{
    private AssemblyJob()
    {
    }

    public AssemblyJob(
        Guid publicId,
        long orderId,
        Guid assemblyGroupKey,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        if (orderId <= 0 || assemblyGroupKey == Guid.Empty)
        {
            throw new ArgumentException("Order and assembly group are required.");
        }

        OrderId = orderId;
        AssemblyGroupKey = assemblyGroupKey;
        Status = AssemblyJobStatus.Pending;
    }

    public long OrderId { get; private set; }

    public Guid AssemblyGroupKey { get; private set; }

    public AssemblyJobStatus Status { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public string? AssignedAdminUserId { get; private set; }

    public string? Note { get; private set; }

    public void Assign(string? adminUserId, string? note, DateTime updatedAtUtc)
    {
        AssignedAdminUserId = string.IsNullOrWhiteSpace(adminUserId)
            ? null
            : adminUserId.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        MarkUpdated(updatedAtUtc);
    }

    public void ChangeStatus(AssemblyJobStatus nextStatus, DateTime occurredAtUtc)
    {
        if (!IsAllowed(Status, nextStatus))
        {
            throw new InvalidOperationException(
                $"Assembly job cannot move from {Status} to {nextStatus}.");
        }

        occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        Status = nextStatus;
        StartedAtUtc = nextStatus == AssemblyJobStatus.Started && !StartedAtUtc.HasValue
            ? occurredAtUtc
            : StartedAtUtc;
        CompletedAtUtc = nextStatus is AssemblyJobStatus.ReadyToShip or
            AssemblyJobStatus.Cancelled
            ? occurredAtUtc
            : CompletedAtUtc;
        MarkUpdated(occurredAtUtc);
    }

    private static bool IsAllowed(AssemblyJobStatus current, AssemblyJobStatus next) =>
        (current, next) switch
        {
            (AssemblyJobStatus.Pending, AssemblyJobStatus.Started or AssemblyJobStatus.Cancelled) =>
                true,
            (AssemblyJobStatus.Started, AssemblyJobStatus.Testing or AssemblyJobStatus.Failed) =>
                true,
            (AssemblyJobStatus.Testing, AssemblyJobStatus.ReadyToShip or AssemblyJobStatus.Failed) =>
                true,
            (AssemblyJobStatus.Failed, AssemblyJobStatus.Started) => true,
            _ => false,
        };
}

public sealed class AssemblyJobStatusHistory : PublicEntity
{
    private AssemblyJobStatusHistory()
    {
    }

    public AssemblyJobStatusHistory(
        Guid publicId,
        long assemblyJobId,
        AssemblyJobStatus? fromStatus,
        AssemblyJobStatus toStatus,
        string? reasonCode,
        string? actorUserId,
        DateTime occurredAtUtc,
        string traceId)
        : base(publicId, occurredAtUtc)
    {
        if (assemblyJobId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(assemblyJobId));
        }

        AssemblyJobId = assemblyJobId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? null : reasonCode.Trim();
        ActorUserId = string.IsNullOrWhiteSpace(actorUserId) ? null : actorUserId.Trim();
        OccurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        TraceId = RequireText(traceId, nameof(traceId));
    }

    public long AssemblyJobId { get; private set; }

    public AssemblyJobStatus? FromStatus { get; private set; }

    public AssemblyJobStatus ToStatus { get; private set; }

    public string? ReasonCode { get; private set; }

    public string? ActorUserId { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public string TraceId { get; private set; } = string.Empty;
}
