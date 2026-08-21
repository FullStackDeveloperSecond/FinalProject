namespace DoSelect.Application.Storage;

/// <summary>
/// Fail-closed malware scan port for a staged private file. Every outcome other than a
/// confirmed clean result maps to <see cref="FileScanResult.Unavailable"/> — a scanner that
/// cannot start, times out, or returns something the adapter does not recognize is never
/// treated as clean. No raw scanner output crosses this port.
/// </summary>
public interface IFileScanner
{
    Task<FileScanResult> ScanAsync(string filePath, CancellationToken cancellationToken);
}

public enum FileScanResult
{
    Clean,
    Malware,
    Unavailable,
}
