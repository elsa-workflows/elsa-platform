using System.Text.Json;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Security;
using Elsa.Platform.Healing.GitHub;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.Api.Healing;

public interface IPlatformHealingGitHubWebhookProcessor
{
    ValueTask<string> ProcessAsync(
        ProviderConnection connection,
        string deliveryId,
        string eventName,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default);
}

public sealed class PlatformHealingGitHubWebhookProcessor(
    HealingDbContext dbContext,
    GitHubWebhookProcessor processor,
    TimeProvider timeProvider,
    HealingAuditService? auditService = null) : IPlatformHealingGitHubWebhookProcessor
{
    private readonly HealingAuditService _auditService = auditService ?? new(new HealingStore(dbContext), timeProvider);

    public async ValueTask<string> ProcessAsync(
        ProviderConnection connection,
        string deliveryId,
        string eventName,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        var delivery = await dbContext.ProviderWebhookDeliveries.SingleOrDefaultAsync(
            x => x.WorkspaceId == connection.WorkspaceId && x.ProviderDeliveryId == deliveryId,
            cancellationToken);
        if (delivery is null)
            return "delivery-missing";
        if (delivery.Status == ProviderWebhookDeliveryStatus.Completed)
            return delivery.OutcomeCode ?? "processed-replay";

        var observation = processor.Parse(eventName, body);
        if (observation is null || observation.RepositoryId.ToString(System.Globalization.CultureInfo.InvariantCulture) != connection.RepositoryProviderId)
            return await CompleteAsync(delivery, ProviderWebhookDeliveryStatus.Rejected, "observation-rejected", cancellationToken);

        var outcome = observation switch
        {
            GitHubPullRequestObservation pullRequest => await ApplyPullRequestAsync(connection, pullRequest, cancellationToken),
            GitHubCheckObservation check => await ApplyCheckAsync(connection, check, cancellationToken),
            GitHubIssueCommandObservation command => await RecordCommandRequestAsync(connection, deliveryId, command, cancellationToken),
            _ => "observation-ignored"
        };
        return await CompleteAsync(delivery, ProviderWebhookDeliveryStatus.Completed, outcome, cancellationToken);
    }

    private async ValueTask<string> ApplyPullRequestAsync(
        ProviderConnection connection,
        GitHubPullRequestObservation observation,
        CancellationToken cancellationToken)
    {
        var authority = await (
            from pullRequest in dbContext.RepairPullRequests
            join attempt in dbContext.RepairAttempts
                on new { pullRequest.WorkspaceId, pullRequest.ApplicationId, Id = pullRequest.AttemptId }
                equals new { attempt.WorkspaceId, attempt.ApplicationId, attempt.Id }
            join incident in dbContext.HealingIncidents
                on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.IncidentId }
                equals new { incident.WorkspaceId, incident.ApplicationId, incident.Id }
            join episode in dbContext.IncidentEpisodes
                on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.EpisodeId }
                equals new { episode.WorkspaceId, episode.ApplicationId, episode.Id }
            where pullRequest.WorkspaceId == connection.WorkspaceId &&
                  pullRequest.ProviderConnectionId == connection.Id &&
                  pullRequest.Number == observation.Number
            select new
            {
                PullRequest = pullRequest,
                Attempt = attempt,
                Incident = incident,
                IsCurrentEpisode = incident.ActiveEpisodeId == attempt.EpisodeId &&
                                   episode.Outcome == IncidentEpisodeOutcome.Active
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (authority is null || observation.HeadReference != $"elsa-healing/{authority.Attempt.Id:N}")
            return "pull-request-unbound";

        if (!string.Equals(authority.PullRequest.HeadRevision, observation.HeadRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(authority.PullRequest.BaseRevision, observation.BaseRevision, StringComparison.OrdinalIgnoreCase))
        {
            if (authority.PullRequest.MergeState is not (PullRequestMergeState.Merged or PullRequestMergeState.Closed))
            {
                authority.PullRequest.ClosureReason = "provider-revision-mismatch";
                authority.PullRequest.Version = Guid.NewGuid().ToByteArray();
                _ = TryTransitionIncident(
                    authority.Incident,
                    authority.IsCurrentEpisode,
                    HealingIncidentStatus.NeedsHuman,
                    NeedsHumanReason.PolicyBlocked);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            return "pull-request-revision-mismatch";
        }

        var pullRequestChanged = authority.PullRequest.IsDraft != observation.IsDraft;
        authority.PullRequest.IsDraft = observation.IsDraft;
        var attemptChanged = false;
        var incidentChanged = false;
        if (observation.IsMerged)
        {
            pullRequestChanged |= authority.PullRequest.MergeState != PullRequestMergeState.Merged ||
                                  authority.PullRequest.MergedRevision != observation.MergeRevision ||
                                  authority.PullRequest.MergedAt is null;
            authority.PullRequest.MergeState = PullRequestMergeState.Merged;
            authority.PullRequest.MergedRevision = observation.MergeRevision;
            authority.PullRequest.MergedAt ??= observation.MergedAt ?? timeProvider.GetUtcNow();
            if (!IsTerminal(authority.Attempt.Status))
            {
                authority.Attempt.Status = RepairAttemptStatus.Succeeded;
                authority.Attempt.CompletedAt ??= authority.PullRequest.MergedAt;
                attemptChanged = true;
            }
            incidentChanged = TryTransitionIncident(
                authority.Incident,
                authority.IsCurrentEpisode,
                HealingIncidentStatus.Merged);
        }
        else if (observation.Action == "closed")
        {
            if (authority.PullRequest.MergeState != PullRequestMergeState.Merged)
            {
                pullRequestChanged |= authority.PullRequest.MergeState != PullRequestMergeState.Closed ||
                                      authority.PullRequest.ClosureReason != "provider-closed" ||
                                      authority.PullRequest.MergePolicyEvaluationId is not null;
                authority.PullRequest.MergeState = PullRequestMergeState.Closed;
                authority.PullRequest.MergePolicyEvaluationId = null;
                authority.PullRequest.ClosureReason = "provider-closed";
                if (!IsTerminal(authority.Attempt.Status))
                {
                    authority.Attempt.Status = RepairAttemptStatus.Stopped;
                    authority.Attempt.OutcomeCode = "pull-request-closed";
                    authority.Attempt.CompletedAt = timeProvider.GetUtcNow();
                    attemptChanged = true;
                }
                incidentChanged = TryTransitionIncident(
                    authority.Incident,
                    authority.IsCurrentEpisode,
                    HealingIncidentStatus.NeedsHuman,
                    NeedsHumanReason.OperatorStopped);
            }
        }
        else if (authority.PullRequest.MergeState is not (PullRequestMergeState.Merged or PullRequestMergeState.Closed) &&
                 !IsTerminal(authority.Attempt.Status))
        {
            pullRequestChanged |= authority.PullRequest.MergeState != PullRequestMergeState.Open ||
                                  authority.PullRequest.MergePolicyEvaluationId is not null;
            authority.PullRequest.MergeState = PullRequestMergeState.Open;
            authority.PullRequest.MergePolicyEvaluationId = null;
        }

        if (pullRequestChanged)
            authority.PullRequest.Version = Guid.NewGuid().ToByteArray();
        if (attemptChanged)
            authority.Attempt.Version = Guid.NewGuid().ToByteArray();
        if (pullRequestChanged || attemptChanged || incidentChanged)
            await dbContext.SaveChangesAsync(cancellationToken);
        return observation.IsMerged ? "pull-request-merged" : "pull-request-observed";
    }

    private async ValueTask<string> ApplyCheckAsync(
        ProviderConnection connection,
        GitHubCheckObservation observation,
        CancellationToken cancellationToken)
    {
        var candidates = await dbContext.RepairPullRequests
            .Where(x => x.WorkspaceId == connection.WorkspaceId &&
                        x.ProviderConnectionId == connection.Id &&
                        x.HeadRevision == observation.HeadRevision)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (candidates.Length != 1)
            return "check-unbound";
        var pullRequest = candidates[0];
        var checks = ParseChecks(pullRequest.CheckSnapshotJson)
            .Where(x => x.Name != observation.Name)
            .Append(new SafeCheck(observation.Name, observation.Status, observation.Conclusion, observation.ObservedAt))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .Take(128)
            .ToArray();
        pullRequest.CheckSnapshotJson = JsonSerializer.Serialize(checks);
        pullRequest.MergePolicyEvaluationId = null;
        if (pullRequest.MergeState == PullRequestMergeState.MergeRequested)
            pullRequest.MergeState = PullRequestMergeState.Open;
        pullRequest.Version = Guid.NewGuid().ToByteArray();
        await dbContext.SaveChangesAsync(cancellationToken);
        return "check-observed";
    }

    private async ValueTask<string> RecordCommandRequestAsync(
        ProviderConnection connection,
        string deliveryId,
        GitHubIssueCommandObservation observation,
        CancellationToken cancellationToken)
    {
        var projections = await dbContext.RepairWorkItemProjections.AsNoTracking()
            .Where(x => x.WorkspaceId == connection.WorkspaceId &&
                        x.ProviderConnectionId == connection.Id &&
                        x.Number == observation.IssueNumber &&
                        x.ProjectionStatus == WorkItemProjectionStatus.Current)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (projections.Length != 1)
            return "command-unbound";
        var projection = projections[0];
        var idempotencyKey = $"github:{deliveryId}";
        if (await dbContext.HumanCommands.AsNoTracking().AnyAsync(
                x => x.WorkspaceId == projection.WorkspaceId &&
                     x.IncidentId == projection.IncidentId &&
                     x.IdempotencyKey == idempotencyKey,
                cancellationToken))
            return "command-request-replay";
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var command = new HumanCommand
        {
            Id = Guid.NewGuid(),
            WorkspaceId = projection.WorkspaceId,
            ApplicationId = projection.ApplicationId,
            IncidentId = projection.IncidentId,
            IdempotencyKey = idempotencyKey,
            Command = observation.Command,
            ProviderActorId = observation.ProviderActorId,
            ProviderActorLogin = observation.ProviderActorLogin,
            ProviderPermissionSnapshotJson = JsonSerializer.Serialize(new
            {
                observation.AuthorAssociation,
                observation.ProviderActorLogin,
                Verification = "pending-provider-permission-query"
            }),
            WorkspacePermissionGranted = false,
            Status = HumanCommandStatus.Pending,
            ResultCode = "dual-authorization-pending",
            RequestedAt = timeProvider.GetUtcNow(),
            Version = Guid.NewGuid().ToByteArray()
        };
        dbContext.HumanCommands.Add(command);
        await dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.AppendAsync(new HealingAuditWrite(
            projection.WorkspaceId,
            "human-command",
            command.Id,
            "human-command-received",
            "verified-provider-webhook",
            HealingActorTypes.SourceProvider,
            observation.ProviderActorId,
            projection.IncidentId,
            null,
            null,
            null,
            null,
            new Dictionary<string, string?>
            {
                ["operationType"] = observation.Command,
                ["status"] = HumanCommandStatus.Pending.ToString()
            }), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return "command-request-recorded";
    }

    private async ValueTask<string> CompleteAsync(
        ProviderWebhookDelivery delivery,
        ProviderWebhookDeliveryStatus status,
        string outcome,
        CancellationToken cancellationToken)
    {
        delivery.Status = status;
        delivery.OutcomeCode = outcome;
        delivery.ProcessedAt = timeProvider.GetUtcNow();
        delivery.Version = Guid.NewGuid().ToByteArray();
        await dbContext.SaveChangesAsync(cancellationToken);
        return outcome;
    }

    private static IReadOnlyList<SafeCheck> ParseChecks(string json)
    {
        try { return JsonSerializer.Deserialize<SafeCheck[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static bool IsTerminal(RepairAttemptStatus status) =>
        status is RepairAttemptStatus.Succeeded or RepairAttemptStatus.Failed or RepairAttemptStatus.Stopped or RepairAttemptStatus.Expired;

    private static bool TryTransitionIncident(
        HealingIncident incident,
        bool isCurrentEpisode,
        HealingIncidentStatus target,
        NeedsHumanReason? needsHumanReason = null)
    {
        if (!isCurrentEpisode)
            return false;
        var transition = incident.TryTransitionTo(target);
        if (!transition.Succeeded && transition.ReasonCode != HealingTransitionReasonCodes.AlreadyInState)
            return false;
        var reasonChanged = target switch
        {
            HealingIncidentStatus.NeedsHuman => incident.NeedsHumanReason != needsHumanReason,
            HealingIncidentStatus.Merged => incident.NeedsHumanReason is not null,
            _ => false
        };
        if (needsHumanReason is not null)
            incident.NeedsHumanReason = needsHumanReason;
        else if (target == HealingIncidentStatus.Merged)
            incident.NeedsHumanReason = null;
        if (transition.Succeeded || reasonChanged)
            incident.Version = Guid.NewGuid().ToByteArray();
        return transition.Succeeded || reasonChanged;
    }

    private sealed record SafeCheck(string Name, string Status, string? Conclusion, DateTimeOffset ObservedAt);
}
