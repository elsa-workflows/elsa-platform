namespace Elsa.Platform.Studio.Submit;

public interface IStudioWorkflowSnapshotPackager
{
    StudioSubmitPackage Package(
        WorkflowSubmissionSnapshot snapshot,
        StudioSubmitOptions options,
        DateTimeOffset? packagedAt = null);
}
