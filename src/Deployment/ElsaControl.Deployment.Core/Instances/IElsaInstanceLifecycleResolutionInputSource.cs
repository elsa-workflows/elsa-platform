using ElsaControl.Deployment.Abstractions.Instances;

namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Supplies the already-governed, typed inputs needed by the asynchronous
/// instance resolver. The source is called only after an operation has been
/// durably claimed. Implementations must load from catalog/admission boundaries;
/// raw request payloads, credentials and provider data are not valid sources.
/// </summary>
public interface IElsaInstanceLifecycleResolutionInputSource
{
    Task<ElsaInstanceLifecycleResolutionInput?> GetAsync(
        ElsaInstance instance,
        ElsaInstanceOperation operation,
        CancellationToken cancellationToken = default);
}
