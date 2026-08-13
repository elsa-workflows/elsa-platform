using ValenceControl.Deployment.Core.Cockpit;

namespace ValenceControl.Deployment.Core.Workspace;

public sealed class ConfirmationService(IWorkspaceDeploymentMutationStore? store = null, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<ActionConfirmation> CreateConfirmationAsync(
        Guid workspaceId,
        CreateActionConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (store is null)
            throw new InvalidOperationException("Action confirmation persistence is not configured.");

        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetId);
        return store.CreateConfirmationAsync(workspaceId, request, _timeProvider.GetUtcNow(), cancellationToken);
    }

    public async Task<ConfirmationConsumptionResult> ConsumeConfirmationAsync(
        Guid workspaceId,
        Guid confirmationId,
        Guid accountId,
        ConfirmationActionType actionType,
        string targetId,
        CancellationToken cancellationToken = default)
    {
        if (store is null)
            throw new InvalidOperationException("Action confirmation persistence is not configured.");

        var confirmation = await store.GetConfirmationAsync(workspaceId, confirmationId, cancellationToken);
        if (confirmation is null)
            return new ConfirmationConsumptionResult(null, Blocker("deployment.confirmation.missing", "Confirmation does not exist."));

        var now = _timeProvider.GetUtcNow();
        var validation = Validate(confirmation, accountId, actionType, targetId, now);
        if (validation.Severity != ValidationSeverity.Pass)
            return new ConfirmationConsumptionResult(confirmation, validation);

        var useAttempt = await store.TryMarkConfirmationUsedAsync(
            workspaceId,
            confirmation.Id,
            now,
            cancellationToken);
        if (useAttempt is null)
            return new ConfirmationConsumptionResult(null, Blocker("deployment.confirmation.missing", "Confirmation does not exist."));

        if (!useAttempt.Consumed)
            return new ConfirmationConsumptionResult(
                useAttempt.Confirmation,
                Blocker("deployment.confirmation.used", "Confirmation has already been used."));

        return new ConfirmationConsumptionResult(useAttempt.Confirmation, validation);
    }

    public DeploymentValidation Validate(ActionConfirmation confirmation, Guid accountId, ConfirmationActionType actionType, string targetId, DateTimeOffset now)
    {
        if (confirmation.ConfirmedByAccountId != accountId)
            return Blocker("deployment.confirmation.account", "Confirmation was created by a different account.");

        if (confirmation.ActionType != actionType || !string.Equals(confirmation.TargetId, targetId, StringComparison.Ordinal))
            return Blocker("deployment.confirmation.target", "Confirmation does not match the requested action.");

        if (confirmation.UsedAt is not null)
            return Blocker("deployment.confirmation.used", "Confirmation has already been used.");

        if (confirmation.ExpiresAt <= now)
            return Blocker("deployment.confirmation.expired", "Confirmation has expired.");

        return new DeploymentValidation("deployment.confirmation.valid", ValidationSeverity.Pass, "Confirmation", "Confirmation is valid.");
    }

    private static DeploymentValidation Blocker(string id, string message) =>
        new(id, ValidationSeverity.Blocker, "Confirmation", message);
}
