using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Runs one command with shell-free argument transport, bounded output capture and a hard
/// process lifetime. This is intentionally only a process boundary; command selection,
/// provider policy, credentials and Azure lifecycle semantics belong to higher layers.
/// </summary>
public sealed class AzureCommandProcess : IAzureCommandProcess
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);
    public const int DefaultOutputLimit = 64 * 1024;

    private readonly TimeSpan _timeout;
    private readonly int _outputCharacterLimit;

    public AzureCommandProcess(
        TimeSpan? timeout = null,
        int outputCharacterLimit = DefaultOutputLimit)
    {
        _timeout = timeout ?? DefaultTimeout;
        if (_timeout <= TimeSpan.Zero || _timeout == Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(timeout), "The command timeout must be positive and finite.");

        if (outputCharacterLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(outputCharacterLimit), "The command output limit must be positive.");

        _outputCharacterLimit = outputCharacterLimit;
    }

    /// <inheritdoc />
    public async Task<AzureCommandProcessResult> ExecuteAsync(
        AzureCommandProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
            return CancelledResult();

        using var process = new Process
        {
            EnableRaisingEvents = true
        };

        try
        {
            process.StartInfo = CreateStartInfo(request);
            if (!process.Start())
                return StartFailedResult();
        }
        catch (Win32Exception exception)
        {
            return exception.NativeErrorCode is 2 or 3
                ? ExecutableNotFoundResult()
                : StartFailedResult();
        }
        catch (Exception)
        {
            return StartFailedResult();
        }

        var outputLimitReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var standardOutputTask = ReadBoundedAsync(process.StandardOutput, _outputCharacterLimit, outputLimitReached);
        var standardErrorTask = ReadBoundedAsync(process.StandardError, _outputCharacterLimit, outputLimitReached);
        var exitTask = process.WaitForExitAsync();
        var timeoutTask = Task.Delay(_timeout);
        var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        var completedTask = await Task.WhenAny(
            exitTask,
            timeoutTask,
            cancellationTask,
            outputLimitReached.Task).ConfigureAwait(false);

        var status = completedTask == cancellationTask || cancellationToken.IsCancellationRequested
            ? AzureCommandProcessStatus.Cancelled
            : completedTask == timeoutTask
                ? AzureCommandProcessStatus.TimedOut
                : completedTask == outputLimitReached.Task
                    ? AzureCommandProcessStatus.OutputLimitExceeded
                    : AzureCommandProcessStatus.Succeeded;

        // A process that exits at the same time as a reader observes its cap still exceeded the
        // cap. Check the signal after the process has naturally exited as well as in WhenAny.
        if (status == AzureCommandProcessStatus.Succeeded && outputLimitReached.Task.IsCompleted)
            status = AzureCommandProcessStatus.OutputLimitExceeded;

        if (status != AzureCommandProcessStatus.Succeeded)
        {
            KillProcessTree(process);
            await WaitForExitAfterTerminationAsync(exitTask).ConfigureAwait(false);
            await ObserveCaptureTasksAsync(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            return status switch
            {
                AzureCommandProcessStatus.Cancelled => CancelledResult(),
                AzureCommandProcessStatus.TimedOut => TimedOutResult(),
                AzureCommandProcessStatus.OutputLimitExceeded => OutputLimitExceededResult(),
                _ => ExecutionFailedResult()
            };
        }

        var captures = await ObserveCaptureTasksAsync(standardOutputTask, standardErrorTask).ConfigureAwait(false);
        if (outputLimitReached.Task.IsCompleted)
            return OutputLimitExceededResult();

        if (process.ExitCode == 0)
        {
            return new AzureCommandProcessResult(
                AzureCommandProcessStatus.Succeeded,
                AzureCommandProcessFailureKind.None,
                process.ExitCode,
                captures.StandardOutput,
                captures.StandardError,
                "azure.command.succeeded",
                "The Azure command completed successfully.");
        }

        // Exit code is safe typed metadata. Captured output is intentionally discarded because
        // command providers may write credentials, tokens or other sensitive values to stderr.
        return new AzureCommandProcessResult(
            AzureCommandProcessStatus.Failed,
            AzureCommandProcessFailureKind.NonZeroExitCode,
            process.ExitCode,
            string.Empty,
            string.Empty,
            "azure.command.non-zero-exit",
            "The Azure command exited with a non-zero status.");
    }

    /// <summary>Convenience alias for callers that describe a process invocation as a run.</summary>
    public Task<AzureCommandProcessResult> RunAsync(
        AzureCommandProcessRequest request,
        CancellationToken cancellationToken = default) => ExecuteAsync(request, cancellationToken);

    private static ProcessStartInfo CreateStartInfo(AzureCommandProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);

        if (request.WorkingDirectory is not null)
            startInfo.WorkingDirectory = request.WorkingDirectory;

        if (request.EnvironmentVariables is not null)
        {
            foreach (var (name, value) in request.EnvironmentVariables)
                startInfo.Environment[name] = value;
        }

        return startInfo;
    }

    private static async Task<BoundedCapture> ReadBoundedAsync(
        StreamReader reader,
        int outputCharacterLimit,
        TaskCompletionSource<bool> outputLimitReached)
    {
        var builder = new StringBuilder(Math.Min(outputCharacterLimit, 4096));
        var buffer = new char[4096];

        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0)
                    return new BoundedCapture(builder.ToString());

                var remaining = outputCharacterLimit - builder.Length;
                if (read > remaining)
                {
                    if (remaining > 0)
                        builder.Append(buffer, 0, remaining);
                    outputLimitReached.TrySetResult(true);
                    return new BoundedCapture(builder.ToString());
                }

                builder.Append(buffer, 0, read);
            }
        }
        catch (ObjectDisposedException)
        {
            return new BoundedCapture(builder.ToString());
        }
        catch (IOException)
        {
            return new BoundedCapture(builder.ToString());
        }
    }

    private static async Task<BoundedCaptures> ObserveCaptureTasksAsync(
        Task<BoundedCapture> standardOutputTask,
        Task<BoundedCapture> standardErrorTask)
    {
        try
        {
            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A stream can close concurrently with process-tree termination. Failed results do
            // not expose captures, and successful process exit has already drained both streams.
        }

        var standardOutput = standardOutputTask.Status == TaskStatus.RanToCompletion
            ? standardOutputTask.Result
            : new BoundedCapture(string.Empty);
        var standardError = standardErrorTask.Status == TaskStatus.RanToCompletion
            ? standardErrorTask.Result
            : new BoundedCapture(string.Empty);
        return new BoundedCaptures(standardOutput.Value, standardError.Value);
    }

    private static async Task WaitForExitAfterTerminationAsync(Task exitTask)
    {
        try
        {
            await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Process termination is best effort. The result remains a safe classification even
            // when the platform reports an already-closed process handle.
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception) when (process.HasExited)
        {
            // The process exited between HasExited and Kill. Nothing remains to terminate.
        }
        catch (Exception)
        {
            // There is no safe diagnostic value in a platform-specific kill exception.
        }
    }

    private static AzureCommandProcessResult CancelledResult() => FailureResult(
        AzureCommandProcessStatus.Cancelled,
        AzureCommandProcessFailureKind.Cancelled,
        "azure.command.cancelled",
        "The Azure command was cancelled.");

    private static AzureCommandProcessResult TimedOutResult() => FailureResult(
        AzureCommandProcessStatus.TimedOut,
        AzureCommandProcessFailureKind.TimedOut,
        "azure.command.timed-out",
        "The Azure command exceeded its execution timeout.");

    private static AzureCommandProcessResult OutputLimitExceededResult() => FailureResult(
        AzureCommandProcessStatus.OutputLimitExceeded,
        AzureCommandProcessFailureKind.OutputLimitExceeded,
        "azure.command.output-limit-exceeded",
        "The Azure command exceeded its output limit.");

    private static AzureCommandProcessResult ExecutableNotFoundResult() => FailureResult(
        AzureCommandProcessStatus.Failed,
        AzureCommandProcessFailureKind.ExecutableNotFound,
        "azure.command.executable-not-found",
        "The Azure command executable could not be found.");

    private static AzureCommandProcessResult StartFailedResult() => FailureResult(
        AzureCommandProcessStatus.Failed,
        AzureCommandProcessFailureKind.StartFailed,
        "azure.command.start-failed",
        "The Azure command could not be started.");

    private static AzureCommandProcessResult ExecutionFailedResult() => FailureResult(
        AzureCommandProcessStatus.Failed,
        AzureCommandProcessFailureKind.ExecutionFailed,
        "azure.command.execution-failed",
        "The Azure command failed before a result could be observed.");

    private static AzureCommandProcessResult FailureResult(
        AzureCommandProcessStatus status,
        AzureCommandProcessFailureKind failureKind,
        string code,
        string message) => new(
        status,
        failureKind,
        null,
        string.Empty,
        string.Empty,
        code,
        message);

    private readonly record struct BoundedCapture(string Value);

    private readonly record struct BoundedCaptures(string StandardOutput, string StandardError);
}
