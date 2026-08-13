namespace ValenceControl.Studio.Submit;

public interface IStudioControlRevisionSubmitClient : IStudioControlSubmitClient
{
    Task<StudioSubmitResult> SubmitRevisionAsync(
        StudioSubmitPackage package,
        StudioSubmitOptions options,
        CancellationToken cancellationToken = default);
}
