using System.Net;
using System.Net.Http.Json;
using ElsaControl.Api.Workspace;
using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
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
        await EnableManagedHostingAsync(app, workspaceId);
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
    public async Task Canonical_list_handles_large_page_numbers_without_overflowing_has_more()
    {
        await using var app = CreateApplication([]);
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient("managed-instance-api-pagination");
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();
        await EnableManagedHostingAsync(app, workspaceId);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{workspaceId}/instances")
        {
            Content = JsonContent.Create(
                new ManagedElsaInstanceCreateRequest("Claims runtime", "claims-runtime", Intent()),
                options: ControlApiTestApplication.JsonOptions)
        };
        request.Headers.Add("Idempotency-Key", "create-for-pagination");
        var accepted = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);

        var list = await client.GetControlJsonAsync<ManagedElsaInstanceListResponse>(
            $"/api/workspaces/{workspaceId}/instances?page={int.MaxValue}&pageSize=100");

        Assert.NotNull(list);
        Assert.Empty(list!.Items);
        Assert.Equal(1, list.TotalCount);
        Assert.False(list.HasMore);
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

        var operationWithoutMatch = new HttpRequestMessage(HttpMethod.Post,
            $"/api/workspaces/{workspaceId}/instances/{instanceId}/operations")
        {
            Content = JsonContent.Create(
                new ManagedElsaInstanceOperationRequest(ElsaInstanceOperationAction.Start, 1),
                options: ControlApiTestApplication.JsonOptions)
        };
        operationWithoutMatch.Headers.Add("Idempotency-Key", "start-claims-runtime");
        response = await client.SendAsync(operationWithoutMatch);
        Assert.Equal((HttpStatusCode)428, response.StatusCode);
    }

    [Fact]
    public async Task Canonical_create_fails_closed_when_managed_hosting_entitlement_is_missing()
    {
        await using var app = CreateApplication([]);
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient("managed-instance-api-no-entitlement");
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{workspaceId}/instances")
        {
            Content = JsonContent.Create(
                new ManagedElsaInstanceCreateRequest("Claims runtime", "claims-runtime", Intent()),
                options: ControlApiTestApplication.JsonOptions)
        };
        request.Headers.Add("Idempotency-Key", "create-without-entitlement");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("instance.entitlement-required", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Canonical_create_rejects_display_names_over_256_characters_as_unprocessable()
    {
        await using var app = CreateApplication([]);
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient("managed-instance-api-long-name");
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();
        await EnableManagedHostingAsync(app, workspaceId);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{workspaceId}/instances")
        {
            Content = JsonContent.Create(
                new ManagedElsaInstanceCreateRequest(new string('n', 257), "claims-runtime", Intent()),
                options: ControlApiTestApplication.JsonOptions)
        };
        request.Headers.Add("Idempotency-Key", "create-long-name");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("instance.shape-invalid", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_projection_hides_binding_unless_instance_is_running_ready_and_healthy()
    {
        var instanceId = Guid.NewGuid();
        var binding = ElsaInstanceIdentityBinding.Create(instanceId, "https://managed.example.test");
        var instance = ElsaInstance.Hydrate(instanceId, Guid.NewGuid(), Guid.NewGuid(), "Claims runtime", "claims-runtime",
            Intent(), ElsaObservedLifecycle.Ready, ElsaInstanceHealth.Degraded, 2, binding);

        var response = ManagedElsaInstanceEndpoints.ToResponse(instance, canOpen: true, instance.WorkspaceId);

        Assert.False(response.CanOpen);
        Assert.Null(response.Audience);
        Assert.Null(response.RedirectUri);
        Assert.Null(response.IdentityBinding);
        Assert.Equal("instance-unavailable", response.IdentityBindingState);
        Assert.Equal("This instance is not currently available.", response.UnavailableReason);
    }

    [Fact]
    public void Customer_audit_projection_redacts_operator_subject()
    {
        var audit = new ElsaInstanceAuditEventSummary(Guid.NewGuid(), 1, "instance.updated", Guid.NewGuid(),
            "sha256:sensitive-operator-fingerprint", null, null, null, null, null, null, null, null, null, null,
            DateTimeOffset.UtcNow);

        var response = ManagedElsaInstanceEndpoints.RedactAudit(audit);

        Assert.Null(response.OperatorSubject);
        Assert.Equal(audit.Id, response.Id);
    }

    [Fact]
    public void Lifecycle_worker_poll_interval_is_clamped_to_one_second()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), ElsaInstanceLifecycleHostedService.NormalizePollInterval(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(1), ElsaInstanceLifecycleHostedService.NormalizePollInterval(TimeSpan.FromMilliseconds(50)));
        Assert.Equal(TimeSpan.FromSeconds(3), ElsaInstanceLifecycleHostedService.NormalizePollInterval(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void Lifecycle_worker_identity_is_safe_bounded_and_unique_per_hosted_service()
    {
        var first = ElsaInstanceLifecycleHostedService.CreateWorkerId();
        var second = ElsaInstanceLifecycleHostedService.CreateWorkerId();

        Assert.NotEqual(first, second);
        Assert.StartsWith($"api-instance-lifecycle-{Environment.ProcessId}-", first, StringComparison.Ordinal);
        Assert.InRange(first.Length, 1, 256);
        Assert.DoesNotContain(first, char.IsControl);
    }

    [Fact]
    public async Task Instance_from_another_workspace_and_unknown_instance_are_indistinguishable()
    {
        await using var app = CreateApplication([]);
        await app.SeedAsync(_ => Task.CompletedTask);
        var first = app.CreateTrustedWorkspaceClient("managed-instance-owner-a");
        var firstWorkspaceId = await first.GetDefaultWorkspaceIdAsync();
        await EnableManagedHostingAsync(app, firstWorkspaceId);
        var create = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{firstWorkspaceId}/instances")
        {
            Content = JsonContent.Create(new ManagedElsaInstanceCreateRequest("Claims runtime", "claims-runtime", Intent()),
                options: ControlApiTestApplication.JsonOptions)
        };
        create.Headers.Add("Idempotency-Key", "create-workspace-a-runtime");
        var accepted = await first.SendAsync(create);
        var body = await accepted.Content.ReadControlJsonAsync<ManagedElsaInstanceAcceptedResponse>();
        Assert.NotNull(body);

        var second = app.CreateTrustedWorkspaceClient("managed-instance-owner-b");
        var secondWorkspaceId = await second.GetDefaultWorkspaceIdAsync();
        var otherWorkspace = await second.GetAsync($"/api/workspaces/{secondWorkspaceId}/instances/{body!.Instance.InstanceId}");
        var unknown = await second.GetAsync($"/api/workspaces/{secondWorkspaceId}/instances/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, otherWorkspace.StatusCode);
        Assert.Equal(unknown.StatusCode, otherWorkspace.StatusCode);
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

    private static async Task EnableManagedHostingAsync(ControlApiTestApplication app, Guid workspaceId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var organizationId = await db.Workspaces.Where(x => x.Id == workspaceId).Select(x => x.OrganizationId).SingleAsync();
        db.OrganizationEntitlementSnapshots.Add(new OrganizationEntitlementSnapshot
        {
            OrganizationId = organizationId,
            ManagedHostingEnabled = true,
            MaxSources = 5,
            MaxWorkspaces = 5
        });
        await db.SaveChangesAsync();
    }

    private sealed class FakeManagedElsaInstanceCatalog(IReadOnlyList<ManagedElsaInstanceSummary> instances) : IManagedElsaInstanceCatalog
    {
        public Task<IReadOnlyList<ManagedElsaInstanceSummary>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(instances);
    }
}
