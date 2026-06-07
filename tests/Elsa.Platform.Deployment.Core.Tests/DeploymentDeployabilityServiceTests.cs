using System.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace Elsa.Platform.Deployment.Core.Tests;

public sealed class DeploymentDeployabilityServiceTests
{
    private readonly Guid _workspaceId = WorkspaceDeploymentTestFixtures.WorkspaceId;
    private readonly Guid _applicationId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private readonly Guid _environmentId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private readonly Guid _revisionId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private readonly Guid _engineId = Guid.Parse("80000000-0000-0000-0000-000000000001");
    private readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-06-07T10:00:00Z");

    [Fact]
    public async Task Compatible_legacy_engine_capability_is_normalized_to_canonical_apply_requirement()
    {
        var fixture = new DeployabilityFixture(_workspaceId, _applicationId, _environmentId, _revisionId, _engineId, _now);
        var artifact = fixture.AddArtifact();
        fixture.SetRevisionFor(artifact);
        fixture.SetEngineCapabilities([new EngineCapability("workflow-definition.apply", "Apply workflow definitions", CapabilityBoundary.EngineApi)]);

        var result = await fixture.Service.EvaluateAsync(_workspaceId, _revisionId, fixture.Request);

        result.Status.Should().Be(DeploymentDeployabilityStatus.Deployable);
        result.Blockers.Should().BeEmpty();
        result.Artifacts.Single().RequiredCapabilities.Should().ContainSingle(ArtifactApplyCapability.For(ArtifactTypeIds.ElsaWorkflowDefinition));
    }

    [Fact]
    public async Task Missing_or_stale_engine_capability_metadata_blocks_deployment()
    {
        var fixture = new DeployabilityFixture(_workspaceId, _applicationId, _environmentId, _revisionId, _engineId, _now);
        var artifact = fixture.AddArtifact();
        fixture.SetRevisionFor(artifact);
        fixture.SetEngineCapabilities([], _now.AddMinutes(-20));

        var result = await fixture.Service.EvaluateAsync(_workspaceId, _revisionId, fixture.Request);

        result.Status.Should().Be(DeploymentDeployabilityStatus.Blocked);
        result.Blockers.Select(x => x.Id).Should().Contain(["artifact.capability.missing", "engine.capabilities.missing", "engine.capabilities.stale"]);
        result.Blockers.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.Remediation));
    }

    [Fact]
    public async Task Artifact_state_blockers_report_distinct_safe_remediations()
    {
        var fixture = new DeployabilityFixture(_workspaceId, _applicationId, _environmentId, _revisionId, _engineId, _now);
        var archived = fixture.AddArtifact(status: WorkspaceArtifactLifecycleStatus.Archived);
        var unsupported = fixture.AddArtifact(
            artifactId: "sha256:unsupported",
            artifactTypeId: "custom.unsupported",
            digestValue: "unsupported");
        var unavailable = fixture.AddArtifact(
            artifactId: "sha256:unavailable",
            digestValue: "unavailable",
            payloadAvailable: false,
            inspectionStatus: WorkspaceArtifactInspectionStatus.Unavailable);
        fixture.SetRevisionFor(archived, unsupported, unavailable);
        fixture.SetEngineCapabilities([new EngineCapability(ArtifactApplyCapability.For(ArtifactTypeIds.ElsaWorkflowDefinition), "Apply workflow definitions", CapabilityBoundary.EngineApi)]);

        var result = await fixture.Service.EvaluateAsync(_workspaceId, _revisionId, fixture.Request);

        result.Status.Should().Be(DeploymentDeployabilityStatus.Blocked);
        result.Blockers.Select(x => x.Id).Should().Contain(["artifact.archived", "artifact.type.unsupported", "artifact.inspection.invalid", "artifact.payload.unavailable"]);
        result.Blockers.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.Remediation));
    }

    [Fact]
    public async Task Unsupported_schema_version_blocks_deployment()
    {
        var fixture = new DeployabilityFixture(_workspaceId, _applicationId, _environmentId, _revisionId, _engineId, _now);
        var artifact = fixture.AddArtifact(schemaVersion: "v9");
        fixture.SetRevisionFor(artifact);
        fixture.SetEngineCapabilities([new EngineCapability(ArtifactApplyCapability.For(ArtifactTypeIds.ElsaWorkflowDefinition), "Apply workflow definitions", CapabilityBoundary.EngineApi)]);

        var result = await fixture.Service.EvaluateAsync(_workspaceId, _revisionId, fixture.Request);

        result.Status.Should().Be(DeploymentDeployabilityStatus.Blocked);
        result.Blockers.Should().ContainSingle(x => x.Id == "artifact.schema.unsupported");
    }

    [Fact]
    public async Task Ten_artifact_evaluation_for_ten_registered_engines_stays_under_one_second()
    {
        var fixture = new DeployabilityFixture(_workspaceId, _applicationId, _environmentId, _revisionId, _engineId, _now);
        var artifacts = Enumerable.Range(0, 10)
            .Select(index => fixture.AddArtifact($"sha256:artifact-{index}", $"artifact-{index}"))
            .ToArray();
        fixture.SetRevisionFor(artifacts);
        fixture.SetEngines(Enumerable.Range(0, 10)
            .Select(index => fixture.Engine(
                index == 0 ? _engineId : Guid.Parse($"80000000-0000-0000-0000-{index + 1:000000000000}"),
                [new EngineCapability(ArtifactApplyCapability.For(ArtifactTypeIds.ElsaWorkflowDefinition), "Apply workflow definitions", CapabilityBoundary.EngineApi)]))
            .ToArray());

        var stopwatch = Stopwatch.StartNew();
        var result = await fixture.Service.EvaluateAsync(_workspaceId, _revisionId, fixture.Request);
        stopwatch.Stop();

        result.Status.Should().Be(DeploymentDeployabilityStatus.Deployable);
        result.Artifacts.Should().HaveCount(10);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    private sealed class DeployabilityFixture
    {
        private readonly Guid _workspaceId;
        private readonly Guid _applicationId;
        private readonly Guid _environmentId;
        private readonly Guid _revisionId;
        private readonly Guid _engineId;
        private readonly DateTimeOffset _now;
        private readonly RecordingDeploymentStore _deploymentStore;
        private readonly RecordingArtifactStore _artifactStore = new();

        public DeployabilityFixture(Guid workspaceId, Guid applicationId, Guid environmentId, Guid revisionId, Guid engineId, DateTimeOffset now)
        {
            _workspaceId = workspaceId;
            _applicationId = applicationId;
            _environmentId = environmentId;
            _revisionId = revisionId;
            _engineId = engineId;
            _now = now;
            _deploymentStore = new RecordingDeploymentStore(workspaceId, applicationId, environmentId, revisionId, engineId, now);
            Service = new DeploymentDeployabilityService(_deploymentStore, _artifactStore, timeProvider: new FixedTimeProvider(now));
        }

        public DeploymentDeployabilityService Service { get; }

        public DeploymentDeployabilityRequest Request => new(_environmentId, _engineId, DeploymentRunMode.Apply);

        public WorkspaceArtifact AddArtifact(
            string artifactId = "sha256:claims-prod",
            string digestValue = "claims-prod",
            string artifactTypeId = ArtifactTypeIds.ElsaWorkflowDefinition,
            string schemaVersion = ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion,
            WorkspaceArtifactLifecycleStatus status = WorkspaceArtifactLifecycleStatus.Active,
            WorkspaceArtifactInspectionStatus inspectionStatus = WorkspaceArtifactInspectionStatus.Valid,
            bool payloadAvailable = true)
        {
            var artifact = new WorkspaceArtifact(
                Guid.NewGuid(),
                _workspaceId,
                artifactId,
                "platform.elsa.io/deployment-artifact/v1alpha1",
                new WorkspaceArtifactDigest("sha256", digestValue),
                WorkspaceArtifactFormat.Zip,
                "local",
                $"/tmp/{digestValue}.zip",
                new WorkspaceArtifactManifestSummary(artifactId, "1.0.0", "dev"),
                [WorkspaceDeploymentTestFixtures.ArtifactResource()],
                WorkspaceArtifactChecksumStatus.Verified,
                inspectionStatus,
                [],
                _now,
                Guid.Parse("20000000-0000-0000-0000-000000000001"),
                _now,
                _now,
                _now,
                ArtifactEnvelopeConstants.EnvelopeVersion,
                artifactTypeId,
                schemaVersion,
                new WorkspaceArtifactDigest("sha256", $"{digestValue}-manifest"),
                payloadAvailable ? new ArtifactPayloadReference("local", $"/tmp/{digestValue}.zip") : null,
                WorkspaceDeploymentTestFixtures.StudioArtifactProducer(),
                WorkspaceDeploymentTestFixtures.ArtifactDisplayMetadata(),
                [new ArtifactCompatibilityHint(artifactTypeId, "elsa-workflows", null, [ArtifactApplyCapability.For(artifactTypeId)], new Dictionary<string, string>())],
                status);
            _artifactStore.Artifacts[artifact.Id] = artifact;
            return artifact;
        }

        public void SetRevisionFor(params WorkspaceArtifact[] artifacts)
        {
            var records = string.Join(",", artifacts.Select(ArtifactRecordJson));
            _deploymentStore.Revision = _deploymentStore.Revision with
            {
                DesiredStateJson = $$"""{"records":[{{records}}]}"""
            };
        }

        public void SetEngineCapabilities(IReadOnlyList<EngineCapability> capabilities, DateTimeOffset? lastHeartbeatAt = null) =>
            _deploymentStore.Engines = [Engine(_engineId, capabilities, lastHeartbeatAt)];

        public WorkflowEngineRegistration Engine(Guid engineId, IReadOnlyList<EngineCapability> capabilities, DateTimeOffset? lastHeartbeatAt = null) =>
            new(
                engineId.ToString("D"),
                $"engine-{engineId:N}",
                _environmentId.ToString("D"),
                new EngineEndpointMetadata("https://runtime.example.test", "westeurope", "4.0.0", CertificateStatus.Trusted),
                new EngineCredentialReference("External secret store", "kv://runtime", CredentialVerificationStatus.Verified, _now),
                DeploymentHealth.Healthy,
                lastHeartbeatAt ?? _now,
                capabilities,
                [],
                "container-apps");

        public void SetEngines(IReadOnlyList<WorkflowEngineRegistration> engines) => _deploymentStore.Engines = engines;

        private static string ArtifactRecordJson(WorkspaceArtifact artifact) =>
            $$"""
            {
              "kind": "ArtifactReference",
              "name": "{{artifact.ArtifactId}}",
              "payload": {
                "artifactRecordId": "{{artifact.Id:D}}",
                "artifactId": "{{artifact.ArtifactId}}",
                "artifactTypeId": "{{artifact.ArtifactTypeId}}",
                "contentDigest": {
                  "algorithm": "{{artifact.ContentDigest.Algorithm}}",
                  "value": "{{artifact.ContentDigest.Value}}"
                }
              }
            }
            """;
    }

    private sealed class RecordingDeploymentStore : IWorkspaceDeploymentStore
    {
        private readonly Guid _workspaceId;
        private readonly Guid _applicationId;
        private readonly Guid _environmentId;
        private readonly Guid _engineId;
        private readonly DateTimeOffset _now;

        public RecordingDeploymentStore(Guid workspaceId, Guid applicationId, Guid environmentId, Guid revisionId, Guid engineId, DateTimeOffset now)
        {
            _workspaceId = workspaceId;
            _applicationId = applicationId;
            _environmentId = environmentId;
            _engineId = engineId;
            _now = now;
            Revision = new WorkspaceDesiredStateRevision(revisionId, workspaceId, applicationId, environmentId, 1, "r1", null, "abc123", "{\"records\":[]}", now, now, null);
            Engines = [DefaultEngine()];
        }

        public WorkspaceDesiredStateRevision Revision { get; set; }
        public IReadOnlyList<WorkflowEngineRegistration> Engines { get; set; }

        public Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeploymentCockpit(
                [new WorkflowApplication(_applicationId.ToString("D"), "Claims", "Workspace", [Environment()])],
                Engines,
                [],
                [],
                [],
                [],
                []));

        public Task<WorkspaceDesiredStateRevision?> GetRevisionAsync(Guid workspaceId, Guid revisionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(revisionId == Revision.Id ? Revision : null);

        public Task<WorkspaceWorkflowEngine?> GetEngineAsync(Guid workspaceId, Guid engineId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceDeploymentApplication> CreateApplicationAsync(Guid workspaceId, CreateWorkflowApplicationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentApplication> UpdateApplicationAsync(Guid workspaceId, Guid applicationId, UpdateWorkflowApplicationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(Guid workspaceId, CreateDeploymentEnvironmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentEnvironment> UpdateEnvironmentAsync(Guid workspaceId, Guid environmentId, UpdateDeploymentEnvironmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceWorkflowEngine> RegisterEngineAsync(Guid workspaceId, RegisterWorkflowEngineRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceWorkflowEngine> UpdateEngineAsync(Guid workspaceId, Guid engineId, UpdateWorkflowEngineRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDesiredStateRevision> CreateRevisionAsync(Guid workspaceId, CreateDesiredStateRevisionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDesiredStateRevision?> GetLatestRevisionAsync(Guid workspaceId, Guid environmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private EnvironmentSummary Environment() =>
            new(
                _environmentId.ToString("D"),
                "Dev",
                EnvironmentTier.Dev,
                DeploymentHealth.Healthy,
                new DesiredStateRevision(Revision.Id.ToString("D"), Revision.RevisionNumber, Revision.Commit ?? "", Revision.Label, Revision.AuthoredAt),
                null,
                DeploymentStatus.Succeeded,
                DriftStatus.InSync,
                [_engineId.ToString("D")],
                "Dev",
                DeploymentTierStatus.Active.ToString(),
                []);

        private WorkflowEngineRegistration DefaultEngine() =>
            new(
                _engineId.ToString("D"),
                "dev-01",
                _environmentId.ToString("D"),
                new EngineEndpointMetadata("https://runtime.example.test", "westeurope", "4.0.0", CertificateStatus.Trusted),
                new EngineCredentialReference("External secret store", "kv://runtime", CredentialVerificationStatus.Verified, _now),
                DeploymentHealth.Healthy,
                _now,
                [new EngineCapability(ArtifactApplyCapability.For(ArtifactTypeIds.ElsaWorkflowDefinition), "Apply workflow definitions", CapabilityBoundary.EngineApi)],
                [],
                "container-apps");
    }

    private sealed class RecordingArtifactStore : IWorkspaceArtifactStore
    {
        public Dictionary<Guid, WorkspaceArtifact> Artifacts { get; } = [];

        public Task<IReadOnlyList<WorkspaceArtifact>> ListArtifactsAsync(Guid workspaceId, bool includeArchived = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceArtifact>>(Artifacts.Values.ToList());

        public Task<WorkspaceArtifact?> GetArtifactAsync(Guid workspaceId, Guid artifactRecordId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Artifacts.GetValueOrDefault(artifactRecordId));

        public Task<WorkspaceArtifact?> FindArtifactByIdentityAsync(Guid workspaceId, string artifactId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Artifacts.Values.SingleOrDefault(x => x.ArtifactId == artifactId));

        public Task<WorkspaceArtifact> RegisterArtifactAsync(Guid workspaceId, RegisterWorkspaceArtifactRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceArtifact> ArchiveArtifactAsync(Guid workspaceId, Guid artifactRecordId, Guid actorAccountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceArtifact> RestoreArtifactAsync(Guid workspaceId, Guid artifactRecordId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceArtifactInspectionResult> UpdateArtifactInspectionAsync(Guid workspaceId, WorkspaceArtifactInspectionUpdate update, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
