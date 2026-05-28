using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace Elsa.Platform.Deployment.Core.Tests;

public sealed class DeploymentTierServiceTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _tierId = Guid.NewGuid();
    private readonly RecordingTierStore _store = new();

    [Fact]
    public void Capability_catalog_contains_platform_defined_semantics()
    {
        var service = new DeploymentTierService();

        var capabilities = service.GetCapabilityCatalog();

        capabilities.Should().Contain(x => x.Id == DeploymentTierCapabilities.ProductionLike);
        capabilities.Should().Contain(x => x.Id == DeploymentTierCapabilities.PromotionSource);
        capabilities.Should().Contain(x => x.Id == DeploymentTierCapabilities.PromotionTarget);
        capabilities.Should().Contain(x => x.Id == DeploymentTierCapabilities.ConfirmationRequired);
        capabilities.Select(x => x.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Default_tier_mappings_preserve_legacy_semantics()
    {
        DeploymentTierService.DefaultCapabilitiesByLegacyTier[EnvironmentTier.Dev].Should().BeEquivalentTo(
            [DeploymentTierCapabilities.DevelopmentLike, DeploymentTierCapabilities.PromotionSource]);
        DeploymentTierService.DefaultCapabilitiesByLegacyTier[EnvironmentTier.Test].Should().Contain(DeploymentTierCapabilities.PromotionTarget);
        DeploymentTierService.DefaultCapabilitiesByLegacyTier[EnvironmentTier.Stage].Should().Contain(DeploymentTierCapabilities.SecretVerificationRequired);
        DeploymentTierService.DefaultCapabilitiesByLegacyTier[EnvironmentTier.Production].Should().BeEquivalentTo(
            [
                DeploymentTierCapabilities.ProductionLike,
                DeploymentTierCapabilities.PromotionTarget,
                DeploymentTierCapabilities.ConfirmationRequired,
                DeploymentTierCapabilities.RollbackEnabled,
                DeploymentTierCapabilities.SecretVerificationRequired,
                DeploymentTierCapabilities.ObservabilityRequired
            ]);
    }

    [Fact]
    public async Task Rejects_unknown_capabilities_before_store_mutation()
    {
        var service = new DeploymentTierService(_store);

        var act = () => service.CreateTierAsync(
            _workspaceId,
            new CreateDeploymentTierRequest("UAT", null, 10, ["deployment.unknown"], null));

        await act.Should().ThrowAsync<ArgumentException>();
        _store.CreatedRequests.Should().BeEmpty();
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

        _store.CreatedRequests.Should().ContainSingle();
        _store.CreatedRequests.Single().Capabilities.Should().Equal(
            DeploymentTierCapabilities.PromotionTarget,
            DeploymentTierCapabilities.PreproductionLike);
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

        await act.Should().ThrowAsync<InvalidOperationException>();
        _store.UpdatedRequests.Should().BeEmpty();
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

        DeploymentTierService.IsProductionLike(environment).Should().BeTrue();
        DeploymentTierService.RequiresConfirmation(environment).Should().BeTrue();
        DeploymentTierService.IsPromotionTarget(environment).Should().BeFalse();
    }

    private sealed class RecordingTierStore : IWorkspaceDeploymentTierStore
    {
        public List<CreateDeploymentTierRequest> CreatedRequests { get; } = [];
        public List<UpdateDeploymentTierRequest> UpdatedRequests { get; } = [];
        public DeploymentTierImpactSummary Impact { get; set; } = new(Guid.Empty, [], [], [], [], 0, [], []);

        public Task<IReadOnlyList<WorkspaceDeploymentTier>> ListTiersAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceDeploymentTier>>([]);

        public Task<WorkspaceDeploymentTier?> GetTierAsync(Guid workspaceId, Guid tierId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkspaceDeploymentTier?>(null);

        public Task<WorkspaceDeploymentTier> CreateTierAsync(Guid workspaceId, CreateDeploymentTierRequest request, CancellationToken cancellationToken = default)
        {
            CreatedRequests.Add(request);
            return Task.FromResult(Tier(workspaceId, request.Name, request.Capabilities));
        }

        public Task<WorkspaceDeploymentTier> UpdateTierAsync(Guid workspaceId, Guid tierId, UpdateDeploymentTierRequest request, DeploymentTierImpactSummary impact, CancellationToken cancellationToken = default)
        {
            UpdatedRequests.Add(request);
            return Task.FromResult(Tier(workspaceId, request.Name, request.Capabilities));
        }

        public Task<WorkspaceDeploymentTier> ArchiveTierAsync(Guid workspaceId, Guid tierId, ArchiveDeploymentTierRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("At least one active deployment tier is required.");

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
