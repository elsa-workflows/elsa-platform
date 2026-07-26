using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Platform.Api.Healing;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.Api.Workspace.Healing;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.Api.Tests.Healing;

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

        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        replay.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.GetProperty("observationId").GetGuid().Should().Be(first.GetProperty("observationId").GetGuid());
        second.GetProperty("isReplay").GetBoolean().Should().BeTrue();
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var scope = app.Factory.Services.CreateAsyncScope();
        var observations = await scope.ServiceProvider.GetRequiredService<HealingDbContext>()
            .DeploymentObservations.AsNoTracking().ToArrayAsync();
        observations.Should().ContainSingle();
        observations[0].Source.Should().Be(DeploymentObservationSource.ExternalDelivery);
        observations[0].TrustIdentity.Should().StartWith("platform-account:");
    }

    [Fact]
    public async Task Platform_managed_deployment_uses_the_same_idempotent_application_contract()
    {
        await using var app = await CreateApplicationAsync("healing-deployment-platform");
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var sink = scope.ServiceProvider.GetRequiredService<IDeploymentObservationSink>();
        var request = new DeploymentObservationRequest(
            HealingContractVersions.DeploymentProtocol,
            app.WorkspaceId,
            app.ApplicationId,
            app.EnvironmentId,
            "platform-fixed-sha",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DeploymentObservationSources.PlatformDeployment,
            "platform-command-7",
            "platform-engine:engine-7",
            $"sha256:{new string('b', 64)}",
            "platform-command:7");

        var accepted = await sink.AppendAsync(request);
        var replay = await sink.AppendAsync(request);

        accepted.IsReplay.Should().BeFalse();
        replay.IsReplay.Should().BeTrue();
        replay.ObservationId.Should().Be(accepted.ObservationId);
        var observation = await scope.ServiceProvider.GetRequiredService<HealingDbContext>()
            .DeploymentObservations.AsNoTracking().SingleAsync();
        observation.Source.Should().Be(DeploymentObservationSource.PlatformDeployment);
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

        (await outsider.PostAsJsonAsync(ObservationUri(app), request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await app.Owner.PostAsJsonAsync(ObservationUri(app), request)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await app.Factory.GrantWorkspaceDeploymentPermissionAsync(app.WorkspaceId, reporterId, HealingPermissions.Configure);
        (await SendAsync(reporter, ObservationUri(app), request, "configure-is-not-report"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await app.Factory.GrantWorkspaceDeploymentPermissionAsync(app.WorkspaceId, reporterId, HealingPermissions.ReportDeployment);
        (await SendAsync(reporter, ObservationUri(app), request, "narrow-deployment-report"))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await SendAsync(app.Owner,
            $"/api/workspaces/{app.WorkspaceId:D}/healing/applications/{app.ApplicationId:D}/environments/{Guid.NewGuid():D}/deployment-observations",
            request, "missing-environment")).StatusCode.Should().Be(HttpStatusCode.NotFound);
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
        var factory = new PlatformApiTestApplication();
        await factory.SeedAsync(_ => Task.CompletedTask);
        await factory.SeedHealingAsync();
        var owner = factory.CreateTrustedWorkspaceClient(ownerSubject);
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var applicationResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Orders API", null));
        applicationResponse.EnsureSuccessStatusCode();
        var application = await applicationResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentApplication>();
        var environmentResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/applications/{application!.Id:D}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Production", EnvironmentTier.Production));
        environmentResponse.EnsureSuccessStatusCode();
        var environment = await environmentResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentEnvironment>();
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
        PlatformApiTestApplication Factory,
        HttpClient Owner,
        Guid WorkspaceId,
        Guid ApplicationId,
        Guid EnvironmentId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }
}
