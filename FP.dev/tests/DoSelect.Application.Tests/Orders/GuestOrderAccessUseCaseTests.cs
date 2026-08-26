using System.Reflection;
using System.Security.Cryptography;
using DoSelect.Application.Common;
using DoSelect.Application.Notifications;
using DoSelect.Application.Orders;
using DoSelect.Domain.Common;
using DoSelect.Domain.Orders;
using Microsoft.Extensions.Options;

namespace DoSelect.Application.Tests.Orders;

public sealed class GuestOrderAccessUseCaseTests
{
    private const string ValidOrderNumber = "ORD-000123";
    private const string ValidEmail = "guest@example.com";
    private const string RequesterIp = "203.0.113.10";

    [Fact]
    public async Task RequestAccessAsync_WhenOrderExists_ReturnsAcceptedAndSendsEmail()
    {
        var gateway = new FakeGuestOrderAccessGateway();
        gateway.SeedOrder(ValidOrderNumber, "GUEST@EXAMPLE.COM", orderId: 1, orderPublicId: Guid.CreateVersion7());
        var emailQueue = new RecordingEmailDispatchQueue();
        var useCase = CreateUseCase(gateway, emailDispatchQueue: emailQueue);

        var result = await useCase.RequestAccessAsync(ValidOrderNumber, ValidEmail, RequesterIp);

        var accepted = Assert.IsType<GuestOrderAccessAcceptedResult.Accepted>(result);
        Assert.NotEqual(Guid.Empty, accepted.RequestPublicId);
        Assert.Single(emailQueue.SentMessages);
    }

    [Fact]
    public async Task RequestAccessAsync_WhenOrderDoesNotExist_ReturnsTheSameAcceptedShapeWithoutSendingEmail()
    {
        // Non-enumerable by design: an unauthenticated caller must not be able to tell whether an
        // order/email combination exists from the response shape (Haru-會員登入訂單與訪客存取最終
        // Schema.md 第 5 節：相同 202 與等效延遲）.
        var gateway = new FakeGuestOrderAccessGateway();
        var emailQueue = new RecordingEmailDispatchQueue();
        var useCase = CreateUseCase(gateway, emailDispatchQueue: emailQueue);

        var result = await useCase.RequestAccessAsync("NO-SUCH-ORDER", "nobody@example.com", RequesterIp);

        var accepted = Assert.IsType<GuestOrderAccessAcceptedResult.Accepted>(result);
        Assert.NotEqual(Guid.Empty, accepted.RequestPublicId);
        Assert.Empty(emailQueue.SentMessages);
    }

    [Fact]
    public async Task RequestAccessAsync_WhenIpScopeExceedsLimit_ReturnsRateLimited()
    {
        var gateway = new FakeGuestOrderAccessGateway();
        var useCase = CreateUseCase(gateway);
        var permitLimit = new RateLimitOptions().GuestOrderAccessIpPermitLimit;

        GuestOrderAccessAcceptedResult? last = null;
        for (var i = 0; i < permitLimit + 1; i++)
        {
            last = await useCase.RequestAccessAsync($"ORD-{i:D6}", $"user{i}@example.com", RequesterIp);
        }

        Assert.IsType<GuestOrderAccessAcceptedResult.RateLimited>(last);
    }

    [Fact]
    public async Task VerifyAsync_WhenCodeIsWrong_ReturnsInvalidAndRecordsAttempt()
    {
        var gateway = new FakeGuestOrderAccessGateway();
        var orderPublicId = Guid.CreateVersion7();
        gateway.SeedOrder(ValidOrderNumber, "GUEST@EXAMPLE.COM", orderId: 1, orderPublicId: orderPublicId);
        var useCase = CreateUseCase(gateway);
        var accepted = (GuestOrderAccessAcceptedResult.Accepted)
            await useCase.RequestAccessAsync(ValidOrderNumber, ValidEmail, RequesterIp);

        var result = await useCase.VerifyAsync(accepted.RequestPublicId, "000000");

        var failure = Assert.IsType<GuestOrderAccessVerifyResult.Failure>(result);
        Assert.Equal(GuestOrderErrorCodes.VerificationInvalid, failure.ErrorCode);
        Assert.Equal(1, gateway.Requests[accepted.RequestPublicId].AttemptCount);
    }

    [Fact]
    public async Task VerifyAsync_AfterFiveFailedAttempts_LocksChallengeEvenWithCorrectCode()
    {
        var gateway = new FakeGuestOrderAccessGateway();
        var orderPublicId = Guid.CreateVersion7();
        gateway.SeedOrder(ValidOrderNumber, "GUEST@EXAMPLE.COM", orderId: 1, orderPublicId: orderPublicId);
        var hasher = new FakeGuestOrderAccessHasher();
        var useCase = CreateUseCase(gateway, hasher: hasher);
        var accepted = (GuestOrderAccessAcceptedResult.Accepted)
            await useCase.RequestAccessAsync(ValidOrderNumber, ValidEmail, RequesterIp);
        var correctCode = hasher.LastHashedCode!;

        for (var i = 0; i < GuestOrderAccessRequest.MaximumAttempts; i++)
        {
            await useCase.VerifyAsync(accepted.RequestPublicId, "000000");
        }

        var result = await useCase.VerifyAsync(accepted.RequestPublicId, correctCode);

        var failure = Assert.IsType<GuestOrderAccessVerifyResult.Failure>(result);
        Assert.Equal(GuestOrderErrorCodes.VerificationInvalid, failure.ErrorCode);
    }

    [Fact]
    public async Task VerifyAsync_WithCorrectCode_IssuesTokenScopedToTheOrder()
    {
        var gateway = new FakeGuestOrderAccessGateway();
        var orderPublicId = Guid.CreateVersion7();
        gateway.SeedOrder(ValidOrderNumber, "GUEST@EXAMPLE.COM", orderId: 1, orderPublicId: orderPublicId);
        var hasher = new FakeGuestOrderAccessHasher();
        var useCase = CreateUseCase(gateway, hasher: hasher);
        var accepted = (GuestOrderAccessAcceptedResult.Accepted)
            await useCase.RequestAccessAsync(ValidOrderNumber, ValidEmail, RequesterIp);
        var correctCode = hasher.LastHashedCode!;

        var result = await useCase.VerifyAsync(accepted.RequestPublicId, correctCode);

        var success = Assert.IsType<GuestOrderAccessVerifyResult.Success>(result);
        Assert.Equal(orderPublicId, success.OrderPublicId);
        Assert.False(string.IsNullOrWhiteSpace(success.RawToken));
        Assert.Single(gateway.Tokens);
    }

    [Fact]
    public async Task VerifyAsync_WrongCode_RetriesOnConcurrencyConflictAndStillCountsTheAttempt()
    {
        // 模擬「平行錯碼」：SaveChangesAsync 前兩次都因為別的平行請求先寫入而樂觀並行衝突，
        // UseCase 必須重新載入、重算後重試，讓這一次猜測依然確實被計數一次——不能因為
        // 衝突就悄悄漏記，也不能讓例外原樣往外傳變成 500。
        var gateway = new FakeGuestOrderAccessGateway();
        gateway.SeedOrder(ValidOrderNumber, "GUEST@EXAMPLE.COM", orderId: 1, orderPublicId: Guid.CreateVersion7());
        var useCase = CreateUseCase(gateway);
        var accepted = (GuestOrderAccessAcceptedResult.Accepted)
            await useCase.RequestAccessAsync(ValidOrderNumber, ValidEmail, RequesterIp);
        gateway.SaveChangesConflictCountdown = 2;

        var result = await useCase.VerifyAsync(accepted.RequestPublicId, "000000");

        var failure = Assert.IsType<GuestOrderAccessVerifyResult.Failure>(result);
        Assert.Equal(GuestOrderErrorCodes.VerificationInvalid, failure.ErrorCode);
        Assert.Equal(1, gateway.Requests[accepted.RequestPublicId].AttemptCount);
        Assert.Equal(0, gateway.SaveChangesConflictCountdown);
    }

    [Fact]
    public async Task VerifyAsync_WrongCode_GivesUpSafelyAfterExhaustingConcurrencyRetries()
    {
        // 重試次數用盡仍持續衝突的極端邊界——不能讓例外往外傳，維持標準安全失敗回應。
        var gateway = new FakeGuestOrderAccessGateway();
        gateway.SeedOrder(ValidOrderNumber, "GUEST@EXAMPLE.COM", orderId: 1, orderPublicId: Guid.CreateVersion7());
        var useCase = CreateUseCase(gateway);
        var accepted = (GuestOrderAccessAcceptedResult.Accepted)
            await useCase.RequestAccessAsync(ValidOrderNumber, ValidEmail, RequesterIp);
        gateway.SaveChangesConflictCountdown = 1000;

        var result = await useCase.VerifyAsync(accepted.RequestPublicId, "000000");

        Assert.IsType<GuestOrderAccessVerifyResult.Failure>(result);
    }

    [Fact]
    public async Task VerifyAsync_CorrectCode_WhenAnotherRequestWonTheRace_FailsWithoutIssuingASecondToken()
    {
        // 模擬「兩個平行正確碼」：本次驗證通過比對後，寫入 Token 那一刻才發現別的平行請求
        // 已經先消耗掉同一個 Request（樂觀並行衝突）——同一張 Challenge 只能核發一個 Token，
        // 這裡必須安全失敗，且不能有 Token 被寫入。
        var gateway = new FakeGuestOrderAccessGateway();
        gateway.SeedOrder(ValidOrderNumber, "GUEST@EXAMPLE.COM", orderId: 1, orderPublicId: Guid.CreateVersion7());
        var hasher = new FakeGuestOrderAccessHasher();
        var useCase = CreateUseCase(gateway, hasher: hasher);
        var accepted = (GuestOrderAccessAcceptedResult.Accepted)
            await useCase.RequestAccessAsync(ValidOrderNumber, ValidEmail, RequesterIp);
        var correctCode = hasher.LastHashedCode!;
        gateway.SaveChangesConflictCountdown = 1;

        var result = await useCase.VerifyAsync(accepted.RequestPublicId, correctCode);

        Assert.IsType<GuestOrderAccessVerifyResult.Failure>(result);
        Assert.Empty(gateway.Tokens);
    }

    [Fact]
    public async Task ResendAsync_WhenRequestDoesNotExist_ReturnsTheSameAcceptedShape()
    {
        var gateway = new FakeGuestOrderAccessGateway();
        var useCase = CreateUseCase(gateway);

        var result = await useCase.ResendAsync(Guid.CreateVersion7(), RequesterIp);

        Assert.IsType<GuestOrderAccessAcceptedResult.Accepted>(result);
    }

    [Fact]
    public async Task ResendAsync_AfterThreeSends_StillReturnsAcceptedWithoutError()
    {
        // "維持安全回應"：已達寄送上限也不能揭露原因，回應形狀必須跟成功一致。
        var gateway = new FakeGuestOrderAccessGateway();
        gateway.SeedOrder(ValidOrderNumber, "GUEST@EXAMPLE.COM", orderId: 1, orderPublicId: Guid.CreateVersion7());
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var useCase = CreateUseCase(gateway, timeProvider: timeProvider);
        var accepted = (GuestOrderAccessAcceptedResult.Accepted)
            await useCase.RequestAccessAsync(ValidOrderNumber, ValidEmail, RequesterIp);

        GuestOrderAccessAcceptedResult? last = null;
        for (var i = 0; i < GuestOrderAccessRequest.MaximumSends; i++)
        {
            timeProvider.Advance(TimeSpan.FromSeconds(61));
            last = await useCase.ResendAsync(accepted.RequestPublicId, RequesterIp);
        }

        Assert.IsType<GuestOrderAccessAcceptedResult.Accepted>(last);
    }

    [Fact]
    public async Task ResendAsync_ForValidRequest_RotatesCodeAndSendsNewEmail()
    {
        // 修正前：Resend 不寄信、也不換碼，只是白算 SaveChanges。修正後每次重寄要換發
        // 新碼、原子取代舊 CodeHash（舊碼立即失效），並實際 Enqueue 一封新信。
        var gateway = new FakeGuestOrderAccessGateway();
        var orderPublicId = Guid.CreateVersion7();
        gateway.SeedOrder(ValidOrderNumber, "GUEST@EXAMPLE.COM", orderId: 1, orderPublicId: orderPublicId);
        var hasher = new FakeGuestOrderAccessHasher();
        var emailQueue = new RecordingEmailDispatchQueue();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var useCase = CreateUseCase(
            gateway, hasher: hasher, emailDispatchQueue: emailQueue, timeProvider: timeProvider);
        var accepted = (GuestOrderAccessAcceptedResult.Accepted)
            await useCase.RequestAccessAsync(ValidOrderNumber, ValidEmail, RequesterIp);
        var originalCode = hasher.LastHashedCode!;

        timeProvider.Advance(TimeSpan.FromSeconds(61));
        var resendResult = await useCase.ResendAsync(accepted.RequestPublicId, RequesterIp);
        var newCode = hasher.LastHashedCode!;

        Assert.IsType<GuestOrderAccessAcceptedResult.Accepted>(resendResult);
        Assert.NotEqual(originalCode, newCode);
        Assert.Equal(2, emailQueue.SentMessages.Count);
        Assert.Equal("GUEST@EXAMPLE.COM", emailQueue.SentMessages[1].RecipientAddress);

        var oldCodeResult = await useCase.VerifyAsync(accepted.RequestPublicId, originalCode);
        Assert.IsType<GuestOrderAccessVerifyResult.Failure>(oldCodeResult);

        var newCodeResult = await useCase.VerifyAsync(accepted.RequestPublicId, newCode);
        var success = Assert.IsType<GuestOrderAccessVerifyResult.Success>(newCodeResult);
        Assert.Equal(orderPublicId, success.OrderPublicId);
    }

    [Fact]
    public async Task ResendAsync_ForDecoyRequest_IsRateLimitedJustLikeARealRequest()
    {
        // 修正前：request.OrderId is null（Decoy）會在限流檢查之前就直接回 202，
        // 永遠不會被限流；有效 Request 卻會，形成 202/429 的訂單存在性 Oracle。
        // 修正後 Decoy 跟有效 Request 共用同一組限流 Scope，達到上限一樣回 429。
        var gateway = new FakeGuestOrderAccessGateway();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var useCase = CreateUseCase(gateway, timeProvider: timeProvider);
        var accepted = (GuestOrderAccessAcceptedResult.Accepted)
            await useCase.RequestAccessAsync("NO-SUCH-ORDER", "nobody@example.com", RequesterIp);
        var orderLookupPermitLimit = new RateLimitOptions().GuestOrderAccessOrderLookupPermitLimit;

        GuestOrderAccessAcceptedResult? last = null;
        for (var i = 0; i < orderLookupPermitLimit; i++)
        {
            timeProvider.Advance(TimeSpan.FromSeconds(61));
            last = await useCase.ResendAsync(accepted.RequestPublicId, RequesterIp);
        }

        Assert.IsType<GuestOrderAccessAcceptedResult.RateLimited>(last);
    }

    [Fact]
    public async Task RequestAccessAsync_CountsInitialSendTowardTheThreeSendLimit()
    {
        // 規格「最多 3 封」＝初次寄送 + 最多 2 次 resend；初次寄送本身要計入 SendCount，
        // 不能變成初次寄送額外再加 3 次 resend（共 4 封）。
        var gateway = new FakeGuestOrderAccessGateway();
        gateway.SeedOrder(ValidOrderNumber, "GUEST@EXAMPLE.COM", orderId: 1, orderPublicId: Guid.CreateVersion7());
        var emailQueue = new RecordingEmailDispatchQueue();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var useCase = CreateUseCase(gateway, emailDispatchQueue: emailQueue, timeProvider: timeProvider);
        var accepted = (GuestOrderAccessAcceptedResult.Accepted)
            await useCase.RequestAccessAsync(ValidOrderNumber, ValidEmail, RequesterIp);
        Assert.Single(emailQueue.SentMessages);

        timeProvider.Advance(TimeSpan.FromSeconds(61));
        await useCase.ResendAsync(accepted.RequestPublicId, RequesterIp);
        timeProvider.Advance(TimeSpan.FromSeconds(61));
        await useCase.ResendAsync(accepted.RequestPublicId, RequesterIp);
        Assert.Equal(3, emailQueue.SentMessages.Count);

        timeProvider.Advance(TimeSpan.FromSeconds(61));
        var fourthAttempt = await useCase.ResendAsync(accepted.RequestPublicId, RequesterIp);

        Assert.IsType<GuestOrderAccessAcceptedResult.Accepted>(fourthAttempt);
        Assert.Equal(3, emailQueue.SentMessages.Count);
    }

    [Fact]
    public async Task ResendAsync_UsesTheCurrentCallerIp_NotTheIpStoredAtCreation()
    {
        // 方法收到的是「這次呼叫當下」的 requester IP，限流要用這把 Hash，不能沿用
        // Request 建立當時保存的舊 IP bucket——同一張 Challenge 換網路重寄時才會準確。
        var gateway = new FakeGuestOrderAccessGateway();
        gateway.SeedOrder(ValidOrderNumber, "GUEST@EXAMPLE.COM", orderId: 1, orderPublicId: Guid.CreateVersion7());
        var hasher = new FakeGuestOrderAccessHasher();
        var throttle = new RecordingThrottle();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var useCase = new GuestOrderAccessUseCase(
            gateway, hasher, throttle, new RecordingEmailDispatchQueue(), timeProvider);
        var accepted = (GuestOrderAccessAcceptedResult.Accepted)
            await useCase.RequestAccessAsync(ValidOrderNumber, ValidEmail, RequesterIp);

        const string differentIp = "198.51.100.7";
        timeProvider.Advance(TimeSpan.FromSeconds(61));
        await useCase.ResendAsync(accepted.RequestPublicId, differentIp);

        Assert.Equal(2, throttle.IpHashes.Count);
        Assert.Equal(hasher.HashIp(RequesterIp), throttle.IpHashes[0]);
        Assert.Equal(hasher.HashIp(differentIp), throttle.IpHashes[1]);
        Assert.NotEqual(throttle.IpHashes[0], throttle.IpHashes[1]);
    }

    private static GuestOrderAccessUseCase CreateUseCase(
        FakeGuestOrderAccessGateway gateway,
        FakeGuestOrderAccessHasher? hasher = null,
        IEmailDispatchQueue? emailDispatchQueue = null,
        TimeProvider? timeProvider = null) =>
        new(
            gateway,
            hasher ?? new FakeGuestOrderAccessHasher(),
            new GuestOrderAccessThrottle(Options.Create(new RateLimitOptions())),
            emailDispatchQueue ?? new RecordingEmailDispatchQueue(),
            timeProvider ?? TimeProvider.System);

    private sealed class RecordingEmailDispatchQueue : IEmailDispatchQueue
    {
        public List<EmailMessage> SentMessages { get; } = [];

        public void Enqueue(EmailMessage message) => SentMessages.Add(message);
    }

    /// <summary>只記錄每次呼叫收到的 Hash，不做真的限流，用來斷言呼叫端傳入了「哪把」Hash。</summary>
    private sealed class RecordingThrottle : IGuestOrderAccessThrottle
    {
        public List<byte[]> IpHashes { get; } = [];

        public bool TryAcquireIp(byte[] ipHash)
        {
            IpHashes.Add(ipHash);
            return true;
        }

        public bool TryAcquireEmail(byte[] emailHash) => true;

        public bool TryAcquireOrderLookup(byte[] orderLookupHash) => true;
    }

    /// <summary>
    /// 確定性雜湊（不需要真的 Pepper）。<see cref="HashCode"/> 是明碼六位數驗證碼唯一會經過的
    /// 地方——Use Case 算完 Hash 就丟棄明碼，Gateway 存的 Entity 只有 Hash，所以測試要讀正確
    /// 驗證碼只能在這裡攔截，不能事後從 Gateway／Entity 反推。
    /// </summary>
    private sealed class FakeGuestOrderAccessHasher : IGuestOrderAccessHasher
    {
        public string? LastHashedCode { get; private set; }

        public byte[] HashIp(string ipAddress) => Hash("ip", ipAddress);

        public byte[] HashEmail(string emailNormalized) => Hash("email", emailNormalized);

        public byte[] HashOrderLookup(string orderNumber, string emailNormalized) =>
            Hash("order-lookup", $"{orderNumber}:{emailNormalized}");

        public byte[] HashCode(string sixDigitCode)
        {
            LastHashedCode = sixDigitCode;
            return Hash("code", sixDigitCode);
        }

        public byte[] HashToken(string rawToken) => Hash("token", rawToken);

        private static byte[] Hash(string scope, string value) =>
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{scope}:{value}"));
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    /// <summary>
    /// In-memory Fake，比照既有 FakeMemberRegistrationGateway 的作法。用反射設定 Entity.Id
    /// （模擬 EF Core 的身分欄位產生）——Domain Entity 的 Id setter 刻意保持 private，
    /// 一般應用程式碼不會（也不該）這樣做，只有測試 Fake 需要模擬持久層行為。
    /// </summary>
    private sealed class FakeGuestOrderAccessGateway : IGuestOrderAccessGateway
    {
        private static readonly PropertyInfo IdProperty =
            typeof(Entity).GetProperty(nameof(Entity.Id))!;

        /// <summary>
        /// <see cref="ReloadRequestAsync"/> 要能真的把「還沒 SaveChanges 成功」的本機異動蓋掉，
        /// 才能如實驗證重試迴圈——不然重試只是對著同一個已經被本機改過的物件重複疊加。
        /// </summary>
        private static readonly PropertyInfo[] RequestMutableProperties =
        [
            typeof(GuestOrderAccessRequest).GetProperty(nameof(GuestOrderAccessRequest.CodeHash))!,
            typeof(GuestOrderAccessRequest).GetProperty(nameof(GuestOrderAccessRequest.AttemptCount))!,
            typeof(GuestOrderAccessRequest).GetProperty(nameof(GuestOrderAccessRequest.SendCount))!,
            typeof(GuestOrderAccessRequest).GetProperty(nameof(GuestOrderAccessRequest.LastSentAtUtc))!,
            typeof(GuestOrderAccessRequest).GetProperty(nameof(GuestOrderAccessRequest.LockedAtUtc))!,
            typeof(GuestOrderAccessRequest).GetProperty(nameof(GuestOrderAccessRequest.ConsumedAtUtc))!,
            typeof(GuestOrderAccessRequest).GetProperty(nameof(GuestOrderAccessRequest.RevokedAtUtc))!,
        ];

        private readonly Dictionary<string, GuestOrderLookup> _ordersByLookupKey = new(StringComparer.Ordinal);
        private readonly Dictionary<long, GuestOrderLookup> _ordersById = new();
        private readonly Dictionary<Guid, object?[]> _committedRequestState = [];
        private readonly List<GuestOrderAccessToken> _pendingTokens = [];
        private long _nextRequestId = 1;
        private long _nextTokenId = 1;

        public Dictionary<Guid, GuestOrderAccessRequest> Requests { get; } = [];

        public List<GuestOrderAccessToken> Tokens { get; } = [];

        public void SeedOrder(string orderNumber, string emailNormalized, long orderId, Guid orderPublicId)
        {
            var lookup = new GuestOrderLookup(orderId, orderPublicId, orderNumber, emailNormalized);
            _ordersByLookupKey[$"{orderNumber}:{emailNormalized}"] = lookup;
            _ordersById[orderId] = lookup;
        }

        public Task<GuestOrderLookup?> FindGuestOrderAsync(
            string orderNumber, string emailNormalized, CancellationToken cancellationToken = default) =>
            Task.FromResult(_ordersByLookupKey.GetValueOrDefault($"{orderNumber}:{emailNormalized}"));

        public Task<GuestOrderLookup?> FindGuestOrderByIdAsync(
            long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_ordersById.GetValueOrDefault(orderId));

        public Task AddRequestAsync(
            GuestOrderAccessRequest request, CancellationToken cancellationToken = default)
        {
            IdProperty.SetValue(request, _nextRequestId++);
            Requests[request.PublicId] = request;
            CommitRequestState(request);
            return Task.CompletedTask;
        }

        public Task<GuestOrderAccessRequest?> FindActiveRequestAsync(
            Guid requestPublicId, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            if (!Requests.TryGetValue(requestPublicId, out var request))
            {
                return Task.FromResult<GuestOrderAccessRequest?>(null);
            }

            var isActive = nowUtc < request.ExpiresAtUtc &&
                request.ConsumedAtUtc is null &&
                request.LockedAtUtc is null &&
                request.RevokedAtUtc is null &&
                request.AttemptCount < GuestOrderAccessRequest.MaximumAttempts;

            return Task.FromResult(isActive ? request : null);
        }

        public Task AddTokenAsync(
            GuestOrderAccessToken token, CancellationToken cancellationToken = default)
        {
            // 只是「排隊等寫入」（比照 EF ChangeTracker 的 Added 狀態），成功 SaveChangesAsync
            // 才會真的出現在 Tokens——否則模擬並行衝突時，測試會看到一個「其實沒真的寫進去」
            // 的 Token，跟真實 Rollback 行為對不起來。
            IdProperty.SetValue(token, _nextTokenId++);
            _pendingTokens.Add(token);
            return Task.CompletedTask;
        }

        public Task<GuestOrderAccessTokenContext?> FindTokenByHashAsync(
            byte[] tokenHash, CancellationToken cancellationToken = default)
        {
            var token = Tokens.FirstOrDefault(t => t.TokenHash.AsSpan().SequenceEqual(tokenHash));
            if (token is null)
            {
                return Task.FromResult<GuestOrderAccessTokenContext?>(null);
            }

            var orderPublicId = _ordersById[token.OrderId].OrderPublicId;
            return Task.FromResult<GuestOrderAccessTokenContext?>(
                new GuestOrderAccessTokenContext(token, orderPublicId));
        }

        public Task<int> PurgeExpiredAsync(
            DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <summary>
        /// 設成 N，接下來 N 次 <see cref="SaveChangesAsync"/> 呼叫會模擬樂觀並行衝突
        /// （比照 EF Core RowVersion 不符時，Gateway 拋出的 <see cref="DomainProblemException"/>），
        /// 用來驗證 UseCase 的重試邏輯不會漏記、也不會讓例外原樣往外傳。
        /// </summary>
        public int SaveChangesConflictCountdown { get; set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (SaveChangesConflictCountdown > 0)
            {
                SaveChangesConflictCountdown--;
                throw DomainProblemException.Conflict(
                    DomainErrorCodes.ConcurrencyConflict, "Simulated concurrency conflict.");
            }

            if (_pendingTokens.Count > 0)
            {
                Tokens.AddRange(_pendingTokens);
                _pendingTokens.Clear();
            }

            foreach (var request in Requests.Values)
            {
                CommitRequestState(request);
            }

            return Task.CompletedTask;
        }

        public Task ReloadRequestAsync(
            GuestOrderAccessRequest request, CancellationToken cancellationToken = default)
        {
            if (_committedRequestState.TryGetValue(request.PublicId, out var values))
            {
                for (var i = 0; i < RequestMutableProperties.Length; i++)
                {
                    RequestMutableProperties[i].SetValue(request, values[i]);
                }
            }

            return Task.CompletedTask;
        }

        private void CommitRequestState(GuestOrderAccessRequest request) =>
            _committedRequestState[request.PublicId] =
                RequestMutableProperties.Select(property => property.GetValue(request)).ToArray();
    }
}
