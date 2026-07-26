using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Healing.Core.Repairs;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Api.Healing;

public sealed record TrustedDeploymentSafetyCapabilitySnapshot(
    string Digest,
    RepairPolicyObservationState State,
    string ReasonCode);

/// <summary>
/// Resolves rollback authority only from Control-owned deployment configuration. Repair workflow output is
/// intentionally outside this interface and cannot assert rollout-stop or rollback availability.
/// </summary>
public interface ITrustedDeploymentSafetyCapabilitySource
{
    ValueTask<TrustedDeploymentSafetyCapabilitySnapshot> GetAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid episodeId,
        CancellationToken cancellationToken = default);
}

public sealed class TrustedDeploymentSafetyCapabilitySource(
    HealingDbContext dbContext,
    IWorkspaceDeploymentStore deploymentStore) : ITrustedDeploymentSafetyCapabilitySource
{
    public async ValueTask<TrustedDeploymentSafetyCapabilitySnapshot> GetAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid episodeId,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty || applicationId == Guid.Empty || episodeId == Guid.Empty)
            throw new ArgumentException("Workspace, application, and episode identities are required.");

        var affectedEnvironmentIds = await dbContext.EnvironmentImpacts.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId &&
                        x.ApplicationId == applicationId &&
                        x.EpisodeId == episodeId &&
                        x.ClosedAt == null)
            .Select(x => x.EnvironmentId)
            .Distinct()
            .OrderBy(x => x)
            .ToArrayAsync(cancellationToken);
        if (affectedEnvironmentIds.Length == 0)
            return Snapshot(affectedEnvironmentIds, [], RepairPolicyObservationState.Missing, "affected-environment-missing");

        var cockpit = await deploymentStore.GetCockpitAsync(workspaceId, cancellationToken);
        var applications = cockpit.Applications
            .Where(x => string.Equals(x.Id, applicationId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (applications.Length == 0)
            return Snapshot(affectedEnvironmentIds, [], RepairPolicyObservationState.Missing, "trusted-deployment-application-missing");
        if (applications.Length > 1)
            return Snapshot(affectedEnvironmentIds, [], RepairPolicyObservationState.Ambiguous, "trusted-deployment-application-ambiguous");

        var observations = new List<DeploymentSafetyObservation>(affectedEnvironmentIds.Length);
        foreach (var environmentId in affectedEnvironmentIds)
        {
            var environments = applications[0].Environments
                .Where(x => string.Equals(x.Id, environmentId.ToString("D"), StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            if (environments.Length == 0)
                return Snapshot(affectedEnvironmentIds, observations, RepairPolicyObservationState.Missing, "trusted-deployment-environment-missing");
            if (environments.Length > 1)
                return Snapshot(affectedEnvironmentIds, observations, RepairPolicyObservationState.Ambiguous, "trusted-deployment-environment-ambiguous");

            var environment = environments[0];
            observations.Add(new(
                environmentId,
                environment.TierStatus,
                (environment.TierCapabilities ?? DeploymentTierService.DefaultCapabilitiesByLegacyTier[environment.Tier])
                    .Contains(DeploymentTierCapabilities.RollbackEnabled, StringComparer.Ordinal)));
        }

        if (observations.Any(x => !string.Equals(
                x.TierStatus,
                DeploymentTierStatus.Active.ToString(),
                StringComparison.Ordinal)))
            return Snapshot(affectedEnvironmentIds, observations, RepairPolicyObservationState.Failed, "trusted-deployment-tier-inactive");
        if (observations.Any(x => !x.RollbackAvailable))
            return Snapshot(affectedEnvironmentIds, observations, RepairPolicyObservationState.Failed, "trusted-deployment-rollback-unavailable");

        return Snapshot(affectedEnvironmentIds, observations, RepairPolicyObservationState.Satisfied, "trusted-deployment-rollback-available");
    }

    private static TrustedDeploymentSafetyCapabilitySnapshot Snapshot(
        IReadOnlyList<Guid> affectedEnvironmentIds,
        IReadOnlyList<DeploymentSafetyObservation> observations,
        RepairPolicyObservationState state,
        string reasonCode)
    {
        var canonicalJson = JsonSerializer.Serialize(new
        {
            AffectedEnvironments = affectedEnvironmentIds.Order(),
            Environments = observations.OrderBy(x => x.EnvironmentId).Select(x => new
            {
                x.EnvironmentId,
                x.TierStatus,
                x.RollbackAvailable
            }),
            State = state.ToString(),
            ReasonCode = reasonCode
        });
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
        return new(digest, state, reasonCode);
    }

    private sealed record DeploymentSafetyObservation(
        Guid EnvironmentId,
        string TierStatus,
        bool RollbackAvailable);
}
