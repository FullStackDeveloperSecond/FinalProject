using DoSelect.Application.Storage;
using Microsoft.Extensions.Logging;

namespace DoSelect.Infrastructure.Storage;

/// <summary>
/// Fail-closed IFileScanner backed by Microsoft Defender's command-line scanner
/// (MpCmdRun.exe). A custom scan (-ScanType 3) is run against the staged file with
/// -DisableRemediation so a detected threat can never come back as an exit-0 "success after
/// remediation" — only a genuinely clean file returns exit code 0. Every other outcome (exit
/// code other than 0/2, the executable missing, a start failure, a timeout, or cancellation)
/// maps to Unavailable rather than being guessed as clean. Raw process output/paths are logged
/// server-side only and never returned through the port.
///
/// Process launching, executable discovery and the timeout are injected via internal
/// abstractions (<see cref="IMpCmdRunProcessRunner"/>, <see cref="IMpCmdRunExecutableLocator"/>)
/// so DoSelect.Infrastructure.Tests can exercise argument construction, exit-code mapping,
/// timeout and start-failure branches deterministically, without ever launching Defender. The
/// public constructor wires up the real Defender-backed implementations for production DI.
/// </summary>
public sealed class DefenderFileScanner : IFileScanner
{
    private const int CleanExitCode = 0;
    private const int MalwareExitCode = 2;
    private static readonly TimeSpan DefaultScanTimeout = TimeSpan.FromSeconds(45);

    private readonly ILogger<DefenderFileScanner> _logger;
    private readonly IMpCmdRunProcessRunner _processRunner;
    private readonly IMpCmdRunExecutableLocator _executableLocator;
    private readonly TimeSpan _scanTimeout;

    public DefenderFileScanner(ILogger<DefenderFileScanner> logger)
        : this(logger, new MpCmdRunProcessRunner(), new MpCmdRunExecutableLocator(), DefaultScanTimeout)
    {
    }

    internal DefenderFileScanner(
        ILogger<DefenderFileScanner> logger,
        IMpCmdRunProcessRunner processRunner,
        IMpCmdRunExecutableLocator executableLocator,
        TimeSpan scanTimeout)
    {
        _logger = logger;
        _processRunner = processRunner;
        _executableLocator = executableLocator;
        _scanTimeout = scanTimeout;
    }

    public async Task<FileScanResult> ScanAsync(string filePath, CancellationToken cancellationToken)
    {
        var executablePath = _executableLocator.Locate();
        if (executablePath is null)
        {
            _logger.LogWarning("No MpCmdRun.exe was found on this host; treating the scan as unavailable.");
            return FileScanResult.Unavailable;
        }

        var invocation = new MpCmdRunInvocation(
            executablePath,
            ["-Scan", "-ScanType", "3", "-File", filePath, "-DisableRemediation"]);

        var result = await _processRunner.RunAsync(invocation, _scanTimeout, cancellationToken);

        return result.Outcome switch
        {
            MpCmdRunProcessOutcome.StartFailed =>
                LogAndReturnUnavailable("MpCmdRun.exe failed to start."),
            MpCmdRunProcessOutcome.TimedOut =>
                LogAndReturnUnavailable($"MpCmdRun.exe scan timed out after {_scanTimeout}."),
            MpCmdRunProcessOutcome.Faulted =>
                LogAndReturnUnavailable("MpCmdRun.exe scan failed unexpectedly."),
            MpCmdRunProcessOutcome.Exited when result.ExitCode == CleanExitCode => FileScanResult.Clean,
            MpCmdRunProcessOutcome.Exited when result.ExitCode == MalwareExitCode => FileScanResult.Malware,
            MpCmdRunProcessOutcome.Exited =>
                LogAndReturnUnavailable($"MpCmdRun.exe returned an unrecognized exit code {result.ExitCode}."),
            _ => LogAndReturnUnavailable("MpCmdRun.exe scan produced an unrecognized outcome."),
        };
    }

    private FileScanResult LogAndReturnUnavailable(string message)
    {
        _logger.LogWarning(message);
        return FileScanResult.Unavailable;
    }
}
