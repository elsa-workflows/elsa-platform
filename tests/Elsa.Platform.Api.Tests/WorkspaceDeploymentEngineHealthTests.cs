using System.Net;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Platform.Api.Tests;

public sealed class WorkspaceDeploymentEngineHealthTests
{
    [Fact]
    public async Task Owner_can_verify_engine_health_and_reload_cockpit_metadata()
    {
        await using var app = new PlatformApiTestApplication(configureServices: services =>
        {
            services.RemoveAll<IEngineHealthProbe>();
            services.AddSingleton<IEngineHealthProbe>(new StubProbe(new EngineHealthProbeResult(
                true,
                "Elsa 4.1.0",
                CertificateStatus.Trusted,
                CredentialVerificationStatus.Verified,
                "Endpoint responded successfully.")));
        });
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("health-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var engine = await SeedEngineAsync(app, workspaceId);

        var response = await owner.PostAsync($"/api/workspaces/{workspaceId}/deployments/engines/{engine.Id}/verify", null);
        var result = await response.Content.ReadPlatformJsonAsync<EngineHealthResult>();
        var cockpit = await owner.GetPlatformJsonAsync<DeploymentCockpit>($"/api/workspaces/{workspaceId}/deployments/cockpit");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result!.Health.Should().Be(DeploymentHealth.Healthy);
        result.Version.Should().Be("Elsa 4.1.0");
        cockpit!.Engines.Should().ContainSingle(x =>
            x.Id == engine.Id.ToString("D")
            && x.Health == DeploymentHealth.Healthy
            && x.VerificationMessage == "Endpoint responded successfully.");
    }

    [Fact]
    public async Task Manual_verification_requires_setup_permission_for_readers()
    {
        await using var app = new PlatformApiTestApplication(configureServices: services =>
        {
            services.RemoveAll<IEngineHealthProbe>();
            services.AddSingleton<IEngineHealthProbe>(new StubProbe(new EngineHealthProbeResult(
                true,
                null,
                CertificateStatus.Trusted,
                CredentialVerificationStatus.Verified,
                "Endpoint responded successfully.")));
        });
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("health-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var engine = await SeedEngineAsync(app, workspaceId);
        await app.AddWorkspaceMemberAsync(workspaceId, "health-reader", WorkspaceRole.Reader);

        var response = await app.CreateTrustedWorkspaceClient("health-reader")
            .PostAsync($"/api/workspaces/{workspaceId}/deployments/engines/{engine.Id}/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Heartbeat_rejects_stale_updates()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("heartbeat-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var engine = await SeedEngineAsync(app, workspaceId);
        var heartbeatAt = DateTimeOffset.Parse("2026-05-26T10:00:00Z");
        var request = new WorkspaceEngineHeartbeatRequest(
            engine.EnvironmentId,
            "Elsa 4.1.0",
            CertificateStatus.Trusted,
            CredentialVerificationStatus.Verified,
            heartbeatAt,
            null,
            "Heartbeat accepted.");

        var accepted = await owner.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/deployments/engines/{engine.Id}/heartbeat", request);
        var stale = await owner.PostPlatformJsonAsync($"/api/workspaces/{workspaceId}/deployments/engines/{engine.Id}/heartbeat", request);

        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Heartbeat_rejects_cross_workspace_engine_ids()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("heartbeat-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var engine = await SeedEngineAsync(app, workspaceId);
        var otherOwner = app.CreateTrustedWorkspaceClient("other-heartbeat-owner");
        var otherWorkspaceId = await otherOwner.GetDefaultWorkspaceIdAsync();

        var response = await otherOwner.PostPlatformJsonAsync(
            $"/api/workspaces/{otherWorkspaceId}/deployments/engines/{engine.Id}/heartbeat",
            new WorkspaceEngineHeartbeatRequest(
                engine.EnvironmentId,
                "Elsa 4.1.0",
                CertificateStatus.Trusted,
                CredentialVerificationStatus.Verified,
                DateTimeOffset.Parse("2026-05-26T10:00:00Z"),
                null,
                "Heartbeat accepted."));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<WorkspaceWorkflowEngine> SeedEngineAsync(PlatformApiTestApplication app, Guid workspaceId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        var application = await store.CreateApplicationAsync(workspaceId, new CreateWorkflowApplicationRequest("Claims Operations", null, null));
        var environment = await store.CreateEnvironmentAsync(workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        return await store.RegisterEngineAsync(
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
    }

    private sealed class StubProbe(EngineHealthProbeResult result) : IEngineHealthProbe
    {
        public Task<EngineHealthProbeResult> ProbeAsync(WorkspaceWorkflowEngine engine, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
