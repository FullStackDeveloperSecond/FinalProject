namespace DoSelect.Api.Configuration;

public sealed class FeatureOptions
{
    public const string SectionName = "Features";

    public bool AiEnabled { get; set; }

    public bool EmailEnabled { get; set; }
}

public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    public bool SimulationEndpointsEnabled { get; set; }

    /// <summary>
    /// Allows the repository-managed local Demo runner to use HTTP on loopback only.
    /// The validator rejects this setting outside the Demo environment, outside the
    /// fixed localhost URL, or when the connection does not target an isolated Demo database.
    /// </summary>
    public bool AllowHttpLoopback { get; set; }
}

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool FileLoggingEnabled { get; set; } = true;
}

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}
