namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Executes one already-authorized Azure command without passing through a shell.
/// Implementations must return only the safe process result contract; command output is
/// never included in a failed result or exception.
/// </summary>
internal interface IAzureCommandProcess
{
    Task<AzureCommandProcessResult<T>> ExecuteAsync<T>(
        AzureCommandProcessRequest request,
        AzureCommandOutputProjector<T> outputProjector,
        CancellationToken cancellationToken = default)
        where T : AzureCommandSafeOutput;

    /// <summary>Convenience terminology for callers that treat a command as a provider step.</summary>
    Task<AzureCommandProcessResult<T>> RunAsync<T>(
        AzureCommandProcessRequest request,
        AzureCommandOutputProjector<T> outputProjector,
        CancellationToken cancellationToken = default)
        where T : AzureCommandSafeOutput => ExecuteAsync(request, outputProjector, cancellationToken);
}

/// <summary>
/// Converts transient stdout into a safe typed value before it crosses the process boundary.
/// Implementations must not return raw provider payloads or secret material.
/// </summary>
internal delegate T AzureCommandOutputProjector<out T>(ReadOnlyMemory<char> standardOutput)
    where T : AzureCommandSafeOutput;
