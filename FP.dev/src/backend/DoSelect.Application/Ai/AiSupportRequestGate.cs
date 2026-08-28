namespace DoSelect.Application.Ai;

public sealed record AiSupportRequestContext(
    AiActorType ActorType,
    bool IsAuthenticated,
    AiConsentState ConsentState,
    int RemainingDailyMessages);

public sealed record AiSupportRequestDecision(
    bool MayCallModel,
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
                AiSafetyReason.MemberScopeRequired,
                AiFallback.HumanSupport);
        }

        if (context.ActorType != AiActorType.Member || !context.IsAuthenticated)
        {
            return Deny(
                AiSafetyReason.AuthenticationRequired,
                AiFallback.HumanSupport);
        }

        if (context.ConsentState == AiConsentState.Unavailable)
        {
            return Deny(
                AiSafetyReason.ServiceUnavailable,
                AiFallback.HumanSupport);
        }

        if (context.ConsentState != AiConsentState.Granted)
        {
            var reason = context.ConsentState == AiConsentState.Denied
                ? AiSafetyReason.ConsentDenied
                : AiSafetyReason.ConsentRequired;

            return Deny(reason, AiFallback.HumanSupport);
        }

        if (context.RemainingDailyMessages <= 0)
        {
            return Deny(
                AiSafetyReason.DailyQuotaExceeded,
                AiFallback.HumanSupport);
        }

        return new AiSupportRequestDecision(
            MayCallModel: true,
            AiSafetyReason.None,
            AiFallback.None);
    }

    private static AiSupportRequestDecision Deny(
        AiSafetyReason reason,
        AiFallback fallback)
    {
        return new AiSupportRequestDecision(
            MayCallModel: false,
            reason,
            fallback);
    }
}
