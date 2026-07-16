using Elsa.Platform.Healing.Core.Manifests;
using Elsa.Platform.Healing.Core.Ownership;

namespace Elsa.Platform.Healing.Core.Incidents;

public static class ComponentAttributionReasonCodes
{
    public const string ProducingManifestNotTrusted = "producing-manifest-not-trusted";
    public const string NoComponentCandidate = "no-component-candidate";
    public const string ExplicitComponent = "explicit-component";
    public const string AssemblyFrame = "assembly-frame";
    public const string ApprovedAuthority = "approved-authority";
    public const string AmbiguousComponent = "ambiguous-component";
    public const string AmbiguousAuthority = "ambiguous-authority";
    public const string UnauthorizedAuthority = "unauthorized-authority";
}

public sealed record ComponentAttributionCandidate(
    ComponentManifestEntry Component,
    SourceOwnershipBinding? Binding,
    decimal Confidence,
    AttributionBasis Basis,
    AttributionResolution Resolution,
    IReadOnlyList<string> ReasonCodes);

public sealed record ComponentAttributionResult(
    IReadOnlyList<ComponentAttributionCandidate> Candidates,
    ComponentManifestEntry? SelectedComponent,
    SourceOwnershipBinding? SelectedBinding,
    ProviderConnection? SelectedProvider,
    string RepairRepositoryKey,
    IReadOnlyList<string> ReasonCodes)
{
    public bool IsRepairable => SelectedComponent is not null && SelectedBinding is not null && SelectedProvider is not null;
}

public sealed class ComponentAttributionService(
    IHealingOwnershipStore store,
    SourceOwnershipService ownershipService)
{
    public async ValueTask<ComponentAttributionResult> AttributeAsync(
        Guid workspaceId,
        NormalizedHealingSignal signal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var manifests = (await store.ListManifestsAsync(
                workspaceId,
                signal.ApplicationId,
                trustedOnly: true,
                cancellationToken))
            .Where(ComponentManifestService.IsAutomationAuthoritative)
            .Where(manifest => MatchesProducingManifest(manifest, signal))
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToArray();
        if (manifests.Length != 1)
        {
            return ObservationOnly(
                manifests.Length == 0
                    ? ComponentAttributionReasonCodes.ProducingManifestNotTrusted
                    : ComponentAttributionReasonCodes.AmbiguousComponent);
        }

        var evidenceCandidates = FindCandidates(manifests[0], signal);
        if (evidenceCandidates.Count == 0)
            return ObservationOnly(ComponentAttributionReasonCodes.NoComponentCandidate);

        var resolved = new List<ComponentAttributionCandidate>(evidenceCandidates.Count);
        foreach (var evidence in evidenceCandidates)
        {
            var authority = await ownershipService.ResolveForAutomationAsync(
                workspaceId,
                signal.ApplicationId,
                evidence.Component,
                cancellationToken);
            resolved.Add(ToCandidate(evidence, authority));
        }

        var highestConfidence = resolved.Max(x => x.Confidence);
        var highest = resolved.Where(x => x.Confidence == highestConfidence).ToArray();
        var selectedAuthorities = highest
            .Where(x => x.Resolution == AttributionResolution.Selected && x.Binding is not null)
            .Select(x => x.Binding!)
            .GroupBy(AuthorityKey, StringComparer.Ordinal)
            .ToArray();
        if (highest.Length != 1 || selectedAuthorities.Length != 1)
        {
            var reason = selectedAuthorities.Length > 1
                ? ComponentAttributionReasonCodes.AmbiguousAuthority
                : ComponentAttributionReasonCodes.AmbiguousComponent;
            return new ComponentAttributionResult(
                resolved.Select(x => x with
                {
                    Resolution = x.Resolution == AttributionResolution.Selected
                        ? AttributionResolution.Ambiguous
                        : x.Resolution,
                    ReasonCodes = x.ReasonCodes.Append(reason).Distinct(StringComparer.Ordinal).ToArray()
                }).ToArray(),
                null,
                null,
                null,
                "observation-only",
                [reason]);
        }

        var selected = highest[0];
        var provider = await store.GetProviderConnectionAsync(
            workspaceId,
            selected.Binding!.ProviderConnectionId,
            cancellationToken);
        if (provider is null || provider.Status != ProviderConnectionStatus.Active ||
            string.IsNullOrWhiteSpace(provider.RepositoryProviderId))
            return new ComponentAttributionResult(resolved, null, null, null, "observation-only", [ComponentAttributionReasonCodes.UnauthorizedAuthority]);

        var repositoryKey = $"{provider.Provider.Trim().ToLowerInvariant()}:{provider.RepositoryProviderId.Trim().ToLowerInvariant()}";
        return new ComponentAttributionResult(
            resolved,
            selected.Component,
            selected.Binding,
            provider,
            repositoryKey,
            [ComponentAttributionReasonCodes.ApprovedAuthority]);
    }

    private static bool MatchesProducingManifest(ComponentManifest manifest, NormalizedHealingSignal signal)
    {
        if (signal.RevisionId is not null && manifest.RevisionId != signal.RevisionId)
            return false;
        if (!string.IsNullOrWhiteSpace(signal.Source.ComponentManifestDigest) &&
            !string.Equals(manifest.ManifestDigest, signal.Source.ComponentManifestDigest, StringComparison.Ordinal))
            return false;
        return signal.RevisionId is not null || !string.IsNullOrWhiteSpace(signal.Source.ComponentManifestDigest);
    }

    private static List<EvidenceCandidate> FindCandidates(ComponentManifest manifest, NormalizedHealingSignal signal)
    {
        var candidates = new Dictionary<Guid, EvidenceCandidate>();
        if (!string.IsNullOrWhiteSpace(signal.Source.ComponentKey))
        {
            foreach (var component in manifest.Entries.Where(x =>
                         string.Equals(x.ComponentKey, signal.Source.ComponentKey, StringComparison.Ordinal)))
                AddCandidate(candidates, component, 1m, AttributionBasis.ExplicitComponent, ComponentAttributionReasonCodes.ExplicitComponent);
        }

        var assemblies = signal.Frames
            .Select(x => x.AssemblyName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (assemblies.Count > 0)
        {
            foreach (var component in manifest.Entries.Where(entry =>
                         (!string.IsNullOrWhiteSpace(entry.AssemblyName) && assemblies.Contains(entry.AssemblyName)) ||
                         entry.Assemblies.Any(artifact => assemblies.Contains(artifact.Name))))
                AddCandidate(candidates, component, .95m, AttributionBasis.StackFrame | AttributionBasis.Assembly, ComponentAttributionReasonCodes.AssemblyFrame);
        }

        return candidates.Values.OrderByDescending(x => x.Confidence).ThenBy(x => x.Component.Id).ToList();
    }

    private static void AddCandidate(
        IDictionary<Guid, EvidenceCandidate> candidates,
        ComponentManifestEntry component,
        decimal confidence,
        AttributionBasis basis,
        string reasonCode)
    {
        if (candidates.TryGetValue(component.Id, out var existing))
        {
            candidates[component.Id] = existing with
            {
                Confidence = Math.Max(existing.Confidence, confidence),
                Basis = existing.Basis | basis,
                ReasonCodes = existing.ReasonCodes.Append(reasonCode).Distinct(StringComparer.Ordinal).ToArray()
            };
            return;
        }

        candidates.Add(component.Id, new EvidenceCandidate(component, confidence, basis, [reasonCode]));
    }

    private static ComponentAttributionCandidate ToCandidate(
        EvidenceCandidate evidence,
        SourceOwnershipResolution authority)
    {
        var resolution = authority.Status switch
        {
            SourceOwnershipResolutionStatus.Selected => AttributionResolution.Selected,
            SourceOwnershipResolutionStatus.Ambiguous => AttributionResolution.Ambiguous,
            SourceOwnershipResolutionStatus.Unauthorized => AttributionResolution.Unauthorized,
            _ => AttributionResolution.Unmapped
        };
        return new ComponentAttributionCandidate(
            evidence.Component,
            authority.SelectedBinding,
            evidence.Confidence,
            evidence.Basis,
            resolution,
            evidence.ReasonCodes.Concat(authority.ReasonCodes).Distinct(StringComparer.Ordinal).ToArray());
    }

    private static ComponentAttributionResult ObservationOnly(string reasonCode) =>
        new([], null, null, null, "observation-only", [reasonCode]);

    private static string AuthorityKey(SourceOwnershipBinding binding) =>
        $"{binding.ProviderConnectionId:N}:{binding.RepositoryProviderId}:{binding.RepositoryOwner}:{binding.RepositoryName}:{binding.TargetBranch}:{binding.WorkflowIdentity}:{binding.WorkflowRevision}";

    private sealed record EvidenceCandidate(
        ComponentManifestEntry Component,
        decimal Confidence,
        AttributionBasis Basis,
        IReadOnlyList<string> ReasonCodes);
}
