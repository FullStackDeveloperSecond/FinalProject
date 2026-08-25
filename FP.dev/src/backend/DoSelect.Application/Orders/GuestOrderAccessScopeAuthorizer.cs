using System.Security.Claims;
using DoSelect.Application.Security;

namespace DoSelect.Application.Orders;

/// <summary>
/// GuestOrderAccess Cookie 專用 Claim 型別。定義在 Application 層（而非 Api 層的
/// <c>DoSelectClaimTypes</c>）——這裡的 <see cref="GuestOrderAccessScopeAuthorizer"/>
/// 跟 Api 層的 Cookie 核發程式碼都要用到同一組字串，Application 不能反向依賴 Api 專案。
/// </summary>
public static class GuestOrderAccessClaimTypes
{
    /// <summary>限單存取權杖明文——只在這張 Cookie 內流動，比對時重新雜湊，DB 只存 Hash。</summary>
    public const string TokenValue = "doselect:guest_order_token";
}

public abstract record GuestOrderAccessAuthorizationResult
{
    public sealed record Success(Guid OrderPublicId) : GuestOrderAccessAuthorizationResult;

    public sealed record Failure(string ErrorCode) : GuestOrderAccessAuthorizationResult;
}

/// <summary>
/// 驗證 GuestOrderAccess Cookie 對「這一筆」訂單是否仍然有效。不能只信任 Cookie 內嵌的到期
/// 時間——DEC-P264：失效由 ExpiresAtUtc、RevokedAtUtc 及安全 Policy 決定，Cookie 本身不查
/// 撤銷清單，所以每次都要打 DB 重新確認。之後訂單查詢／取消／退貨端點都應該呼叫這裡，
/// 而不是各自重寫一份 Scope 比對邏輯。
/// </summary>
public sealed class GuestOrderAccessScopeAuthorizer(
    IGuestOrderAccessGateway gateway,
    IGuestOrderAccessHasher hasher,
    IAuditLogWriter auditLogWriter,
    TimeProvider timeProvider)
{
    public async Task<GuestOrderAccessAuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal principal,
        Guid targetOrderPublicId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var rawToken = principal.FindFirst(GuestOrderAccessClaimTypes.TokenValue)?.Value;
        if (string.IsNullOrEmpty(rawToken))
        {
            return new GuestOrderAccessAuthorizationResult.Failure(GuestOrderErrorCodes.AccessExpired);
        }

        var tokenHash = hasher.HashToken(rawToken);
        var context = await gateway.FindTokenByHashAsync(tokenHash, cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        if (context is null ||
            context.Token.RevokedAtUtc.HasValue ||
            nowUtc >= context.Token.ExpiresAtUtc)
        {
            return new GuestOrderAccessAuthorizationResult.Failure(GuestOrderErrorCodes.AccessExpired);
        }

        if (context.OrderPublicId != targetOrderPublicId)
        {
            context.Token.RecordScopeViolation();
            await gateway.SaveChangesAsync(cancellationToken);
            await auditLogWriter.RecordAsync(
                new AuditLogEntryDraft(
                    ActorType: "guest",
                    ActorUserId: context.Token.PublicId.ToString(),
                    ActorPublicId: context.Token.PublicId,
                    ActorRoles: [],
                    Action: "guest_order.access.scope_violation",
                    ResourceType: "order",
                    ResourcePublicId: targetOrderPublicId,
                    Outcome: AuditLogOutcomes.Denied,
                    ErrorCode: GuestOrderErrorCodes.ScopeMismatch),
                cancellationToken);

            return new GuestOrderAccessAuthorizationResult.Failure(GuestOrderErrorCodes.ScopeMismatch);
        }

        return new GuestOrderAccessAuthorizationResult.Success(context.OrderPublicId);
    }
}
