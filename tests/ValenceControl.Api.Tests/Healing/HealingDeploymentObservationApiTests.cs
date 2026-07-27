using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ValenceControl.Api.Healing;
using ValenceControl.Api.Workspace;
using ValenceControl.Api.Workspace.Healing;
using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using ValenceControl.PackageCatalog.Core.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ValenceControl.Api.Tests.Healing;

public sealed class HealingDeploymentObservationApiTests
{
    [Fact]
    public async Task External_delivery_observation_is_idempotent_and_conflicting_replay_is_rejected()
    {
        await using var app = await CreateApplicationAsync("healing-deployment-external");
        var deployedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var request = new HealingDeploymentObservationApiRequest(
            "fixed-sha", deployedAt, "delivery-42", $"sha256:{new string('a', 64)}");
        var uri = ObservationUri(app);

        var accepted = await SendAsync(app.Owner, uri, request, "delivery-key-42");
        var first = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        var replay = await SendAsync(app.Owner, uri, request, "delivery-key-42");
        var second = await replay.Content.ReadFromJsonAsync<JsonElement>();
        var conflict = await SendAsync(app.Owner, uri, request with { Revision = "different-sha" }, "delivery-key-42");

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(first.GetProperty("observationId").GetGuid(), second.GetProperty("observationId").GetGuid());
        Assert.True(second.GetProperty("isReplay").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        await using var scope = app.Factory.Services.CreateAsyncScope();
        var observations = await scope.ServiceProvider.GetRequiredService<HealingDbContext>()
            .DeploymentObservations.AsNoTracking().ToArrayAsync();
        Assert.Single(observations);
        Assert.Equal(DeploymentObservationSource.ExternalDelivery, observations[0].Source);
        Assert.StartsWith("control-account:", observations[0].TrustIdentity);
    }

    [Fact]
    public async Task Control_managed_deployment_uses_the_same_idempotent_application_contract()
    {
        await using var app = await CreateApplicationAsync("healing-deployment-control");
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var sink = scope.ServiceProvider.GetRequiredService<IDeploymentObservationSink>();
        var request = new DeploymentObservationRequest(
            HealingContractVersions.DeploymentProtocol,
            app.WorkspaceId,
            app.ApplicationId,
            app.EnvironmentId,
            "control-fixed-sha",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DeploymentObservationSources.ControlDeployment,
            "control-command-7",
            "control-engine:engine-7",
            $"sha256:{new string('b', 64)}",
            "control-command:7");

        var accepted = await sink.AppendAsync(request);
        var replay = await sink.AppendAsync(request);

        Assert.False(accepted.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(accepted.ObservationId, replay.ObservationId);
        var observation = await scope.ServiceProvider.GetRequiredService<HealingDbContext>()
            .DeploymentObservations.AsNoTracking().SingleAsync();
        Assert.Equal(DeploymentObservationSource.ControlDeployment, observation.Source);
    }

    [Fact]
    public async Task External_observation_requires_permission_idempotency_and_current_application_scope()
    {
        await using var app = await CreateApplicationAsync("healing-deployment-auth");
        var request = new HealingDeploymentObservationApiRequest(
            "fixed-sha", DateTimeOffset.UtcNow, "delivery-auth", $"sha256:{new string('c', 64)}");
        var outsider = app.Factory.CreateTrustedWorkspaceClient("healing-deployment-outsider");
        const string reporterSubject = "healing-deployment-reporter";
        var reporterId = await app.Factory.AddWorkspaceMemberAsync(app.WorkspaceId, reporterSubject, WorkspaceRole.Reader);
        var reporter = app.Factory.CreateTrustedWorkspaceClient(reporterSubject);

        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync(ObservationUri(app), request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Owner.PostAsJsonAsync(ObservationUri(app), request)).StatusCode);
        await app.Factory.GrantWorkspaceDeploymentPermissionAsync(app.WorkspaceId, reporterId, HealingPermissions.Configure);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendAsync(reporter, ObservationUri(app), request, "configure-is-not-report")).StatusCode);
        await app.Factory.GrantWorkspaceDeploymentPermissionAsync(app.WorkspaceId, reporterId, HealingPermissions.ReportDeployment);
        Assert.Equal(
            HttpStatusCode.Accepted,
            (await SendAsync(reporter, ObservationUri(app), request, "narrow-deployment-report")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendAsync(
                app.Owner,
                $"/api/workspaces/{app.WorkspaceId:D}/healing/applications/{app.ApplicationId:D}/environments/{Guid.NewGuid():D}/deployment-observations",
                request,
                "missing-environment")).StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string uri, object body, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static string ObservationUri(TestApplication app) =>
        $"/api/workspaces/{app.WorkspaceId:D}/healing/applications/{app.ApplicationId:D}/environments/{app.EnvironmentId:D}/deployment-observations";

    private static async Task<TestApplication> CreateApplicationAsync(string ownerSubject)
    {
        var factory = new ControlApiTestApplication();
        await factory.SeedAsync(_ => Task.CompletedTask);
        await factory.SeedHealingAsync();
        var owner = factory.CreateTrustedWorkspaceClient(ownerSubject);
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var applicationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Orders API", null));
        applicationResponse.EnsureSuccessStatusCode();
        var application = await applicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>();
        var environmentResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/applications/{application!.Id:D}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Production", EnvironmentTier.Production));
        environmentResponse.EnsureSuccessStatusCode();
        var environment = await environmentResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentEnvironment>();
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        dbContext.HealingConfigurations.Add(new HealingConfiguration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ApplicationId = application.Id,
            DiscoveryEnabled = true,
            RepairEnabled = true,
            SignalProfileVersion = HealingContractVersions.SignalProfile,
            DefaultAttemptLimit = 2,
            VerificationWindow = TimeSpan.FromMinutes(15),
            TimeBudget = TimeSpan.FromMinutes(10),
            ConcurrencyBudget = 2,
            InferenceBudget = 100,
            RepositoryRunBudget = 2,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        return new TestApplication(factory, owner, workspaceId, application.Id, environment!.Id);
    }

    private sealed record TestApplication(
        ControlApiTestApplication Factory,
        HttpClient Owner,
        Guid WorkspaceId,
        Guid ApplicationId,
        Guid EnvironmentId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }
}
