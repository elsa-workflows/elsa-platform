using ElsaControl.Deployment.Abstractions.Artifacts;

namespace ElsaControl.Workflows.RuntimeApplier;

public interface IWorkflowRuntimeCommandClient
{
    Task<IReadOnlyList<WorkflowRuntimeCommand>> PollAsync(int limit = 10, CancellationToken cancellationToken = default);

    Task<WorkflowRuntimeCommandClaimResult> ClaimAsync(Guid commandId, CancellationToken cancellationToken = default);

    Task<WorkflowRuntimeCommandReportResult> HeartbeatAsync(
        Guid commandId,
        string leaseToken,
        CancellationToken cancellationToken = default);

    Task<WorkflowRuntimeCommandReportResult> ReportProgressAsync(
        Guid commandId,
        string leaseToken,
        string status,
        int? percentComplete,
        string message,
        CancellationToken cancellationToken = default);

    Task<WorkflowRuntimeCommandReportResult> CompleteAsync(
        Guid commandId,
        string leaseToken,
        ArtifactDigest? observedDigest,
        string? runtimeReference,
        IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics,
        CancellationToken cancellationToken = default);

    Task<WorkflowRuntimeCommandReportResult> FailAsync(
        Guid commandId,
        string leaseToken,
        IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics,
        CancellationToken cancellationToken = default);

    Task<WorkflowRuntimeCommandReportResult> RejectAsync(
        Guid commandId,
        string leaseToken,
        IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics,
        CancellationToken cancellationToken = default);
}
