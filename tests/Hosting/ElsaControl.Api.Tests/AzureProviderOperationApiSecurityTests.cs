using System.Net;
using System.Net.Http.Json;

namespace ElsaControl.Api.Tests;

public sealed class AzureProviderOperationApiSecurityTests
{
    [Fact]
    public async Task Caller_asserted_provider_projection_cannot_be_submitted_through_the_public_api()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient("azure-operation-caller");
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/azure-operations",
            new
            {
                idempotencyKey = "caller-asserted",
                planFingerprint = new string('a', 64),
                templateFingerprint = new string('b', 64),
                workloadName = "workload-a",
                location = "westeurope",
                elsaVersion = "3.8.0",
                releaseLine = "3.8",
                topology = "combined",
                isolation = "Dedicated",
                imageRepository = "valenceruntimeimages.azurecr.io/runtime-combined",
                imageDigest = "sha256:" + new string('c', 64),
                releaseManifestReference = "oci://attacker.example/manifest",
                releaseManifestDigest = "sha256:" + new string('d', 64),
                releaseManifestSignatureReference = "oci://attacker.example/signature",
                releaseManifestSignatureDigest = "sha256:" + new string('e', 64),
                secretReferences = new { database = "secret://attacker/database" }
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Azure_operation_status_requires_workspace_access_and_does_not_cross_workspace_boundaries()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var workspaceAClient = app.CreateTrustedWorkspaceClient("azure-status-a");
        var workspaceBClient = app.CreateTrustedWorkspaceClient("azure-status-b");
        var workspaceAId = await workspaceAClient.GetDefaultWorkspaceIdAsync();
        var workspaceBId = await workspaceBClient.GetDefaultWorkspaceIdAsync();
        var operationId = Guid.NewGuid();

        var anonymous = await app.CreateClient().GetAsync(
            $"/api/workspaces/{workspaceAId:D}/deployments/azure-operations/{operationId:D}");
        var crossWorkspace = await workspaceAClient.GetAsync(
            $"/api/workspaces/{workspaceBId:D}/deployments/azure-operations/{operationId:D}");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossWorkspace.StatusCode);
    }
}
