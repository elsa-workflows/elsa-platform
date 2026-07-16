using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.GitHub;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.Api.Healing;

public sealed class PlatformGitHubReplayStore(HealingDbContext dbContext, TimeProvider? timeProvider = null)
    : IGitHubWorkloadReplayStore, IGitHubWebhookReplayStore
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    public async ValueTask<bool> TryAcceptAsync(
        GitHubWorkloadReplayRecord exchange,
        CancellationToken cancellationToken = default)
    {
        var authority = await (
            from attempt in dbContext.RepairAttempts.AsNoTracking()
            join binding in dbContext.SourceOwnershipBindings.AsNoTracking()
                on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.BindingId }
                equals new { binding.WorkspaceId, binding.ApplicationId, binding.Id }
            where attempt.WorkspaceId == exchange.WorkspaceId &&
                  attempt.ApplicationId == exchange.ApplicationId &&
                  attempt.Id == exchange.AttemptId &&
                  attempt.Status != RepairAttemptStatus.Succeeded &&
                  attempt.Status != RepairAttemptStatus.Failed &&
                  attempt.Status != RepairAttemptStatus.Stopped &&
                  attempt.Status != RepairAttemptStatus.Expired &&
                  binding.Status == SourceOwnershipBindingStatus.Active
            select new
            {
                attempt.NonceHash,
                binding.RepositoryProviderId,
                binding.RepositoryOwner,
                binding.RepositoryName,
                binding.WorkflowIdentity,
                binding.WorkflowReference,
                binding.WorkflowRevision
            }).SingleOrDefaultAsync(cancellationToken);
        var expectedScopes = exchange.Phase == "initial"
            ? new HashSet<string>([WorkloadCapabilityScopes.ReadEvidence, WorkloadCapabilityScopes.CreateProposal], StringComparer.Ordinal)
            : new HashSet<string>([WorkloadCapabilityScopes.FinalizeProposal, WorkloadCapabilityScopes.UploadResult], StringComparer.Ordinal);
        var nonceAuthorized = authority is not null && (exchange.Phase switch
        {
            "initial" => exchange.ProposalId is null && authority.NonceHash == exchange.NonceHash,
            "finalize" when exchange.ProposalId is { } proposalId => await dbContext.ManagedRepairProposals.AsNoTracking().AnyAsync(
                x => x.WorkspaceId == exchange.WorkspaceId &&
                     x.ApplicationId == exchange.ApplicationId &&
                     x.AttemptId == exchange.AttemptId &&
                     x.Id == proposalId &&
                     x.FinalizationNonceHash == exchange.NonceHash &&
                     x.Status == ManagedRepairProposalStatus.Ready &&
                     x.ExpiresAt > _timeProvider.GetUtcNow(),
                cancellationToken),
            _ => false
        });
        if (authority is null || !nonceAuthorized || !exchange.Scopes.SetEquals(expectedScopes) ||
            authority.RepositoryProviderId != exchange.RepositoryProviderId ||
            !string.Equals(authority.RepositoryOwner, exchange.RepositoryOwner, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(authority.RepositoryName, exchange.RepositoryName, StringComparison.OrdinalIgnoreCase) ||
            !exchange.WorkflowReference.Contains(authority.WorkflowIdentity, StringComparison.Ordinal) ||
            exchange.WorkflowRevision != authority.WorkflowRevision ||
            exchange.SourceReference != authority.WorkflowReference ||
            exchange.SourceRevision != authority.WorkflowRevision)
            return false;

        var entity = new WorkloadIdentityExchange
        {
            Id = Guid.NewGuid(),
            WorkspaceId = exchange.WorkspaceId,
            ApplicationId = exchange.ApplicationId,
            AttemptId = exchange.AttemptId,
            ProposalId = exchange.ProposalId,
            Phase = exchange.Phase,
            ScopesJson = System.Text.Json.JsonSerializer.Serialize(exchange.Scopes.Order(StringComparer.Ordinal)),
            Issuer = exchange.Issuer,
            Audience = exchange.Audience,
            Subject = exchange.Subject,
            RepositoryProviderId = exchange.RepositoryProviderId,
            RepositoryOwner = exchange.RepositoryOwner,
            RepositoryName = exchange.RepositoryName,
            WorkflowReference = exchange.WorkflowReference,
            WorkflowRevision = exchange.WorkflowRevision,
            SourceReference = exchange.SourceReference,
            SourceRevision = exchange.SourceRevision,
            WorkflowRunId = exchange.WorkflowRunId,
            WorkflowRunAttempt = exchange.WorkflowRunAttempt,
            ActorId = exchange.ActorId,
            JwtId = exchange.JwtId,
            NonceHash = exchange.NonceHash,
            IssuedAt = exchange.IssuedAt,
            ExpiresAt = exchange.ExpiresAt,
            Status = WorkloadIdentityExchangeStatus.Pending
        };
        dbContext.WorkloadIdentityExchanges.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(entity).State = EntityState.Detached;
            var replay = await dbContext.WorkloadIdentityExchanges.AsNoTracking().AnyAsync(
                x => x.JwtId == exchange.JwtId || x.NonceHash == exchange.NonceHash,
                cancellationToken);
            if (replay)
                return false;
            throw;
        }
    }

    public async ValueTask<GitHubWebhookReplayResult> TryAcceptAsync(
        GitHubWebhookReplayRecord delivery,
        CancellationToken cancellationToken = default)
    {
        var authorized = await dbContext.ProviderConnections.AsNoTracking().AnyAsync(
            x => x.WorkspaceId == delivery.WorkspaceId &&
                 x.Status == ProviderConnectionStatus.Active &&
                 x.InstallationId == delivery.InstallationId &&
                 x.RepositoryProviderId == delivery.RepositoryProviderId,
            cancellationToken);
        if (!authorized)
            return GitHubWebhookReplayResult.Conflict();

        var entity = new ProviderWebhookDelivery
        {
            Id = Guid.NewGuid(),
            WorkspaceId = delivery.WorkspaceId,
            ProviderDeliveryId = delivery.DeliveryId,
            InstallationId = delivery.InstallationId,
            RepositoryProviderId = delivery.RepositoryProviderId,
            Event = delivery.Event,
            Action = delivery.Action,
            BodyDigest = delivery.BodyDigest,
            ReceivedAt = delivery.ReceivedAt,
            Status = ProviderWebhookDeliveryStatus.Pending,
            OutcomeCode = "verified"
        };
        dbContext.ProviderWebhookDeliveries.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return GitHubWebhookReplayResult.Accepted();
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(entity).State = EntityState.Detached;
            var existingBodyDigest = await dbContext.ProviderWebhookDeliveries.AsNoTracking()
                .Where(
                x => x.WorkspaceId == delivery.WorkspaceId &&
                     x.ProviderDeliveryId == delivery.DeliveryId)
                .Select(x => x.BodyDigest)
                .SingleOrDefaultAsync(cancellationToken);
            if (existingBodyDigest is not null)
                return string.Equals(existingBodyDigest, delivery.BodyDigest, StringComparison.Ordinal)
                    ? GitHubWebhookReplayResult.ExactReplay(existingBodyDigest)
                    : GitHubWebhookReplayResult.Conflict(existingBodyDigest);
            throw;
        }
    }
}
