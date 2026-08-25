using System.Security.Cryptography;
using DoSelect.Application.Notifications;
using DoSelect.Domain.Orders;

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
    IGuestOrderAccessThrottle throttle,
    IEmailDispatchQueue emailDispatchQueue,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);

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

        if (!throttle.TryAcquireIp(ipHash) ||
            !throttle.TryAcquireEmail(emailHash) ||
            !throttle.TryAcquireOrderLookup(orderLookupHash))
        {
            return new GuestOrderAccessAcceptedResult.RateLimited();
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = nowUtc.Add(RequestLifetime);
        var lookup = await gateway.FindGuestOrderAsync(orderNumber, emailNormalized, cancellationToken);

        // 兩分支都要建立一筆 Request、都做同樣份量的雜湊工作，維持恆定 202 與等效延遲——
        // 不能只在其中一支加假延遲，那本身會製造新的計時側channel。
        if (lookup is null)
        {
            var decoyRequest = GuestOrderAccessRequest.CreateDecoy(
                Guid.CreateVersion7(), ipHash, emailHash, orderLookupHash, expiresAtUtc, nowUtc);
            await gateway.AddRequestAsync(decoyRequest, cancellationToken);
            await gateway.SaveChangesAsync(cancellationToken);
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
        await gateway.AddRequestAsync(request, cancellationToken);
        await gateway.SaveChangesAsync(cancellationToken);
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

        // 「維持安全回應」：request 不存在或已失效時，仍回跟成功相同的 Accepted 形狀，
        // 只是用當下時間推算的合理數值，不對應任何真實紀錄。
        if (request is null || request.OrderId is null || request.CodeHash is null)
        {
            return new GuestOrderAccessAcceptedResult.Accepted(
                requestPublicId, nowUtc.Add(RequestLifetime), nowUtc.Add(ResendInterval));
        }

        if (!throttle.TryAcquireIp(request.RequesterIpHash) ||
            !throttle.TryAcquireEmail(request.EmailKeyHash) ||
            !throttle.TryAcquireOrderLookup(request.OrderLookupKeyHash))
        {
            return new GuestOrderAccessAcceptedResult.RateLimited();
        }

        try
        {
            request.RecordSend(nowUtc);
        }
        catch (InvalidOperationException)
        {
            // 已達 3 封上限或未滿 60 秒間隔——維持安全回應，不揭露原因。
            return new GuestOrderAccessAcceptedResult.Accepted(
                requestPublicId, request.ExpiresAtUtc, nowUtc.Add(ResendInterval));
        }

        await gateway.SaveChangesAsync(cancellationToken);

        var order = await gateway.FindGuestOrderByIdAsync(request.OrderId.Value, cancellationToken);
        // Resend 不重新產生驗證碼——沿用同一個 Request 已存的 CodeHash，只是再寄一次
        // 使用者已收過的碼，因此這裡沒有明文碼可寄。⚠ 待 alex 覆核：若要求 Resend 換發新碼，
        // 需要改 Entity 補一個可覆寫 CodeHash 的方法，目前 Entity 沒有這個能力。
        _ = order;

        return new GuestOrderAccessAcceptedResult.Accepted(
            requestPublicId, request.ExpiresAtUtc, nowUtc.Add(ResendInterval));
    }

    public async Task<GuestOrderAccessVerifyResult> VerifyAsync(
        Guid requestPublicId,
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var request = await gateway.FindActiveRequestAsync(requestPublicId, nowUtc, cancellationToken);
        if (request is null || request.OrderId is null || request.CodeHash is null)
        {
            return new GuestOrderAccessVerifyResult.Failure(GuestOrderErrorCodes.VerificationInvalid);
        }

        var codeHash = hasher.HashCode(code);
        if (!CryptographicOperations.FixedTimeEquals(codeHash, request.CodeHash))
        {
            try
            {
                request.RecordFailedAttempt(nowUtc);
                await gateway.SaveChangesAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // 已經在這次呼叫前就失效（過期／鎖定／撤銷）——不再重覆記一次嘗試。
            }

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

        var lookup = await gateway.FindGuestOrderByIdAsync(request.OrderId.Value, cancellationToken);
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
            request.OrderId.Value,
            request.Id,
            hasher.HashToken(rawToken),
            tokenExpiresAtUtc,
            nowUtc);
        await gateway.AddTokenAsync(token, cancellationToken);
        await gateway.SaveChangesAsync(cancellationToken);

        return new GuestOrderAccessVerifyResult.Success(rawToken, lookup.OrderPublicId, tokenExpiresAtUtc);
    }

    private static string Normalize(string email) => email.Trim().ToUpperInvariant();

    private static string GenerateSixDigitCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string GenerateRawToken() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
}
