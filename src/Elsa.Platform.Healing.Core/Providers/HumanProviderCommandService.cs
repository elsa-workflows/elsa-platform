using Elsa.Platform.Healing.Abstractions;

namespace Elsa.Platform.Healing.Core.Providers;

public sealed record HumanProviderCommandAuthorization(
    bool ProviderPermissionGranted,
    string ProviderPermission,
    Guid? LinkedPlatformActorId,
    IReadOnlySet<string> WorkspacePermissions,
    Guid? ConfirmationId = null,
    bool ConfirmationValid = false);

public sealed record HumanProviderCommandContext(
    HumanCommand Command,
    int AttemptCount,
    int MaximumAttempts,
    HealingIncidentStatus IncidentStatus = HealingIncidentStatus.NeedsHuman,
    bool HasEnvironmentTarget = false);

public sealed record HumanProviderCommandDecision(
    bool Authorized,
    bool Executed,
    HumanCommandStatus Status,
    string ReasonCode,
    string? RequiredPermission = null);

public interface IHumanProviderCommandStore
{
    ValueTask<HumanProviderCommandContext?> GetAsync(Guid commandId, CancellationToken cancellationToken = default);

    ValueTask CompleteAsync(
        HumanProviderCommandContext context,
        HumanProviderCommandAuthorization authorization,
        HumanProviderCommandDecision decision,
        CancellationToken cancellationToken = default);
}

public static class HumanProviderCommandPolicy
{
    public static HumanProviderCommandDecision Evaluate(
        HumanProviderCommandContext context,
        HumanProviderCommandAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Command);
        ArgumentNullException.ThrowIfNull(authorization);
        if (!HealingHumanCommands.All.Contains(context.Command.Command))
            return Deny("command-unsupported");
        if (!authorization.ProviderPermissionGranted || !IsMaintainerPermission(authorization.ProviderPermission))
            return Deny("provider-permission-denied");
        if (authorization.LinkedPlatformActorId is null)
            return Deny("platform-identity-link-missing");

        var requiredPermission = RequiredPermission(context.Command.Command);
        if (!authorization.WorkspacePermissions.Contains(requiredPermission))
            return Deny("workspace-permission-denied", requiredPermission);
        if (context.Command.Command == HealingHumanCommands.Retry && context.AttemptCount >= context.MaximumAttempts)
            return Deny("maximum-attempts-reached", requiredPermission);
        if (context.Command.Command == HealingHumanCommands.Retry &&
            context.IncidentStatus is not (HealingIncidentStatus.NeedsHuman or HealingIncidentStatus.FailedVerification))
            return Deny("retry-not-applicable", requiredPermission);
        if (context.Command.Command == HealingHumanCommands.Stop &&
            context.IncidentStatus is not (HealingIncidentStatus.Repairing or HealingIncidentStatus.PullRequestOpen or HealingIncidentStatus.NeedsHuman))
            return Deny("stop-not-applicable", requiredPermission);

        if (context.Command.Command is HealingHumanCommands.Stop or HealingHumanCommands.WaiveEnvironment &&
            (!authorization.ConfirmationId.HasValue || !authorization.ConfirmationValid))
        {
            return new(true, false, HumanCommandStatus.Authorized, "confirmation-required", requiredPermission);
        }
        if (context.Command.Command == HealingHumanCommands.WaiveEnvironment && !context.HasEnvironmentTarget)
            return new(true, false, HumanCommandStatus.Authorized, "environment-target-required", requiredPermission);
        if (context.Command.Command == HealingHumanCommands.WaiveEnvironment)
            return new(true, false, HumanCommandStatus.Authorized, "environment-waiver-details-required", requiredPermission);
        if (context.Command.Command == HealingHumanCommands.RequestEvidence)
            return new(true, false, HumanCommandStatus.Authorized, "evidence-request-details-required", requiredPermission);

        return new(true, true, HumanCommandStatus.Executed, "command-executed", requiredPermission);
    }

    private static bool IsMaintainerPermission(string permission) =>
        permission is "admin" or "maintain" or "write";

    private static string RequiredPermission(string command) => command switch
    {
        HealingHumanCommands.Retry => HealingPermissions.RetryRepair,
        HealingHumanCommands.Stop => HealingPermissions.StopRepair,
        HealingHumanCommands.RequestEvidence => HealingPermissions.ElevateEvidence,
        HealingHumanCommands.WaiveEnvironment => HealingPermissions.WaiveVerification,
        _ => string.Empty
    };

    private static HumanProviderCommandDecision Deny(string reasonCode, string? permission = null) =>
        new(false, false, HumanCommandStatus.Rejected, reasonCode, permission);
}

public sealed class HumanProviderCommandService(IHumanProviderCommandStore store)
{
    public async ValueTask<HumanProviderCommandDecision> ExecuteAsync(
        Guid commandId,
        HumanProviderCommandAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        if (commandId == Guid.Empty)
            throw new ArgumentException("Command identity is required.", nameof(commandId));
        var context = await store.GetAsync(commandId, cancellationToken)
                      ?? throw new KeyNotFoundException("The human command does not exist.");
        if (context.Command.Status is HumanCommandStatus.Executed or HumanCommandStatus.Rejected or HumanCommandStatus.Failed)
            return new(context.Command.Status == HumanCommandStatus.Executed,
                context.Command.Status == HumanCommandStatus.Executed,
                context.Command.Status,
                context.Command.ResultCode ?? "command-terminal");

        var decision = HumanProviderCommandPolicy.Evaluate(context, authorization);
        await store.CompleteAsync(context, authorization, decision, cancellationToken);
        return decision;
    }
}
