using System.Text.Json.Serialization;

namespace ElsaControl.Deployment.Azure;

/// <summary>
/// A process invocation expressed as typed executable and argument values. Arguments are
/// appended to <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> one at a time;
/// they are never assembled into a shell command line.
/// </summary>
public sealed record AzureCommandProcessRequest
{
    public AzureCommandProcessRequest(
        string fileName,
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("The command executable is required.", nameof(fileName));

        arguments ??= [];
        if (arguments.Any(argument => argument is null))
            throw new ArgumentException("Command arguments cannot be null.", nameof(arguments));

        if (environmentVariables is not null && environmentVariables.Keys.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Command environment variable names cannot be blank.", nameof(environmentVariables));

        FileName = fileName.Trim();
        Arguments = Array.AsReadOnly(arguments.ToArray());
        EnvironmentVariables = environmentVariables is null
            ? null
            : new Dictionary<string, string?>(environmentVariables, StringComparer.Ordinal);
        WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory;
    }

    public string FileName { get; }

    public IReadOnlyList<string> Arguments { get; }

    /// <summary>
    /// Environment entries to add or remove for this process. Values are transient invocation
    /// data and are not copied into a result, diagnostic or exception.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string?>? EnvironmentVariables { get; }

    public string? WorkingDirectory { get; }

    // These aliases keep the contract readable at call sites that use executable terminology.
    public string Executable => FileName;

    [JsonIgnore]
    public IReadOnlyDictionary<string, string?>? Environment => EnvironmentVariables;

    public override string ToString() =>
        $"{nameof(AzureCommandProcessRequest)}(Executable={FileName}, ArgumentCount={Arguments.Count})";
}

public enum AzureCommandProcessStatus
{
    Succeeded,
    Failed,
    TimedOut,
    Cancelled,
    OutputLimitExceeded
}

/// <summary>
/// Stable, value-free classification for a command failure. Process output and exception text
/// are deliberately not part of this classification.
/// </summary>
public enum AzureCommandProcessFailureKind
{
    None,
    NonZeroExitCode,
    ExecutableNotFound,
    StartFailed,
    TimedOut,
    Cancelled,
    OutputLimitExceeded,
    ExecutionFailed
}

/// <summary>
/// Safe outcome of one command process invocation. Standard output and error are populated only
/// when <see cref="Status"/> is <see cref="AzureCommandProcessStatus.Succeeded"/>.
/// </summary>
public sealed record AzureCommandProcessResult(
    AzureCommandProcessStatus Status,
    AzureCommandProcessFailureKind FailureKind,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    string Code,
    string Message)
{
    public bool Succeeded => Status == AzureCommandProcessStatus.Succeeded;

    public AzureCommandProcessStatus Outcome => Status;

    public AzureCommandProcessFailureKind Failure => FailureKind;

    public string Stdout => StandardOutput;

    public string Stderr => StandardError;

    public override string ToString() =>
        $"{nameof(AzureCommandProcessResult)}(Status={Status}, FailureKind={FailureKind}, ExitCode={ExitCode}, Code={Code})";
}
