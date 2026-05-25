using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class RuntimeControlService
{
    public DeploymentValidation ValidateControl(RuntimeControl control, IReadOnlyCollection<EngineCapability> capabilities)
    {
        var supported = capabilities.Any(capability => capability.Id == control.CapabilityId && capability.Boundary == control.Boundary);
        return supported
            ? new DeploymentValidation("deployment.control.supported", ValidationSeverity.Pass, control.Boundary.ToString(), "Runtime control is supported by the selected engine.")
            : new DeploymentValidation("deployment.control.unsupported", ValidationSeverity.Blocker, control.Boundary.ToString(), "Runtime control is not supported by the selected engine.");
    }
}
