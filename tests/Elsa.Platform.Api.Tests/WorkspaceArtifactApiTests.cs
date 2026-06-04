using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Deployment.Manifest;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;

namespace Elsa.Platform.Api.Tests;

public sealed class WorkspaceArtifactApiTests
{
    [Fact]
    public async Task Owner_can_register_list_and_read_safe_artifact_metadata()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var registerResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.ArtifactRegistration());
        var registered = await registerResponse.Content.ReadPlatformJsonAsync<WorkspaceArtifact>();
        var list = await owner.GetPlatformJsonAsync<WorkspaceArtifactListResponse>($"/api/workspaces/{workspaceId}/artifacts");
        var detail = await owner.GetPlatformJsonAsync<WorkspaceArtifact>($"/api/workspaces/{workspaceId}/artifacts/{registered!.Id}");
        var responseText = await registerResponse.Content.ReadAsStringAsync();

        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        list!.Items.Should().ContainSingle(x => x.Id == registered.Id);
        detail!.Manifest.Name.Should().Be("claims");
        detail.ArtifactTypeId.Should().Be(ArtifactTypeIds.ElsaWorkflowDefinition);
        detail.Producer!.ProducerType.Should().Be("manual");
        detail.CompatibilityHints.Should().ContainSingle(x => x.RequiredArtifactType == ArtifactTypeIds.ElsaWorkflowDefinition);
        detail.Resources.Should().ContainSingle(x => x.LogicalId == "payment-retry");
        responseText.Should().NotContain("workflow definition payload");
        responseText.Should().NotContain("secret value");
        responseText.Should().NotContain("token");
    }

    [Fact]
    public async Task Owner_can_register_envelope_metadata_and_discover_artifact_types()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-envelope-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var types = await owner.GetPlatformJsonAsync<WorkspaceArtifactTypeListResponse>($"/api/workspaces/{workspaceId}/artifacts/types");
        var registerResponse = await owner.PostPlatformJsonAsync(
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
                        ArtifactTypeIds.ElsaWorkflowDefinition,
                        "elsa-workflows",
                        ">=4.0.0",
                        ["workflow-definition.apply"],
                        new Dictionary<string, string>())
                ]
            });
        var registered = await registerResponse.Content.ReadPlatformJsonAsync<WorkspaceArtifact>();

        types!.Items.Should().ContainSingle(x => x.TypeId == ArtifactTypeIds.ElsaWorkflowDefinition);
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        registered!.Producer!.ProducerType.Should().Be("studio");
        registered.DisplayMetadata!.Labels.Should().ContainKey("domain");
        registered.CompatibilityHints.Should().ContainSingle(x => x.RuntimeFamily == "elsa-workflows");
    }

    [Fact]
    public async Task Studio_submit_artifact_registration_does_not_create_deployment_run()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("studio-submit-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var registerResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.WorkflowEnvelopeRegistration("sha256:studio-submit"));
        var duplicateResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.WorkflowEnvelopeRegistration("sha256:studio-submit"));
        var artifact = await registerResponse.Content.ReadPlatformJsonAsync<WorkspaceArtifact>();
        var cockpit = await owner.GetPlatformJsonAsync<DeploymentCockpit>($"/api/workspaces/{workspaceId}/deployments/cockpit");

        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        artifact!.Producer!.ProducerType.Should().Be("studio");
        artifact.ArtifactTypeId.Should().Be(ArtifactTypeIds.ElsaWorkflowDefinition);
        cockpit!.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Envelope_registration_rejects_unknown_type_and_unsafe_metadata()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-envelope-invalid-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var unknownType = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.ArtifactRegistration() with { ArtifactTypeId = "unknown.type" });
        var unsafeMetadata = await owner.PostPlatformJsonAsync(
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

        unknownType.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unsafeMetadata.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Duplicate_registration_is_deterministic()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-duplicate-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var request = WorkspaceDeploymentTestFixtures.ArtifactRegistration();

        var created = await owner.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/artifacts", request);
        var same = await owner.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/artifacts", request);
        var conflicting = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            request with { Reference = "/tmp/other-artifact" });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        same.StatusCode.Should().Be(HttpStatusCode.OK);
        conflicting.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Artifact_routes_enforce_read_and_setup_permissions()
    {
        await using var app = new PlatformApiTestApplication();
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
        var registerDenied = await reader.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.ArtifactRegistration("sha256:reader"));
        var refreshDenied = await reader.PostAsync($"/api/workspaces/{workspaceId}/artifacts/{artifact.Id}/refresh", null);
        var nonMemberDenied = await nonMember.GetAsync($"/api/workspaces/{workspaceId}/artifacts/{artifact.Id}");

        readDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        readAllowed.StatusCode.Should().Be(HttpStatusCode.OK);
        registerDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        refreshDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        nonMemberDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_and_detail_are_workspace_isolated()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-isolation-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var artifact = await RegisterArtifactAsync(owner, workspaceId);
        var other = app.CreateTrustedWorkspaceClient("artifact-isolation-other");
        var otherWorkspaceId = await other.GetDefaultWorkspaceIdAsync();
        await RegisterArtifactAsync(other, otherWorkspaceId, "sha256:other");

        var crossDetail = await other.GetAsync($"/api/workspaces/{workspaceId}/artifacts/{artifact.Id}");
        var otherList = await other.GetPlatformJsonAsync<WorkspaceArtifactListResponse>($"/api/workspaces/{otherWorkspaceId}/artifacts");

        crossDetail.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        otherList!.Items.Should().ContainSingle(x => x.ArtifactId == "sha256:other");
        otherList.Items.Should().NotContain(x => x.ArtifactId == artifact.ArtifactId);
    }

    [Fact]
    public async Task Refresh_marks_missing_and_unsupported_references_without_changing_identity()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-refresh-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var missing = await RegisterArtifactAsync(owner, workspaceId, "sha256:missing", "/tmp/does-not-exist");
        var unsupported = await RegisterArtifactAsync(owner, workspaceId, "sha256:unsupported", "oci://registry/claims", "oci");

        var missingResponse = await owner.PostAsync($"/api/workspaces/{workspaceId}/artifacts/{missing.Id}/refresh", null);
        var missingResult = await missingResponse.Content.ReadPlatformJsonAsync<WorkspaceArtifactInspectionResult>();
        var unsupportedResponse = await owner.PostAsync($"/api/workspaces/{workspaceId}/artifacts/{unsupported.Id}/refresh", null);
        var unsupportedResult = await unsupportedResponse.Content.ReadPlatformJsonAsync<WorkspaceArtifactInspectionResult>();

        missingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        missingResult!.ArtifactId.Should().Be("sha256:missing");
        missingResult.InspectionStatus.Should().Be(WorkspaceArtifactInspectionStatus.Unavailable);
        missingResult.Diagnostics.Should().ContainSingle(x => x.Code == "artifact.reference.unavailable");
        unsupportedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        unsupportedResult!.ArtifactId.Should().Be("sha256:unsupported");
        unsupportedResult.InspectionStatus.Should().Be(WorkspaceArtifactInspectionStatus.Unsupported);
    }

    [Fact]
    public async Task Normal_artifact_dataset_lists_under_three_seconds()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-large-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        await SeedArtifactsAsync(app, workspaceId, 250);

        var stopwatch = Stopwatch.StartNew();
        var response = await owner.GetAsync($"/api/workspaces/{workspaceId}/artifacts");
        stopwatch.Stop();
        var list = await response.Content.ReadPlatformJsonAsync<WorkspaceArtifactListResponse>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        list!.Items.Should().HaveCount(250);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Owner_can_upload_zip_and_server_computes_artifact_identity()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-upload-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        await using var zip = await BuildValidArtifactZipAsync();

        var create = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/artifact-uploads",
            new CreateArtifactUploadRequest("claims-prod.zip", "application/zip", zip.Bytes.Length));
        var session = await create.Content.ReadPlatformJsonAsync<CreateArtifactUploadResponse>();
        using var content = new ByteArrayContent(zip.Bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
        var upload = await owner.PutAsync($"/api/workspaces/{workspaceId}/artifact-uploads/{session!.UploadId}/content", content);
        var complete = await owner.PostAsync($"/api/workspaces/{workspaceId}/artifact-uploads/{session.UploadId}/complete", null);
        var completed = await complete.Content.ReadPlatformJsonAsync<CompleteArtifactUploadResponse>();

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        upload.StatusCode.Should().Be(HttpStatusCode.NoContent);
        complete.StatusCode.Should().Be(HttpStatusCode.Created);
        completed!.Status.Should().Be(WorkspaceArtifactUploadStatus.Completed);
        completed.Artifact.Should().NotBeNull();
        completed.Artifact!.ArtifactId.Should().Be(zip.ArtifactId);
        completed.Artifact.ReferenceProvider.Should().Be("local");
        completed.Artifact.Format.Should().Be(WorkspaceArtifactFormat.Zip);
        completed.Artifact.ChecksumStatus.Should().Be(WorkspaceArtifactChecksumStatus.Verified);
        completed.Artifact.InspectionStatus.Should().Be(WorkspaceArtifactInspectionStatus.Valid);
        completed.Artifact.Resources.Should().ContainSingle(x => x.LogicalId == "order-approval");
    }

    private static async Task<WorkspaceArtifact> RegisterArtifactAsync(
        HttpClient client,
        Guid workspaceId,
        string artifactId = "sha256:claims-prod",
        string reference = "/tmp/claims-prod",
        string referenceProvider = "local")
    {
        var response = await client.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.ArtifactRegistration(artifactId, reference, referenceProvider));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadPlatformJsonAsync<WorkspaceArtifact>())!;
    }

    private static async Task SeedArtifactsAsync(PlatformApiTestApplication app, Guid workspaceId, int count)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceArtifactStore>();
        for (var i = 0; i < count; i++)
        {
            await store.RegisterArtifactAsync(
                workspaceId,
                new RegisterWorkspaceArtifactRequest(
                    $"sha256:artifact-{i:000}",
                    "platform.elsa.io/deployment-artifact/v1alpha1",
                    new WorkspaceArtifactDigest("sha256", $"artifact-{i:000}"),
                    WorkspaceArtifactFormat.Folder,
                    "local",
                    $"/tmp/artifact-{i:000}",
                    new WorkspaceArtifactManifestSummary("claims", "1.0.0", "prod"),
                    [
                        new WorkspaceArtifactResourceSummary(
                            "workflowDefinition",
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
        var root = Path.Combine(Path.GetTempPath(), $"elsa-platform-api-artifact-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "workflows"));
        await File.WriteAllTextAsync(Path.Combine(root, "workflows", "order-approval.json"), """{"id":"order-approval"}""");
        var outputPath = Path.Combine(root, "artifact.zip");
        var manifest = """
            apiVersion: platform.elsa.io/v1alpha1
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
        build.Succeeded.Should().BeTrue();
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
