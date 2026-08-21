using System.Diagnostics;
using System.ComponentModel;
using DoSelect.Application.Files;

namespace DoSelect.Infrastructure.Files;

public sealed class MicrosoftDefenderFileScanner : IFileScanner
{
    private const string ScannerName = "Microsoft Defender Antivirus";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly Func<string?> _executableResolver;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeout;

    public MicrosoftDefenderFileScanner()
        : this(ResolveExecutablePath, TimeProvider.System, DefaultTimeout)
    {
    }

    internal MicrosoftDefenderFileScanner(
        Func<string?> executableResolver,
        TimeProvider timeProvider,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(executableResolver);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _executableResolver = executableResolver;
        _timeProvider = timeProvider;
        _timeout = timeout;
    }

    public async Task<FileScanResult> ScanAsync(
        string quarantinedFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quarantinedFilePath);
        cancellationToken.ThrowIfCancellationRequested();

        var startedAt = _timeProvider.GetUtcNow();
        string? executablePath;
        try
        {
            executablePath = _executableResolver();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Unavailable(startedAt, "scanner_not_found");
        }

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return Unavailable(startedAt, "scanner_not_found");
        }

        if (!File.Exists(quarantinedFilePath))
        {
            return Unavailable(startedAt, "scan_target_unavailable");
        }

        Task<string>? standardOutputTask = null;
        Task<string>? standardErrorTask = null;
        using var process = new Process
        {
            StartInfo = CreateStartInfo(executablePath, Path.GetFullPath(quarantinedFilePath)),
        };

        try
        {
            if (!process.Start())
            {
                return Unavailable(startedAt, "scanner_start_failed");
            }

            standardOutputTask = process.StandardOutput.ReadToEndAsync();
            standardErrorTask = process.StandardError.ReadToEndAsync();
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_timeout);

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryTerminate(process);
                await DrainOutputAsync(standardOutputTask, standardErrorTask);
                return Unavailable(startedAt, "scanner_timeout");
            }

            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;
            var completedAt = _timeProvider.GetUtcNow();
            if (process.ExitCode == 0)
            {
                return new FileScanResult(
                    FileScanOutcome.Clean,
                    ScannerName,
                    startedAt,
                    completedAt,
                    process.ExitCode);
            }

            var combinedOutput = string.Concat(standardOutput, Environment.NewLine, standardError);
            if (IndicatesMalware(combinedOutput))
            {
                return new FileScanResult(
                    FileScanOutcome.MalwareDetected,
                    ScannerName,
                    startedAt,
                    completedAt,
                    process.ExitCode);
            }

            return new FileScanResult(
                FileScanOutcome.Unavailable,
                ScannerName,
                startedAt,
                completedAt,
                process.ExitCode,
                FailureCode: "scanner_result_unknown");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            if (standardOutputTask is not null && standardErrorTask is not null)
            {
                await DrainOutputAsync(standardOutputTask, standardErrorTask);
            }

            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            TryTerminate(process);
            return Unavailable(startedAt, "scanner_process_failed");
        }
    }

    internal static bool IndicatesMalware(string scannerOutput)
    {
        if (string.IsNullOrWhiteSpace(scannerOutput))
        {
            return false;
        }

        string[] detectionMarkers =
        [
            "threat detected",
            "threats found",
            "malware detected",
            "virus detected",
            "detected threat",
            "偵測到威脅",
            "發現威脅",
            "检测到威胁",
            "发现威胁",
        ];

        return detectionMarkers.Any(marker =>
            scannerOutput.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    internal static string? ResolveExecutablePath()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var platformRoot = Path.Combine(
            programData,
            "Microsoft",
            "Windows Defender",
            "Platform");
        try
        {
            if (Directory.Exists(platformRoot))
            {
                var platformExecutable = Directory
                    .EnumerateDirectories(platformRoot)
                    .Select(directory => new DirectoryInfo(directory))
                    .OrderByDescending(directory => directory.LastWriteTimeUtc)
                    .Select(directory => Path.Combine(directory.FullName, "MpCmdRun.exe"))
                    .FirstOrDefault(File.Exists);
                if (platformExecutable is not null)
                {
                    return platformExecutable;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var fallbackExecutable = Path.Combine(
            programFiles,
            "Windows Defender",
            "MpCmdRun.exe");
        return File.Exists(fallbackExecutable) ? fallbackExecutable : null;
    }

    private static ProcessStartInfo CreateStartInfo(string executablePath, string filePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-Scan");
        startInfo.ArgumentList.Add("-ScanType");
        startInfo.ArgumentList.Add("3");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(filePath);
        startInfo.ArgumentList.Add("-DisableRemediation");
        return startInfo;
    }

    private FileScanResult Unavailable(DateTimeOffset startedAt, string failureCode) =>
        new(
            FileScanOutcome.Unavailable,
            ScannerName,
            startedAt,
            _timeProvider.GetUtcNow(),
            FailureCode: failureCode);

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task DrainOutputAsync(params Task<string>[] outputTasks)
    {
        try
        {
            await Task.WhenAll(outputTasks);
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
