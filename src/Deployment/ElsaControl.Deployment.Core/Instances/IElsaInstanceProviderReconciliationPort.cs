namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Reads safe provider state for one uncertain operation. Implementations must not
/// apply, retry or mutate provider resources from this read boundary.
/// </summary>
public interface IElsaInstanceProviderReconciliationPort
{
    Task<ElsaInstanceProviderObservation> ObserveAsync(
        ElsaInstanceProviderReconciliationRequest request,
        CancellationToken cancellationToken = default);
}
