using System.Net;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.PackageCatalog.Api.Authentication;
using Elsa.Platform.PackageCatalog.Api.Workspace;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Elsa.Platform.PackageCatalog.Api.Tests;

public sealed class WorkspaceDeploymentApiTests
{
    [Fact]
    public async Task Workspace_member_can_read_deployment_cockpit_from_backend()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = WorkspaceClient(app);
        var workspaceId = await GetWorkspaceIdAsync(client);

        var response = await client.GetAsync($"/api/workspaces/{workspaceId}/deployments/cockpit");
        var cockpit = await response.Content.ReadCatalogJsonAsync<DeploymentCockpit>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        cockpit!.Applications.Should().ContainSingle(x => x.Id == "claims-ops");
        cockpit.Engines.Should().Contain(x =>
            x.Id == "stage-engine"
            && x.CredentialReference.Reference == "kv://acme-platform/stage/elsa-api"
            && x.CredentialReference.VerificationStatus == CredentialVerificationStatus.Verified);
        cockpit.Comparisons.Should().Contain(x =>
            x.SourceEnvironmentId == "claims-stage"
            && x.TargetEnvironmentId == "claims-prod"
            && x.Validations.Any(validation => validation.Severity == ValidationSeverity.Blocker));
    }

    [Fact]
    public async Task Deployment_cockpit_route_rejects_non_members()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var member = WorkspaceClient(app, "member");
        var workspaceId = await GetWorkspaceIdAsync(member);

        var anonymous = await app.CreateClient().GetAsync($"/api/workspaces/{workspaceId}/deployments/cockpit");
        var nonMember = await WorkspaceClient(app, "other").GetAsync($"/api/workspaces/{workspaceId}/deployments/cockpit");

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        nonMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<Guid> GetWorkspaceIdAsync(HttpClient client) =>
        (await client.GetCatalogJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

    private static HttpClient WorkspaceClient(WebApplicationFactory<Program> app, string subject = "user-123")
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.IssuerHeader, "https://elsaworkflows.io");
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.EmailHeader, $"{subject}@example.test");
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.NameHeader, subject);
        return client;
    }
}
