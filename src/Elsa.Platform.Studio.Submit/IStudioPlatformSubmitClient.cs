namespace Elsa.Platform.Studio.Submit;

public interface IStudioPlatformSubmitClient
{
    Task<StudioSubmitResult> SubmitAsync(
        StudioSubmitPackage package,
        StudioSubmitOptions options,
        CancellationToken cancellationToken = default);
}
