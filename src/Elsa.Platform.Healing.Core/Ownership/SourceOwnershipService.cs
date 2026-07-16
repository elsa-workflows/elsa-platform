using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core.Manifests;
using Elsa.Platform.Healing.Core.Security;

namespace Elsa.Platform.Healing.Core.Ownership;

public enum SourceOwnershipResolutionStatus { Selected, Ambiguous, Unauthorized, ManifestNotTrusted }

public sealed record SourceOwnershipSuggestion(
    string RepositoryUrl,
    IReadOnlyList<Guid> ComponentEntryIds,
    bool GrantsMutationAuthority);

public sealed record SourceOwnershipResolution(
    SourceOwnershipResolutionStatus Status,
    SourceOwnershipBinding? SelectedBinding,
    IReadOnlyList<SourceOwnershipBinding> MatchingBindings,
    IReadOnlyList<string> ReasonCodes);

public sealed class SourceOwnershipService(
    IHealingOwnershipStore store,
    HealingAuditService auditService,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<IReadOnlyList<SourceOwnershipSuggestion>> SuggestAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid manifestId,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        var authorizationFailure = HealingOwnershipAuthorization.ReadFailure(authorization, workspaceId, applicationId);
        if (authorizationFailure is not null)
            return [];
        var manifest = await store.GetManifestAsync(workspaceId, applicationId, manifestId, cancellationToken);
        if (manifest is null)
            return [];

        return manifest.Entries
            .Select(x => (Entry: x, RepositoryUrl: TryNormalizeRepositoryUrl(x.RepositoryUrl)))
            .Where(x => x.RepositoryUrl is not null)
            .GroupBy(x => x.RepositoryUrl!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new SourceOwnershipSuggestion(
                x.Key,
                x.Select(entry => entry.Entry.Id).Order().ToArray(),
                GrantsMutationAuthority: false))
            .ToArray();
    }

    public async ValueTask<HealingOperationResult<SourceOwnershipBinding>> ActivateAsync(
        SourceOwnershipBinding binding,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var authorizationFailure = HealingOwnershipAuthorization.OwnerFailure(authorization, binding.WorkspaceId, binding.ApplicationId);
        if (authorizationFailure is not null)
            return HealingOperationResult<SourceOwnershipBinding>.Denied(authorizationFailure);
        var existingBinding = await store.GetBindingAsync(
            binding.WorkspaceId, binding.ApplicationId, binding.Id, cancellationToken);
        if (existingBinding?.Status == SourceOwnershipBindingStatus.Revoked ||
            !IsValidBinding(binding, allowActive: true))
            return HealingOperationResult<SourceOwnershipBinding>.Denied(HealingOwnershipReasonCodes.InvalidConfiguration);

        var provider = await store.GetProviderConnectionAsync(binding.WorkspaceId, binding.ProviderConnectionId, cancellationToken);
        if (provider is null || provider.Status != ProviderConnectionStatus.Active)
            return HealingOperationResult<SourceOwnershipBinding>.Denied(HealingOwnershipReasonCodes.ProviderNotAuthorized);
        if (!ProviderMatches(provider, binding))
            return HealingOperationResult<SourceOwnershipBinding>.Denied(HealingOwnershipReasonCodes.ProviderRepositoryMismatch);
        if (!await store.PoliciesAreTrustedAsync(
                binding.WorkspaceId, binding.ApplicationId, binding.PathPolicyId, binding.EvidencePolicyId, binding.MergePolicyId, cancellationToken))
            return HealingOperationResult<SourceOwnershipBinding>.Denied(HealingOwnershipReasonCodes.PolicyNotTrusted);

        var manifests = (await store.ListManifestsAsync(binding.WorkspaceId, binding.ApplicationId, trustedOnly: true, cancellationToken))
            .Where(ComponentManifestService.IsAutomationAuthoritative);
        var activeBindings = await store.ListBindingsAsync(binding.WorkspaceId, binding.ApplicationId, activeOnly: true, cancellationToken);
        var conflicts = manifests.SelectMany(x => x.Entries)
            .Where(entry => Matches(binding, entry))
            .Any(entry => activeBindings.Any(active => active.Id != binding.Id && Matches(active, entry) && !HasSameAuthority(active, binding)));
        if (conflicts)
            return HealingOperationResult<SourceOwnershipBinding>.Denied(HealingOwnershipReasonCodes.AmbiguousAuthority);

        var now = _timeProvider.GetUtcNow();
        binding.Status = SourceOwnershipBindingStatus.Active;
        binding.ApprovedBy = authorization.ActorId;
        binding.ApprovedAt = now;
        if (binding.CreatedAt == default)
            binding.CreatedAt = now;
        binding.UpdatedAt = now;
        var transactionResult = await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var currentProvider = await store.GetProviderConnectionAsync(
                binding.WorkspaceId, binding.ProviderConnectionId, transactionCancellationToken);
            if (currentProvider is null || currentProvider.Status != ProviderConnectionStatus.Active)
                return (Binding: (SourceOwnershipBinding?)null, Failure: HealingOwnershipReasonCodes.ProviderNotAuthorized);
            if (!ProviderMatches(currentProvider, binding))
                return (Binding: (SourceOwnershipBinding?)null, Failure: HealingOwnershipReasonCodes.ProviderRepositoryMismatch);
            if (!await store.PoliciesAreTrustedAsync(
                    binding.WorkspaceId, binding.ApplicationId, binding.PathPolicyId, binding.EvidencePolicyId,
                    binding.MergePolicyId, transactionCancellationToken))
                return (Binding: (SourceOwnershipBinding?)null, Failure: HealingOwnershipReasonCodes.PolicyNotTrusted);
            var currentManifests = (await store.ListManifestsAsync(
                binding.WorkspaceId, binding.ApplicationId, trustedOnly: true, transactionCancellationToken))
                .Where(ComponentManifestService.IsAutomationAuthoritative);
            var currentActiveBindings = await store.ListBindingsAsync(
                binding.WorkspaceId, binding.ApplicationId, activeOnly: true, transactionCancellationToken);
            if (currentManifests.SelectMany(x => x.Entries)
                .Where(entry => Matches(binding, entry))
                .Any(entry => currentActiveBindings.Any(active =>
                    active.Id != binding.Id && Matches(active, entry) && !HasSameAuthority(active, binding))))
                return (Binding: (SourceOwnershipBinding?)null, Failure: HealingOwnershipReasonCodes.AmbiguousAuthority);
            var persisted = await store.SaveBindingAsync(binding, transactionCancellationToken);
            await AuditAsync(persisted, "ownership-binding-activated", "active", HealingOwnershipReasonCodes.Succeeded, authorization, transactionCancellationToken);
            return (Binding: (SourceOwnershipBinding?)persisted, Failure: (string?)null);
        }, cancellationToken);
        return transactionResult.Binding is null
            ? HealingOperationResult<SourceOwnershipBinding>.Denied(transactionResult.Failure!)
            : HealingOperationResult<SourceOwnershipBinding>.Success(transactionResult.Binding);
    }

    public async ValueTask<HealingOperationResult<SourceOwnershipBinding>> SaveDraftAsync(
        SourceOwnershipBinding binding,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var authorizationFailure = HealingOwnershipAuthorization.ConfigurationFailure(
            authorization, binding.WorkspaceId, binding.ApplicationId);
        if (authorizationFailure is not null)
            return HealingOperationResult<SourceOwnershipBinding>.Denied(authorizationFailure);
        var existing = await store.GetBindingAsync(binding.WorkspaceId, binding.ApplicationId, binding.Id, cancellationToken);
        if (existing?.Status is SourceOwnershipBindingStatus.Active or SourceOwnershipBindingStatus.Revoked ||
            !IsValidBinding(binding, allowActive: false))
            return HealingOperationResult<SourceOwnershipBinding>.Denied(HealingOwnershipReasonCodes.InvalidBindingTransition);

        var provider = await store.GetProviderConnectionAsync(binding.WorkspaceId, binding.ProviderConnectionId, cancellationToken);
        if (provider is null)
            return HealingOperationResult<SourceOwnershipBinding>.Denied(HealingOwnershipReasonCodes.ProviderNotAuthorized);
        if (!ProviderMatches(provider, binding))
            return HealingOperationResult<SourceOwnershipBinding>.Denied(HealingOwnershipReasonCodes.ProviderRepositoryMismatch);
        if (!await store.PoliciesAreTrustedAsync(
                binding.WorkspaceId, binding.ApplicationId, binding.PathPolicyId, binding.EvidencePolicyId, binding.MergePolicyId, cancellationToken))
            return HealingOperationResult<SourceOwnershipBinding>.Denied(HealingOwnershipReasonCodes.PolicyNotTrusted);

        var now = _timeProvider.GetUtcNow();
        binding.Status = SourceOwnershipBindingStatus.Draft;
        binding.ApprovedAt = null;
        binding.ApprovedBy = null;
        if (binding.CreatedAt == default)
            binding.CreatedAt = now;
        binding.UpdatedAt = now;
        var saved = await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var persisted = await store.SaveBindingAsync(binding, transactionCancellationToken);
            await AuditAsync(persisted, "ownership-binding-saved", "draft", HealingOwnershipReasonCodes.Succeeded, authorization, transactionCancellationToken);
            return persisted;
        }, cancellationToken);
        return HealingOperationResult<SourceOwnershipBinding>.Success(saved);
    }

    public ValueTask<HealingOperationResult<SourceOwnershipBinding>> SuspendAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid bindingId,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default) =>
        TransitionBindingAsync(workspaceId, applicationId, bindingId, SourceOwnershipBindingStatus.Suspended, authorization, cancellationToken);

    public ValueTask<HealingOperationResult<SourceOwnershipBinding>> RevokeAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid bindingId,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default) =>
        TransitionBindingAsync(workspaceId, applicationId, bindingId, SourceOwnershipBindingStatus.Revoked, authorization, cancellationToken);

    public async ValueTask<HealingOperationResult<SourceOwnershipBinding>> GetAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid bindingId,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        var authorizationFailure = HealingOwnershipAuthorization.ReadFailure(authorization, workspaceId, applicationId);
        if (authorizationFailure is not null)
            return HealingOperationResult<SourceOwnershipBinding>.Denied(authorizationFailure);
        var binding = await store.GetBindingAsync(workspaceId, applicationId, bindingId, cancellationToken);
        return binding is null
            ? HealingOperationResult<SourceOwnershipBinding>.Denied(HealingOwnershipReasonCodes.NotFound)
            : HealingOperationResult<SourceOwnershipBinding>.Success(binding);
    }

    public async ValueTask<IReadOnlyList<SourceOwnershipBinding>> ListAsync(
        Guid workspaceId,
        Guid applicationId,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        var authorizationFailure = HealingOwnershipAuthorization.ReadFailure(authorization, workspaceId, applicationId);
        return authorizationFailure is null
            ? await store.ListBindingsAsync(workspaceId, applicationId, activeOnly: false, cancellationToken)
            : [];
    }

    public async ValueTask<SourceOwnershipResolution> ResolveAsync(
        Guid workspaceId,
        Guid applicationId,
        ComponentManifestEntry component,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        var authorizationFailure = HealingOwnershipAuthorization.ReadFailure(authorization, workspaceId, applicationId);
        if (authorizationFailure is not null || component.WorkspaceId != workspaceId || component.ApplicationId != applicationId)
            return new SourceOwnershipResolution(SourceOwnershipResolutionStatus.Unauthorized, null, [], [HealingOwnershipReasonCodes.Unauthorized]);

        return await ResolveTrustedComponentAsync(workspaceId, applicationId, component, cancellationToken);
    }

    /// <summary>
    /// Resolves repair authority for a Platform-owned background operation. The supplied component remains evidence
    /// only: it must be found in an automation-authoritative persisted manifest before any binding can be selected.
    /// </summary>
    public ValueTask<SourceOwnershipResolution> ResolveForAutomationAsync(
        Guid workspaceId,
        Guid applicationId,
        ComponentManifestEntry component,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component.WorkspaceId != workspaceId || component.ApplicationId != applicationId)
            return ValueTask.FromResult(new SourceOwnershipResolution(
                SourceOwnershipResolutionStatus.Unauthorized,
                null,
                [],
                [HealingOwnershipReasonCodes.Unauthorized]));

        return ResolveTrustedComponentAsync(workspaceId, applicationId, component, cancellationToken);
    }

    private async ValueTask<SourceOwnershipResolution> ResolveTrustedComponentAsync(
        Guid workspaceId,
        Guid applicationId,
        ComponentManifestEntry component,
        CancellationToken cancellationToken)
    {

        var manifests = (await store.ListManifestsAsync(workspaceId, applicationId, trustedOnly: true, cancellationToken))
            .Where(ComponentManifestService.IsAutomationAuthoritative);
        var persistedComponent = manifests
            .Where(x => x.Id == component.ManifestId)
            .SelectMany(x => x.Entries)
            .SingleOrDefault(entry => entry.Id == component.Id);
        if (persistedComponent is null)
            return new SourceOwnershipResolution(SourceOwnershipResolutionStatus.ManifestNotTrusted, null, [], [HealingOwnershipReasonCodes.ManifestNotTrusted]);

        var selectorMatches = (await store.ListBindingsAsync(workspaceId, applicationId, activeOnly: true, cancellationToken))
            .Where(binding => Matches(binding, persistedComponent))
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Id)
            .ToArray();
        var trustedMatches = new List<SourceOwnershipBinding>(selectorMatches.Length);
        var trustFailures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in selectorMatches)
        {
            var trustFailure = await AuthorityTrustFailureAsync(binding, cancellationToken);
            if (trustFailure is null)
                trustedMatches.Add(binding);
            else
                trustFailures.Add(trustFailure);
        }
        var matches = trustedMatches.ToArray();
        SourceOwnershipResolution resolution;
        if (matches.Length == 0)
        {
            resolution = new SourceOwnershipResolution(
                SourceOwnershipResolutionStatus.Unauthorized,
                null,
                selectorMatches,
                trustFailures.Count == 0 ? [HealingOwnershipReasonCodes.NoApprovedBinding] : trustFailures.Order().ToArray());
        }
        else if (matches.Select(AuthorityKey).Distinct(StringComparer.Ordinal).Skip(1).Any())
        {
            resolution = new SourceOwnershipResolution(
                SourceOwnershipResolutionStatus.Ambiguous, null, matches, [HealingOwnershipReasonCodes.AmbiguousAuthority]);
        }
        else
        {
            resolution = new SourceOwnershipResolution(
                SourceOwnershipResolutionStatus.Selected, matches[0], matches, []);
        }

        return resolution;
    }

    public static bool Matches(SourceOwnershipBinding binding, ComponentManifestEntry component)
    {
        if (component.Kind == ComponentKind.Unknown)
            return false;
        IEnumerable<string> candidates = binding.SelectorKind switch
        {
            SourceSelectorKind.Application => component.Kind == ComponentKind.Application ? new[] { component.Name } : [],
            SourceSelectorKind.Package => component.Kind == ComponentKind.Package ? new[] { component.Name } : [],
            SourceSelectorKind.Assembly => component.Assemblies.Select(x => x.Name)
                .Concat(component.Kind == ComponentKind.Assembly ? new[] { component.Name } : [])
                .Distinct(StringComparer.OrdinalIgnoreCase),
            SourceSelectorKind.ComponentKey => new[] { component.ComponentKey },
            _ => []
        };
        return candidates.Any(candidate => GlobMatches(binding.SelectorPattern, candidate));
    }

    private async ValueTask<HealingOperationResult<SourceOwnershipBinding>> TransitionBindingAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid bindingId,
        SourceOwnershipBindingStatus target,
        HealingAuthorization authorization,
        CancellationToken cancellationToken)
    {
        var authorizationFailure = HealingOwnershipAuthorization.OwnerFailure(authorization, workspaceId, applicationId);
        if (authorizationFailure is not null)
            return HealingOperationResult<SourceOwnershipBinding>.Denied(authorizationFailure);
        var binding = await store.GetBindingAsync(workspaceId, applicationId, bindingId, cancellationToken);
        if (binding is null)
            return HealingOperationResult<SourceOwnershipBinding>.Denied(HealingOwnershipReasonCodes.NotFound);
        if (binding.Status == target)
            return HealingOperationResult<SourceOwnershipBinding>.Success(binding);
        if (binding.Status == SourceOwnershipBindingStatus.Revoked ||
            target == SourceOwnershipBindingStatus.Suspended && binding.Status != SourceOwnershipBindingStatus.Active)
            return HealingOperationResult<SourceOwnershipBinding>.Denied(HealingOwnershipReasonCodes.InvalidBindingTransition);

        binding.Status = target;
        binding.UpdatedAt = _timeProvider.GetUtcNow();
        var saved = await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var persisted = await store.SaveBindingAsync(binding, transactionCancellationToken);
            await AuditAsync(
                persisted,
                target == SourceOwnershipBindingStatus.Revoked ? "ownership-binding-revoked" : "ownership-binding-suspended",
                target.ToString().ToLowerInvariant(),
                HealingOwnershipReasonCodes.Succeeded,
                authorization,
                transactionCancellationToken);
            return persisted;
        }, cancellationToken);
        return HealingOperationResult<SourceOwnershipBinding>.Success(saved);
    }

    private async ValueTask<string?> AuthorityTrustFailureAsync(
        SourceOwnershipBinding binding,
        CancellationToken cancellationToken)
    {
        var provider = await store.GetProviderConnectionAsync(
            binding.WorkspaceId, binding.ProviderConnectionId, cancellationToken);
        if (provider is null || provider.Status != ProviderConnectionStatus.Active)
            return HealingOwnershipReasonCodes.ProviderNotAuthorized;
        if (!ProviderMatches(provider, binding))
            return HealingOwnershipReasonCodes.ProviderRepositoryMismatch;
        return await store.PoliciesAreTrustedAsync(
            binding.WorkspaceId, binding.ApplicationId, binding.PathPolicyId, binding.EvidencePolicyId,
            binding.MergePolicyId, cancellationToken)
            ? null
            : HealingOwnershipReasonCodes.PolicyNotTrusted;
    }

    private ValueTask<HealingAuditEvent> AuditAsync(
        SourceOwnershipBinding binding,
        string eventType,
        string status,
        string reasonCode,
        HealingAuthorization authorization,
        CancellationToken cancellationToken) =>
        auditService.AppendAsync(new HealingAuditWrite(
            binding.WorkspaceId,
            "source-ownership-binding",
            binding.Id,
            eventType,
            reasonCode,
            HealingActorTypes.Human,
            authorization.ActorId,
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            new Dictionary<string, string?>
            {
                ["status"] = status,
                ["gateReason"] = reasonCode,
                ["repositoryOwner"] = binding.RepositoryOwner,
                ["repositoryName"] = binding.RepositoryName
            }), cancellationToken);

    private static bool IsValidBinding(SourceOwnershipBinding binding, bool allowActive) =>
        binding.Id != Guid.Empty && binding.WorkspaceId != Guid.Empty && binding.ApplicationId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(binding.Name) && !string.IsNullOrWhiteSpace(binding.SelectorPattern) &&
        binding.ProviderConnectionId != Guid.Empty && !string.IsNullOrWhiteSpace(binding.RepositoryProviderId) &&
        !string.IsNullOrWhiteSpace(binding.RepositoryOwner) && !string.IsNullOrWhiteSpace(binding.RepositoryName) &&
        !string.IsNullOrWhiteSpace(binding.TargetBranch) && !string.IsNullOrWhiteSpace(binding.WorkflowIdentity) &&
        IsCanonicalWorkflowReference(binding.WorkflowReference) &&
        !string.IsNullOrWhiteSpace(binding.WorkflowRevision) && binding.PathPolicyId != Guid.Empty &&
        binding.EvidencePolicyId != Guid.Empty && binding.MergePolicyId != Guid.Empty &&
        (binding.Status is SourceOwnershipBindingStatus.Draft or SourceOwnershipBindingStatus.Suspended ||
         allowActive && binding.Status == SourceOwnershipBindingStatus.Active);

    private static bool ProviderMatches(ProviderConnection provider, SourceOwnershipBinding binding) =>
        provider.WorkspaceId == binding.WorkspaceId &&
        string.Equals(provider.RepositoryProviderId, binding.RepositoryProviderId, StringComparison.Ordinal) &&
        string.Equals(provider.RepositoryOwner, binding.RepositoryOwner, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(provider.RepositoryName, binding.RepositoryName, StringComparison.OrdinalIgnoreCase);

    private static bool IsCanonicalWorkflowReference(string value) =>
        value.StartsWith("refs/heads/", StringComparison.Ordinal) ||
        value.StartsWith("refs/tags/", StringComparison.Ordinal);

    public static bool HasSameAuthority(SourceOwnershipBinding left, SourceOwnershipBinding right) =>
        string.Equals(AuthorityKey(left), AuthorityKey(right), StringComparison.Ordinal);

    private static string AuthorityKey(SourceOwnershipBinding binding) => string.Join('\n',
        binding.ProviderConnectionId.ToString("N"),
        binding.RepositoryProviderId,
        binding.TargetBranch,
        binding.WorkflowIdentity,
        binding.WorkflowReference,
        binding.WorkflowRevision,
        binding.PathPolicyId.ToString("N"),
        binding.EvidencePolicyId.ToString("N"),
        binding.MergePolicyId.ToString("N"));

    private static bool GlobMatches(string pattern, string value)
    {
        var patternIndex = 0;
        var valueIndex = 0;
        var starIndex = -1;
        var retryValueIndex = -1;
        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '?' || char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(value[valueIndex])))
            {
                patternIndex++;
                valueIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                retryValueIndex = valueIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                valueIndex = ++retryValueIndex;
            }
            else
            {
                return false;
            }
        }
        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            patternIndex++;
        return patternIndex == pattern.Length;
    }

    private static string? TryNormalizeRepositoryUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return null;

        var builder = new UriBuilder(uri)
        {
            Host = uri.IdnHost.ToLowerInvariant(),
            Path = uri.AbsolutePath.TrimEnd('/'),
            Port = uri.IsDefaultPort ? -1 : uri.Port
        };
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

}
