using ElsaControl.RuntimeBuilder.Abstractions.Plans;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;

namespace ElsaControl.RuntimeBuilder.Core.ReleaseManifests;

/// <summary>
/// Projects only the verified producer facts into the provider-neutral plan. Customer
/// policy, packages, configuration and provider capabilities remain owned by the caller.
/// </summary>
public static class ReleaseManifestPlanProjector
{
    private const string GenericEvidenceDescription = "Retained immutable evidence.";
    private static readonly IReadOnlyDictionary<string, string> FixedEvidenceDescriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ReleaseManifestEvidenceKinds.Manifest] = "Verified producer release manifest.",
            [ReleaseManifestEvidenceKinds.Signature] = "Verified release-manifest signature evidence.",
            [ReleaseManifestEvidenceKinds.Sbom] = "Verified release SBOM evidence.",
            [ReleaseManifestEvidenceKinds.Provenance] = "Verified release provenance evidence.",
            [ReleaseManifestEvidenceKinds.VulnerabilityScan] = "Producer-retained release vulnerability-scan evidence."
        };

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
            throw new ReleaseManifestProjectionValidationException("Only an admitted signed release manifest can be projected.");

        ValidateExistingEvidence(plan.Evidence);
        var manifest = admission.Manifest;
        ValidateProjectionShape(manifest);
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
            throw new ReleaseManifestProjectionValidationException($"Admitted release manifest produced an invalid resolved plan: {findings[0].Code}.");

        return projected.Normalize();
    }

    internal static void ValidateProjectionShape(CommercialReleaseManifest manifest)
    {
        if (manifest.Distribution is null || manifest.Distribution.Source is null || manifest.Topologies is null)
            throw new ReleaseManifestProjectionValidationException("The admitted release manifest is structurally incomplete.");

        foreach (var topology in manifest.Topologies)
        {
            if (topology is null
                || topology.RuntimeKinds is null
                || topology.Images is null
                || topology.Compatibility is null
                || topology.Compatibility.RuntimeCapabilities is null
                || topology.SupplyChain is null
                || topology.SupplyChain.Sbom is null
                || topology.SupplyChain.Provenance is null
                || topology.SupplyChain.VulnerabilityScan is null)
                throw new ReleaseManifestProjectionValidationException("The admitted release manifest is structurally incomplete.");
        }
    }

    private static ReleaseManifestTopology SelectTopology(CommercialReleaseManifest manifest, string? topologyId)
    {
        var topology = string.IsNullOrWhiteSpace(topologyId)
            ? manifest.Topologies.FirstOrDefault()
            : manifest.Topologies.FirstOrDefault(x => string.Equals(x.Id, topologyId, StringComparison.OrdinalIgnoreCase));
        return topology ?? throw new ReleaseManifestProjectionValidationException("The admitted manifest does not contain the selected topology.");
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
                ?? throw new ReleaseManifestProjectionValidationException($"Topology component {group.Key} has no image for registry class {registryClass}.");
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
        evidence.Add(new(ReleaseManifestEvidenceKinds.Manifest, admission.Reference!, admission.Digest, FixedEvidenceDescriptions[ReleaseManifestEvidenceKinds.Manifest]));
        evidence.Add(new(ReleaseManifestEvidenceKinds.Signature, admission.SignatureEvidence!.Reference, admission.SignatureEvidence.Digest, FixedEvidenceDescriptions[ReleaseManifestEvidenceKinds.Signature]));
        evidence.Add(new(ReleaseManifestEvidenceKinds.Sbom, supplyChain.Sbom!.Uri, EvidenceDigest(supplyChain.Sbom.Digest, supplyChain.Sbom.Uri), FixedEvidenceDescriptions[ReleaseManifestEvidenceKinds.Sbom]));
        evidence.Add(new(ReleaseManifestEvidenceKinds.Provenance, supplyChain.Provenance!.Uri, EvidenceDigest(supplyChain.Provenance.Digest, supplyChain.Provenance.Uri), FixedEvidenceDescriptions[ReleaseManifestEvidenceKinds.Provenance]));

        var scan = supplyChain.VulnerabilityScan!;
        evidence.Add(new(ReleaseManifestEvidenceKinds.VulnerabilityScan, scan.Report, EvidenceDigest(scan.Digest, scan.Report), FixedEvidenceDescriptions[ReleaseManifestEvidenceKinds.VulnerabilityScan]));
        return evidence;
    }

    private static string EvidenceDigest(string? digest, string reference) =>
        ReleaseManifestAdmissionService.IsDigest(digest)
            ? digest!
            : ReleaseManifestAdmissionService.ExtractDigest(reference)
              ?? throw new ReleaseManifestProjectionValidationException("Admitted evidence must retain a sha256 digest.");

    private static void ValidateExistingEvidence(IReadOnlyList<ResolvedPlanEvidence>? existing)
    {
        foreach (var evidence in existing ?? [])
        {
            if (evidence is null)
                throw new ReleaseManifestProjectionValidationException("Existing plan evidence cannot contain null items.");

            // Legacy entries without a kind are discarded by ProjectEvidence. They
            // cannot be retained, but remain tolerated so old plans can be upgraded.
            if (string.IsNullOrWhiteSpace(evidence.Kind))
                continue;

            if (!ReleaseManifestAdmissionService.IsDigest(evidence.Digest)
                || ReleaseManifestAdmissionService.ExtractDigest(evidence.Reference) is not { } embeddedDigest
                || !string.Equals(embeddedDigest, evidence.Digest, StringComparison.OrdinalIgnoreCase)
                || !ReleaseManifestAdmissionService.IsSafeEvidenceReference(evidence.Reference, evidence.Digest)
                || !IsAllowedEvidenceDescription(evidence.Kind, evidence.Description))
                throw new ReleaseManifestProjectionValidationException("Existing plan evidence must be a safe locator with a non-sensitive description.");
        }
    }

    private static bool IsAllowedEvidenceDescription(string kind, string description) =>
        !string.IsNullOrWhiteSpace(description)
        && !description.Any(char.IsControl)
        && (FixedEvidenceDescriptions.TryGetValue(kind, out var expected)
            ? string.Equals(description, expected, StringComparison.Ordinal)
            : string.Equals(description, GenericEvidenceDescription, StringComparison.Ordinal));

    private static ResolvedElsaEndpoint ToEndpoint(ReleaseManifestEndpoint endpoint) =>
        new(endpoint.Name, endpoint.Protocol, endpoint.Port, endpoint.Visibility, endpoint.RequiresTls, EndpointPathPolicy.Normalize(endpoint.Path));

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
            ?? throw new ReleaseManifestProjectionValidationException("Admitted image references must retain a sha256 digest.")}";
}
