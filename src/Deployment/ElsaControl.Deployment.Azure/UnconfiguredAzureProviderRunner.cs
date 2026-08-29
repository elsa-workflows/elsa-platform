namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Safe default for hosts that have not installed an Azure execution adapter. It prevents a
/// deployment request from silently becoming a no-op while allowing the API to expose durable
/// operation status and recovery state in development/test environments.
/// </summary>
public sealed class UnconfiguredAzureProviderRunner : IAzureProviderRunner
{
    public Task<AzureProviderRunnerResult> RunAsync(
        AzureProviderRunnerCommand command,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The Azure provider runner is not configured.");
}
