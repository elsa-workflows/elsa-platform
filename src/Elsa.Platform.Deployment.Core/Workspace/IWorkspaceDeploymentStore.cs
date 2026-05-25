using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public interface IWorkspaceDeploymentStore
{
    Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentApplication> CreateApplicationAsync(
        Guid workspaceId,
        CreateWorkflowApplicationRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(
        Guid workspaceId,
        CreateDeploymentEnvironmentRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceWorkflowEngine> RegisterEngineAsync(
        Guid workspaceId,
        RegisterWorkflowEngineRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDesiredStateRevision> CreateRevisionAsync(
        Guid workspaceId,
        CreateDesiredStateRevisionRequest request,
        CancellationToken cancellationToken = default);
}
