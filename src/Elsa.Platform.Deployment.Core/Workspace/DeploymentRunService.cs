using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class DeploymentRunService
{
    public DeploymentValidation ValidateRunRequest(WorkspaceDeploymentRunRequest request)
    {
        return request.Mode switch
        {
            DeploymentRunMode.DryRun or DeploymentRunMode.Apply => new DeploymentValidation("deployment.run.request.valid", ValidationSeverity.Pass, "Deployment run", "Deployment run request is valid."),
            _ => new DeploymentValidation("deployment.run.mode.invalid", ValidationSeverity.Blocker, "Deployment run", "Deployment run mode is not supported.")
        };
    }
}
