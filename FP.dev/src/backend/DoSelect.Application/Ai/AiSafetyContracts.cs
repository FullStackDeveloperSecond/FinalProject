namespace DoSelect.Application.Ai;

public enum AiActorType
{
    Anonymous = 0,
    Member = 1,
    GuestOrderScope = 2,
}

public enum AiConsentState
{
    Missing = 0,
    Granted = 1,
    Denied = 2,
}

public enum AiSafetyReason
{
    None = 0,
    AuthenticationRequired = 1,
    MemberScopeRequired = 2,
    ConsentRequired = 3,
    ConsentDenied = 4,
    DailyQuotaExceeded = 5,
    SecretDetected = 6,
    PersonalDataDetected = 7,
    ResourceOwnershipMismatch = 8,
    SemanticKeyNotAllowed = 9,
    InvalidBudgetRange = 10,
    InvalidSearchIntent = 11,
}

public enum AiFallback
{
    None = 0,
    KeywordSearch = 1,
    HumanSupport = 2,
}

public enum AiProjectionStatus
{
    Allowed = 0,
    Forbidden = 1,
    UnsafeContent = 2,
}

public enum AiContentTrust
{
    UntrustedUserInput = 0,
    UntrustedData = 1,
}

public enum AiFeature
{
    ProductSearch = 0,
    Support = 1,
}

public enum AiFailureKind
{
    Timeout = 0,
    RateLimited = 1,
    TemporaryServiceError = 2,
    InvalidSchema = 3,
    TruncatedOutput = 4,
    Refusal = 5,
}
