namespace DoSelect.Api.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string DataRoot { get; set; } = Path.Combine(Path.GetTempPath(), "DoSelectData");
}

public sealed class FeatureOptions
{
    public const string SectionName = "Features";

    public bool AiEnabled { get; set; }

    public bool EmailEnabled { get; set; }
}

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gpt-5.6-luna";
}

public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    public bool SimulationEndpointsEnabled { get; set; }
}

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool FileLoggingEnabled { get; set; } = true;
}
