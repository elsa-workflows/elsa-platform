namespace Elsa.Platform.Studio.Submit;

public sealed class SubmitToPlatformCommand(
    IStudioWorkflowSnapshotPackager packager,
    IStudioPlatformSubmitClient submitClient,
    StudioSubmitOptions options)
{
    public async Task<StudioSubmitResult> ExecuteAsync(
        WorkflowSubmissionSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        StudioSubmitPackage package;
        try
        {
            package = packager.Package(snapshot, options);
        }
        catch (InvalidOperationException ex)
        {
            return new StudioSubmitResult(StudioSubmitStatus.ValidationFailed, StudioSubmitMessageSanitizer.SafeMessage(ex.Message));
        }

        try
        {
            var result = await submitClient.SubmitAsync(package, options, cancellationToken);
            return result with { Message = StudioSubmitMessageSanitizer.SafeMessage(result.Message) };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return new StudioSubmitResult(StudioSubmitStatus.Unavailable, StudioSubmitMessageSanitizer.SafeMessage(ex.Message));
        }
    }
}
