using System.Text.Json;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Providers;
using Elsa.Platform.Healing.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.Healing.Persistence.EntityFrameworkCore;

public sealed class HealingHumanProviderCommandStore(
    HealingDbContext dbContext,
    HealingAuditService auditService,
    TimeProvider timeProvider) : IHumanProviderCommandStore
{
    public async ValueTask<HumanProviderCommandContext?> GetAsync(
        Guid commandId,
        CancellationToken cancellationToken = default)
    {
        var command = await dbContext.HumanCommands.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == commandId, cancellationToken);
        if (command is null)
            return null;

        var incident = await dbContext.HealingIncidents.AsNoTracking().SingleAsync(
            x => x.Id == command.IncidentId &&
                 x.WorkspaceId == command.WorkspaceId &&
                 x.ApplicationId == command.ApplicationId,
            cancellationToken);
        var attemptCount = incident.ActiveEpisodeId.HasValue
            ? await dbContext.RepairAttempts.AsNoTracking().CountAsync(x =>
                x.WorkspaceId == command.WorkspaceId &&
                x.ApplicationId == command.ApplicationId &&
                x.IncidentId == command.IncidentId &&
                x.EpisodeId == incident.ActiveEpisodeId,
                cancellationToken)
            : 0;
        var maximumAttempts = await dbContext.HealingConfigurations.AsNoTracking()
            .Where(x => x.WorkspaceId == command.WorkspaceId && x.ApplicationId == command.ApplicationId)
            .Select(x => (int?)x.DefaultAttemptLimit)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;
        return new HumanProviderCommandContext(command, attemptCount, maximumAttempts, incident.Status);
    }

    public async ValueTask CompleteAsync(
        HumanProviderCommandContext context,
        HumanProviderCommandAuthorization authorization,
        HumanProviderCommandDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(decision);

        await HealingPersistenceTransaction.ExecuteAsync(dbContext, async token =>
        {
            var command = await dbContext.HumanCommands.SingleAsync(x => x.Id == context.Command.Id, token);
            if (command.Status is HumanCommandStatus.Executed or HumanCommandStatus.Rejected or HumanCommandStatus.Failed)
                return true;

            var now = timeProvider.GetUtcNow();
            command.PlatformActorId = authorization.LinkedPlatformActorId?.ToString("D");
            command.ProviderPermissionSnapshotJson = JsonSerializer.Serialize(new
            {
                authorization.ProviderPermissionGranted,
                authorization.ProviderPermission,
                EvaluatedAt = now
            });
            command.WorkspacePermissionGranted = decision.RequiredPermission is not null &&
                                                 authorization.WorkspacePermissions.Contains(decision.RequiredPermission);
            command.ConfirmationId = authorization.ConfirmationId;
            command.Status = decision.Status;
            command.ResultCode = decision.ReasonCode;
            command.CompletedAt = decision.Status == HumanCommandStatus.Authorized ? null : now;

            if (decision.Executed)
                await ApplyCommandAsync(command, now, token);
            await dbContext.SaveChangesAsync(token);
            await auditService.AppendAsync(new HealingAuditWrite(
                command.WorkspaceId,
                "human-command",
                command.Id,
                decision.Executed ? "human-command-executed" :
                    decision.Authorized ? "human-command-authorized" : "human-command-rejected",
                decision.ReasonCode,
                HealingActorTypes.Human,
                authorization.LinkedPlatformActorId?.ToString("D") ?? command.ProviderActorId,
                command.IncidentId,
                authorization.ConfirmationId,
                null,
                null,
                null,
                new Dictionary<string, string?>
                {
                    ["outcomeCode"] = decision.ReasonCode,
                    ["status"] = decision.Status.ToString()
                }), token);
            return true;
        }, cancellationToken);
    }

    private async ValueTask ApplyCommandAsync(
        HumanCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var incident = await dbContext.HealingIncidents.SingleAsync(x =>
            x.Id == command.IncidentId &&
            x.WorkspaceId == command.WorkspaceId &&
            x.ApplicationId == command.ApplicationId,
            cancellationToken);
        if (command.Command == HealingHumanCommands.Retry)
        {
            var transition = incident.TryTransitionTo(HealingIncidentStatus.ReadyForRepair);
            if (!transition.Succeeded)
                throw new InvalidOperationException($"Retry is invalid while the incident is {transition.From}.");
        }
        else if (command.Command == HealingHumanCommands.Stop)
        {
            var attempts = await dbContext.RepairAttempts.Where(x =>
                    x.WorkspaceId == command.WorkspaceId &&
                    x.ApplicationId == command.ApplicationId &&
                    x.IncidentId == command.IncidentId &&
                    x.Status != RepairAttemptStatus.Succeeded &&
                    x.Status != RepairAttemptStatus.Failed &&
                    x.Status != RepairAttemptStatus.Stopped &&
                    x.Status != RepairAttemptStatus.Expired)
                .ToArrayAsync(cancellationToken);
            foreach (var attempt in attempts)
            {
                attempt.Status = RepairAttemptStatus.Stopped;
                attempt.OutcomeCode = "operator-stopped";
                attempt.CompletedAt = now;
            }

            var transition = incident.TryTransitionTo(HealingIncidentStatus.NeedsHuman);
            if (!transition.Succeeded && incident.Status != HealingIncidentStatus.NeedsHuman)
                throw new InvalidOperationException($"Stop is invalid while the incident is {transition.From}.");
            incident.NeedsHumanReason = NeedsHumanReason.OperatorStopped;
        }
    }
}
