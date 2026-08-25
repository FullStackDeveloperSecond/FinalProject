using DoSelect.Application.Storage;
using DoSelect.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace DoSelect.Infrastructure.Tests;

public sealed class DefenderFileScannerTests
{
    [Theory]
    [InlineData(0, FileScanResult.Clean)]
    [InlineData(2, FileScanResult.Malware)]
    [InlineData(1, FileScanResult.Unavailable)]
    [InlineData(99, FileScanResult.Unavailable)]
    public async Task ExitedProcess_UsesFailClosedMappingAndExactSafeArguments(int exitCode, FileScanResult expected)
    {
        var runner = new RecordingRunner(new(MpCmdRunProcessOutcome.Exited, exitCode));
        var scanner = CreateScanner(runner);
        var target = Path.Combine(Path.GetTempPath(), $"scan target ; {Guid.NewGuid():N}.png");

        var result = await scanner.ScanAsync(target, default);

        Assert.Equal(expected, result);
        var invocation = Assert.Single(runner.Invocations);
        Assert.Equal("C:\\fake-defender\\MpCmdRun.exe", invocation.ExecutablePath);
        Assert.Equal(["-Scan", "-ScanType", "3", "-File", target, "-DisableRemediation"], invocation.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(7), runner.Timeouts.Single());
        Assert.Equal(target, invocation.Arguments[4]);
    }

    [Theory]
    [InlineData((int)MpCmdRunProcessOutcome.StartFailed)]
    [InlineData((int)MpCmdRunProcessOutcome.TimedOut)]
    [InlineData((int)MpCmdRunProcessOutcome.Faulted)]
    public async Task NonExitOutcome_IsUnavailable(int outcomeValue)
    {
        var outcome = (MpCmdRunProcessOutcome)outcomeValue;
        var scanner = CreateScanner(new RecordingRunner(new(outcome, -1)));
        Assert.Equal(FileScanResult.Unavailable, await scanner.ScanAsync("task-owned.tmp", default));
    }

    [Fact]
    public async Task MissingExecutable_IsUnavailableWithoutStartingProcess()
    {
        var runner = new RecordingRunner(new(MpCmdRunProcessOutcome.Exited, 0));
        var scanner = new DefenderFileScanner(
            NullLogger<DefenderFileScanner>.Instance, runner, new FixedLocator(null), TimeSpan.FromSeconds(7));

        Assert.Equal(FileScanResult.Unavailable, await scanner.ScanAsync("task-owned.tmp", default));
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesInsteadOfBecomingUnavailable()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var runner = new RecordingRunner(new(MpCmdRunProcessOutcome.Exited, 0), propagateCancellation: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateScanner(runner).ScanAsync("task-owned.tmp", cts.Token));
    }

    private static DefenderFileScanner CreateScanner(IMpCmdRunProcessRunner runner) => new(
        NullLogger<DefenderFileScanner>.Instance,
        runner,
        new FixedLocator("C:\\fake-defender\\MpCmdRun.exe"),
        TimeSpan.FromSeconds(7));

    private sealed class FixedLocator(string? path) : IMpCmdRunExecutableLocator
    {
        public string? Locate() => path;
    }

    private sealed class RecordingRunner(MpCmdRunProcessResult result, bool propagateCancellation = false)
        : IMpCmdRunProcessRunner
    {
        public List<MpCmdRunInvocation> Invocations { get; } = [];
        public List<TimeSpan> Timeouts { get; } = [];

        public Task<MpCmdRunProcessResult> RunAsync(
            MpCmdRunInvocation invocation,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Invocations.Add(invocation);
            Timeouts.Add(timeout);
            if (propagateCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            return Task.FromResult(result);
        }
    }
}
