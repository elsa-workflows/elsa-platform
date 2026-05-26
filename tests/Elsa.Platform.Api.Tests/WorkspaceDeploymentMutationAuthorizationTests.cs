using System.Net;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;

namespace Elsa.Platform.Api.Tests;

public sealed class WorkspaceDeploymentMutationAuthorizationTests
{
    [Fact]
    public async Task Runtime_control_requires_execute_permission()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (_, _, engine) = await SeedControlTopologyAsync(app, workspaceId, hasCapability: true);
        await app.AddWorkspaceMemberAsync(workspaceId, "reader", WorkspaceRole.Reader);
        var reader = app.CreateTrustedWorkspaceClient("reader");

        var response = await reader.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/engines/{engine.Id}/controls/reload-configuration/run",
            new WorkspaceRuntimeControlRunRequest(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Runtime_control_rejects_missing_confirmation()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (_, _, engine) = await SeedControlTopologyAsync(app, workspaceId, hasCapability: true);

        var response = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/engines/{engine.Id}/controls/reload-configuration/run",
            new WorkspaceRuntimeControlRunRequest(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Runtime_control_rejects_unsupported_capability_without_consuming_confirmation()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (_, _, engine) = await SeedControlTopologyAsync(app, workspaceId, hasCapability: false);
        var confirmation = await CreateConfirmationAsync(owner, workspaceId, engine.Id, "reload-configuration");

        var response = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/engines/{engine.Id}/controls/reload-configuration/run",
            new WorkspaceRuntimeControlRunRequest(confirmation.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var usedAt = await ConfirmationUsedAtAsync(app, workspaceId, confirmation.Id);
        usedAt.Should().BeNull();
    }

    [Fact]
    public async Task Runtime_control_rejects_unreachable_engine_without_consuming_confirmation()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (_, _, engine) = await SeedControlTopologyAsync(app, workspaceId, hasCapability: true, health: DeploymentHealth.Unreachable);
        var confirmation = await CreateConfirmationAsync(owner, workspaceId, engine.Id, "reload-configuration");

        var response = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/engines/{engine.Id}/controls/reload-configuration/run",
            new WorkspaceRuntimeControlRunRequest(confirmation.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var usedAt = await ConfirmationUsedAtAsync(app, workspaceId, confirmation.Id);
        usedAt.Should().BeNull();
    }

    [Fact]
    public async Task Runtime_control_consumes_same_user_confirmation_once()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (_, _, engine) = await SeedControlTopologyAsync(app, workspaceId, hasCapability: true);
        var confirmation = await CreateConfirmationAsync(owner, workspaceId, engine.Id, "reload-configuration");

        var first = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/engines/{engine.Id}/controls/reload-configuration/run",
            new WorkspaceRuntimeControlRunRequest(confirmation.Id));
        var execution = await first.Content.ReadPlatformJsonAsync<RuntimeControlExecution>();
        var replay = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/engines/{engine.Id}/controls/reload-configuration/run",
            new WorkspaceRuntimeControlRunRequest(confirmation.Id));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        execution!.Status.Should().Be(RuntimeControlExecutionStatus.Succeeded);
        replay.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Runtime_control_rejects_confirmation_created_by_another_user()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (_, _, engine) = await SeedControlTopologyAsync(app, workspaceId, hasCapability: true);
        var operatorAccountId = await app.AddWorkspaceMemberAsync(workspaceId, "operator", WorkspaceRole.Reader);
        await app.GrantWorkspaceDeploymentPermissionAsync(workspaceId, operatorAccountId, WorkspaceDeploymentPermissions.ExecuteControls);
        var confirmation = await CreateConfirmationAsync(owner, workspaceId, engine.Id, "reload-configuration");

        var response = await app.CreateTrustedWorkspaceClient("operator")
            .PostPlatformJsonAsync(
                $"/api/workspaces/{workspaceId}/deployments/engines/{engine.Id}/controls/reload-configuration/run",
                new WorkspaceRuntimeControlRunRequest(confirmation.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private static async Task<(WorkspaceDeploymentApplication Application, WorkspaceDeploymentEnvironment Environment, WorkspaceWorkflowEngine Engine)> SeedControlTopologyAsync(
        PlatformApiTestApplication app,
        Guid workspaceId,
        bool hasCapability,
        DeploymentHealth health = DeploymentHealth.Healthy)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        var application = await store.CreateApplicationAsync(workspaceId, new CreateWorkflowApplicationRequest("Claims", null, null));
        var environment = await store.CreateEnvironmentAsync(workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        var engine = await store.RegisterEngineAsync(
            workspaceId,
            new RegisterWorkflowEngineRequest(
                environment.Id,
                "claims-prod",
                "https://workflows.example.test/elsa",
                null,
                "Azure Key Vault",
                "kv://claims/prod/elsa-api",
                hasCapability ? [new EngineCapability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi)] : [],
                [new RuntimeControl("reload-configuration", "Reload Configuration", CapabilityBoundary.EngineApi, "engine.reload-configuration", "Reloads engine API configuration.")],
                null));
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE WorkflowEngines
            SET Health = {health.ToString()}, LastHeartbeatAt = {DateTimeOffset.Parse("2026-05-26T10:00:00Z")}
            WHERE WorkspaceId = {workspaceId} AND Id = {engine.Id};
            """);
        return (application, environment, engine);
    }

    private static async Task<ActionConfirmation> CreateConfirmationAsync(HttpClient client, Guid workspaceId, Guid engineId, string controlId)
    {
        var response = await client.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/confirmations",
            new WorkspaceActionConfirmationRequest(
                ConfirmationActionType.RuntimeControl,
                RuntimeControlService.RuntimeControlTargetId(engineId, controlId),
                null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadPlatformJsonAsync<ActionConfirmation>())!;
    }

    private static async Task<DateTimeOffset?> ConfirmationUsedAtAsync(PlatformApiTestApplication app, Guid workspaceId, Guid confirmationId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentMutationStore>();
        return (await store.GetConfirmationAsync(workspaceId, confirmationId))?.UsedAt;
    }
}
