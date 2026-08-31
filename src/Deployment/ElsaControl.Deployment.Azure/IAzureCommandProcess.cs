namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Executes one already-authorized Azure command without passing through a shell.
/// Implementations must return only the safe process result contract; command output is
/// never included in a failed result or exception.
/// </summary>
public interface IAzureCommandProcess
{
    Task<AzureCommandProcessResult> ExecuteAsync(
        AzureCommandProcessRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Convenience terminology for callers that treat a command as a provider step.</summary>
    Task<AzureCommandProcessResult> RunAsync(
        AzureCommandProcessRequest request,
        CancellationToken cancellationToken = default) => ExecuteAsync(request, cancellationToken);
}
