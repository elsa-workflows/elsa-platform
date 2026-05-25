using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class ConfirmationService
{
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
