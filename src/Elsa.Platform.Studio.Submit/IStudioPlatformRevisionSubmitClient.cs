namespace Elsa.Platform.Studio.Submit;

public interface IStudioPlatformRevisionSubmitClient : IStudioPlatformSubmitClient
{
    Task<StudioSubmitResult> SubmitRevisionAsync(
        StudioSubmitPackage package,
        StudioSubmitOptions options,
        CancellationToken cancellationToken = default);
}
