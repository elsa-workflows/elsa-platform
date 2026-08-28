using ElsaControl.Deployment.Core.Cockpit;
using ElsaControl.Deployment.Core.Workspace;
using Xunit;

namespace ElsaControl.Deployment.Core.Tests;

public sealed class DeploymentTierServiceTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _tierId = Guid.NewGuid();
    private readonly RecordingTierStore _store = new();

    [Fact]
    public void Capability_catalog_contains_control_defined_semantics()
    {
        var service = new DeploymentTierService();

        var capabilities = service.GetCapabilityCatalog();

        Assert.Contains(capabilities, x => x.Id == DeploymentTierCapabilities.ProductionLike);
        Assert.Contains(capabilities, x => x.Id == DeploymentTierCapabilities.PromotionSource);
        Assert.Contains(capabilities, x => x.Id == DeploymentTierCapabilities.PromotionTarget);
        Assert.Contains(capabilities, x => x.Id == DeploymentTierCapabilities.ConfirmationRequired);
        Assert.Equal(capabilities.Select(x => x.Id).Distinct().Count(), capabilities.Select(x => x.Id).Count());
    }

    [Fact]
    public void Default_tier_mappings_preserve_legacy_semantics()
    {
        Assert.Equivalent(
            new[] { DeploymentTierCapabilities.DevelopmentLike, DeploymentTierCapabilities.PromotionSource },
            DeploymentTierService.DefaultCapabilitiesByLegacyTier[EnvironmentTier.Dev]);
        Assert.Contains(DeploymentTierCapabilities.PromotionTarget, DeploymentTierService.DefaultCapabilitiesByLegacyTier[EnvironmentTier.Test]);
        Assert.Contains(DeploymentTierCapabilities.SecretVerificationRequired, DeploymentTierService.DefaultCapabilitiesByLegacyTier[EnvironmentTier.Stage]);
        Assert.Equivalent(
            new[]
            {
                DeploymentTierCapabilities.ProductionLike,
                DeploymentTierCapabilities.PromotionTarget,
                DeploymentTierCapabilities.ConfirmationRequired,
                DeploymentTierCapabilities.RollbackEnabled,
                DeploymentTierCapabilities.SecretVerificationRequired,
                DeploymentTierCapabilities.ObservabilityRequired
            },
            DeploymentTierService.DefaultCapabilitiesByLegacyTier[EnvironmentTier.Production]);
    }

    [Fact]
    public async Task Rejects_unknown_capabilities_before_store_mutation()
    {
        var service = new DeploymentTierService(_store);

        var act = () => service.CreateTierAsync(
            _workspaceId,
            new CreateDeploymentTierRequest("UAT", null, 10, ["deployment.unknown"], null));

        await Assert.ThrowsAsync<ArgumentException>(act);
        Assert.Empty(_store.CreatedRequests);
    }

    [Fact]
    public async Task Surfaces_duplicate_active_name_rejection_from_store()
    {
        _store.CreateException = new InvalidOperationException("An active deployment tier with the same name already exists in this workspace.");
        var service = new DeploymentTierService(_store);

        var act = () => service.CreateTierAsync(
            _workspaceId,
            new CreateDeploymentTierRequest("Production", null, 10, [DeploymentTierCapabilities.ProductionLike], null));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("An active deployment tier with the same name already exists in this workspace.", exception.Message);
    }

    [Fact]
    public async Task Normalizes_capabilities_before_create()
    {
        var service = new DeploymentTierService(_store);

        await service.CreateTierAsync(
            _workspaceId,
            new CreateDeploymentTierRequest(
                "UAT",
                null,
                10,
                [DeploymentTierCapabilities.PromotionTarget, " ", DeploymentTierCapabilities.PromotionTarget, DeploymentTierCapabilities.PreproductionLike],
                null));

        Assert.Single(_store.CreatedRequests);
        Assert.Equal(
            new[] { DeploymentTierCapabilities.PromotionTarget, DeploymentTierCapabilities.PreproductionLike },
            _store.CreatedRequests.Single().Capabilities);
    }

    [Fact]
    public async Task Requires_impact_acceptance_when_capability_changes_affect_existing_environments()
    {
        _store.Impact = new DeploymentTierImpactSummary(
            _tierId,
            [DeploymentTierCapabilities.PromotionTarget],
            [DeploymentTierCapabilities.PromotionTarget, DeploymentTierCapabilities.ConfirmationRequired],
            [DeploymentTierCapabilities.ConfirmationRequired],
            [],
            2,
            [],
            ["Deployments to environments using this tier will require explicit confirmation."]);
        var service = new DeploymentTierService(_store);

        var act = () => service.UpdateTierAsync(
            _workspaceId,
            _tierId,
            new UpdateDeploymentTierRequest(
                "Production EU",
                null,
                20,
                [DeploymentTierCapabilities.PromotionTarget, DeploymentTierCapabilities.ConfirmationRequired],
                ImpactAccepted: false,
                ActorAccountId: null));

        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Empty(_store.UpdatedRequests);
    }

    [Fact]
    public async Task Surfaces_last_active_tier_archive_prevention_from_store()
    {
        _store.ArchiveException = new InvalidOperationException("At least one active deployment tier is required.");
        var service = new DeploymentTierService(_store);

        var act = () => service.ArchiveTierAsync(_workspaceId, _tierId, new ArchiveDeploymentTierRequest(null));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("At least one active deployment tier is required.", exception.Message);
        Assert.Equal(_tierId, Assert.Single(_store.ArchivedTierIds));
    }

    [Fact]
    public void Environment_helpers_use_capability_ids_not_names()
    {
        var environment = new EnvironmentSummary(
            Guid.NewGuid().ToString("D"),
            "Totally Not Production",
            EnvironmentTier.Dev,
            DeploymentHealth.Healthy,
            new DesiredStateRevision("", 0, "", "None", DateTimeOffset.UtcNow),
            null,
            DeploymentStatus.Blocked,
            DriftStatus.Unknown,
            [],
            "Customer Acceptance",
            DeploymentTierStatus.Active.ToString(),
            [DeploymentTierCapabilities.ProductionLike, DeploymentTierCapabilities.ConfirmationRequired]);

        Assert.True(DeploymentTierService.IsProductionLike(environment));
        Assert.True(DeploymentTierService.RequiresConfirmation(environment));
        Assert.False(DeploymentTierService.IsPromotionTarget(environment));
    }

    private sealed class RecordingTierStore : IWorkspaceDeploymentTierStore
    {
        public List<CreateDeploymentTierRequest> CreatedRequests { get; } = [];
        public List<UpdateDeploymentTierRequest> UpdatedRequests { get; } = [];
        public List<Guid> ArchivedTierIds { get; } = [];
        public DeploymentTierImpactSummary Impact { get; set; } = new(Guid.Empty, [], [], [], [], 0, [], []);
        public Exception? CreateException { get; set; }
        public Exception? ArchiveException { get; set; }

        public Task<IReadOnlyList<WorkspaceDeploymentTier>> ListTiersAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceDeploymentTier>>([]);

        public Task<WorkspaceDeploymentTier?> GetTierAsync(Guid workspaceId, Guid tierId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkspaceDeploymentTier?>(null);

        public Task<WorkspaceDeploymentTier> CreateTierAsync(Guid workspaceId, CreateDeploymentTierRequest request, CancellationToken cancellationToken = default)
        {
            if (CreateException is not null)
                return Task.FromException<WorkspaceDeploymentTier>(CreateException);
            CreatedRequests.Add(request);
            return Task.FromResult(Tier(workspaceId, request.Name, request.Capabilities));
        }

        public Task<WorkspaceDeploymentTier> UpdateTierAsync(Guid workspaceId, Guid tierId, UpdateDeploymentTierRequest request, DeploymentTierImpactSummary impact, CancellationToken cancellationToken = default)
        {
            UpdatedRequests.Add(request);
            return Task.FromResult(Tier(workspaceId, request.Name, request.Capabilities));
        }

        public Task<WorkspaceDeploymentTier> ArchiveTierAsync(Guid workspaceId, Guid tierId, ArchiveDeploymentTierRequest request, CancellationToken cancellationToken = default)
        {
            ArchivedTierIds.Add(tierId);
            return ArchiveException is not null
                ? Task.FromException<WorkspaceDeploymentTier>(ArchiveException)
                : Task.FromResult(Tier(workspaceId, "Archived", []));
        }

        public Task<WorkspaceDeploymentTier> RestoreTierAsync(Guid workspaceId, Guid tierId, RestoreDeploymentTierRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DeploymentTierImpactSummary> PreviewTierImpactAsync(Guid workspaceId, Guid tierId, IReadOnlyList<string> proposedCapabilities, CancellationToken cancellationToken = default) =>
            Task.FromResult(Impact with { TierId = tierId, ProposedCapabilities = proposedCapabilities });

        public Task<IReadOnlyList<WorkspaceDeploymentTier>> EnsureDefaultTiersAsync(Guid workspaceId, Guid? actorAccountId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceDeploymentTier>>([]);

        private static WorkspaceDeploymentTier Tier(Guid workspaceId, string name, IReadOnlyList<string> capabilities) =>
            new(
                Guid.NewGuid(),
                workspaceId,
                name,
                null,
                10,
                false,
                DeploymentTierStatus.Active,
                capabilities,
                0,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                null,
                null,
                null);
    }
}
