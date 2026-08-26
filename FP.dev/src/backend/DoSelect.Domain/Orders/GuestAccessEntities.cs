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

    public void RecordSend(DateTime sentAtUtc)
    {
        EnsureCanSend(sentAtUtc);
        SendCount++;
        LastSentAtUtc = sentAtUtc;
        MarkUpdated(sentAtUtc);
    }

    /// <summary>
    /// 重寄：驗證 <paramref name="previous"/>（同一張 Challenge 目前這一筆有效 Row）是否仍可
    /// 寄送（沿用 <see cref="EnsureCanSend"/> 的寄送上限／間隔規則），成功才建立一筆延續同一組
    /// Scope Hash／到期時間的新 Row——<see cref="SendCount"/> 從舊 Row 累加、
    /// <see cref="AttemptCount"/> 歸零。呼叫端必須在同一交易內對 <paramref name="previous"/>
    /// 呼叫 <see cref="Revoke"/> 讓舊 Row／舊碼立即失效；這個工廠方法只讀取 previous 目前的
    /// 狀態做驗證與延續，不會修改 previous 本身。有效與 Decoy 都走這個工廠（Decoy 傳
    /// <paramref name="newCodeHash"/> 為 null）——三 Scope 限流用的是「新增一筆 Row」，
    /// 兩種情況都要留下可計數的 Row，不能只有有效請求才新增。
    /// </summary>
    public static GuestOrderAccessRequest CreateResend(
        Guid publicId, GuestOrderAccessRequest previous, byte[]? newCodeHash, DateTime sentAtUtc)
    {
        ArgumentNullException.ThrowIfNull(previous);
        previous.EnsureCanSend(sentAtUtc);

        var successor = previous.OrderId is null
            ? CreateDecoy(
                publicId,
                previous.RequesterIpHash,
                previous.EmailKeyHash,
                previous.OrderLookupKeyHash,
                previous.ExpiresAtUtc,
                sentAtUtc)
            : CreateValid(
                publicId,
                previous.OrderId.Value,
                newCodeHash ?? throw new ArgumentNullException(nameof(newCodeHash)),
                previous.RequesterIpHash,
                previous.EmailKeyHash,
                previous.OrderLookupKeyHash,
                previous.ExpiresAtUtc,
                sentAtUtc);

        successor.SendCount = previous.SendCount + 1;
        successor.LastSentAtUtc = sentAtUtc;
        return successor;
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
