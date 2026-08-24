namespace DoSelect.Application.Storage;

/// <summary>
/// The single validated source of the private storage root shared by every local storage
/// adapter (support attachments, private files, product images). Binds from configuration
/// section "Storage" and falls back to a temp-rooted default when unset; <c>ValidateOnStart</c>
/// (see DoSelect.Api's ConfigurationValidationExtensions) enforces that whatever value is bound
/// is an absolute, non-root path.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string DataRoot { get; set; } = Path.Combine(Path.GetTempPath(), "DoSelectData");
}
