using System.Security.Cryptography;
using System.Text;
using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.RuntimeBuilder.Abstractions;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseCatalog;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;

/// <summary>
/// Reconstructs the resolver input for a claimed managed-instance operation from
/// the durable catalog projection. The request body and producer payload are not
/// available at this boundary; only the exact admitted catalog row is projected.
/// </summary>
public sealed class CatalogElsaInstanceLifecycleResolutionInputSource(
    CatalogDbContext dbContext,
    IGovernedReleaseCatalogStore releaseCatalog) : IElsaInstanceLifecycleResolutionInputSource
{
    private const string PlanAuthority = "https://control.example.invalid";

    public async Task<ElsaInstanceLifecycleResolutionInput?> GetAsync(
        ElsaInstance instance,
        ElsaInstanceOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.InstanceId != instance.Id || operation.Action == ElsaInstanceOperationAction.Delete)
            return null;

        var candidates = await releaseCatalog.QueryAsync(new GovernedReleaseCatalogQuery(
            DistributionId: instance.ReleaseIntent.DistributionId,
            ReleaseLine: instance.ReleaseIntent.ReleaseLine,
            ReleaseVersion: instance.ReleaseIntent.RequestedVersion,
            Channel: instance.ReleaseIntent.Channel,
            RegistryClass: "paid",
            TopologyId: instance.ApplicationIntent.TopologyId), cancellationToken);

        // An omitted patch is intentionally fail-closed until the catalog has one
        // unambiguous admitted row for this topology. Choosing by display order
        // would make a restart resolve a different immutable release.
        if (candidates.Count != 1)
            return null;

        var entry = candidates[0];
        if (!Matches(instance, entry) || !TryBuildManifest(entry, out var admission))
            return null;

        var target = await FindDeploymentTargetAsync(instance, operation, cancellationToken);
        if (target is null)
            return null;

        var planId = PlanId(entry.ManifestDigest);
        var planUri = $"{PlanAuthority}/api/workspaces/{instance.WorkspaceId:D}/instances/{instance.Id:D}/resolved-plans/{planId}";
        var builderIntent = new RuntimeBuilderIntent(
            new RuntimeImageSelection("elsa-instance", null, null, null),
            [], [], [], null);
        var request = new ElsaInstancePlanResolutionRequest(
            instance.Intent,
            builderIntent,
            admission,
            planId,
            planUri,
            instance.WorkspaceId);
        return new ElsaInstanceLifecycleResolutionInput(request, target);
    }

    private async Task<ElsaInstanceLifecycleDeploymentTarget?> FindDeploymentTargetAsync(
        ElsaInstance instance,
        ElsaInstanceOperation operation,
        CancellationToken cancellationToken)
    {
        var environments = await dbContext.DeploymentEnvironments
            .AsNoTracking()
            .Include(x => x.Engines)
            .Where(x => x.WorkspaceId == instance.WorkspaceId && x.ElsaInstanceId == instance.Id)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        if (environments.Count != 1)
            return null;

        var environment = environments[0];
        var engine = environment.Engines.OrderBy(x => x.Id).FirstOrDefault();
        if (engine is null)
            return null;

        var actorAccountId = await dbContext.ElsaInstanceAuditEvents
            .AsNoTracking()
            .Where(x => x.WorkspaceId == instance.WorkspaceId &&
                        x.InstanceId == instance.Id &&
                        x.OperationId == operation.Id &&
                        x.EventType == "lifecycle.accepted")
            .Select(x => x.ActorAccountId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!actorAccountId.HasValue || actorAccountId.Value == Guid.Empty)
            return null;

        // The deployment-run contract requires a confirmation identity. The
        // confirmation is a stable operation-scoped placeholder because the
        // managed provider path does not expose a provider confirmation token.
        // Preserve the authenticated actor from the acceptance audit event.
        var confirmationId = DeterministicGuid(operation.Id, "confirmation");
        var sourceRevisionId = environment.DesiredRevisionId ?? DeterministicGuid(instance.Id, "revision");
        return new(
            environment.ApplicationId,
            environment.Id,
            engine.Id,
            sourceRevisionId,
            confirmationId,
            actorAccountId.Value);
    }

    private static bool Matches(ElsaInstance instance, GovernedReleaseCatalogEntry entry) =>
        string.Equals(entry.Distribution.Id, instance.ReleaseIntent.DistributionId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(entry.Distribution.ReleaseLine, instance.ReleaseIntent.ReleaseLine, StringComparison.OrdinalIgnoreCase) &&
        (instance.ReleaseIntent.RequestedVersion is null ||
        string.Equals(entry.Distribution.ReleaseVersion, instance.ReleaseIntent.RequestedVersion, StringComparison.OrdinalIgnoreCase)) &&
        string.Equals(entry.Distribution.Channel, instance.ReleaseIntent.Channel, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(entry.Topology.Id, instance.ApplicationIntent.TopologyId, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(entry.CatalogLifecycle);

    private static bool TryBuildManifest(
        GovernedReleaseCatalogEntry entry,
        out ReleaseManifestAdmissionResult admission)
    {
        try
        {
            var topology = entry.Topology;
            var images = topology.Components
                .Select(component => new ReleaseManifestImage(
                    entry.RegistryClass,
                    component.ImageReference,
                    component.ImageDigest,
                    component.PlatformDigests,
                    component.Id,
                    component.Roles,
                    component.Capabilities,
                    component.Endpoints.Select(endpoint => new ReleaseManifestEndpoint(
                        endpoint.Name,
                        endpoint.Protocol,
                        endpoint.Port,
                        endpoint.Visibility,
                        endpoint.RequiresTls,
                        endpoint.Path)).ToArray(),
                    component.CompanionComponentId))
                .ToArray();
            if (images.Length == 0)
            {
                admission = null!;
                return false;
            }

            var manifest = new CommercialReleaseManifest(
                entry.SchemaVersion,
                new(
                    entry.Distribution.Id,
                    entry.Distribution.Generation,
                    entry.Distribution.ReleaseLine,
                    entry.Distribution.ReleaseVersion,
                    entry.Distribution.Channel,
                    entry.Distribution.ProducerLifecycle,
                    new(
                        entry.Distribution.SourceRepository,
                        entry.Distribution.SourceCommit,
                        "release-manifest",
                        entry.Distribution.SourceRunId),
                    entry.Distribution.Edition),
                [new ReleaseManifestTopology(
                    topology.Id,
                    topology.RuntimeKinds,
                    images,
                    topology.ComponentVersions.ToDictionary(x => x.Id, x => x.Version, StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    new(topology.PackageManifestSchema, topology.Capabilities),
                    new(
                        topology.Evidence.FirstOrDefault(x => x.Kind == ReleaseManifestEvidenceKinds.Sbom) is { } sbom
                            ? new ReleaseManifestAttestation(sbom.Reference, sbom.Digest)
                            : null,
                        topology.Evidence.FirstOrDefault(x => x.Kind == ReleaseManifestEvidenceKinds.Provenance) is { } provenance
                            ? new ReleaseManifestAttestation(provenance.Reference, provenance.Digest)
                            : null,
                        [],
                        topology.Evidence.FirstOrDefault(x => x.Kind == ReleaseManifestEvidenceKinds.VulnerabilityScan) is { } scan
                            ? new ReleaseManifestVulnerabilityScan("catalog", "governed-policy", scan.Reference, scan.Digest)
                            : null))]);

            admission = new ReleaseManifestAdmissionResult(
                true,
                entry.ManifestReference,
                entry.ManifestDigest,
                manifest,
                new ReleaseManifestAdmissionEvidence(entry.SignatureEvidenceReference, entry.SignatureEvidenceDigest),
                entry.RegistryClass,
                topology.Id,
                [],
                entry.PayloadDigest);
            return true;
        }
        catch (ArgumentException)
        {
            admission = null!;
            return false;
        }
    }

    private static string PlanId(string manifestDigest)
    {
        var digest = manifestDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? manifestDigest["sha256:".Length..]
            : manifestDigest;
        return $"release-{digest.ToLowerInvariant()}";
    }

    private static Guid DeterministicGuid(Guid seed, string purpose)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"elsa-control:{purpose}:{seed:D}"));
        return new Guid(bytes[..16]);
    }
}
