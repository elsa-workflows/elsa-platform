using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace ValenceControl.Deployment.Core.Tests;

public sealed class WorkspaceDeploymentServiceTests
{
    private readonly Guid _workspaceId = WorkspaceDeploymentTestFixtures.WorkspaceId;
    private readonly RecordingDeploymentStore _store = new();

    [Fact]
    public async Task Gets_cockpit_projection_from_workspace_store()
    {
        _store.Cockpit = new DeploymentCockpit(
            [new WorkflowApplication("app-1", "Claims", "Workspace", [])],
            [],
            [],
            [],
            [],
            [],
            []);
        var service = new WorkspaceDeploymentService(_store);

        var cockpit = await service.GetCockpitAsync(_workspaceId);

        cockpit.Applications.Should().ContainSingle(x => x.Id == "app-1");
        _store.LastWorkspaceId.Should().Be(_workspaceId);
    }

    [Fact]
    public void Computes_stable_desired_state_hashes()
    {
        var hash = WorkspaceDeploymentService.ComputeDesiredStateHash("{\"kind\":\"Workflow\"}");

        hash.Should().Be(WorkspaceDeploymentService.ComputeDesiredStateHash("{\"kind\":\"Workflow\"}"));
        hash.Should().Be(WorkspaceDeploymentService.ComputeDesiredStateHash("{ \"kind\" : \"Workflow\" }"));
        hash.Should().NotBe(WorkspaceDeploymentService.ComputeDesiredStateHash("{\"kind\":\"Feature\"}"));
    }

    [Fact]
    public void Computes_canonical_desired_state_hashes()
    {
        var left = WorkspaceDeploymentService.ComputeDesiredStateHash("{\"payload\":{\"name\":\"Payment Retry\",\"version\":8}}");
        var right = WorkspaceDeploymentService.ComputeDesiredStateHash("{\"payload\":{\"version\":8,\"name\":\"Payment Retry\"}}");

        left.Should().Be(right);
    }

    [Fact]
    public async Task Validates_engine_registration_before_store_call()
    {
        var service = new WorkspaceDeploymentService(_store);

        var act = () => service.RegisterEngineAsync(
            _workspaceId,
            new RegisterWorkflowEngineRequest(
                Guid.NewGuid(),
                "claims-prod",
                "not-a-url",
                null,
                "Azure Key Vault",
                "kv://claims/prod",
                [],
                [],
                null));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Allows_engine_registration_with_credentials_deferred()
    {
        var service = new WorkspaceDeploymentService(_store);

        var engine = await service.RegisterEngineAsync(
            _workspaceId,
            new RegisterWorkflowEngineRequest(
                Guid.NewGuid(),
                "claims-dev",
                "https://workflows-dev.example.test/elsa",
                null,
                null,
                null,
                [],
                [],
                null,
                null,
                EngineCredentialAssignmentStatus.Deferred));

        engine.CredentialAssignmentStatus.Should().Be(EngineCredentialAssignmentStatus.Deferred);
        engine.CredentialProvider.Should().BeEmpty();
        engine.CredentialReference.Should().BeEmpty();
        _store.RegisteredEngineRequests.Should().ContainSingle().Which.CredentialAssignmentStatus.Should().Be(EngineCredentialAssignmentStatus.Deferred);
    }

    [Fact]
    public async Task Derives_secret_store_provider_from_type_when_provider_is_omitted()
    {
        var service = new WorkspaceDeploymentService(_store);

        var store = await service.CreateSecretStoreAsync(
            _workspaceId,
            new CreateDeploymentSecretStoreRequest(
                "Local credentials",
                null,
                null,
                null,
                DeploymentSecretStoreType.LocalEncryptedDatabase));

        store.Provider.Should().Be("Local encrypted database");
        _store.CreatedSecretStoreRequests.Should().ContainSingle().Which.Provider.Should().Be("Local encrypted database");
    }

    [Fact]
    public async Task Validates_protected_secret_usage_against_secret_store_type()
    {
        var service = new WorkspaceDeploymentService(_store);
        var externalStore = SecretStore(Guid.NewGuid(), DeploymentSecretStoreType.AzureKeyVault);
        var localStore = SecretStore(Guid.NewGuid(), DeploymentSecretStoreType.LocalEncryptedDatabase);
        _store.SecretStores.Add(externalStore);
        _store.SecretStores.Add(localStore);

        var external = () => service.CreateCredentialReferenceAsync(
            _workspaceId,
            new CreateDeploymentCredentialReferenceRequest(externalStore.Id, "Prod engine", "kv://claims/prod", null, null, "protected:v1"));
        var local = await service.CreateCredentialReferenceAsync(
            _workspaceId,
            new CreateDeploymentCredentialReferenceRequest(localStore.Id, "Dev engine", "local://engine-credentials/dev", null, null, "protected:v1"));

        await external.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("External engine credential stores accept locator metadata only, not secret values.");
        local.HasProtectedSecret.Should().BeTrue();
    }

    [Fact]
    public async Task Creates_environment_with_selected_tier_id()
    {
        var tierId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var service = new WorkspaceDeploymentService(_store);

        var environment = await service.CreateEnvironmentAsync(
            _workspaceId,
            new CreateDeploymentEnvironmentRequest(applicationId, "Production EU", EnvironmentTier.Production, tierId));

        environment.TierId.Should().Be(tierId);
        _store.CreatedEnvironmentRequests.Should().ContainSingle().Which.TierId.Should().Be(tierId);
    }

    [Fact]
    public async Task Updates_environment_with_selected_tier_id()
    {
        var tierId = Guid.NewGuid();
        var environmentId = Guid.NewGuid();
        var service = new WorkspaceDeploymentService(_store);

        var environment = await service.UpdateEnvironmentAsync(
            _workspaceId,
            environmentId,
            new UpdateDeploymentEnvironmentRequest(Guid.NewGuid(), "UAT", EnvironmentTier.Stage, tierId));

        environment.Id.Should().Be(environmentId);
        environment.TierId.Should().Be(tierId);
        _store.UpdatedEnvironmentRequests.Should().ContainSingle().Which.TierId.Should().Be(tierId);
    }

    [Fact]
    public async Task Surfaces_archived_or_cross_workspace_tier_rejection_from_store()
    {
        _store.CreateEnvironmentException = new InvalidOperationException("Deployment tier must be active in the workspace.");
        var service = new WorkspaceDeploymentService(_store);

        var act = () => service.CreateEnvironmentAsync(
            _workspaceId,
            new CreateDeploymentEnvironmentRequest(Guid.NewGuid(), "Prod", EnvironmentTier.Production, Guid.NewGuid()));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Deployment tier must be active in the workspace.");
    }

    private static WorkspaceDeploymentSecretStore SecretStore(Guid id, DeploymentSecretStoreType type) =>
        new(
            id,
            WorkspaceDeploymentTestFixtures.WorkspaceId,
            "Engine credentials",
            type.ToString(),
            type,
            null,
            DeploymentSecretStoreStatus.Active,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null);

    private sealed class RecordingDeploymentStore : IWorkspaceDeploymentStore
    {
        public Guid LastWorkspaceId { get; private set; }
        public DeploymentCockpit Cockpit { get; set; } = new([], [], [], [], [], [], []);
        public List<CreateDeploymentEnvironmentRequest> CreatedEnvironmentRequests { get; } = [];
        public List<UpdateDeploymentEnvironmentRequest> UpdatedEnvironmentRequests { get; } = [];
        public List<RegisterWorkflowEngineRequest> RegisteredEngineRequests { get; } = [];
        public List<CreateDeploymentSecretStoreRequest> CreatedSecretStoreRequests { get; } = [];
        public List<CreateDeploymentCredentialReferenceRequest> CreatedCredentialReferenceRequests { get; } = [];
        public List<WorkspaceDeploymentSecretStore> SecretStores { get; } = [];
        public Exception? CreateEnvironmentException { get; set; }

        public Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        {
            LastWorkspaceId = workspaceId;
            return Task.FromResult(Cockpit);
        }

        public Task<WorkspaceDeploymentApplication> CreateApplicationAsync(Guid workspaceId, CreateWorkflowApplicationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceDeploymentApplication> UpdateApplicationAsync(Guid workspaceId, Guid applicationId, UpdateWorkflowApplicationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(Guid workspaceId, CreateDeploymentEnvironmentRequest request, CancellationToken cancellationToken = default)
        {
            if (CreateEnvironmentException is not null)
                return Task.FromException<WorkspaceDeploymentEnvironment>(CreateEnvironmentException);
            CreatedEnvironmentRequests.Add(request);
            return Task.FromResult(Environment(Guid.NewGuid(), workspaceId, request.ApplicationId, request.Name, request.Tier, request.TierId));
        }

        public Task<WorkspaceDeploymentEnvironment> UpdateEnvironmentAsync(Guid workspaceId, Guid environmentId, UpdateDeploymentEnvironmentRequest request, CancellationToken cancellationToken = default)
        {
            UpdatedEnvironmentRequests.Add(request);
            return Task.FromResult(Environment(environmentId, workspaceId, request.ApplicationId, request.Name, request.Tier, request.TierId));
        }

        public Task<WorkspaceWorkflowEngine> RegisterEngineAsync(Guid workspaceId, RegisterWorkflowEngineRequest request, CancellationToken cancellationToken = default)
        {
            RegisteredEngineRequests.Add(request);
            return Task.FromResult(new WorkspaceWorkflowEngine(
                Guid.NewGuid(),
                workspaceId,
                request.EnvironmentId,
                request.Name,
                request.BaseUrl,
                request.Region,
                null,
                CertificateStatus.Trusted,
                request.CredentialAssignmentStatus == EngineCredentialAssignmentStatus.Deferred ? "" : request.CredentialProvider ?? "",
                request.CredentialAssignmentStatus == EngineCredentialAssignmentStatus.Deferred ? "" : request.CredentialReference ?? "",
                CredentialVerificationStatus.Unverified,
                null,
                DeploymentHealth.Degraded,
                null,
                request.HostingProvider,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                "",
                request.CredentialAssignmentStatus == EngineCredentialAssignmentStatus.Deferred ? null : request.CredentialReferenceId,
                request.CredentialAssignmentStatus));
        }

        public Task<WorkspaceWorkflowEngine> UpdateEngineAsync(Guid workspaceId, Guid engineId, UpdateWorkflowEngineRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceDesiredStateRevision> CreateRevisionAsync(Guid workspaceId, CreateDesiredStateRevisionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceDesiredStateRevision?> GetRevisionAsync(Guid workspaceId, Guid revisionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceDesiredStateRevision?> GetLatestRevisionAsync(Guid workspaceId, Guid environmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceWorkflowEngine?> GetEngineAsync(Guid workspaceId, Guid engineId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WorkspaceDeploymentSecretStore>> ListSecretStoresAsync(Guid workspaceId, bool includeArchived = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceDeploymentSecretStore>>(SecretStores);

        public Task<WorkspaceDeploymentSecretStore> CreateSecretStoreAsync(Guid workspaceId, CreateDeploymentSecretStoreRequest request, CancellationToken cancellationToken = default)
        {
            CreatedSecretStoreRequests.Add(request);
            return Task.FromResult(SecretStore(Guid.NewGuid(), request.Type, request.Provider));
        }

        public Task<IReadOnlyList<WorkspaceDeploymentCredentialReference>> ListCredentialReferencesAsync(Guid workspaceId, Guid? secretStoreId = null, bool includeArchived = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceDeploymentCredentialReference>>([]);

        public Task<WorkspaceDeploymentCredentialReference> CreateCredentialReferenceAsync(Guid workspaceId, CreateDeploymentCredentialReferenceRequest request, CancellationToken cancellationToken = default)
        {
            CreatedCredentialReferenceRequests.Add(request);
            var store = SecretStores.Single(x => x.Id == request.SecretStoreId);
            return Task.FromResult(CredentialReference(Guid.NewGuid(), store, request));
        }

        private static WorkspaceDeploymentEnvironment Environment(
            Guid id,
            Guid workspaceId,
            Guid applicationId,
            string name,
            EnvironmentTier tier,
            Guid? tierId) =>
            new(
                id,
                workspaceId,
                applicationId,
                name,
                tier,
                null,
                null,
                DeploymentStatus.Blocked,
                DriftStatus.Unknown,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                tierId,
                tierId is null
                    ? null
                    : new DeploymentTierProfile(tierId.Value, "Custom", DeploymentTierStatus.Active, [DeploymentTierCapabilities.PromotionTarget]));

        private static WorkspaceDeploymentSecretStore SecretStore(Guid id, DeploymentSecretStoreType type, string? provider = null) =>
            new(
                id,
                WorkspaceDeploymentTestFixtures.WorkspaceId,
                "Engine credentials",
                provider ?? type.ToString(),
                type,
                null,
                DeploymentSecretStoreStatus.Active,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                null,
                null,
                null);

        private static WorkspaceDeploymentCredentialReference CredentialReference(
            Guid id,
            WorkspaceDeploymentSecretStore store,
            CreateDeploymentCredentialReferenceRequest request) =>
            new(
                id,
                store.WorkspaceId,
                store.Id,
                store.Name,
                store.Provider,
                store.Type,
                request.Name,
                request.Reference,
                request.Description,
                DeploymentSecretStoreStatus.Active,
                CredentialVerificationStatus.Unverified,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                request.ActorAccountId,
                request.ActorAccountId,
                null,
                null,
                !string.IsNullOrWhiteSpace(request.ProtectedSecret),
                0);
    }
}
