using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace Elsa.Platform.Deployment.Core.Tests;

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

    private sealed class RecordingDeploymentStore : IWorkspaceDeploymentStore
    {
        public Dictionary<Guid, WorkspaceDesiredStateRevision> Revisions { get; } = [];
        public Dictionary<Guid, WorkspaceDesiredStateRevision?> LatestByEnvironment { get; } = [];
        public Dictionary<Guid, WorkspaceWorkflowEngine> Engines { get; } = [];

        public Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
