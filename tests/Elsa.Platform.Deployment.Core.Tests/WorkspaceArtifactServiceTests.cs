using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Deployment.Core.Workspace;
using FluentAssertions;
using Xunit;
using ArtifactDigest = Elsa.Platform.Deployment.Abstractions.Artifacts.ArtifactDigest;
using ArtifactChecksumStatus = Elsa.Platform.Deployment.Artifacts.DeploymentArtifactChecksumStatus;

namespace Elsa.Platform.Deployment.Core.Tests;

public sealed class WorkspaceArtifactServiceTests : IDisposable
{
    private readonly Guid _workspaceId = WorkspaceDeploymentTestFixtures.WorkspaceId;
    private readonly RecordingArtifactStore _store = new();
    private readonly RecordingArtifactReader _reader = new();
    private readonly FixedTimeProvider _time = new(DateTimeOffset.Parse("2026-05-28T10:00:00Z"));
    private readonly List<string> _tempPaths = [];

    [Fact]
    public async Task Registers_valid_metadata_without_payload_content()
    {
        var service = new WorkspaceArtifactService(_store, _reader, _time);

        var result = await service.RegisterArtifactAsync(
            _workspaceId,
            WorkspaceDeploymentTestFixtures.ArtifactRegistration());

        result.Created.Should().BeTrue();
        result.Artifact.ArtifactId.Should().Be("sha256:claims-prod");
        result.Artifact.Resources.Should().ContainSingle(x => x.LogicalId == "payment-retry");
        _store.RegisteredRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task Duplicate_registration_is_idempotent_when_metadata_matches()
    {
        var request = WorkspaceDeploymentTestFixtures.ArtifactRegistration();
        var existing = RecordingArtifactStore.Artifact(_workspaceId, request);
        _store.Artifacts.Add(existing.Id, existing);
        var service = new WorkspaceArtifactService(_store, _reader, _time);

        var result = await service.RegisterArtifactAsync(_workspaceId, request);

        result.Created.Should().BeFalse();
        result.Artifact.Id.Should().Be(existing.Id);
        _store.RegisteredRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_registration_rejects_conflicting_metadata()
    {
        var request = WorkspaceDeploymentTestFixtures.ArtifactRegistration();
        _store.Artifacts.Add(Guid.NewGuid(), RecordingArtifactStore.Artifact(_workspaceId, request with { Reference = "/tmp/other" }));
        var service = new WorkspaceArtifactService(_store, _reader, _time);

        var act = () => service.RegisterArtifactAsync(_workspaceId, request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Artifact identity is already registered with different metadata.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("artifact.invalid/v1")]
    public async Task Rejects_unsupported_or_missing_layout_versions(string layoutVersion)
    {
        var service = new WorkspaceArtifactService(_store, _reader, _time);

        var act = () => service.RegisterArtifactAsync(
            _workspaceId,
            WorkspaceDeploymentTestFixtures.ArtifactRegistration() with { LayoutVersion = layoutVersion });

        await act.Should().ThrowAsync<InvalidOperationException>();
        _store.RegisteredRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_secret_like_metadata()
    {
        var service = new WorkspaceArtifactService(_store, _reader, _time);

        var act = () => service.RegisterArtifactAsync(
            _workspaceId,
            WorkspaceDeploymentTestFixtures.ArtifactRegistration(reference: "local:///tmp/token-value"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Artifact metadata contains unsafe secret-like content.");
    }

    [Fact]
    public async Task Refresh_valid_local_artifact_updates_status_and_resources()
    {
        var path = CreateTempFile();
        var request = WorkspaceDeploymentTestFixtures.ArtifactRegistration(reference: path);
        var artifact = RecordingArtifactStore.Artifact(_workspaceId, request);
        _store.Artifacts.Add(artifact.Id, artifact);
        _reader.Result = Inspection(
            artifact.ArtifactId,
            succeeded: true,
            [
                new DeploymentArtifactChecksumVerification("payload/workflow.json", DeploymentArtifactEntryKind.Payload, ArtifactChecksumStatus.Verified)
            ]);
        var service = new WorkspaceArtifactService(_store, _reader, _time);

        var result = await service.RefreshInspectionAsync(_workspaceId, artifact.Id);

        result.ChecksumStatus.Should().Be(WorkspaceArtifactChecksumStatus.Verified);
        result.InspectionStatus.Should().Be(WorkspaceArtifactInspectionStatus.Valid);
        result.LastInspectedAt.Should().Be(_time.GetUtcNow());
        result.Resources.Should().ContainSingle(x => x.LogicalId == "payment-retry");
    }

    [Fact]
    public async Task Refresh_mismatched_reference_preserves_identity_and_redacts_diagnostics()
    {
        var path = CreateTempFile();
        var request = WorkspaceDeploymentTestFixtures.ArtifactRegistration(reference: path);
        var artifact = RecordingArtifactStore.Artifact(_workspaceId, request);
        _store.Artifacts.Add(artifact.Id, artifact);
        _reader.Result = Inspection(
            "sha256:other",
            succeeded: false,
            [
                new DeploymentArtifactChecksumVerification("payload/workflow.json", DeploymentArtifactEntryKind.Payload, ArtifactChecksumStatus.Mismatched)
            ],
            [new DeploymentDiagnostic("payload.invalid", DeploymentDiagnosticSeverity.Error, "secret value leaked")]);
        var service = new WorkspaceArtifactService(_store, _reader, _time);

        var result = await service.RefreshInspectionAsync(_workspaceId, artifact.Id);

        result.ArtifactId.Should().Be(artifact.ArtifactId);
        result.ChecksumStatus.Should().Be(WorkspaceArtifactChecksumStatus.Mismatched);
        result.InspectionStatus.Should().Be(WorkspaceArtifactInspectionStatus.Invalid);
        result.Diagnostics.Should().Contain(x => x.Code == "artifact.identity.mismatch");
        result.Diagnostics.Should().Contain(x => x.Message.Contains("[redacted]", StringComparison.Ordinal));
        result.Diagnostics.Should().NotContain(x => x.Message.Contains("secret value", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Refresh_unsupported_reference_provider_fails_closed()
    {
        var request = WorkspaceDeploymentTestFixtures.ArtifactRegistration(referenceProvider: "oci", reference: "oci://registry/claims");
        var artifact = RecordingArtifactStore.Artifact(_workspaceId, request);
        _store.Artifacts.Add(artifact.Id, artifact);
        var service = new WorkspaceArtifactService(_store, _reader, _time);

        var result = await service.RefreshInspectionAsync(_workspaceId, artifact.Id);

        result.ChecksumStatus.Should().Be(WorkspaceArtifactChecksumStatus.Unavailable);
        result.InspectionStatus.Should().Be(WorkspaceArtifactInspectionStatus.Unsupported);
        result.Diagnostics.Should().ContainSingle(x => x.Code == "artifact.reference.unsupported");
    }

    public void Dispose()
    {
        foreach (var path in _tempPaths.Where(File.Exists))
            File.Delete(path);
    }

    private string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid():N}.zip");
        File.WriteAllText(path, "artifact");
        _tempPaths.Add(path);
        return path;
    }

    private static DeploymentArtifactInspectionResult Inspection(
        string artifactId,
        bool succeeded,
        IReadOnlyCollection<DeploymentArtifactChecksumVerification> checksums,
        IReadOnlyCollection<DeploymentDiagnostic>? diagnostics = null) =>
        new(
            succeeded,
            artifactId,
            new DeploymentArtifactMetadata(
                ArtifactLayoutConstants.LayoutVersion,
                artifactId,
                DateTimeOffset.UtcNow,
                new DeploymentArtifactManifestMetadata("claims", "1.0.0", "prod", new Dictionary<string, string>(), new Dictionary<string, string>()),
                [
                    new DeploymentArtifactResourceSummary(
                        "workflowDefinition",
                        "payment-retry",
                        null,
                        "8",
                        new ArtifactDigest("sha256", "workflow-hash"))
                ],
                new ArtifactDigest("sha256", "claims-prod")),
            null,
            null,
            [],
            checksums,
            diagnostics ?? []);

    private sealed class RecordingArtifactReader : IDeploymentArtifactReader
    {
        public DeploymentArtifactInspectionResult Result { get; set; } = Inspection("sha256:claims-prod", true, []);

        public ValueTask<DeploymentArtifactInspectionResult> InspectFolderAsync(string path, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result);

        public ValueTask<DeploymentArtifactInspectionResult> InspectZipAsync(string path, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result);
    }

    private sealed class RecordingArtifactStore : IWorkspaceArtifactStore
    {
        public Dictionary<Guid, WorkspaceArtifact> Artifacts { get; } = [];
        public List<RegisterWorkspaceArtifactRequest> RegisteredRequests { get; } = [];

        public Task<IReadOnlyList<WorkspaceArtifact>> ListArtifactsAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceArtifact>>(Artifacts.Values.Where(x => x.WorkspaceId == workspaceId).ToList());

        public Task<WorkspaceArtifact?> GetArtifactAsync(Guid workspaceId, Guid artifactRecordId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Artifacts.TryGetValue(artifactRecordId, out var artifact) && artifact.WorkspaceId == workspaceId ? artifact : null);

        public Task<WorkspaceArtifact?> FindArtifactByIdentityAsync(Guid workspaceId, string artifactId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Artifacts.Values.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.ArtifactId == artifactId));

        public Task<WorkspaceArtifact> RegisterArtifactAsync(Guid workspaceId, RegisterWorkspaceArtifactRequest request, CancellationToken cancellationToken = default)
        {
            RegisteredRequests.Add(request);
            var artifact = Artifact(workspaceId, request);
            Artifacts.Add(artifact.Id, artifact);
            return Task.FromResult(artifact);
        }

        public Task<WorkspaceArtifactInspectionResult> UpdateArtifactInspectionAsync(Guid workspaceId, WorkspaceArtifactInspectionUpdate update, CancellationToken cancellationToken = default)
        {
            var artifact = Artifacts[update.ArtifactRecordId];
            artifact = artifact with
            {
                ChecksumStatus = update.ChecksumStatus,
                InspectionStatus = update.InspectionStatus,
                LastInspectedAt = update.LastInspectedAt,
                Resources = update.Resources,
                Diagnostics = update.Diagnostics,
                UpdatedAt = update.LastInspectedAt
            };
            Artifacts[artifact.Id] = artifact;
            return Task.FromResult(new WorkspaceArtifactInspectionResult(
                artifact.Id,
                artifact.ArtifactId,
                artifact.ChecksumStatus,
                artifact.InspectionStatus,
                artifact.LastInspectedAt,
                artifact.Resources.Count,
                artifact.Resources,
                artifact.Diagnostics));
        }

        public static WorkspaceArtifact Artifact(Guid workspaceId, RegisterWorkspaceArtifactRequest request) =>
            new(
                Guid.NewGuid(),
                workspaceId,
                request.ArtifactId,
                request.LayoutVersion,
                request.ContentDigest,
                request.Format,
                request.ReferenceProvider,
                request.Reference,
                request.Manifest,
                request.Resources,
                WorkspaceArtifactChecksumStatus.Unverified,
                WorkspaceArtifactInspectionStatus.NeverInspected,
                request.Diagnostics,
                DateTimeOffset.UtcNow,
                request.ActorAccountId,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
