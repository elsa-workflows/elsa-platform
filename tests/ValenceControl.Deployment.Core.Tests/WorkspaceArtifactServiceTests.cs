using ValenceControl.Deployment.Abstractions.Diagnostics;
using ValenceControl.Deployment.Artifacts;
using ValenceControl.Deployment.Core.Workspace;
using Xunit;
using ArtifactDigest = ValenceControl.Deployment.Abstractions.Artifacts.ArtifactDigest;
using ArtifactChecksumStatus = ValenceControl.Deployment.Artifacts.DeploymentArtifactChecksumStatus;

namespace ValenceControl.Deployment.Core.Tests;

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

        Assert.True(result.Created);
        Assert.Equal("sha256:claims-prod", result.Artifact.ArtifactId);
        Assert.Equal(ArtifactTypeIds.ElsaLoomRecipe, result.Artifact.ArtifactTypeId);
        Assert.Equal("manual", result.Artifact.Producer!.ProducerType);
        Assert.NotNull(result.Artifact.CompatibilityHints);
        Assert.Single(result.Artifact.CompatibilityHints!, x => x.RequiredArtifactType == ArtifactTypeIds.ElsaLoomRecipe);
        Assert.Single(result.Artifact.Resources, x => x.LogicalId == "payment-retry");
        Assert.Single(_store.RegisteredRequests);
    }

    [Fact]
    public async Task Duplicate_registration_is_idempotent_when_metadata_matches()
    {
        var request = WorkspaceDeploymentTestFixtures.ArtifactRegistration();
        var existing = RecordingArtifactStore.Artifact(_workspaceId, request);
        _store.Artifacts.Add(existing.Id, existing);
        var service = new WorkspaceArtifactService(_store, _reader, _time);

        var result = await service.RegisterArtifactAsync(_workspaceId, request);

        Assert.False(result.Created);
        Assert.Equal(existing.Id, result.Artifact.Id);
        Assert.Empty(_store.RegisteredRequests);
    }

    [Fact]
    public async Task Duplicate_registration_rejects_conflicting_metadata()
    {
        var request = WorkspaceDeploymentTestFixtures.ArtifactRegistration();
        _store.Artifacts.Add(Guid.NewGuid(), RecordingArtifactStore.Artifact(_workspaceId, request with { Reference = "/tmp/other" }));
        var service = new WorkspaceArtifactService(_store, _reader, _time);

        var act = () => service.RegisterArtifactAsync(_workspaceId, request);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Artifact identity is already registered with different metadata.", exception.Message);
    }

    [Fact]
    public async Task Duplicate_registration_rejects_conflicting_envelope_metadata()
    {
        var request = WorkspaceDeploymentTestFixtures.ArtifactRegistration();
        _store.Artifacts.Add(Guid.NewGuid(), RecordingArtifactStore.Artifact(_workspaceId, request with { Producer = new ArtifactProducer("studio", "Elsa Studio") }));
        var service = new WorkspaceArtifactService(_store, _reader, _time);

        var act = () => service.RegisterArtifactAsync(_workspaceId, request);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Artifact identity is already registered with different metadata.", exception.Message);
    }

    [Fact]
    public async Task Rejects_unknown_artifact_type()
    {
        var service = new WorkspaceArtifactService(_store, _reader, _time);

        var act = () => service.RegisterArtifactAsync(
            _workspaceId,
            WorkspaceDeploymentTestFixtures.ArtifactRegistration() with { ArtifactTypeId = "unknown.type" });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Artifact type is not supported.", exception.Message);
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

        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Empty(_store.RegisteredRequests);
    }

    [Fact]
    public async Task Rejects_secret_like_metadata()
    {
        var service = new WorkspaceArtifactService(_store, _reader, _time);

        var act = () => service.RegisterArtifactAsync(
            _workspaceId,
            WorkspaceDeploymentTestFixtures.ArtifactRegistration(reference: "local:///tmp/token-value"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Artifact metadata contains unsafe secret-like content.", exception.Message);
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

        Assert.Equal(WorkspaceArtifactChecksumStatus.Verified, result.ChecksumStatus);
        Assert.Equal(WorkspaceArtifactInspectionStatus.Valid, result.InspectionStatus);
        Assert.Equal(_time.GetUtcNow(), result.LastInspectedAt);
        Assert.Single(result.Resources, x => x.LogicalId == "payment-retry");
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

        Assert.Equal(artifact.ArtifactId, result.ArtifactId);
        Assert.Equal(WorkspaceArtifactChecksumStatus.Mismatched, result.ChecksumStatus);
        Assert.Equal(WorkspaceArtifactInspectionStatus.Invalid, result.InspectionStatus);
        Assert.Contains(result.Diagnostics, x => x.Code == "artifact.identity.mismatch");
        Assert.Contains(result.Diagnostics, x => x.Message.Contains("[redacted]", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, x => x.Message.Contains("secret value", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Refresh_unsupported_reference_provider_fails_closed()
    {
        var request = WorkspaceDeploymentTestFixtures.ArtifactRegistration(referenceProvider: "oci", reference: "oci://registry/claims");
        var artifact = RecordingArtifactStore.Artifact(_workspaceId, request);
        _store.Artifacts.Add(artifact.Id, artifact);
        var service = new WorkspaceArtifactService(_store, _reader, _time);

        var result = await service.RefreshInspectionAsync(_workspaceId, artifact.Id);

        Assert.Equal(WorkspaceArtifactChecksumStatus.Unavailable, result.ChecksumStatus);
        Assert.Equal(WorkspaceArtifactInspectionStatus.Unsupported, result.InspectionStatus);
        Assert.Single(result.Diagnostics, x => x.Code == "artifact.reference.unsupported");
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
                        ArtifactTypeIds.ElsaLoomRecipe,
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

        public Task<IReadOnlyList<WorkspaceArtifact>> ListArtifactsAsync(Guid workspaceId, bool includeArchived = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceArtifact>>(Artifacts.Values
                .Where(x => x.WorkspaceId == workspaceId)
                .Where(x => includeArchived || x.Status == WorkspaceArtifactLifecycleStatus.Active)
                .ToList());

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

        public Task<WorkspaceArtifact> ArchiveArtifactAsync(Guid workspaceId, Guid artifactRecordId, Guid actorAccountId, CancellationToken cancellationToken = default)
        {
            var artifact = GetExistingArtifact(workspaceId, artifactRecordId);
            if (artifact.Status == WorkspaceArtifactLifecycleStatus.Archived)
                return Task.FromResult(artifact);

            var now = DateTimeOffset.UtcNow;
            artifact = artifact with
            {
                Status = WorkspaceArtifactLifecycleStatus.Archived,
                ArchivedAt = now,
                ArchivedByAccountId = actorAccountId,
                UpdatedAt = now
            };
            Artifacts[artifact.Id] = artifact;
            return Task.FromResult(artifact);
        }

        public Task<WorkspaceArtifact> RestoreArtifactAsync(Guid workspaceId, Guid artifactRecordId, CancellationToken cancellationToken = default)
        {
            var artifact = GetExistingArtifact(workspaceId, artifactRecordId);
            if (artifact.Status == WorkspaceArtifactLifecycleStatus.Active)
                return Task.FromResult(artifact);

            artifact = artifact with
            {
                Status = WorkspaceArtifactLifecycleStatus.Active,
                ArchivedAt = null,
                ArchivedByAccountId = null,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Artifacts[artifact.Id] = artifact;
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
                DateTimeOffset.UtcNow,
                request.EnvelopeVersion,
                request.ArtifactTypeId,
                request.ArtifactSchemaVersion,
                request.ManifestDigest,
                request.PayloadReference,
                request.Producer,
                request.DisplayMetadata,
                request.CompatibilityHints);

        private WorkspaceArtifact GetExistingArtifact(Guid workspaceId, Guid artifactRecordId)
        {
            if (!Artifacts.TryGetValue(artifactRecordId, out var artifact) || artifact.WorkspaceId != workspaceId)
                throw new KeyNotFoundException("Artifact does not exist in the workspace.");
            return artifact;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
