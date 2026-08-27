using System.Security.Cryptography;
using DoSelect.Application.Common;
using DoSelect.Application.Notifications;
using DoSelect.Domain.Orders;
using Microsoft.Extensions.Options;

namespace DoSelect.Application.Orders;

public abstract record GuestOrderAccessAcceptedResult
{
    public sealed record Accepted(
        Guid RequestPublicId,
        DateTime ExpiresAtUtc,
        DateTime? ResendAvailableAtUtc) : GuestOrderAccessAcceptedResult;

    public sealed record RateLimited : GuestOrderAccessAcceptedResult;
}

public abstract record GuestOrderAccessVerifyResult
{
    public sealed record Success(
        string RawToken,
        Guid OrderPublicId,
        DateTime ExpiresAtUtc) : GuestOrderAccessVerifyResult;

    public sealed record Failure(string ErrorCode) : GuestOrderAccessVerifyResult;
}

/// <summary>
/// 訪客查單兩階段存取（DEC-BATCH-013／Haru-會員登入訂單與訪客存取最終Schema.md 第 5 節）：
/// 訂單編號＋Email → 恆定 202（不論訂單是否存在，兩分支都建立一筆 Request 並做等量的雜湊／
/// 寫入工作，維持等效延遲）→ 六位數碼寄信 → 驗證成功後核發 30 分鐘、可重複使用的限單存取
/// 權杖。呼叫端（Api 層）負責把 <see cref="GuestOrderAccessVerifyResult.Success.RawToken"/>
/// 放進 HttpOnly Cookie Claim——這個類別本身不接觸 ASP.NET Authentication。
/// </summary>
public sealed class GuestOrderAccessUseCase(
    IGuestOrderAccessGateway gateway,
    IGuestOrderAccessHasher hasher,
    IOptions<RateLimitOptions> rateLimitOptions,
    IEmailDispatchQueue emailDispatchQueue,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 錯誤驗證碼的樂觀並行衝突重試上限。只保護「每一次猜測都要計數」這件事——
    /// 正常情況下平行猜測的次數遠小於這個值,超過只在極端壓力下發生,超過就安全失敗，
    /// 不冒著無限重試或 500 的風險。
    /// </summary>
    private const int MaxFailedAttemptConcurrencyRetries = 5;

    public async Task<GuestOrderAccessAcceptedResult> RequestAccessAsync(
        string orderNumber,
        string email,
        string requesterIp,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(requesterIp);

        var emailNormalized = Normalize(email);
        var ipHash = hasher.HashIp(requesterIp);
        var emailHash = hasher.HashEmail(emailNormalized);
        var orderLookupHash = hasher.HashOrderLookup(orderNumber, emailNormalized);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = nowUtc.Add(RequestLifetime);
        var window = CreateWindow(ipHash, emailHash, orderLookupHash, nowUtc);
        var lookup = await gateway.FindGuestOrderAsync(orderNumber, emailNormalized, cancellationToken);

        // 兩分支都要建立一筆 Request、都呼叫同一個 Gateway 方法（同樣的三 Scope 原子核對＋
        // 寫入），維持恆定 202／429 與等效延遲——不能只在其中一支加假延遲，那本身會製造
        // 新的計時側channel。
        if (lookup is null)
        {
            var decoyRequest = GuestOrderAccessRequest.CreateDecoy(
                Guid.CreateVersion7(), ipHash, emailHash, orderLookupHash, expiresAtUtc, nowUtc);
            // Decoy 不會真的寄信，但仍須記錄等價的初次寄送狀態；否則它可以立即重寄，
            // 有效 Request 卻要等 60 秒，RequestPublicId 是否改變會成為訂單存在性 Oracle。
            decoyRequest.RecordSend(nowUtc);
            var created = await TryCreateRequestWithRetryAsync(window, decoyRequest, cancellationToken);
            if (!created)
            {
                return new GuestOrderAccessAcceptedResult.RateLimited();
            }

            return new GuestOrderAccessAcceptedResult.Accepted(
                decoyRequest.PublicId, expiresAtUtc, nowUtc.Add(ResendInterval));
        }

        var code = GenerateSixDigitCode();
        var codeHash = hasher.HashCode(code);
        var request = GuestOrderAccessRequest.CreateValid(
            Guid.CreateVersion7(),
            lookup.OrderId,
            codeHash,
            ipHash,
            emailHash,
            orderLookupHash,
            expiresAtUtc,
            nowUtc);
        // 初次寄送本身也要計入 SendCount／LastSentAtUtc，否則規格「最多 3 封」會被繞過成
        // 「初次寄送＋3 次 resend」共 4 封。
        request.RecordSend(nowUtc);
        var accepted = await TryCreateRequestWithRetryAsync(window, request, cancellationToken);
        if (!accepted)
        {
            return new GuestOrderAccessAcceptedResult.RateLimited();
        }

        emailDispatchQueue.Enqueue(GuestOrderAccessEmailComposer.Compose(email, orderNumber, code));

        return new GuestOrderAccessAcceptedResult.Accepted(
            request.PublicId, expiresAtUtc, nowUtc.Add(ResendInterval));
    }

    public async Task<GuestOrderAccessAcceptedResult> ResendAsync(
        Guid requestPublicId,
        string requesterIp,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requesterIp);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var request = await gateway.FindActiveRequestAsync(requestPublicId, nowUtc, cancellationToken);
        var ipHash = hasher.HashIp(requesterIp);

        // Request 根本不存在（不是 Decoy，是查無此 PublicId 或已完全失效）：沒有任何儲存的
        // Scope Hash 可用，也沒有真實 Row 可延續，只能核對目前呼叫者 IP 這一個 Scope。結果
        // 一定要照實回應且確實持久消耗——不能只唯讀計數既有筆數卻不寫入，否則攻擊者能用
        // 隨機 GUID 無限打 DB lookup 而永遠拿到 202，等於這條公開分支沒有實際 abuse
        // protection（review #1）。用哨兵 Row 只讓 IP Scope 的索引可數；Email／OrderLookup
        // 兩個 Scope 因為沒有可信 Hash 可用，無法計數，可以接受略過。
        if (request is null)
        {
            var sentinelRequest = GuestOrderAccessRequest.CreateUnknownResendAttempt(
                Guid.CreateVersion7(), ipHash, nowUtc.Add(RequestLifetime), nowUtc);
            var withinIpLimit = await TryRecordUnknownResendAttemptWithRetryAsync(
                ipHash, sentinelRequest, cancellationToken);
            if (!withinIpLimit)
            {
                return new GuestOrderAccessAcceptedResult.RateLimited();
            }

            return new GuestOrderAccessAcceptedResult.Accepted(
                requestPublicId, nowUtc.Add(RequestLifetime), nowUtc.Add(ResendInterval));
        }

        // A1：同一張 Challenge 原地換碼，PublicId 維持穩定；同一交易新增一筆不可驗證的
        // rate-limit event，讓這次呼叫的 IP 與原 Challenge 的 Email／OrderLookup 三個 Scope
        // 都能被既有索引計數。有效與 Decoy 都走相同交易，避免存在性 Oracle。
        var isDecoy = request.OrderId is null || request.CodeHash is null;
        var code = GenerateSixDigitCode();
        var codeHash = hasher.HashCode(code);
        var window = CreateWindow(ipHash, request.EmailKeyHash, request.OrderLookupKeyHash, nowUtc);

        var recorded = false;
        for (var attempt = 0; ; attempt++)
        {
            var rateLimitEvent = GuestOrderAccessRequest.CreateResendRateLimitEvent(
                Guid.CreateVersion7(),
                ipHash,
                request.EmailKeyHash,
                request.OrderLookupKeyHash,
                AsUtc(request.ExpiresAtUtc),
                nowUtc);

            try
            {
                recorded = await gateway.TryRecordResendWithinRateLimitAsync(
                    window,
                    request,
                    rateLimitEvent,
                    isDecoy ? null : codeHash,
                    nowUtc,
                    cancellationToken);
                break;
            }
            catch (InvalidOperationException)
            {
                // 未滿 60 秒、已達三封上限，或平行重寄輸家 reload 後看到贏家的寄送狀態：
                // 維持相同 202 形狀與同一個穩定 RequestPublicId，不新增限流事件。
                return new GuestOrderAccessAcceptedResult.Accepted(
                    request.PublicId, AsUtc(request.ExpiresAtUtc), nowUtc.Add(ResendInterval));
            }
            catch (DomainProblemException exception)
                when (exception.Code == DomainErrorCodes.ConcurrencyConflict)
            {
                // SQL Server 死結或 RowVersion 衝突：重新載入穩定 Request，再重跑寄送資格。
                // 若另一個重寄已成功，本輪會被 60 秒間隔擋下，不會寫第二筆限流事件。
                if (attempt >= MaxFailedAttemptConcurrencyRetries)
                {
                    return new GuestOrderAccessAcceptedResult.RateLimited();
                }

                await gateway.ReloadRequestAsync(request, cancellationToken);
            }
        }

        if (!recorded)
        {
            return new GuestOrderAccessAcceptedResult.RateLimited();
        }

        if (!isDecoy)
        {
            var order = await gateway.FindGuestOrderByIdAsync(request.OrderId!.Value, cancellationToken);
            if (order is not null && !string.IsNullOrWhiteSpace(order.GuestEmailNormalized))
            {
                emailDispatchQueue.Enqueue(GuestOrderAccessEmailComposer.Compose(
                    order.GuestEmailNormalized, order.OrderNumber, code));
            }
        }

        return new GuestOrderAccessAcceptedResult.Accepted(
            request.PublicId, AsUtc(request.ExpiresAtUtc), nowUtc.Add(ResendInterval));
    }

    public async Task<GuestOrderAccessVerifyResult> VerifyAsync(
        Guid requestPublicId,
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var request = await gateway.FindActiveRequestAsync(requestPublicId, nowUtc, cancellationToken);
        if (request is null)
        {
            return new GuestOrderAccessVerifyResult.Failure(GuestOrderErrorCodes.VerificationInvalid);
        }

        var codeHash = hasher.HashCode(code);
        var codeMatches = request.OrderId is not null &&
            request.CodeHash is not null &&
            CryptographicOperations.FixedTimeEquals(codeHash, request.CodeHash);
        if (!codeMatches)
        {
            // Read-Modify-Write 在平行錯碼下會遺失更新：兩個請求可能讀到同一個 AttemptCount，
            // 各自 +1 存回去，RowVersion 只保護「其中一個先存成功」，另一個會拋並行衝突而不是
            // 靜靜蓋過去——靠這裡重新載入最新版本、重算一次，確保每一次猜測都確實被計數，
            // 第五次也才會可靠地原子鎖定，而不是被併發吃掉。
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    request.RecordFailedAttempt(nowUtc);
                    await gateway.SaveChangesAsync(cancellationToken);
                    break;
                }
                catch (InvalidOperationException)
                {
                    // 已經在這次呼叫前就失效（過期／鎖定／撤銷）——不再重覆記一次嘗試。
                    break;
                }
                catch (DomainProblemException exception)
                    when (exception.Code == DomainErrorCodes.ConcurrencyConflict)
                {
                    if (attempt >= MaxFailedAttemptConcurrencyRetries)
                    {
                        // 重試次數用盡仍持續衝突——安全放棄，維持標準失敗回應，
                        // 不讓例外繼續往上傳變成非預期的錯誤形狀。
                        break;
                    }

                    await gateway.ReloadRequestAsync(request, cancellationToken);
                    nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                }
            }

            return new GuestOrderAccessVerifyResult.Failure(GuestOrderErrorCodes.VerificationInvalid);
        }

        // codeMatches 為 true 時必定是有效 Request；保留明確 guard 讓 nullable flow 與未來修改都
        // 安全失敗，不讓不完整資料核發 Token。
        if (request.OrderId is not long orderId)
        {
            return new GuestOrderAccessVerifyResult.Failure(GuestOrderErrorCodes.VerificationInvalid);
        }

        try
        {
            request.Consume(nowUtc);
        }
        catch (InvalidOperationException)
        {
            return new GuestOrderAccessVerifyResult.Failure(GuestOrderErrorCodes.VerificationInvalid);
        }

        var lookup = await gateway.FindGuestOrderByIdAsync(orderId, cancellationToken);
        if (lookup is null)
        {
            // 訂單在 Challenge 有效期間被刪除／不再是訪客訂單——理論上不會發生，
            // 保守起見仍當作驗證失敗處理，不核發權杖。
            await gateway.SaveChangesAsync(cancellationToken);
            return new GuestOrderAccessVerifyResult.Failure(GuestOrderErrorCodes.VerificationInvalid);
        }

        var rawToken = GenerateRawToken();
        var tokenExpiresAtUtc = nowUtc.Add(TokenLifetime);
        var token = new GuestOrderAccessToken(
            Guid.CreateVersion7(),
            orderId,
            request.Id,
            hasher.HashToken(rawToken),
            tokenExpiresAtUtc,
            nowUtc);
        await gateway.AddTokenAsync(token, cancellationToken);

        try
        {
            await gateway.SaveChangesAsync(cancellationToken);
        }
        catch (DomainProblemException exception) when (exception.Code == DomainErrorCodes.ConcurrencyConflict)
        {
            // 平行的另一個正確碼已經先消耗掉這個 Request（同一張 Challenge 只能核發一個
            // Token）——這裡永遠是輸的那一邊，直接安全失敗，不重新嘗試核發第二個 Token。
            return new GuestOrderAccessVerifyResult.Failure(GuestOrderErrorCodes.VerificationInvalid);
        }

        return new GuestOrderAccessVerifyResult.Success(rawToken, lookup.OrderPublicId, tokenExpiresAtUtc);
    }

    /// <summary>
    /// 首次建立（Decoy／有效皆同）的 <paramref name="newRequest"/>
    /// 的欄位不依賴任何會變動的既有資料——遇到 SQL Server 死結／並行衝突，直接用同一個
    /// Entity 重跑整段交易即可，不需要像 <see cref="ResendAsync"/> 那樣重新載入、重算資格。
    /// </summary>
    private async Task<bool> TryCreateRequestWithRetryAsync(
        GuestOrderAccessRateLimitWindow window,
        GuestOrderAccessRequest newRequest,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await gateway.TryCreateRequestWithinRateLimitAsync(
                    window, newRequest, cancellationToken);
            }
            catch (DomainProblemException exception)
                when (exception.Code == DomainErrorCodes.ConcurrencyConflict)
            {
                if (attempt >= MaxFailedAttemptConcurrencyRetries)
                {
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// 查無 PublicId／已完全失效的 Resend 呼叫專用：哨兵 Row 的欄位不依賴任何既有資料，
    /// 遇到 SQL Server 死結／並行衝突直接用同一個 Entity 重跑整段交易即可。
    /// </summary>
    private async Task<bool> TryRecordUnknownResendAttemptWithRetryAsync(
        byte[] ipHash,
        GuestOrderAccessRequest sentinelRequest,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await gateway.TryRecordUnknownResendAttemptAsync(
                    ipHash,
                    rateLimitOptions.Value.GuestOrderAccessIpPermitLimit,
                    WindowStartUtc(sentinelRequest.CreatedAtUtc),
                    sentinelRequest,
                    cancellationToken);
            }
            catch (DomainProblemException exception)
                when (exception.Code == DomainErrorCodes.ConcurrencyConflict)
            {
                if (attempt >= MaxFailedAttemptConcurrencyRetries)
                {
                    return false;
                }
            }
        }
    }

    private DateTime WindowStartUtc(DateTime nowUtc) =>
        nowUtc.Add(-TimeSpan.FromMinutes(rateLimitOptions.Value.GuestOrderAccessWindowMinutes));

    private GuestOrderAccessRateLimitWindow CreateWindow(
        byte[] ipHash, byte[] emailHash, byte[] orderLookupHash, DateTime nowUtc) =>
        new(
            ipHash,
            rateLimitOptions.Value.GuestOrderAccessIpPermitLimit,
            emailHash,
            rateLimitOptions.Value.GuestOrderAccessEmailPermitLimit,
            orderLookupHash,
            rateLimitOptions.Value.GuestOrderAccessOrderLookupPermitLimit,
            WindowStartUtc(nowUtc));

    private static string Normalize(string email) => email.Trim().ToUpperInvariant();

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string GenerateSixDigitCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string GenerateRawToken() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
}
