using ValenceControl.Deployment.Core.Cockpit;

namespace ValenceControl.Deployment.Core.Workspace;

public sealed class DeploymentTierService(IWorkspaceDeploymentTierStore? store = null)
{
    public const string ObservabilityBindingRequirementId = "observability-binding";
    public const string ObservabilityBindingRecordKind = "ObservabilityBinding";
    public const string ObservabilityRequiredValidationId = "deployment.tier.observability-required";

    public static IReadOnlyList<DeploymentTierCapability> CapabilityCatalog { get; } =
    [
        new(DeploymentTierCapabilities.DevelopmentLike, "Development-like", "Marks environments as development-grade deployment contexts.", DeploymentTierCapabilityCategory.Classification),
        new(DeploymentTierCapabilities.TestLike, "Test-like", "Marks environments as test-grade deployment contexts.", DeploymentTierCapabilityCategory.Classification),
        new(DeploymentTierCapabilities.PreproductionLike, "Pre-production-like", "Marks environments as final validation contexts before production.", DeploymentTierCapabilityCategory.Classification),
        new(DeploymentTierCapabilities.ProductionLike, "Production-like", "Marks environments as production-grade deployment targets.", DeploymentTierCapabilityCategory.Classification),
        new(DeploymentTierCapabilities.PromotionSource, "Promotion source", "Allows environments using this tier to act as a promotion source.", DeploymentTierCapabilityCategory.Promotion),
        new(DeploymentTierCapabilities.PromotionTarget, "Promotion target", "Allows environments using this tier to act as a promotion target.", DeploymentTierCapabilityCategory.Promotion),
        new(DeploymentTierCapabilities.ConfirmationRequired, "Confirmation required", "Requires explicit confirmation for deployment actions.", DeploymentTierCapabilityCategory.Safeguards),
        new(DeploymentTierCapabilities.RollbackEnabled, "Rollback enabled", "Allows rollback actions for environments using this tier.", DeploymentTierCapabilityCategory.Rollback),
        new(DeploymentTierCapabilities.SecretVerificationRequired, "Secret verification required", "Requires secret reference validation before deployment.", DeploymentTierCapabilityCategory.Validation),
        new(DeploymentTierCapabilities.ObservabilityRequired, "Observability required", "Requires observability bindings for environments using this tier.", DeploymentTierCapabilityCategory.Observability)
    ];

    public static IReadOnlyDictionary<EnvironmentTier, IReadOnlyList<string>> DefaultCapabilitiesByLegacyTier { get; } =
        new Dictionary<EnvironmentTier, IReadOnlyList<string>>
        {
            [EnvironmentTier.Dev] = [DeploymentTierCapabilities.DevelopmentLike, DeploymentTierCapabilities.PromotionSource],
            [EnvironmentTier.Test] = [DeploymentTierCapabilities.TestLike, DeploymentTierCapabilities.PromotionSource, DeploymentTierCapabilities.PromotionTarget],
            [EnvironmentTier.Stage] =
            [
                DeploymentTierCapabilities.PreproductionLike,
                DeploymentTierCapabilities.PromotionSource,
                DeploymentTierCapabilities.PromotionTarget,
                DeploymentTierCapabilities.SecretVerificationRequired
            ],
            [EnvironmentTier.Production] =
            [
                DeploymentTierCapabilities.ProductionLike,
                DeploymentTierCapabilities.PromotionTarget,
                DeploymentTierCapabilities.ConfirmationRequired,
                DeploymentTierCapabilities.RollbackEnabled,
                DeploymentTierCapabilities.SecretVerificationRequired,
                DeploymentTierCapabilities.ObservabilityRequired
            ]
        };

    public IReadOnlyList<DeploymentTierCapability> GetCapabilityCatalog() => CapabilityCatalog;

    public Task<IReadOnlyList<WorkspaceDeploymentTier>> ListTiersAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        if (store is null)
            return Task.FromResult<IReadOnlyList<WorkspaceDeploymentTier>>([]);

        return store.ListTiersAsync(workspaceId, cancellationToken);
    }

    public async Task<WorkspaceDeploymentTier> CreateTierAsync(
        Guid workspaceId,
        CreateDeploymentTierRequest request,
        CancellationToken cancellationToken = default)
    {
        if (store is null)
            throw new InvalidOperationException("Deployment tier persistence is not configured.");

        var capabilities = NormalizeCapabilities(request.Capabilities);
        ValidateTierMutation(request.Name, capabilities);
        return await store.CreateTierAsync(workspaceId, request with { Capabilities = capabilities }, cancellationToken);
    }

    public async Task<WorkspaceDeploymentTier> UpdateTierAsync(
        Guid workspaceId,
        Guid tierId,
        UpdateDeploymentTierRequest request,
        CancellationToken cancellationToken = default)
    {
        if (store is null)
            throw new InvalidOperationException("Deployment tier persistence is not configured.");

        var capabilities = NormalizeCapabilities(request.Capabilities);
        ValidateTierMutation(request.Name, capabilities);
        var impact = await store.PreviewTierImpactAsync(workspaceId, tierId, capabilities, cancellationToken);
        if (!request.ImpactAccepted && impact.AffectedEnvironmentCount > 0 && (impact.AddedCapabilities.Count > 0 || impact.RemovedCapabilities.Count > 0))
            throw new InvalidOperationException("Tier capability changes affect existing environments and require impact acceptance.");

        return await store.UpdateTierAsync(workspaceId, tierId, request with { Capabilities = capabilities }, impact, cancellationToken);
    }

    public Task<WorkspaceDeploymentTier> ArchiveTierAsync(
        Guid workspaceId,
        Guid tierId,
        ArchiveDeploymentTierRequest request,
        CancellationToken cancellationToken = default)
    {
        if (store is null)
            throw new InvalidOperationException("Deployment tier persistence is not configured.");

        return store.ArchiveTierAsync(workspaceId, tierId, request, cancellationToken);
    }

    public Task<WorkspaceDeploymentTier> RestoreTierAsync(
        Guid workspaceId,
        Guid tierId,
        RestoreDeploymentTierRequest request,
        CancellationToken cancellationToken = default)
    {
        if (store is null)
            throw new InvalidOperationException("Deployment tier persistence is not configured.");

        return store.RestoreTierAsync(workspaceId, tierId, request, cancellationToken);
    }

    public Task<DeploymentTierImpactSummary> PreviewImpactAsync(
        Guid workspaceId,
        Guid tierId,
        PreviewDeploymentTierImpactRequest request,
        CancellationToken cancellationToken = default)
    {
        if (store is null)
            throw new InvalidOperationException("Deployment tier persistence is not configured.");

        var capabilities = NormalizeCapabilities(request.Capabilities);
        ValidateCapabilities(capabilities);
        return store.PreviewTierImpactAsync(workspaceId, tierId, capabilities, cancellationToken);
    }

    public static bool HasCapability(DeploymentTierProfile? tier, string capability) =>
        tier?.Capabilities.Contains(capability, StringComparer.Ordinal) == true;

    public static bool HasCapability(EnvironmentSummary? environment, string capability) =>
        environment is not null && CapabilitiesFor(environment).Contains(capability, StringComparer.Ordinal);

    public static bool IsPromotionSource(EnvironmentSummary? environment) =>
        HasCapability(environment, DeploymentTierCapabilities.PromotionSource);

    public static bool IsPromotionTarget(EnvironmentSummary? environment) =>
        HasCapability(environment, DeploymentTierCapabilities.PromotionTarget);

    public static bool RequiresConfirmation(EnvironmentSummary? environment) =>
        HasCapability(environment, DeploymentTierCapabilities.ConfirmationRequired);

    public static bool CanRollback(EnvironmentSummary? environment) =>
        HasCapability(environment, DeploymentTierCapabilities.RollbackEnabled);

    public static bool RequiresSecretVerification(EnvironmentSummary? environment) =>
        HasCapability(environment, DeploymentTierCapabilities.SecretVerificationRequired);

    public static bool RequiresObservability(EnvironmentSummary? environment) =>
        HasCapability(environment, DeploymentTierCapabilities.ObservabilityRequired);

    public static bool IsProductionLike(EnvironmentSummary? environment) =>
        HasCapability(environment, DeploymentTierCapabilities.ProductionLike);

    public static IReadOnlyList<string> CapabilitiesFor(EnvironmentSummary environment) =>
        environment.TierCapabilities is { Count: > 0 }
            ? environment.TierCapabilities
            : DefaultCapabilitiesByLegacyTier[environment.Tier];

    public static IReadOnlyList<DesiredStateRequirement> DesiredStateRequirementsFor(EnvironmentSummary environment)
    {
        var requirements = new List<DesiredStateRequirement>();
        if (RequiresObservability(environment))
            requirements.Add(ObservabilityRequirement(DesiredStateRequirementApplicability.CurrentTier, required: true));

        return requirements;
    }

    public static DesiredStateRequirement ObservabilityRequirement(DesiredStateRequirementApplicability applicability, bool required) =>
        new(
            ObservabilityBindingRequirementId,
            DeploymentTierCapabilities.ObservabilityRequired,
            ObservabilityBindingRecordKind,
            "Observability binding",
            "Requires at least one logs, metrics, traces, or console telemetry binding.",
            ObservabilityRequiredValidationId,
            required,
            applicability);

    public static IReadOnlyList<string> ChangedSafeguards(
        IReadOnlyCollection<string> addedCapabilities,
        IReadOnlyCollection<string> removedCapabilities)
    {
        var changes = new List<string>();
        AddChange(DeploymentTierCapabilities.ConfirmationRequired, "Deployments to environments using this tier will require explicit confirmation.", "Deployments to environments using this tier will no longer require tier-based explicit confirmation.");
        AddChange(DeploymentTierCapabilities.RollbackEnabled, "Rollback will be offered for environments using this tier.", "Rollback will no longer be offered for environments using this tier.");
        AddChange(DeploymentTierCapabilities.PromotionSource, "Environments using this tier can be selected as promotion sources.", "Environments using this tier can no longer be selected as promotion sources.");
        AddChange(DeploymentTierCapabilities.PromotionTarget, "Environments using this tier can be selected as promotion targets.", "Environments using this tier can no longer be selected as promotion targets.");
        AddChange(DeploymentTierCapabilities.SecretVerificationRequired, "Secret references will be required before deployment.", "Secret reference verification will no longer be required by this tier.");
        AddChange(DeploymentTierCapabilities.ObservabilityRequired, "Observability binding checks will be expected for this tier.", "Observability binding checks will no longer be expected by this tier.");
        AddChange(DeploymentTierCapabilities.ProductionLike, "Production-grade safeguards will apply regardless of tier name.", "Production-grade classification will no longer apply to this tier.");
        return changes;

        void AddChange(string capability, string addedMessage, string removedMessage)
        {
            if (addedCapabilities.Contains(capability, StringComparer.Ordinal))
                changes.Add(addedMessage);
            if (removedCapabilities.Contains(capability, StringComparer.Ordinal))
                changes.Add(removedMessage);
        }
    }

    public static IReadOnlyList<string> NormalizeCapabilities(IReadOnlyList<string> capabilities) =>
        capabilities
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static void ValidateTierMutation(string name, IReadOnlyList<string> capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ValidateCapabilities(capabilities);
    }

    private static void ValidateCapabilities(IReadOnlyList<string> capabilities)
    {
        var capabilityIds = CapabilityCatalog.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var capability in capabilities)
        {
            if (!capabilityIds.TryGetValue(capability, out var definition))
                throw new ArgumentException($"Unknown deployment tier capability '{capability}'.", nameof(capabilities));
            if (definition.IsDeprecated)
                throw new ArgumentException($"Deployment tier capability '{capability}' is deprecated for new assignments.", nameof(capabilities));
        }
    }
}
