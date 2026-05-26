using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class RuntimeControlService(
    IWorkspaceDeploymentStore? deploymentStore = null,
    IWorkspaceDeploymentMutationStore? mutationStore = null,
    ConfirmationService? confirmations = null,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public DeploymentValidation ValidateControl(RuntimeControl control, IReadOnlyCollection<EngineCapability> capabilities)
    {
        var supported = capabilities.Any(capability => capability.Id == control.CapabilityId && capability.Boundary == control.Boundary);
        return supported
            ? new DeploymentValidation("deployment.control.supported", ValidationSeverity.Pass, control.Boundary.ToString(), "Runtime control is supported by the selected engine.")
            : new DeploymentValidation("deployment.control.unsupported", ValidationSeverity.Blocker, control.Boundary.ToString(), "Runtime control is not supported by the selected engine.");
    }

    public async Task<RuntimeControlExecution> ExecuteControlAsync(
        Guid workspaceId,
        RuntimeControlExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (deploymentStore is null || mutationStore is null || confirmations is null)
            throw new InvalidOperationException("Runtime control persistence is not configured.");

        var cockpit = await deploymentStore.GetCockpitAsync(workspaceId, cancellationToken);
        var engine = cockpit.Engines.SingleOrDefault(x => string.Equals(x.Id, request.EngineId.ToString("D"), StringComparison.Ordinal));
        if (engine is null)
            throw new InvalidOperationException("Workflow engine does not exist in the workspace.");

        var control = engine.Controls.SingleOrDefault(x => string.Equals(x.Id, request.ControlId, StringComparison.Ordinal));
        if (control is null)
            throw new InvalidOperationException("Runtime control is not registered for the selected engine.");

        if (engine.Health == DeploymentHealth.Unreachable)
            throw new InvalidOperationException("Runtime control cannot execute while the selected engine is unreachable.");

        var controlValidation = ValidateControl(control, engine.Capabilities);
        if (controlValidation.Severity == ValidationSeverity.Blocker)
            throw new InvalidOperationException(controlValidation.Message);

        var confirmation = await confirmations.ConsumeConfirmationAsync(
            workspaceId,
            request.ConfirmationId,
            request.ActorAccountId,
            ConfirmationActionType.RuntimeControl,
            RuntimeControlTargetId(request.EngineId, request.ControlId),
            cancellationToken);
        if (!confirmation.Succeeded)
            throw new InvalidOperationException(confirmation.Validation.Message);

        var execution = new RuntimeControlExecution(
            Guid.NewGuid(),
            workspaceId,
            request.EngineId,
            Guid.Parse(engine.EnvironmentId),
            control.Id,
            control.Label,
            control.Boundary,
            control.CapabilityId,
            request.ConfirmationId,
            request.ActorAccountId,
            RuntimeControlExecutionStatus.Succeeded,
            _timeProvider.GetUtcNow(),
            $"{control.Label} executed for {engine.Name}.");
        return await mutationStore.RecordRuntimeControlExecutionAsync(workspaceId, execution, cancellationToken);
    }

    public static string RuntimeControlTargetId(Guid engineId, string controlId) =>
        $"{engineId:D}:{controlId}";
}
