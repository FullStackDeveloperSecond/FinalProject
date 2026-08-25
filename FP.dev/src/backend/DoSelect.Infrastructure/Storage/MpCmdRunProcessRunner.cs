using System.Diagnostics;

namespace DoSelect.Infrastructure.Storage;

/// <summary>
/// A single MpCmdRun.exe invocation: the resolved executable path and the exact argument list to
/// pass (no shell interpolation — <see cref="ProcessStartInfo.ArgumentList"/> is used verbatim).
/// </summary>
internal readonly record struct MpCmdRunInvocation(string ExecutablePath, IReadOnlyList<string> Arguments);

internal enum MpCmdRunProcessOutcome
{
    /// <summary>The process started and exited on its own; <see cref="MpCmdRunProcessResult.ExitCode"/> is meaningful.</summary>
    Exited,

    /// <summary>The process started but did not exit within the configured timeout and was killed.</summary>
    TimedOut,

    /// <summary>The process could not be started at all.</summary>
    StartFailed,

    /// <summary>The process started but an unexpected error occurred while waiting for it.</summary>
    Faulted,
}

internal sealed record MpCmdRunProcessResult(MpCmdRunProcessOutcome Outcome, int ExitCode);

/// <summary>
/// Runs an MpCmdRun.exe invocation and reports how it ended, without interpreting exit codes —
/// exit-code-to-scan-result mapping stays in <see cref="DefenderFileScanner"/> so it can be unit
/// tested against a fake runner without ever launching a real process.
/// </summary>
internal interface IMpCmdRunProcessRunner
{
    /// <summary>
    /// Caller cancellation (<paramref name="cancellationToken"/>) propagates as
    /// <see cref="OperationCanceledException"/>; the runner's own <paramref name="timeout"/>
    /// elapsing does not throw and instead yields <see cref="MpCmdRunProcessOutcome.TimedOut"/>.
    /// </summary>
    Task<MpCmdRunProcessResult> RunAsync(
        MpCmdRunInvocation invocation,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

/// <summary>
/// Production <see cref="IMpCmdRunProcessRunner"/>: launches the process with no shell
/// interpolation, drains a bounded amount of stdout/stderr so the child never blocks on a full
/// pipe buffer (output content itself is never surfaced), and kills the full process tree on
/// timeout or caller cancellation.
/// </summary>
internal sealed class MpCmdRunProcessRunner : IMpCmdRunProcessRunner
{
    private const int MaxCapturedOutputChars = 4096;

    public async Task<MpCmdRunProcessResult> RunAsync(
        MpCmdRunInvocation invocation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = false };

        try
        {
            if (!process.Start())
            {
                return new MpCmdRunProcessResult(MpCmdRunProcessOutcome.StartFailed, ExitCode: -1);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new MpCmdRunProcessResult(MpCmdRunProcessOutcome.StartFailed, ExitCode: -1);
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var stdOutTask = ReadBoundedAsync(process.StandardOutput, linkedCts.Token);
            var stdErrTask = ReadBoundedAsync(process.StandardError, linkedCts.Token);
            await process.WaitForExitAsync(linkedCts.Token);
            await Task.WhenAll(stdOutTask, stdErrTask);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            // Our own timeout elapsed, not the caller's cancellation.
            KillProcessTree(process);
            return new MpCmdRunProcessResult(MpCmdRunProcessOutcome.TimedOut, ExitCode: -1);
        }
        catch (Exception)
        {
            KillProcessTree(process);
            return new MpCmdRunProcessResult(MpCmdRunProcessOutcome.Faulted, ExitCode: -1);
        }

        return new MpCmdRunProcessResult(MpCmdRunProcessOutcome.Exited, process.ExitCode);
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // The process may have already exited between the check and the kill attempt.
        }
    }

    private static async Task ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[1024];
        var captured = 0;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            captured += read;
            if (captured >= MaxCapturedOutputChars)
            {
                // Keep draining the pipe so the child process never blocks on a full buffer, but
                // stop accumulating — output content is never surfaced through this port anyway.
            }
        }
    }
}

/// <summary>Discovers the MpCmdRun.exe executable path, or null if none can be found.</summary>
internal interface IMpCmdRunExecutableLocator
{
    string? Locate();
}

/// <summary>
/// Production <see cref="IMpCmdRunExecutableLocator"/>: prefers the newest Defender platform
/// binary under ProgramData, falling back to the fixed ProgramFiles install path.
/// </summary>
internal sealed class MpCmdRunExecutableLocator : IMpCmdRunExecutableLocator
{
    public string? Locate()
    {
        var platformRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft",
            "Windows Defender",
            "Platform");

        if (Directory.Exists(platformRoot))
        {
            var candidate = Directory.GetDirectories(platformRoot)
                .Select(dir => new DirectoryInfo(dir))
                .OrderByDescending(dir => dir.LastWriteTimeUtc)
                .Select(dir => Path.Combine(dir.FullName, "MpCmdRun.exe"))
                .FirstOrDefault(File.Exists);

            if (candidate is not null)
            {
                return candidate;
            }
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Windows Defender",
            "MpCmdRun.exe");
        return File.Exists(fallback) ? fallback : null;
    }
}
