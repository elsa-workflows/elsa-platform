using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace ValenceControl.Deployment.Core.Tests;

public sealed class DeploymentValidationServiceTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _sourceEnvironmentId = Guid.NewGuid();
    private readonly Guid _targetEnvironmentId = Guid.NewGuid();
    private readonly Guid _sourceRevisionId = Guid.NewGuid();
    private readonly Guid _targetRevisionId = Guid.NewGuid();
    private readonly Guid _targetEngineId = Guid.NewGuid();
    private readonly RecordingDeploymentStore _store = new();

    [Fact]
    public async Task Previews_structured_desired_state_without_mutating_target_revision()
    {
        _store.Revisions[_sourceRevisionId] = Revision(_sourceRevisionId, _sourceEnvironmentId, 12, """
            {"records":[
              {"kind":"Workflow","name":"Payment Retry","payload":{"version":8}},
              {"kind":"SecretReference","name":"Payment API","payload":{"reference":"kv://payments/api"}}
            ]}
            """);
        _store.LatestByEnvironment[_targetEnvironmentId] = Revision(_targetRevisionId, _targetEnvironmentId, 9, """
            {"records":[
              {"kind":"Workflow","name":"Payment Retry","payload":{"version":7}},
              {"kind":"Feature","name":"Legacy Toggle","payload":{"enabled":true}}
            ]}
            """);
        _store.Engines[_targetEngineId] = Engine(_targetEngineId, _targetEnvironmentId);
        var service = new DeploymentValidationService(_store);

        var comparison = await service.PreviewPromotionAsync(
            _workspaceId,
            new WorkspacePromotionPreviewRequest(_sourceEnvironmentId, _targetEnvironmentId, _sourceRevisionId, _targetEngineId));

        comparison.SourceRevision.Should().Be(12);
        comparison.TargetRevision.Should().Be(9);
        comparison.Diff.Should().Contain(x => x.Name == "Payment Retry" && x.Impact == DiffImpact.Changed);
        comparison.Diff.Should().Contain(x => x.Name == "Payment API" && x.Impact == DiffImpact.Added);
        comparison.Diff.Should().Contain(x => x.Name == "Legacy Toggle" && x.Impact == DiffImpact.Removed);
        comparison.Validations.Should().ContainSingle(x => x.Severity == ValidationSeverity.Pass && x.Scope == "Secret references");
        _store.LatestByEnvironment[_targetEnvironmentId]!.Id.Should().Be(_targetRevisionId);
    }

    [Fact]
    public async Task Blocks_preview_when_required_secret_reference_is_missing()
    {
        _store.Revisions[_sourceRevisionId] = Revision(_sourceRevisionId, _sourceEnvironmentId, 3, """
            {"records":[{"kind":"SecretReference","name":"Payment API","payload":{"reference":""}}]}
            """);
        _store.Engines[_targetEngineId] = Engine(_targetEngineId, _targetEnvironmentId);
        var service = new DeploymentValidationService(_store);

        var comparison = await service.PreviewPromotionAsync(
            _workspaceId,
            new WorkspacePromotionPreviewRequest(_sourceEnvironmentId, _targetEnvironmentId, _sourceRevisionId, _targetEngineId));

        comparison.Validations.Should().ContainSingle(x =>
            x.Severity == ValidationSeverity.Blocker
            && x.Scope == "Secret references"
            && x.Message == "Payment API secret reference is missing.");
    }

    [Fact]
    public async Task Blocks_preview_when_tiers_do_not_allow_promotion_direction()
    {
        _store.Revisions[_sourceRevisionId] = Revision(_sourceRevisionId, _sourceEnvironmentId, 4, """{"records":[]}""");
        _store.Engines[_targetEngineId] = Engine(_targetEngineId, _targetEnvironmentId);
        _store.Environments.Add(Environment(_sourceEnvironmentId, "Sandbox", EnvironmentTier.Dev, DeploymentTierCapabilities.DevelopmentLike));
        _store.Environments.Add(Environment(_targetEnvironmentId, "Review", EnvironmentTier.Test, DeploymentTierCapabilities.TestLike));
        var service = new DeploymentValidationService(_store);

        var comparison = await service.PreviewPromotionAsync(
            _workspaceId,
            new WorkspacePromotionPreviewRequest(_sourceEnvironmentId, _targetEnvironmentId, _sourceRevisionId, _targetEngineId));

        comparison.Validations.Should().Contain(x =>
            x.Id == "deployment.tier.source.unsupported"
            && x.Severity == ValidationSeverity.Blocker
            && x.Message == "Sandbox cannot be used as a promotion source.");
        comparison.Validations.Should().Contain(x =>
            x.Id == "deployment.tier.target.unsupported"
            && x.Severity == ValidationSeverity.Blocker
            && x.Message == "Review cannot be used as a promotion target.");
    }

    [Fact]
    public async Task Blocks_preview_when_target_engine_belongs_to_another_environment()
    {
        var otherEnvironmentId = Guid.NewGuid();
        _store.Revisions[_sourceRevisionId] = Revision(_sourceRevisionId, _sourceEnvironmentId, 4, """{"records":[]}""");
        _store.Engines[_targetEngineId] = Engine(_targetEngineId, otherEnvironmentId);
        _store.Environments.Add(Environment(_sourceEnvironmentId, "Stage", EnvironmentTier.Stage, DeploymentTierCapabilities.PromotionSource));
        _store.Environments.Add(Environment(_targetEnvironmentId, "Prod", EnvironmentTier.Production, DeploymentTierCapabilities.PromotionTarget));
        _store.EngineRegistrations.Add(EngineRegistration(_targetEngineId, otherEnvironmentId));
        var service = new DeploymentValidationService(_store);

        var comparison = await service.PreviewPromotionAsync(
            _workspaceId,
            new WorkspacePromotionPreviewRequest(_sourceEnvironmentId, _targetEnvironmentId, _sourceRevisionId, _targetEngineId));

        comparison.Validations.Should().ContainSingle(x =>
            x.Id == "deployment.engine.environment-mismatch"
            && x.Severity == ValidationSeverity.Blocker);
    }

    [Fact]
    public async Task Applies_target_tier_safeguards_from_capabilities()
    {
        _store.Revisions[_sourceRevisionId] = Revision(_sourceRevisionId, _sourceEnvironmentId, 5, """
            {"records":[{"kind":"SecretReference","name":"Payment API","payload":{"reference":"kv://payments/api"}}]}
            """);
        _store.Engines[_targetEngineId] = Engine(_targetEngineId, _targetEnvironmentId);
        _store.Environments.Add(Environment(_sourceEnvironmentId, "UAT", EnvironmentTier.Stage, DeploymentTierCapabilities.PromotionSource));
        _store.Environments.Add(Environment(
            _targetEnvironmentId,
            "Production EU",
            EnvironmentTier.Production,
            DeploymentTierCapabilities.PromotionTarget,
            DeploymentTierCapabilities.ProductionLike,
            DeploymentTierCapabilities.ConfirmationRequired,
            DeploymentTierCapabilities.RollbackEnabled,
            DeploymentTierCapabilities.SecretVerificationRequired,
            DeploymentTierCapabilities.ObservabilityRequired));
        var service = new DeploymentValidationService(_store);

        var comparison = await service.PreviewPromotionAsync(
            _workspaceId,
            new WorkspacePromotionPreviewRequest(_sourceEnvironmentId, _targetEnvironmentId, _sourceRevisionId, _targetEngineId));

        comparison.Validations.Should().Contain(x => x.Id == "deployment.tier.production-like" && x.Severity == ValidationSeverity.Warning);
        comparison.Validations.Should().Contain(x => x.Id == "deployment.tier.confirmation-required" && x.Severity == ValidationSeverity.Warning);
        comparison.Validations.Should().Contain(x => x.Id == "deployment.tier.rollback-enabled" && x.Severity == ValidationSeverity.Pass);
        comparison.Validations.Should().Contain(x => x.Id == "deployment.tier.observability-required" && x.Severity == ValidationSeverity.Blocker);
        comparison.Validations.Should().Contain(x => x.Scope == "Secret references" && x.Severity == ValidationSeverity.Pass);
    }

    [Fact]
    public async Task Skips_secret_reference_validation_when_target_tier_does_not_require_it()
    {
        _store.Revisions[_sourceRevisionId] = Revision(_sourceRevisionId, _sourceEnvironmentId, 6, """
            {"records":[{"kind":"SecretReference","name":"Payment API","payload":{"reference":""}}]}
            """);
        _store.Engines[_targetEngineId] = Engine(_targetEngineId, _targetEnvironmentId);
        _store.Environments.Add(Environment(_sourceEnvironmentId, "Build", EnvironmentTier.Dev, DeploymentTierCapabilities.PromotionSource));
        _store.Environments.Add(Environment(_targetEnvironmentId, "QA", EnvironmentTier.Test, DeploymentTierCapabilities.PromotionTarget));
        var service = new DeploymentValidationService(_store);

        var comparison = await service.PreviewPromotionAsync(
            _workspaceId,
            new WorkspacePromotionPreviewRequest(_sourceEnvironmentId, _targetEnvironmentId, _sourceRevisionId, _targetEngineId));

        comparison.Validations.Should().NotContain(x => x.Scope == "Secret references");
        comparison.Validations.Should().NotContain(x => x.Severity == ValidationSeverity.Blocker);
    }

    [Fact]
    public async Task Previews_artifact_backed_desired_state_with_safe_artifact_contract()
    {
        var artifactRecordId = Guid.NewGuid();
        var desiredStateJson = """
            {"records":[{
              "kind":"ArtifactReference",
              "name":"Payment Retry",
              "payload":{
                "artifactRecordId":"__artifactRecordId__",
                "artifactId":"workflow:payment-retry:v2",
                "artifactTypeId":"elsa.workflow-definition",
                "contentDigest":{"algorithm":"sha256","value":"v2"},
                "safeMetadata":{"displayName":"Payment Retry","version":"2"},
                "configuration":{"environment":"stage"},
                "compatibilityHints":[{"requiredCapabilities":["workflow-definition.apply"]}]
              }}]}
            """.Replace("__artifactRecordId__", artifactRecordId.ToString("D"), StringComparison.Ordinal);
        _store.Revisions[_sourceRevisionId] = Revision(_sourceRevisionId, _sourceEnvironmentId, 7, desiredStateJson);
        _store.LatestByEnvironment[_targetEnvironmentId] = Revision(_targetRevisionId, _targetEnvironmentId, 6, """
            {"records":[{
              "kind":"ArtifactReference",
              "name":"Payment Retry",
              "payload":{
                "artifactId":"workflow:payment-retry:v2",
                "artifactTypeId":"elsa.workflow-definition",
                "contentDigest":{"algorithm":"sha256","value":"v2"},
                "configuration":{"environment":"prod"}
              }}]}
            """);
        _store.Engines[_targetEngineId] = Engine(_targetEngineId, _targetEnvironmentId);
        _store.EngineRegistrations.Add(EngineRegistration(_targetEngineId, _targetEnvironmentId));
        var service = new DeploymentValidationService(_store);

        var comparison = await service.PreviewPromotionAsync(
            _workspaceId,
            new WorkspacePromotionPreviewRequest(_sourceEnvironmentId, _targetEnvironmentId, _sourceRevisionId, _targetEngineId));

        var artifact = comparison.Artifacts.Should().ContainSingle().Subject;
        artifact.Name.Should().Be("Payment Retry");
        artifact.Impact.Should().Be(PromotionArtifactImpact.Changed);
        artifact.Source!.ArtifactRecordId.Should().Be(artifactRecordId.ToString("D"));
        artifact.Source.ArtifactId.Should().Be("workflow:payment-retry:v2");
        artifact.Source.ArtifactTypeId.Should().Be("elsa.workflow-definition");
        artifact.Source.ContentDigest.Should().Be(new PromotionArtifactDigest("sha256", "v2"));
        artifact.Source.Metadata.Should().Contain("displayName", "Payment Retry");
        artifact.Source.Configuration.Should().Contain("environment", "stage");
        artifact.Target!.Configuration.Should().Contain("environment", "prod");
        artifact.RuntimeCompatibility.Should().ContainSingle(x => x.Severity == ValidationSeverity.Pass);
    }

    private WorkspaceDesiredStateRevision Revision(Guid revisionId, Guid environmentId, int revisionNumber, string desiredStateJson) =>
        new(
            revisionId,
            _workspaceId,
            Guid.NewGuid(),
            environmentId,
            revisionNumber,
            $"Revision {revisionNumber}",
            null,
            WorkspaceDeploymentService.ComputeDesiredStateHash(desiredStateJson),
            desiredStateJson,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

    private WorkspaceWorkflowEngine Engine(Guid engineId, Guid environmentId) =>
        new(
            engineId,
            _workspaceId,
            environmentId,
            "target-engine",
            "https://engine.example.test/elsa",
            null,
            null,
            CertificateStatus.Trusted,
            "Azure Key Vault",
            "kv://engine",
            CredentialVerificationStatus.Verified,
            DateTimeOffset.UtcNow,
            DeploymentHealth.Healthy,
            DateTimeOffset.UtcNow,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private WorkflowEngineRegistration EngineRegistration(Guid engineId, Guid environmentId) =>
        new(
            engineId.ToString("D"),
            "target-engine",
            environmentId.ToString("D"),
            new EngineEndpointMetadata("https://engine.example.test/elsa", "", "4.1.0", CertificateStatus.Trusted),
            new EngineCredentialReference("Azure Key Vault", "kv://engine", CredentialVerificationStatus.Verified, DateTimeOffset.UtcNow),
            DeploymentHealth.Healthy,
            DateTimeOffset.UtcNow,
            [new EngineCapability("workflow-definition.apply", "Apply workflow definitions", CapabilityBoundary.EngineApi)],
            [],
            null);

    private EnvironmentSummary Environment(Guid environmentId, string name, EnvironmentTier tier, params string[] capabilities) =>
        new(
            environmentId.ToString("D"),
            name,
            tier,
            DeploymentHealth.Healthy,
            new DesiredStateRevision(_sourceRevisionId.ToString("D"), 1, "abc123", "Revision 1", DateTimeOffset.UtcNow),
            null,
            DeploymentStatus.Succeeded,
            DriftStatus.InSync,
            [],
            name,
            DeploymentTierStatus.Active.ToString(),
            capabilities);

    private sealed class RecordingDeploymentStore : IWorkspaceDeploymentStore
    {
        public Dictionary<Guid, WorkspaceDesiredStateRevision> Revisions { get; } = [];
        public Dictionary<Guid, WorkspaceDesiredStateRevision?> LatestByEnvironment { get; } = [];
        public Dictionary<Guid, WorkspaceWorkflowEngine> Engines { get; } = [];
        public List<WorkflowEngineRegistration> EngineRegistrations { get; } = [];
        public List<EnvironmentSummary> Environments { get; } = [];

        public Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeploymentCockpit(
                [new WorkflowApplication(Guid.NewGuid().ToString("D"), "Payments", "Workspace", Environments)],
                EngineRegistrations,
                [],
                [],
                [],
                [],
                []));

        public Task<WorkspaceDeploymentApplication> CreateApplicationAsync(Guid workspaceId, CreateWorkflowApplicationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceDeploymentApplication> UpdateApplicationAsync(Guid workspaceId, Guid applicationId, UpdateWorkflowApplicationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(Guid workspaceId, CreateDeploymentEnvironmentRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceDeploymentEnvironment> UpdateEnvironmentAsync(Guid workspaceId, Guid environmentId, UpdateDeploymentEnvironmentRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceWorkflowEngine> RegisterEngineAsync(Guid workspaceId, RegisterWorkflowEngineRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceWorkflowEngine> UpdateEngineAsync(Guid workspaceId, Guid engineId, UpdateWorkflowEngineRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceDesiredStateRevision> CreateRevisionAsync(Guid workspaceId, CreateDesiredStateRevisionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceDesiredStateRevision?> GetRevisionAsync(Guid workspaceId, Guid revisionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Revisions.GetValueOrDefault(revisionId));

        public Task<WorkspaceDesiredStateRevision?> GetLatestRevisionAsync(Guid workspaceId, Guid environmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(LatestByEnvironment.GetValueOrDefault(environmentId));

        public Task<WorkspaceWorkflowEngine?> GetEngineAsync(Guid workspaceId, Guid engineId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Engines.GetValueOrDefault(engineId));
    }
}
