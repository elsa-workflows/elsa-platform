using System.Diagnostics;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed class DeploymentWorkspaceArtifactPersistenceTests : IDisposable
{
    private readonly CatalogDbContext _db;
    private readonly DeploymentWorkspaceStore _store;
    private readonly Guid _workspaceId;
    private readonly Guid _accountId;

    public DeploymentWorkspaceArtifactPersistenceTests()
    {
        _db = CreateDbContext();
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        var workspace = new Workspace { Name = "Artifact Workspace" };
        var account = new Account { DisplayName = "Artifact User", Email = "artifact@example.test" };
        _db.Workspaces.Add(workspace);
        _db.Accounts.Add(account);
        _db.SaveChanges();

        _workspaceId = workspace.Id;
        _accountId = account.Id;
        _store = new DeploymentWorkspaceStore(_db);
    }

    [Fact]
    public async Task Persists_artifact_metadata_without_payload_content()
    {
        var artifact = await _store.RegisterArtifactAsync(_workspaceId, ArtifactRegistration());
        _db.ChangeTracker.Clear();

        var loaded = await _store.GetArtifactAsync(_workspaceId, artifact.Id);
        var listed = await _store.ListArtifactsAsync(_workspaceId);
        var rawJson = await ReadScalarAsync<string>("SELECT ResourceSummaryJson || DiagnosticsJson FROM WorkspaceDeploymentArtifacts LIMIT 1");

        loaded.Should().NotBeNull();
        loaded!.ArtifactId.Should().Be("sha256:claims-prod");
        loaded.ArtifactTypeId.Should().Be(ArtifactTypeIds.ElsaWorkflowDefinition);
        loaded.Producer!.ProducerType.Should().Be("manual");
        loaded.DisplayMetadata!.Name.Should().Be("claims");
        loaded.CompatibilityHints.Should().ContainSingle(x => x.RequiredArtifactType == ArtifactTypeIds.ElsaWorkflowDefinition);
        loaded.Manifest.Name.Should().Be("claims");
        loaded.Resources.Should().ContainSingle(x => x.LogicalId == "payment-retry");
        listed.Should().ContainSingle(x => x.Id == artifact.Id);
        rawJson.Should().NotContain("workflow definition payload");
        rawJson.Should().NotContain("secret value");
        rawJson.Should().NotContain("token");
    }

    [Fact]
    public async Task Persists_envelope_metadata_without_payload_or_secrets()
    {
        var artifact = await _store.RegisterArtifactAsync(
            _workspaceId,
            ArtifactRegistration() with
            {
                Producer = new ArtifactProducer("studio", "Elsa Studio", "4.0.0", "workflow:claims"),
                DisplayMetadata = new ArtifactDisplayMetadata(
                    "Claims",
                    "1.0.0",
                    "Claims workflow",
                    new Dictionary<string, string> { ["domain"] = "claims" },
                    new Dictionary<string, string> { ["studio.workflowId"] = "claims" }),
                CompatibilityHints =
                [
                    new ArtifactCompatibilityHint(
                        ArtifactTypeIds.ElsaWorkflowDefinition,
                        "elsa-workflows",
                        ">=4.0.0",
                        ["workflow-definition.apply"],
                        new Dictionary<string, string>())
                ]
            });
        _db.ChangeTracker.Clear();

        var loaded = await _store.GetArtifactAsync(_workspaceId, artifact.Id);
        var rawJson = await ReadScalarAsync<string>("SELECT PayloadReferenceJson || ProducerJson || DisplayMetadataJson || CompatibilityHintsJson FROM WorkspaceDeploymentArtifacts LIMIT 1");

        loaded!.Producer!.ProducerType.Should().Be("studio");
        loaded.DisplayMetadata!.Labels.Should().ContainKey("domain");
        loaded.CompatibilityHints.Should().ContainSingle(x => x.RuntimeFamily == "elsa-workflows");
        rawJson.Should().NotContain("workflow definition payload");
        rawJson.Should().NotContain("secret value");
        rawJson.Should().NotContain("token");
        rawJson.Should().NotContain("password");
    }

    [Fact]
    public async Task Enforces_unique_artifact_identity_per_workspace()
    {
        await _store.RegisterArtifactAsync(_workspaceId, ArtifactRegistration());

        var act = () => _store.RegisterArtifactAsync(_workspaceId, ArtifactRegistration(reference: "/tmp/other"));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Allows_same_artifact_identity_in_different_workspaces()
    {
        var otherWorkspaceId = await CreateWorkspaceAsync("Other Artifact Workspace");
        var first = await _store.RegisterArtifactAsync(_workspaceId, ArtifactRegistration());
        var second = await _store.RegisterArtifactAsync(otherWorkspaceId, ArtifactRegistration());

        first.ArtifactId.Should().Be(second.ArtifactId);
        first.WorkspaceId.Should().NotBe(second.WorkspaceId);
    }

    [Fact]
    public async Task Updates_inspection_status_without_changing_identity()
    {
        var artifact = await _store.RegisterArtifactAsync(_workspaceId, ArtifactRegistration());
        var inspectedAt = DateTimeOffset.Parse("2026-05-28T10:00:00Z");

        var update = await _store.UpdateArtifactInspectionAsync(
            _workspaceId,
            new WorkspaceArtifactInspectionUpdate(
                artifact.Id,
                artifact.ArtifactId,
                WorkspaceArtifactChecksumStatus.Mismatched,
                WorkspaceArtifactInspectionStatus.Invalid,
                inspectedAt,
                [ArtifactResource("workflowDefinition", "payment-retry-v2")],
                [new WorkspaceArtifactDiagnostic("artifact.identity.mismatch", WorkspaceArtifactDiagnosticSeverity.Error, "Referenced artifact identity does not match.")]));
        _db.ChangeTracker.Clear();
        var loaded = await _store.GetArtifactAsync(_workspaceId, artifact.Id);

        update.ArtifactId.Should().Be(artifact.ArtifactId);
        update.ChecksumStatus.Should().Be(WorkspaceArtifactChecksumStatus.Mismatched);
        loaded!.ArtifactId.Should().Be(artifact.ArtifactId);
        loaded.Resources.Should().ContainSingle(x => x.LogicalId == "payment-retry-v2");
        loaded.Diagnostics.Should().ContainSingle(x => x.Code == "artifact.identity.mismatch");
    }

    [Fact]
    public async Task Persists_artifact_upload_session_lifecycle_and_workspace_scope()
    {
        var otherWorkspaceId = await CreateWorkspaceAsync("Other Upload Workspace");
        var now = DateTimeOffset.Parse("2026-06-04T12:00:00Z");
        var session = new WorkspaceArtifactUploadSession(
            Guid.NewGuid(),
            _workspaceId,
            WorkspaceArtifactUploadStatus.Pending,
            "claims-prod.zip",
            "application/zip",
            1024,
            null,
            "/tmp/staged.zip",
            "upload-key",
            [],
            now.AddMinutes(30),
            null,
            _accountId,
            now,
            now);

        var created = await _store.CreateArtifactUploadSessionAsync(session);
        var updated = await _store.UpdateArtifactUploadSessionAsync(created with
        {
            Status = WorkspaceArtifactUploadStatus.Completed,
            UploadedSizeBytes = 1024,
            Diagnostics = [new WorkspaceArtifactDiagnostic("artifact.upload.completed", WorkspaceArtifactDiagnosticSeverity.Info, "Upload completed.")],
            UpdatedAt = now.AddMinutes(1)
        });
        _db.ChangeTracker.Clear();

        var loaded = await _store.GetArtifactUploadSessionAsync(_workspaceId, session.Id);
        var byIdempotency = await _store.FindArtifactUploadByIdempotencyKeyAsync(_workspaceId, "upload-key");
        var crossWorkspace = await _store.GetArtifactUploadSessionAsync(otherWorkspaceId, session.Id);

        updated.Status.Should().Be(WorkspaceArtifactUploadStatus.Completed);
        loaded!.UploadedSizeBytes.Should().Be(1024);
        loaded.Diagnostics.Should().ContainSingle(x => x.Code == "artifact.upload.completed");
        byIdempotency!.Id.Should().Be(session.Id);
        crossWorkspace.Should().BeNull();
    }

    [Fact]
    public async Task Rejects_inspection_update_that_changes_identity()
    {
        var artifact = await _store.RegisterArtifactAsync(_workspaceId, ArtifactRegistration());

        var act = () => _store.UpdateArtifactInspectionAsync(
            _workspaceId,
            new WorkspaceArtifactInspectionUpdate(
                artifact.Id,
                "sha256:other",
                WorkspaceArtifactChecksumStatus.Verified,
                WorkspaceArtifactInspectionStatus.Valid,
                DateTimeOffset.UtcNow,
                artifact.Resources,
                []));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Artifact inspection update cannot change the registered artifact identity.");
    }

    [Fact]
    public async Task Lists_normal_artifact_dataset_under_three_seconds()
    {
        for (var i = 0; i < 250; i++)
            await _store.RegisterArtifactAsync(_workspaceId, ArtifactRegistration($"sha256:artifact-{i:000}", $"/tmp/artifact-{i:000}"));

        var stopwatch = Stopwatch.StartNew();
        var artifacts = await _store.ListArtifactsAsync(_workspaceId);
        stopwatch.Stop();

        artifacts.Should().HaveCount(250);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    public void Dispose() => _db.Dispose();

    private async Task<Guid> CreateWorkspaceAsync(string name)
    {
        var workspace = new Workspace { Name = name };
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync();
        return workspace.Id;
    }

    private async Task<T> ReadScalarAsync<T>(string sql)
    {
        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T));
    }

    private RegisterWorkspaceArtifactRequest ArtifactRegistration(
        string artifactId = "sha256:claims-prod",
        string reference = "/tmp/claims-prod") =>
        new(
            artifactId,
            "platform.elsa.io/deployment-artifact/v1alpha1",
            new WorkspaceArtifactDigest("sha256", artifactId.Replace("sha256:", "", StringComparison.Ordinal)),
            WorkspaceArtifactFormat.Folder,
            "local",
            reference,
            new WorkspaceArtifactManifestSummary("claims", "1.0.0", "prod"),
            [ArtifactResource()],
            [],
            _accountId);

    private static WorkspaceArtifactResourceSummary ArtifactResource(
        string type = "workflowDefinition",
        string logicalId = "payment-retry") =>
        new(type, logicalId, null, "8", new WorkspaceArtifactDigest("sha256", "workflow-hash"));

    private static CatalogDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        return new CatalogDbContext(options);
    }
}
