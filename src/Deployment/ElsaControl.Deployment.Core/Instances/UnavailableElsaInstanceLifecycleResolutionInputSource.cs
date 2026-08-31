using ElsaControl.Deployment.Abstractions.Instances;

namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Safe default for hosts that have not configured the catalog-to-instance
/// resolution adapter yet. Returning no input causes claimed work to complete as
/// a stable resolution failure; it never permits a provider call with guessed data.
/// </summary>
public sealed class UnavailableElsaInstanceLifecycleResolutionInputSource : IElsaInstanceLifecycleResolutionInputSource
{
    public Task<ElsaInstanceLifecycleResolutionInput?> GetAsync(
        ElsaInstance instance,
        ElsaInstanceOperation operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ElsaInstanceLifecycleResolutionInput?>(null);
    }
}
