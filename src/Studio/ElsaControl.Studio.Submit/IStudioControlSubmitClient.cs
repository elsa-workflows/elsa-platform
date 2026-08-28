namespace ElsaControl.Studio.Submit;

public interface IStudioControlSubmitClient
{
    Task<StudioSubmitResult> SubmitAsync(
        StudioSubmitPackage package,
        StudioSubmitOptions options,
        CancellationToken cancellationToken = default);
}
