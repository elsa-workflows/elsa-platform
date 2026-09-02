using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ElsaControl.Deployment.Azure;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task Azure_operation_status_projects_only_customer_safe_state()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient("azure-status-safe-projection");
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();
        const string subscriptionId = "11111111-1111-1111-1111-111111111111";
        var resourceId =
            $"/subscriptions/{subscriptionId}/resourceGroups/managed-rg/providers/Microsoft.App/containerApps/workload-a";
        Guid operationId;

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var store = new AzureProviderOperationStore(
                scope.ServiceProvider.GetRequiredService<CatalogDbContext>());
            var operation = await store.CreateOrGetAsync(new(
                workspaceId,
                "workload-a",
                AzureProviderOperationAction.Reconcile,
                "safe-status-request",
                new string('a', 64),
                new string('b', 64),
                "3.8.0",
                "3.8",
                "combined",
                "Dedicated",
                "westeurope",
                "valenceruntimeimages.azurecr.io/runtime-combined",
                "sha256:" + new string('c', 64)),
                DateTimeOffset.UtcNow);
            operationId = operation.Id;
            var claimed = Assert.IsType<AzureProviderOperation>(await store.ClaimAsync(
                workspaceId,
                operationId,
                "provider-worker",
                "provider-lease",
                TimeSpan.FromMinutes(5),
                DateTimeOffset.UtcNow));
            Assert.NotNull(await store.CheckpointAsync(
                workspaceId,
                operationId,
                "provider-lease",
                new(
                    AzureProviderOperationPhase.HealthVerified,
                    "health.verified",
                    "Health verified.",
                    new(ResourceGroupName: "managed-rg", WorkloadResourceId: resourceId),
                    "HTTPS://Runtime.Example.Test:443/",
                    AzureProviderHealth.Healthy,
                    [new("health.safe", "Safe diagnostic.")]),
                DateTimeOffset.UtcNow,
                claimed.Version));
        }

        var response = await client.GetAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/azure-operations/{operationId:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var operationJson = document.RootElement.GetProperty("operation");

        Assert.Equal(operationId, operationJson.GetProperty("id").GetGuid());
        Assert.Equal("https://runtime.example.test", operationJson.GetProperty("endpointUri").GetString());
        Assert.Equal("health.safe", Assert.Single(operationJson.GetProperty("diagnosticCodes").EnumerateArray()).GetString());
        Assert.False(operationJson.TryGetProperty("resources", out _));
        Assert.False(operationJson.TryGetProperty("diagnostics", out _));
        Assert.False(operationJson.TryGetProperty("workerId", out _));
        Assert.False(operationJson.TryGetProperty("requestHash", out _));
        Assert.DoesNotContain(subscriptionId, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(resourceId, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider-worker", json, StringComparison.OrdinalIgnoreCase);
        Assert.All(document.RootElement.GetProperty("transitions").EnumerateArray(), transition =>
            Assert.False(transition.TryGetProperty("message", out _)));
    }
}
