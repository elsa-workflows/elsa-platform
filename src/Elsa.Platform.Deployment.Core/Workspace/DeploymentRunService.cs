using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class DeploymentRunService(
    IWorkspaceDeploymentMutationStore? store = null,
    ConfirmationService? confirmations = null,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public DeploymentValidation ValidateRunRequest(WorkspaceDeploymentRunRequest request)
    {
        return request.Mode switch
        {
            DeploymentRunMode.DryRun or DeploymentRunMode.Apply => new DeploymentValidation("deployment.run.request.valid", ValidationSeverity.Pass, "Deployment run", "Deployment run request is valid."),
            _ => new DeploymentValidation("deployment.run.mode.invalid", ValidationSeverity.Blocker, "Deployment run", "Deployment run mode is not supported.")
        };
    }

    public Task<WorkspaceDeploymentRun> QueueDeploymentAsync(
        Guid workspaceId,
        WorkspaceDeploymentRunRequest request,
        Guid confirmationId,
        CancellationToken cancellationToken = default) =>
        QueueRunAsync(workspaceId, request, confirmationId, ConfirmationActionType.Deploy, null, cancellationToken);

    public Task<WorkspaceDeploymentRun> QueueRollbackAsync(
        Guid workspaceId,
        WorkspaceDeploymentRunRequest request,
        Guid confirmationId,
        Guid rollbackSourceRunId,
        CancellationToken cancellationToken = default) =>
        QueueRunAsync(workspaceId, request, confirmationId, ConfirmationActionType.Rollback, rollbackSourceRunId, cancellationToken);

    public async Task<WorkspaceDeploymentRunDetail?> GetRunDetailAsync(
        Guid workspaceId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        if (store is null)
            throw new InvalidOperationException("Deployment run persistence is not configured.");

        var run = await store.GetRunAsync(workspaceId, runId, cancellationToken);
        if (run is null)
            return null;

        var history = await store.GetRunHistoryAsync(workspaceId, runId, cancellationToken);
        var commands = await store.GetRunCommandSummariesAsync(workspaceId, runId, cancellationToken);
        return new WorkspaceDeploymentRunDetail(run, history, commands);
    }

    private async Task<WorkspaceDeploymentRun> QueueRunAsync(
        Guid workspaceId,
        WorkspaceDeploymentRunRequest request,
        Guid confirmationId,
        ConfirmationActionType actionType,
        Guid? rollbackSourceRunId,
        CancellationToken cancellationToken)
    {
        if (store is null || confirmations is null)
            throw new InvalidOperationException("Deployment run persistence is not configured.");

        var requestValidation = ValidateRunRequest(request);
        if (requestValidation.Severity == ValidationSeverity.Blocker)
            throw new InvalidOperationException(requestValidation.Message);

        if (await store.HasActiveRunAsync(workspaceId, request.TargetEnvironmentId, cancellationToken))
            throw new InvalidOperationException("An active deployment run already exists for the target environment.");

        var targetEnvironment = await GetTargetEnvironmentAsync(workspaceId, request.TargetEnvironmentId, cancellationToken);
        ValidateTierCapabilities(targetEnvironment, actionType);

        var confirmation = await confirmations.ConsumeConfirmationAsync(
            workspaceId,
            confirmationId,
            request.ActorAccountId,
            actionType,
            request.SourceRevisionId.ToString("D"),
            cancellationToken);
        if (!confirmation.Succeeded)
            throw new InvalidOperationException(confirmation.Validation.Message);

        return await store.CreateRunAsync(
            workspaceId,
            new QueueWorkspaceDeploymentRunRequest(
                request.SourceRevisionId,
                request.TargetEnvironmentId,
                request.TargetEngineId,
                confirmationId,
                request.ActorAccountId,
                rollbackSourceRunId),
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private async Task<EnvironmentSummary?> GetTargetEnvironmentAsync(
        Guid workspaceId,
        Guid targetEnvironmentId,
        CancellationToken cancellationToken)
    {
        if (store is not IWorkspaceDeploymentStore deploymentStore)
            return null;

        var cockpit = await deploymentStore.GetCockpitAsync(workspaceId, cancellationToken);
        return cockpit.Applications
            .SelectMany(x => x.Environments)
            .SingleOrDefault(x => string.Equals(x.Id, targetEnvironmentId.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateTierCapabilities(EnvironmentSummary? targetEnvironment, ConfirmationActionType actionType)
    {
        if (targetEnvironment is null)
            return;

        if (actionType == ConfirmationActionType.Rollback && !DeploymentTierService.CanRollback(targetEnvironment))
            throw new InvalidOperationException($"{TierLabel(targetEnvironment)} does not allow rollback actions.");
    }

    private static string TierLabel(EnvironmentSummary environment) =>
        string.IsNullOrWhiteSpace(environment.TierName) ? environment.Tier.ToString() : environment.TierName;
}
