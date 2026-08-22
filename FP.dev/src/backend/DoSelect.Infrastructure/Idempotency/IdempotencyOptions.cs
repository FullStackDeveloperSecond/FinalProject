namespace DoSelect.Infrastructure.Idempotency;

public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    public string ActorScopePepper { get; set; } = string.Empty;
}
