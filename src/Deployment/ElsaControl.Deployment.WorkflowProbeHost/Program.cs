using System.Buffers;
using System.Text;
using System.Text.Json;
using ElsaControl.Deployment.Azure;
using ElsaControl.Deployment.Proof;

const int failedExitCode = 5;

try
{
    var options = ReadOptions(args);
    await using var credentials = new PrivateFileCredentialSource(options.PasswordFile);
    using var probe = new ElsaHttpWorkflowProbe(
        new ElsaHttpWorkflowProbeOptions(
            options.Username,
            options.WorkflowDefinitionId,
            requestTimeout: TimeSpan.FromSeconds(30),
            workflowTimeout: TimeSpan.FromMinutes(10),
            pollInterval: TimeSpan.FromSeconds(2),
            mode: options.Mode,
            expectedAbsentWorkflowDefinitionId: options.ExpectedAbsentWorkflowDefinitionId),
        credentials);
    var result = await probe.RunAsync(
        options.Endpoint,
        new DeploymentProofEnvironment(
            options.EnvironmentName,
            "westeurope",
            "azure",
            ["sql-connection", "identity-signing-key", "admin-password"]));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        outcome = result.Succeeded ? "passed" : "failed",
        workflowId = result.WorkflowId,
        result = result.Result,
        evidence = result.SafeMetadata
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return result.Succeeded ? 0 : failedExitCode;
}
catch (Exception)
{
    Console.Error.WriteLine("workflow-probe.failed");
    return failedExitCode;
}

static ProbeOptions ReadOptions(string[] arguments)
{
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length || !arguments[index].StartsWith("--", StringComparison.Ordinal) ||
            !values.TryAdd(arguments[index], arguments[index + 1]))
            throw new ArgumentException("The workflow probe arguments are invalid.");
    }

    var known = new HashSet<string>(StringComparer.Ordinal)
    {
        "--endpoint", "--environment", "--username", "--password-file", "--workflow-id", "--mode", "--absent-workflow-id"
    };
    if (values.Keys.Any(key => !known.Contains(key)))
        throw new ArgumentException("The workflow probe arguments are invalid.");

    string Required(string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException("A required workflow probe argument is missing.");

    var mode = Required("--mode") switch
    {
        "create" => ElsaHttpWorkflowProbeMode.CreatePublishAndExecute,
        "verify" => ElsaHttpWorkflowProbeMode.VerifyExistingAndExecute,
        _ => throw new ArgumentException("The workflow probe mode is invalid.")
    };
    values.TryGetValue("--absent-workflow-id", out var absent);
    if (mode == ElsaHttpWorkflowProbeMode.CreatePublishAndExecute && absent is not null)
        throw new ArgumentException("Absence verification is available only in verify mode.");

    return new ProbeOptions(
        Required("--endpoint"),
        Required("--environment"),
        Required("--username"),
        Path.GetFullPath(Required("--password-file")),
        Required("--workflow-id"),
        mode,
        absent);
}

file sealed record ProbeOptions(
    string Endpoint,
    string EnvironmentName,
    string Username,
    string PasswordFile,
    string WorkflowDefinitionId,
    ElsaHttpWorkflowProbeMode Mode,
    string? ExpectedAbsentWorkflowDefinitionId);

file sealed class PrivateFileCredentialSource : IElsaProofCredentialSource, IAsyncDisposable
{
    private const int MaximumCharacters = 8192;
    private readonly string path;

    public PrivateFileCredentialSource(string path)
    {
        this.path = path;
        var info = new FileInfo(path);
        if (!info.Exists || info.LinkTarget is not null || info.Length is <= 0 or > MaximumCharacters * 4)
            throw new InvalidOperationException("The private credential file is invalid.");
        if (!OperatingSystem.IsWindows())
        {
            var unsafeBits = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                             UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            if ((File.GetUnixFileMode(path) & unsafeBits) != 0)
                throw new InvalidOperationException("The private credential file permissions are invalid.");
        }
    }

    public async ValueTask<AzureSecretLease> ResolvePasswordAsync(CancellationToken cancellationToken = default)
    {
        var buffer = ArrayPool<char>.Shared.Rent(MaximumCharacters + 1);
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 4096, leaveOpen: false);
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(count, buffer.Length - count), cancellationToken);
                if (read == 0)
                    break;
                count += read;
            }
            if (count is 0 or > MaximumCharacters)
                throw new InvalidOperationException("The private credential file is invalid.");
            return new AzureSecretLease(buffer.AsSpan(0, count));
        }
        finally
        {
            Array.Clear(buffer);
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
