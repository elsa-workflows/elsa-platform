using System.Diagnostics;
using System.Net;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.Api.Tests;

public sealed class WorkspaceDeploymentApiTests
{
    [Fact]
    public async Task Workspace_member_can_read_persisted_deployment_cockpit()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient();
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();
        await SeedDeploymentAsync(app, workspaceId);

        var response = await client.GetAsync($"/api/workspaces/{workspaceId}/deployments/cockpit");
        var cockpit = await response.Content.ReadPlatformJsonAsync<DeploymentCockpit>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        cockpit!.Applications.Should().ContainSingle(x => x.Name == "Claims Operations");
        cockpit.Engines.Should().ContainSingle(x =>
            x.Name == "claims-prod"
            && x.CredentialReference.Reference == "kv://claims/prod/elsa-api");
        cockpit.ObservabilityBindings.Should().ContainSingle(x => x.Provider == "Azure Monitor");
        cockpit.DriftReport.Should().ContainSingle(x => x.Area == "RuntimeConfiguration");
    }

    [Fact]
    public async Task Deployment_cockpit_route_rejects_non_members()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var member = app.CreateTrustedWorkspaceClient("member");
        var workspaceId = await member.GetDefaultWorkspaceIdAsync();
        await SeedDeploymentAsync(app, workspaceId);

        var anonymous = await app.CreateClient().GetAsync($"/api/workspaces/{workspaceId}/deployments/cockpit");
        var nonMember = await app.CreateTrustedWorkspaceClient("other").GetAsync($"/api/workspaces/{workspaceId}/deployments/cockpit");

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        nonMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Normal_dataset_cockpit_loads_under_three_seconds()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient("large-workspace");
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();
        await SeedNormalDatasetAsync(app, workspaceId);

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync($"/api/workspaces/{workspaceId}/deployments/cockpit");
        stopwatch.Stop();
        var cockpit = await response.Content.ReadPlatformJsonAsync<DeploymentCockpit>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        cockpit!.Applications.Should().HaveCount(25);
        cockpit.Engines.Should().HaveCount(200);
    }

    private static async Task SeedDeploymentAsync(PlatformApiTestApplication app, Guid workspaceId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        var application = await store.CreateApplicationAsync(workspaceId, new CreateWorkflowApplicationRequest("Claims Operations", null, null));
        var environment = await store.CreateEnvironmentAsync(workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        var engine = await store.RegisterEngineAsync(
            workspaceId,
            new RegisterWorkflowEngineRequest(
                environment.Id,
                "claims-prod",
                "https://workflows.example.test/elsa",
                "westeurope",
                "Azure Key Vault",
                "kv://claims/prod/elsa-api",
                [new EngineCapability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi)],
                [new RuntimeControl("reload-configuration", "Reload Configuration", CapabilityBoundary.EngineApi, "engine.reload-configuration", "Reloads engine API configuration.")],
                null));
        await store.CreateRevisionAsync(workspaceId, new CreateDesiredStateRevisionRequest(application.Id, environment.Id, "Baseline", "abc123", "{\"records\":[]}", null));

        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Guid? correlatedRevisionId = null;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ObservabilityBindings (Id, WorkspaceId, EnvironmentId, EngineId, Kind, Provider, Status, Scope, CorrelatedRevisionId, Sample)
            VALUES ({Guid.NewGuid()}, {workspaceId}, {environment.Id}, {engine.Id}, {"Logs"}, {"Azure Monitor"}, {"Connected"}, {"workspace:/prod"}, {correlatedRevisionId}, {"Imported status"});
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO DriftReportItems (Id, WorkspaceId, EnvironmentId, EngineId, Area, Desired, Observed, Action, DetectedAt)
            VALUES ({Guid.NewGuid()}, {workspaceId}, {environment.Id}, {engine.Id}, {"RuntimeConfiguration"}, {"Concurrency 32"}, {"Concurrency 16"}, {"Review"}, {DateTimeOffset.UtcNow.UtcTicks});
            """);
    }

    private static async Task SeedNormalDatasetAsync(PlatformApiTestApplication app, Guid workspaceId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        for (var appIndex = 0; appIndex < 25; appIndex++)
        {
            var application = await store.CreateApplicationAsync(workspaceId, new CreateWorkflowApplicationRequest($"Application {appIndex:00}", null, null));
            for (var envIndex = 0; envIndex < 4; envIndex++)
            {
                var environment = await store.CreateEnvironmentAsync(workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, $"Env {envIndex}", EnvironmentTier.Dev));
                for (var engineIndex = 0; engineIndex < 2; engineIndex++)
                {
                    await store.RegisterEngineAsync(
                        workspaceId,
                        new RegisterWorkflowEngineRequest(
                            environment.Id,
                            $"engine-{appIndex:00}-{envIndex:00}-{engineIndex:00}",
                            $"https://engine-{appIndex}-{envIndex}-{engineIndex}.example.test/elsa",
                            null,
                            "Azure Key Vault",
                            $"kv://workspace/{appIndex}/{envIndex}/{engineIndex}",
                            [new EngineCapability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi)],
                            [],
                            null));
                }
            }
        }
    }
}
