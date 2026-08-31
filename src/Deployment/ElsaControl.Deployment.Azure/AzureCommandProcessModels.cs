using System.Text.Json.Serialization;

namespace ElsaControl.Deployment.Azure;

/// <summary>
/// A process invocation expressed as typed executable and argument values. Arguments are
/// appended to <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> one at a time;
/// they are never assembled into a shell command line. Arguments must contain safe locators and
/// identifiers only. Secret material must never be placed in argv because operating systems can
/// expose it through process inspection.
/// </summary>
public sealed record AzureCommandProcessRequest
{
    public AzureCommandProcessRequest(
        string fileName,
        IReadOnlyList<AzureCommandArgument>? arguments = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 1024 || fileName.Any(char.IsControl))
            throw new ArgumentException("The command executable locator is unsafe.", nameof(fileName));

        arguments ??= [];
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

    /// <summary>
    /// Safe, non-secret argument values. Omitted from JSON so accidental request serialization
    /// cannot disclose invocation details.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<AzureCommandArgument> Arguments { get; }

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

/// <summary>
/// An explicitly classified non-secret command argument. Secret leases deliberately have no
/// conversion to this type and must be transported through a provider-specific protected seam.
/// </summary>
public readonly record struct AzureCommandArgument
{
    private AzureCommandArgument(string value) => Value = value;

    [JsonIgnore]
    public string Value { get; }

    public static AzureCommandArgument Safe(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new AzureCommandArgument(value);
    }

    public override string ToString() => nameof(AzureCommandArgument);
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
    InvalidOutput,
    ExecutionFailed
}

/// <summary>
/// Safe outcome of one command process invocation. Standard output and error are populated only
/// when <see cref="Status"/> is <see cref="AzureCommandProcessStatus.Succeeded"/>.
/// </summary>
public sealed record AzureCommandProcessResult<T>(
    AzureCommandProcessStatus Status,
    AzureCommandProcessFailureKind FailureKind,
    int? ExitCode,
    T? Value,
    string Code,
    string Message)
{
    public bool Succeeded => Status == AzureCommandProcessStatus.Succeeded;

    public AzureCommandProcessStatus Outcome => Status;

    public AzureCommandProcessFailureKind Failure => FailureKind;

    public override string ToString() =>
        $"AzureCommandProcessResult(Status={Status}, FailureKind={FailureKind}, ExitCode={ExitCode}, Code={Code})";
}
