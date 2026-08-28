using ElsaControl.Deployment.Core.Cockpit;

namespace ElsaControl.Deployment.Core.Workspace;

public sealed class ObservabilityDriftService
{
    public DeploymentValidation ValidateBinding(WorkspaceObservabilityBinding binding)
    {
        return string.IsNullOrWhiteSpace(binding.Provider) || string.IsNullOrWhiteSpace(binding.Scope)
            ? new DeploymentValidation("deployment.observability.invalid", ValidationSeverity.Blocker, "Observability", "Observability binding metadata is incomplete.")
            : new DeploymentValidation("deployment.observability.valid", ValidationSeverity.Pass, "Observability", "Observability binding metadata is valid.");
    }

    public DeploymentValidation ValidateDriftReport(WorkspaceDriftReportItem item)
    {
        return string.IsNullOrWhiteSpace(item.Area)
            ? new DeploymentValidation("deployment.drift.invalid", ValidationSeverity.Blocker, "Drift", "Drift report metadata is incomplete.")
            : new DeploymentValidation("deployment.drift.valid", ValidationSeverity.Pass, "Drift", "Drift report metadata is valid.");
    }
}
