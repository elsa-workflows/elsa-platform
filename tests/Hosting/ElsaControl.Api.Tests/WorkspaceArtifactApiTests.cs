using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using ElsaControl.Api.Workspace;
using ElsaControl.Deployment.Artifacts;
using ElsaControl.Deployment.Core.Cockpit;
using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.Deployment.Manifest;
using ElsaControl.PackageCatalog.Core.Accounts;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed class WorkspaceArtifactApiTests : IClassFixture<DefaultControlApiTestApplicationFixture>
{
    private readonly ControlApiTestApplication _app;

    public WorkspaceArtifactApiTests(DefaultControlApiTestApplicationFixture fixture) => _app = fixture.Application;

    [Fact]
    public async Task Owner_can_register_list_and_read_safe_artifact_metadata()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var registerResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.ArtifactRegistration());
        var registered = (await registerResponse.Content.ReadControlJsonAsync<WorkspaceArtifact>())!;
        var list = (await owner.GetControlJsonAsync<WorkspaceArtifactListResponse>($"/api/workspaces/{workspaceId}/artifacts"))!;
        var detail = (await owner.GetControlJsonAsync<WorkspaceArtifact>($"/api/workspaces/{workspaceId}/artifacts/{registered.Id}"))!;
        var responseText = await registerResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        Assert.Single(list.Items, x => x.Id == registered.Id);
        Assert.Equal("claims", detail.Manifest.Name);
        Assert.Equal(WorkspaceArtifactLifecycleStatus.Active, detail.Status);
        Assert.Equal(ArtifactTypeIds.ElsaLoomRecipe, detail.ArtifactTypeId);
        Assert.Equal("manual", detail.Producer!.ProducerType);
        Assert.Single(detail.CompatibilityHints!, x => x.RequiredArtifactType == ArtifactTypeIds.ElsaLoomRecipe);
        Assert.Single(detail.Resources, x => x.LogicalId == "payment-retry");
        Assert.DoesNotContain("workflow definition payload", responseText);
        Assert.DoesNotContain("secret value", responseText);
        Assert.DoesNotContain("token", responseText);
    }

    [Fact]
    public async Task Owner_can_register_envelope_metadata_and_discover_artifact_types()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-envelope-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var types = (await owner.GetControlJsonAsync<WorkspaceArtifactTypeListResponse>($"/api/workspaces/{workspaceId}/artifacts/types"))!;
        var registerResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.ArtifactRegistration() with
            {
                Producer = new ArtifactProducer("studio", "Elsa Studio", "4.0.0", "workflow:claims"),
                DisplayMetadata = new ArtifactDisplayMetadata(
                    "Claims",
                    "1.0.0",
                    "Claims workflow",
                    new Dictionary<string, string> { ["domain"] = "claims" },
                    new Dictionary<string, string>()),
                CompatibilityHints =
                [
                    new ArtifactCompatibilityHint(
                        ArtifactTypeIds.ElsaLoomRecipe,
                        "elsa-workflows",
                        ">=4.0.0",
                        ["loom.recipe.apply"],
                        new Dictionary<string, string>())
                ]
            });
        var registered = (await registerResponse.Content.ReadControlJsonAsync<WorkspaceArtifact>())!;

        Assert.Single(types.Items, x => x.TypeId == ArtifactTypeIds.ElsaLoomRecipe);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        Assert.Equal("studio", registered.Producer!.ProducerType);
        Assert.Contains("domain", registered.DisplayMetadata!.Labels.Keys);
        Assert.Single(registered.CompatibilityHints!, x => x.RuntimeFamily == "elsa-workflows");
    }

    [Fact]
    public async Task Studio_submit_artifact_registration_does_not_create_deployment_run()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("studio-submit-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var registerResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.WorkflowEnvelopeRegistration("sha256:studio-submit"));
        var duplicateResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.WorkflowEnvelopeRegistration("sha256:studio-submit"));
        var artifact = (await registerResponse.Content.ReadControlJsonAsync<WorkspaceArtifact>())!;
        var cockpit = (await owner.GetControlJsonAsync<DeploymentCockpit>($"/api/workspaces/{workspaceId}/deployments/cockpit"))!;

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
        Assert.Equal("studio", artifact.Producer!.ProducerType);
        Assert.Equal(ArtifactTypeIds.ElsaWorkflowDefinition, artifact.ArtifactTypeId);
        Assert.Empty(cockpit.History);
    }

    [Fact]
    public async Task Envelope_registration_rejects_unknown_type_and_unsafe_metadata()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-envelope-invalid-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var unknownType = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.ArtifactRegistration() with { ArtifactTypeId = "unknown.type" });
        var unsafeMetadata = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.ArtifactRegistration("sha256:unsafe") with
            {
                DisplayMetadata = new ArtifactDisplayMetadata(
                    "Claims",
                    "1.0.0",
                    null,
                    new Dictionary<string, string> { ["password"] = "redacted" },
                    new Dictionary<string, string>())
            });

        Assert.Equal(HttpStatusCode.BadRequest, unknownType.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unsafeMetadata.StatusCode);
    }

    [Fact]
    public async Task Duplicate_registration_is_deterministic()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-duplicate-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var request = WorkspaceDeploymentTestFixtures.ArtifactRegistration();

        var created = await owner.PostControlJsonAsync($"/api/workspaces/{workspaceId}/artifacts", request);
        var same = await owner.PostControlJsonAsync($"/api/workspaces/{workspaceId}/artifacts", request);
        var conflicting = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            request with { Reference = "/tmp/other-artifact" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
    }

    [Fact]
    public async Task Owner_can_archive_and_restore_artifact_without_removing_detail_history()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-archive-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var artifact = await RegisterArtifactAsync(owner, workspaceId);

        var archiveResponse = await owner.PostAsync($"/api/workspaces/{workspaceId}/artifacts/{artifact.Id}/archive", null);
        var archived = (await archiveResponse.Content.ReadControlJsonAsync<WorkspaceArtifact>())!;
        var activeList = (await owner.GetControlJsonAsync<WorkspaceArtifactListResponse>($"/api/workspaces/{workspaceId}/artifacts"))!;
        var allList = (await owner.GetControlJsonAsync<WorkspaceArtifactListResponse>($"/api/workspaces/{workspaceId}/artifacts?includeArchived=true"))!;
        var detail = (await owner.GetControlJsonAsync<WorkspaceArtifact>($"/api/workspaces/{workspaceId}/artifacts/{artifact.Id}"))!;
        var restoreResponse = await owner.PostAsync($"/api/workspaces/{workspaceId}/artifacts/{artifact.Id}/restore", null);
        var restored = (await restoreResponse.Content.ReadControlJsonAsync<WorkspaceArtifact>())!;

        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);
        Assert.Equal(WorkspaceArtifactLifecycleStatus.Archived, archived.Status);
        Assert.NotNull(archived.ArchivedAt);
        Assert.Empty(activeList.Items);
        Assert.Single(allList.Items, x => x.Id == artifact.Id && x.Status == WorkspaceArtifactLifecycleStatus.Archived);
        Assert.Equal(WorkspaceArtifactLifecycleStatus.Archived, detail.Status);
        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
        Assert.Equal(WorkspaceArtifactLifecycleStatus.Active, restored.Status);
        Assert.Null(restored.ArchivedAt);
    }

    [Fact]
    public async Task Artifact_routes_enforce_read_and_setup_permissions()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-permission-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var artifact = await RegisterArtifactAsync(owner, workspaceId);
        var readerAccountId = await app.AddWorkspaceMemberAsync(workspaceId, "artifact-reader", WorkspaceRole.Reader);
        var reader = app.CreateTrustedWorkspaceClient("artifact-reader");
        var nonMember = app.CreateTrustedWorkspaceClient("artifact-nonmember");

        var readDenied = await reader.GetAsync($"/api/workspaces/{workspaceId}/artifacts");
        await app.GrantWorkspaceDeploymentPermissionAsync(workspaceId, readerAccountId, WorkspaceDeploymentPermissions.Read);
        var readAllowed = await reader.GetAsync($"/api/workspaces/{workspaceId}/artifacts/{artifact.Id}");
        var registerDenied = await reader.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.ArtifactRegistration("sha256:reader"));
        var refreshDenied = await reader.PostAsync($"/api/workspaces/{workspaceId}/artifacts/{artifact.Id}/refresh", null);
        var archiveDenied = await reader.PostAsync($"/api/workspaces/{workspaceId}/artifacts/{artifact.Id}/archive", null);
        var nonMemberDenied = await nonMember.GetAsync($"/api/workspaces/{workspaceId}/artifacts/{artifact.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, readDenied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readAllowed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, registerDenied.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refreshDenied.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, archiveDenied.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, nonMemberDenied.StatusCode);
    }

    [Fact]
    public async Task Owner_can_download_local_artifact_reference()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"elsa-control-artifact-download-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var artifactPath = Path.Combine(tempRoot, "claims-prod.zip");
        var bytes = "artifact bytes"u8.ToArray();
        await File.WriteAllBytesAsync(artifactPath, bytes);
        try
        {
            var app = _app;
            await app.SeedAsync(_ => Task.CompletedTask);
            var owner = app.CreateTrustedWorkspaceClient("artifact-download-owner");
            var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
            var artifact = await RegisterArtifactAsync(owner, workspaceId, "sha256:download", artifactPath, format: WorkspaceArtifactFormat.Zip);

            var response = await owner.GetAsync($"/api/workspaces/{workspaceId}/artifacts/{artifact.Id}/download");
            var downloaded = await response.Content.ReadAsByteArrayAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal("claims-prod.zip", response.Content.Headers.ContentDisposition!.FileNameStar);
            Assert.Equal(bytes, downloaded);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task List_and_detail_are_workspace_isolated()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-isolation-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var artifact = await RegisterArtifactAsync(owner, workspaceId);
        var other = app.CreateTrustedWorkspaceClient("artifact-isolation-other");
        var otherWorkspaceId = await other.GetDefaultWorkspaceIdAsync();
        await RegisterArtifactAsync(other, otherWorkspaceId, "sha256:other");

        var crossDetail = await other.GetAsync($"/api/workspaces/{workspaceId}/artifacts/{artifact.Id}");
        var otherList = (await other.GetControlJsonAsync<WorkspaceArtifactListResponse>($"/api/workspaces/{otherWorkspaceId}/artifacts"))!;

        Assert.Equal(HttpStatusCode.Forbidden, crossDetail.StatusCode);
        Assert.Single(otherList.Items, x => x.ArtifactId == "sha256:other");
        Assert.DoesNotContain(otherList.Items, x => x.ArtifactId == artifact.ArtifactId);
    }

    [Fact]
    public async Task Refresh_marks_missing_and_unsupported_references_without_changing_identity()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-refresh-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var missing = await RegisterArtifactAsync(owner, workspaceId, "sha256:missing", "/tmp/does-not-exist");
        var unsupported = await RegisterArtifactAsync(owner, workspaceId, "sha256:unsupported", "oci://registry/claims", "oci");

        var missingResponse = await owner.PostAsync($"/api/workspaces/{workspaceId}/artifacts/{missing.Id}/refresh", null);
        var missingResult = (await missingResponse.Content.ReadControlJsonAsync<WorkspaceArtifactInspectionResult>())!;
        var unsupportedResponse = await owner.PostAsync($"/api/workspaces/{workspaceId}/artifacts/{unsupported.Id}/refresh", null);
        var unsupportedResult = (await unsupportedResponse.Content.ReadControlJsonAsync<WorkspaceArtifactInspectionResult>())!;

        Assert.Equal(HttpStatusCode.OK, missingResponse.StatusCode);
        Assert.Equal("sha256:missing", missingResult.ArtifactId);
        Assert.Equal(WorkspaceArtifactInspectionStatus.Unavailable, missingResult.InspectionStatus);
        Assert.Single(missingResult.Diagnostics, x => x.Code == "artifact.reference.unavailable");
        Assert.Equal(HttpStatusCode.OK, unsupportedResponse.StatusCode);
        Assert.Equal("sha256:unsupported", unsupportedResult.ArtifactId);
        Assert.Equal(WorkspaceArtifactInspectionStatus.Unsupported, unsupportedResult.InspectionStatus);
    }

    [Fact]
    public async Task Normal_artifact_dataset_lists_under_three_seconds()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-large-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        await SeedArtifactsAsync(app, workspaceId, 250);

        var stopwatch = Stopwatch.StartNew();
        var response = await owner.GetAsync($"/api/workspaces/{workspaceId}/artifacts");
        stopwatch.Stop();
        var list = (await response.Content.ReadControlJsonAsync<WorkspaceArtifactListResponse>())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(250, list.Items.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Owner_can_upload_zip_and_server_computes_artifact_identity()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-upload-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        await using var zip = await BuildValidArtifactZipAsync();

        var create = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/artifact-uploads",
            new CreateArtifactUploadRequest("claims-prod.zip", "application/zip", zip.Bytes.Length));
        var session = (await create.Content.ReadControlJsonAsync<CreateArtifactUploadResponse>())!;
        using var content = new ByteArrayContent(zip.Bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
        var upload = await owner.PutAsync($"/api/workspaces/{workspaceId}/artifact-uploads/{session.UploadId}/content", content);
        var complete = await owner.PostAsync($"/api/workspaces/{workspaceId}/artifact-uploads/{session.UploadId}/complete", null);
        var completed = (await complete.Content.ReadControlJsonAsync<CompleteArtifactUploadResponse>())!;

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);
        Assert.Equal(HttpStatusCode.Created, complete.StatusCode);
        Assert.Equal(WorkspaceArtifactUploadStatus.Completed, completed.Status);
        Assert.NotNull(completed.Artifact);
        Assert.Equal(zip.ArtifactId, completed.Artifact!.ArtifactId);
        Assert.Equal("local", completed.Artifact.ReferenceProvider);
        Assert.Equal(WorkspaceArtifactFormat.Zip, completed.Artifact.Format);
        Assert.Equal(WorkspaceArtifactChecksumStatus.Verified, completed.Artifact.ChecksumStatus);
        Assert.Equal(WorkspaceArtifactInspectionStatus.Valid, completed.Artifact.InspectionStatus);
        Assert.Single(completed.Artifact.Resources, x => x.LogicalId == "order-approval");
    }

    private static async Task<WorkspaceArtifact> RegisterArtifactAsync(
        HttpClient client,
        Guid workspaceId,
        string artifactId = "sha256:claims-prod",
        string reference = "/tmp/claims-prod",
        string referenceProvider = "local",
        WorkspaceArtifactFormat format = WorkspaceArtifactFormat.Folder)
    {
        var response = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.ArtifactRegistration(artifactId, reference, referenceProvider) with { Format = format });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadControlJsonAsync<WorkspaceArtifact>())!;
    }

    private static async Task SeedArtifactsAsync(ControlApiTestApplication app, Guid workspaceId, int count)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceArtifactStore>();
        for (var i = 0; i < count; i++)
        {
            await store.RegisterArtifactAsync(
                workspaceId,
                new RegisterWorkspaceArtifactRequest(
                    $"sha256:artifact-{i:000}",
                    "elsa-control/deployment-artifact/v1alpha1",
                    new WorkspaceArtifactDigest("sha256", $"artifact-{i:000}"),
                    WorkspaceArtifactFormat.Folder,
                    "local",
                    $"/tmp/artifact-{i:000}",
                    new WorkspaceArtifactManifestSummary("claims", "1.0.0", "prod"),
                    [
                        new WorkspaceArtifactResourceSummary(
                            ArtifactTypeIds.ElsaLoomRecipe,
                            $"payment-retry-{i:000}",
                            null,
                            "8",
                            new WorkspaceArtifactDigest("sha256", $"workflow-{i:000}"))
                    ],
                    [],
                    Guid.NewGuid()));
        }
    }

    private static async Task<ArtifactZipFixture> BuildValidArtifactZipAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"elsa-control-api-artifact-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "workflows"));
        await File.WriteAllTextAsync(Path.Combine(root, "workflows", "order-approval.json"), """{"id":"order-approval"}""");
        var outputPath = Path.Combine(root, "artifact.zip");
        var manifest = """
            apiVersion: elsa-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: claims-prod
              version: 1.0.0
              environment: prod
            resources:
              workflows:
                - id: order-approval
                  path: workflows/order-approval.json
            """;
        var build = await new DeploymentArtifactBuilder().BuildZipAsync(
            new DeploymentArtifactBuildOptions(
                manifest,
                ManifestFormat.Yaml,
                root,
                outputPath,
                overwrite: true,
                builtAt: new DateTimeOffset(2026, 6, 4, 12, 0, 0, TimeSpan.Zero),
                builder: "api-tests",
                source: "fixture"));
        Assert.True(build.Succeeded);
        return new ArtifactZipFixture(root, build.ArtifactId!, await File.ReadAllBytesAsync(outputPath));
    }

    private sealed record ArtifactZipFixture(string Root, string ArtifactId, byte[] Bytes) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
