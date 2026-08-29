using ElsaControl.RuntimeBuilder.Abstractions.Plans;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;

namespace ElsaControl.RuntimeBuilder.Core.ReleaseManifests;

/// <summary>
/// Projects only the verified producer facts into the provider-neutral plan. Customer
/// policy, packages, configuration and provider capabilities remain owned by the caller.
/// </summary>
public static class ReleaseManifestPlanProjector
{
    public static ResolvedElsaApplicationPlan Project(
        ReleaseManifestAdmissionResult admission,
        ResolvedElsaApplicationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(plan);
        if (!admission.Accepted
            || admission.Manifest is null
            || admission.SignatureEvidence is null
            || admission.Reference is null
            || admission.Digest is null)
            throw new InvalidOperationException("Only an admitted signed release manifest can be projected.");

        ValidateExistingEvidence(plan.Evidence);
        var manifest = admission.Manifest;
        var topology = SelectTopology(manifest, admission.TopologyId);
        var components = SelectComponents(topology, admission.RegistryClass);
        var projected = plan with
        {
            Release = new(
                manifest.Distribution.Id,
                manifest.Distribution.ReleaseLine,
                manifest.Distribution.ReleaseVersion,
                manifest.Distribution.Source.Repository,
                manifest.Distribution.Source.Commit,
                admission.Reference,
                admission.Digest),
            Topology = new(topology.Id, components),
            Evidence = ProjectEvidence(admission, topology, plan.Evidence)
        };

        var findings = ResolvedElsaApplicationPlanValidator.Validate(projected);
        if (findings.Count > 0)
            throw new InvalidOperationException($"Admitted release manifest produced an invalid resolved plan: {findings[0].Code}.");

        return projected.Normalize();
    }

    private static ReleaseManifestTopology SelectTopology(CommercialReleaseManifest manifest, string? topologyId)
    {
        var topology = string.IsNullOrWhiteSpace(topologyId)
            ? manifest.Topologies.FirstOrDefault()
            : manifest.Topologies.FirstOrDefault(x => string.Equals(x.Id, topologyId, StringComparison.OrdinalIgnoreCase));
        return topology ?? throw new InvalidOperationException("The admitted manifest does not contain the selected topology.");
    }

    private static IReadOnlyList<ResolvedElsaComponent> SelectComponents(ReleaseManifestTopology topology, string registryClass)
    {
        var groups = topology.Images
            .Where(x => x is not null)
            .GroupBy(x => x.ComponentId ?? topology.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var components = new List<ResolvedElsaComponent>(groups.Count);

        foreach (var group in groups)
        {
            var image = group.SingleOrDefault(x => string.Equals(x.RegistryClass, registryClass, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Topology component {group.Key} has no image for registry class {registryClass}.");
            var runtimeKinds = topology.RuntimeKinds.ToArray();
            var roles = image.Roles is { Count: > 0 }
                ? image.Roles.ToArray()
                : runtimeKinds.Select(ToRole).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var capabilities = image.Capabilities is { Count: > 0 }
                ? image.Capabilities.ToArray()
                : topology.Compatibility.RuntimeCapabilities.ToArray();
            var endpoints = image.Endpoints is { Count: > 0 }
                ? image.Endpoints.Select(ToEndpoint).ToArray()
                : (topology.Endpoints ?? new Dictionary<string, string>()).Select(x => new ResolvedElsaEndpoint(x.Key, "https", 443, "public", true, x.Value)).ToArray();

            components.Add(new(
                group.Key,
                roles,
                new(
                    image.RegistryClass,
                    RepositoryFromReference(image.Reference),
                    StandardizeImageReference(image.Reference),
                    image.IndexDigest,
                    image.PlatformDigests),
                runtimeKinds,
                endpoints,
                capabilities,
                image.CompanionComponentId));
        }

        return components;
    }

    private static IReadOnlyList<ResolvedPlanEvidence> ProjectEvidence(
        ReleaseManifestAdmissionResult admission,
        ReleaseManifestTopology topology,
        IReadOnlyList<ResolvedPlanEvidence> existing)
    {
        var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ReleaseManifestEvidenceKinds.Manifest,
            ReleaseManifestEvidenceKinds.Signature,
            ReleaseManifestEvidenceKinds.Sbom,
            ReleaseManifestEvidenceKinds.Provenance,
            ReleaseManifestEvidenceKinds.VulnerabilityScan
        };
        // Invalid legacy/deserialized entries are not unrelated evidence and must not
        // ride through projection or cause a null Kind dereference.
        var evidence = (existing ?? [])
            .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Kind) && !kinds.Contains(x.Kind))
            .ToList();
        var supplyChain = topology.SupplyChain;
        evidence.Add(new(ReleaseManifestEvidenceKinds.Manifest, admission.Reference!, admission.Digest, "Verified producer release manifest."));
        evidence.Add(new(ReleaseManifestEvidenceKinds.Signature, admission.SignatureEvidence!.Reference, admission.SignatureEvidence.Digest, "Verified release-manifest signature evidence."));
        evidence.Add(new(ReleaseManifestEvidenceKinds.Sbom, supplyChain.Sbom!.Uri, EvidenceDigest(supplyChain.Sbom.Digest, supplyChain.Sbom.Uri), "Verified release SBOM evidence."));
        evidence.Add(new(ReleaseManifestEvidenceKinds.Provenance, supplyChain.Provenance!.Uri, EvidenceDigest(supplyChain.Provenance.Digest, supplyChain.Provenance.Uri), "Verified release provenance evidence."));

        var scan = supplyChain.VulnerabilityScan!;
        evidence.Add(new(ReleaseManifestEvidenceKinds.VulnerabilityScan, scan.Report, EvidenceDigest(scan.Digest, scan.Report), "Producer-retained release vulnerability-scan evidence."));
        return evidence;
    }

    private static string EvidenceDigest(string? digest, string reference) =>
        ReleaseManifestAdmissionService.IsDigest(digest)
            ? digest!
            : ReleaseManifestAdmissionService.ExtractDigest(reference)
              ?? throw new InvalidOperationException("Admitted evidence must retain a sha256 digest.");

    private static void ValidateExistingEvidence(IReadOnlyList<ResolvedPlanEvidence>? existing)
    {
        foreach (var evidence in existing ?? [])
        {
            if (evidence is null
                || !ReleaseManifestAdmissionService.IsSafeRetainedReference(evidence.Reference)
                || string.IsNullOrWhiteSpace(evidence.Description)
                || evidence.Description.Any(char.IsControl))
                throw new InvalidOperationException("Existing plan evidence must be a safe locator with a non-sensitive description.");
        }
    }

    private static ResolvedElsaEndpoint ToEndpoint(ReleaseManifestEndpoint endpoint) =>
        new(endpoint.Name, endpoint.Protocol, endpoint.Port, endpoint.Visibility, endpoint.RequiresTls, endpoint.Path);

    private static string ToRole(string runtimeKind) =>
        runtimeKind.StartsWith("elsa.", StringComparison.OrdinalIgnoreCase)
            ? runtimeKind["elsa.".Length..]
            : runtimeKind;

    private static string RepositoryFromReference(string reference)
    {
        var repository = reference[..reference.IndexOf('@')];
        var schemeMarker = repository.IndexOf("://", StringComparison.Ordinal);
        return schemeMarker >= 0 ? repository[(schemeMarker + 3)..] : repository;
    }

    private static string StandardizeImageReference(string reference) =>
        $"{RepositoryFromReference(reference)}@{ReleaseManifestAdmissionService.ExtractDigest(reference)
            ?? throw new InvalidOperationException("Admitted image references must retain a sha256 digest.")}";
}
