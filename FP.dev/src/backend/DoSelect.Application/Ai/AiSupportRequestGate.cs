namespace DoSelect.Application.Ai;

public sealed record AiSupportRequestContext(
    AiActorType ActorType,
    bool IsAuthenticated,
    AiConsentState ConsentState,
    int RemainingDailyMessages);

public sealed record AiSupportRequestDecision(
    bool MayCallModel,
    int HttpStatus,
    AiSafetyReason Reason,
    AiFallback Fallback);

public static class AiSupportRequestGate
{
    public static AiSupportRequestDecision Evaluate(AiSupportRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ActorType == AiActorType.GuestOrderScope)
        {
            return Deny(
                403,
                AiSafetyReason.MemberScopeRequired,
                AiFallback.HumanSupport);
        }

        if (context.ActorType != AiActorType.Member || !context.IsAuthenticated)
        {
            return Deny(
                401,
                AiSafetyReason.AuthenticationRequired,
                AiFallback.HumanSupport);
        }

        if (context.ConsentState != AiConsentState.Granted)
        {
            var reason = context.ConsentState == AiConsentState.Denied
                ? AiSafetyReason.ConsentDenied
                : AiSafetyReason.ConsentRequired;

            return Deny(200, reason, AiFallback.HumanSupport);
        }

        if (context.RemainingDailyMessages <= 0)
        {
            return Deny(
                429,
                AiSafetyReason.DailyQuotaExceeded,
                AiFallback.HumanSupport);
        }

        return new AiSupportRequestDecision(
            MayCallModel: true,
            HttpStatus: 200,
            AiSafetyReason.None,
            AiFallback.None);
    }

    private static AiSupportRequestDecision Deny(
        int httpStatus,
        AiSafetyReason reason,
        AiFallback fallback)
    {
        return new AiSupportRequestDecision(
            MayCallModel: false,
            httpStatus,
            reason,
            fallback);
    }
}
