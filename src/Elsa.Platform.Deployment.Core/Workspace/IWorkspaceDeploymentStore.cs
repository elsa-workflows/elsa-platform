using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public interface IWorkspaceDeploymentStore
{
    Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentApplication> CreateApplicationAsync(
        Guid workspaceId,
        CreateWorkflowApplicationRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentApplication> UpdateApplicationAsync(
        Guid workspaceId,
        Guid applicationId,
        UpdateWorkflowApplicationRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(
        Guid workspaceId,
        CreateDeploymentEnvironmentRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentEnvironment> UpdateEnvironmentAsync(
        Guid workspaceId,
        Guid environmentId,
        UpdateDeploymentEnvironmentRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceWorkflowEngine> RegisterEngineAsync(
        Guid workspaceId,
        RegisterWorkflowEngineRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceWorkflowEngine> UpdateEngineAsync(
        Guid workspaceId,
        Guid engineId,
        UpdateWorkflowEngineRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDesiredStateRevision> CreateRevisionAsync(
        Guid workspaceId,
        CreateDesiredStateRevisionRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDesiredStateRevision?> GetRevisionAsync(
        Guid workspaceId,
        Guid revisionId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDesiredStateRevision?> GetLatestRevisionAsync(
        Guid workspaceId,
        Guid environmentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceDesiredStateRevisionSummary>> ListApplicationRevisionsAsync(
        Guid workspaceId,
        Guid applicationId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Application revision listing is not supported by this store.");

    Task<WorkspaceDesiredStateRevisionDetail?> GetRevisionDetailAsync(
        Guid workspaceId,
        Guid revisionId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Revision detail reads are not supported by this store.");

    Task<WorkspaceWorkflowEngine?> GetEngineAsync(
        Guid workspaceId,
        Guid engineId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceWorkflowEngine>> ListEnginesDueForVerificationAsync(
        DateTimeOffset verifyBefore,
        int limit,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Engine verification scans are not supported by this store.");

    Task<EngineHealthResult> UpdateEngineHealthAsync(
        Guid workspaceId,
        EngineHealthUpdate update,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Engine health updates are not supported by this store.");

    Task<EngineHealthResult> ApplyEngineHeartbeatAsync(
        Guid workspaceId,
        EngineHealthUpdate update,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Engine heartbeats are not supported by this store.");
}
