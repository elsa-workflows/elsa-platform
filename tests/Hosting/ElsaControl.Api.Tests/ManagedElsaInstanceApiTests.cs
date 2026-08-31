using System.Net;
using System.Net.Http.Json;
using ElsaControl.Api.Workspace;
using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElsaControl.Api.Tests;

public sealed class ManagedElsaInstanceApiTests
{
    [Fact]
    public async Task Healthy_bound_instance_is_openable_but_deleting_instance_fails_closed()
    {
        var healthy = Guid.NewGuid();
        await using var app = CreateApplication([
            Instance(healthy, "Claims runtime", "claims-runtime"),
            Instance(
                Guid.NewGuid(),
                "Deleting runtime",
                "deleting-runtime",
                ElsaDesiredLifecycle.Deleting,
                ElsaObservedLifecycle.Deleting,
                ElsaInstanceHealth.Unknown,
                bound: false),
            Instance(
                Guid.NewGuid(),
                "Unbound healthy runtime",
                "unbound-healthy-runtime",
                bound: false)
        ]);
        await app.SeedAsync(_ => Task.CompletedTask);

        var client = app.CreateControlIdentityClient(subject: "managed-owner");
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();
        var response = await client.GetAsync($"/api/workspaces/{workspaceId}/managed-elsa/instances");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("private", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Contains("no-cache", response.Headers.Pragma.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
        var instances = await response.Content.ReadFromJsonAsync<List<ManagedElsaInstanceResponse>>(ControlApiTestApplication.JsonOptions);
        Assert.NotNull(instances);
        var openable = Assert.Single(instances!, x => x.InstanceId == healthy);
        Assert.True(openable.CanOpen);
        Assert.Equal("urn:elsa:instance:" + healthy.ToString("D"), openable.Audience);
        Assert.Equal("https://managed.example.test/managed-elsa/handoff/callback", openable.RedirectUri);

        var deleting = Assert.Single(instances, x => x.DesiredLifecycle == ElsaDesiredLifecycle.Deleting);
        Assert.False(deleting.CanOpen);
        Assert.Null(deleting.Audience);
        Assert.Null(deleting.RedirectUri);

        var unbound = Assert.Single(instances, x => x.Slug == "unbound-healthy-runtime");
        Assert.False(unbound.CanOpen);
        Assert.Null(unbound.Audience);
        Assert.Null(unbound.RedirectUri);
    }

    [Fact]
    public async Task Caller_without_workspace_access_cannot_read_managed_instances()
    {
        await using var app = CreateApplication([Instance(Guid.NewGuid(), "Claims runtime", "claims-runtime")]);
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "managed-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var response = await app.CreateControlIdentityClient(subject: "managed-outsider")
            .GetAsync($"/api/workspaces/{workspaceId}/managed-elsa/instances");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Workspace_reader_without_open_permission_sees_redacted_binding()
    {
        var instanceId = Guid.NewGuid();
        await using var app = CreateApplication([Instance(instanceId, "Claims runtime", "claims-runtime")]);
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("managed-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        await app.AddWorkspaceMemberAsync(workspaceId, "managed-reader", ElsaControl.PackageCatalog.Core.Accounts.WorkspaceRole.Reader);

        var response = await app.CreateTrustedWorkspaceClient("managed-reader")
            .GetAsync($"/api/workspaces/{workspaceId}/managed-elsa/instances");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var instances = await response.Content.ReadFromJsonAsync<List<ManagedElsaInstanceResponse>>(ControlApiTestApplication.JsonOptions);
        var item = Assert.Single(instances!);
        Assert.False(item.CanOpen);
        Assert.Null(item.Audience);
        Assert.Null(item.RedirectUri);
    }

    [Fact]
    public async Task Canonical_create_returns_an_async_operation_and_safe_detail_projection()
    {
        await using var app = CreateApplication([]);
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient("managed-instance-api-owner");
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{workspaceId}/instances")
        {
            Content = JsonContent.Create(
                new ManagedElsaInstanceCreateRequest("Claims runtime", "Claims Runtime", Intent()),
                options: ControlApiTestApplication.JsonOptions)
        };
        request.Headers.Add("Idempotency-Key", "create-claims-runtime");

        var accepted = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.NotNull(accepted.Headers.Location);
        var acceptedJson = await accepted.Content.ReadAsStringAsync();
        Assert.DoesNotContain("serializedPlan", acceptedJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("providerId", acceptedJson, StringComparison.OrdinalIgnoreCase);
        var acceptedBody = System.Text.Json.JsonSerializer.Deserialize<ManagedElsaInstanceAcceptedResponse>(acceptedJson, ControlApiTestApplication.JsonOptions);
        Assert.NotNull(acceptedBody);
        Assert.Equal(ElsaInstanceOperationAction.Create, acceptedBody!.Operation.Action);
        Assert.Equal(accepted.Headers.Location!.ToString(), acceptedBody.Links["self"]);

        var operation = await client.GetControlJsonAsync<ManagedElsaInstanceOperationResponse>(accepted.Headers.Location!.ToString());
        Assert.NotNull(operation);
        Assert.Equal(acceptedBody.Operation.Id, operation!.Id);

        var detail = await client.GetControlJsonAsync<ManagedElsaInstanceResponse>(
            $"/api/workspaces/{workspaceId}/instances/{acceptedBody.Instance.InstanceId}");
        Assert.NotNull(detail);
        Assert.Equal("claims-runtime", detail!.Slug);
        Assert.Equal(detail.ETag, acceptedBody.Instance.ETag);
    }

    [Fact]
    public async Task Canonical_mutations_require_idempotency_and_strong_etags()
    {
        await using var app = CreateApplication([]);
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient("managed-instance-api-preconditions");
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();
        var instanceId = Guid.NewGuid();

        var missingKey = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/instances/{instanceId}/operations",
            new ManagedElsaInstanceOperationRequest(ElsaInstanceOperationAction.Start, 1));
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);

        var missingMatch = new HttpRequestMessage(HttpMethod.Patch, $"/api/workspaces/{workspaceId}/instances/{instanceId}")
        {
            Content = JsonContent.Create(new ManagedElsaInstancePatchRequest(Name: "Renamed"), options: ControlApiTestApplication.JsonOptions)
        };
        missingMatch.Headers.Add("Idempotency-Key", "rename-claims-runtime");
        var response = await client.SendAsync(missingMatch);
        Assert.Equal((HttpStatusCode)428, response.StatusCode);
    }

    private static ControlApiTestApplication CreateApplication(IReadOnlyList<ManagedElsaInstanceSummary> instances)
    {
        return new ControlApiTestApplication(
            configureServices: services =>
            {
                services.RemoveAll<IManagedElsaInstanceCatalog>();
                services.AddSingleton<IManagedElsaInstanceCatalog>(new FakeManagedElsaInstanceCatalog(instances));
            });
    }

    private static ManagedElsaInstanceSummary Instance(
        Guid instanceId,
        string name,
        string slug,
        ElsaDesiredLifecycle desiredLifecycle = ElsaDesiredLifecycle.Running,
        ElsaObservedLifecycle observedLifecycle = ElsaObservedLifecycle.Ready,
        ElsaInstanceHealth health = ElsaInstanceHealth.Healthy,
        bool bound = true,
        string? audience = null,
        Uri? callbackUri = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            instanceId,
            name,
            slug,
            desiredLifecycle,
            observedLifecycle,
            health,
            bound ? audience ?? "urn:elsa:instance:" + instanceId.ToString("D") : null,
            bound ? callbackUri ?? new Uri("https://managed.example.test/managed-elsa/handoff/callback") : null,
            bound ? 1 : null);

    private static ElsaInstanceIntent Intent() => new(
        new ElsaReleaseIntent("valence-runtime", "3.8", channel: "stable"),
        new ElsaApplicationIntent("combined", "starter",
            new Dictionary<string, ElsaFeatureOverride> { ["replicas"] = ElsaFeatureOverride.FromNumber(3) },
            "approved"),
        new ElsaPlacementIntent("managed", "westeurope", "dedicated", "standard-small", "public", "managed"));

    private sealed class FakeManagedElsaInstanceCatalog(IReadOnlyList<ManagedElsaInstanceSummary> instances) : IManagedElsaInstanceCatalog
    {
        public Task<IReadOnlyList<ManagedElsaInstanceSummary>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(instances);
    }
}
