using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using ValenceControl.Api.Workspace;
using ValenceControl.Api.Workspace.Healing;
using ValenceControl.Api.Healing;
using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using ValenceControl.PackageCatalog.Core.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ValenceControl.Api.Tests.Healing;

public sealed class HealingIntakeApiTests
{
    [Fact]
    public async Task Explicit_incident_uses_route_scope_and_is_idempotent()
    {
        await using var app = await CreateApplicationAsync(
            "healing-explicit-owner",
            services => services.AddSingleton<IWorkspaceHealingOpenTelemetryQuery, RecordingTelemetryQuery>());
        var occurrenceId = $"explicit-{Guid.NewGuid():N}";
        var request = Signal(Guid.NewGuid(), Guid.NewGuid(), occurrenceId);
        var uri = ApplicationUri(app, "/incidents");

        var accepted = await app.Owner.PostControlJsonAsync(uri, request);
        var first = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        var replay = await app.Owner.PostControlJsonAsync(uri, request);
        var second = await replay.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.False(first.GetProperty("isReplay").GetBoolean());
        Assert.True(second.GetProperty("isReplay").GetBoolean());
        Assert.Equal(first.GetProperty("inboxId").GetGuid(), second.GetProperty("inboxId").GetGuid());

        await using var scope = app.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var item = await db.HealingSignalInboxItems.SingleAsync();
        var persisted = JsonSerializer.Deserialize<HealingSignal>(item.RedactedEnvelopeJson, ControlApiTestApplication.JsonOptions);
        Assert.Equal(app.WorkspaceId, item.WorkspaceId);
        Assert.Equal(app.ApplicationId, item.ApplicationId);
        Assert.Equal(app.EnvironmentId, item.EnvironmentId);
        Assert.Equal(app.ApplicationId, persisted!.ApplicationId);
        Assert.Equal(app.EnvironmentId, persisted.EnvironmentId);
    }

    [Fact]
    public async Task Explicit_incident_rejects_conflicting_replay_and_invalid_scope_or_profile()
    {
        await using var app = await CreateApplicationAsync("healing-explicit-validation");
        var occurrenceId = $"explicit-{Guid.NewGuid():N}";
        var uri = ApplicationUri(app, "/incidents");
        Assert.Equal(
            HttpStatusCode.Accepted,
            (await app.Owner.PostControlJsonAsync(uri, Signal(app.ApplicationId, app.EnvironmentId, occurrenceId))).StatusCode);

        var conflict = await app.Owner.PostControlJsonAsync(
            uri,
            Signal(app.ApplicationId, app.EnvironmentId, occurrenceId) with { OperationName = "POST /changed" });
        var unsupported = await app.Owner.PostControlJsonAsync(
            uri,
            Signal(app.ApplicationId, app.EnvironmentId, $"unsupported-{Guid.NewGuid():N}") with { ProfileVersion = "2.0" });
        var missingEnvironment = await app.Owner.PostControlJsonAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/healing/applications/{app.ApplicationId:D}/environments/{Guid.NewGuid():D}/incidents",
            Signal(app.ApplicationId, app.EnvironmentId, $"missing-{Guid.NewGuid():N}"));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingEnvironment.StatusCode);
    }

    [Fact]
    public async Task OpenTelemetry_routes_delegate_with_server_authoritative_scope()
    {
        var query = new RecordingTelemetryQuery();
        await using var app = await CreateApplicationAsync(
            "healing-otel-owner",
            services => services.AddSingleton<IWorkspaceHealingOpenTelemetryQuery>(query));
        var baseUri = ApplicationUri(app, "/opentelemetry");

        var configuration = await app.Owner.GetAsync($"{baseUri}/collector-configuration");
        var logs = await app.Owner.PostControlJsonAsync(
            $"{baseUri}/logs/search",
            new OpenTelemetryLogFilter { Severity = "error", Take = 20 });

        Assert.Equal(HttpStatusCode.OK, configuration.StatusCode);
        Assert.Equal(HttpStatusCode.OK, logs.StatusCode);
        Assert.All(query.Scopes, scope => Assert.True(
            scope.WorkspaceId == app.WorkspaceId &&
            scope.ApplicationId == app.ApplicationId &&
            scope.EnvironmentId == app.EnvironmentId));
        Assert.Equal("error", Assert.Single(query.LogFilters).Severity);
    }

    [Fact]
    public async Task Explicit_write_and_telemetry_read_require_their_healing_permissions()
    {
        await using var app = await CreateApplicationAsync(
            "healing-intake-permissions",
            services => services.AddSingleton<IWorkspaceHealingOpenTelemetryQuery, RecordingTelemetryQuery>());
        const string subject = "healing-intake-reader";
        var accountId = await app.Factory.AddWorkspaceMemberAsync(app.WorkspaceId, subject, WorkspaceRole.Reader);
        var reader = app.Factory.CreateTrustedWorkspaceClient(subject);
        var incidentUri = ApplicationUri(app, "/incidents");
        var telemetryUri = ApplicationUri(app, "/opentelemetry/logs/search");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await reader.PostControlJsonAsync(incidentUri, Signal(app.ApplicationId, app.EnvironmentId, "denied"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostControlJsonAsync(telemetryUri, new OpenTelemetryLogFilter())).StatusCode);

        await app.Factory.GrantWorkspaceDeploymentPermissionAsync(app.WorkspaceId, accountId, HealingPermissions.Read);
        Assert.Equal(HttpStatusCode.OK, (await reader.PostControlJsonAsync(telemetryUri, new OpenTelemetryLogFilter())).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await reader.PostControlJsonAsync(incidentUri, Signal(app.ApplicationId, app.EnvironmentId, "still-denied"))).StatusCode);

        await app.Factory.GrantWorkspaceDeploymentPermissionAsync(app.WorkspaceId, accountId, HealingPermissions.Configure);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await reader.PostControlJsonAsync(incidentUri, Signal(app.ApplicationId, app.EnvironmentId, "configure-still-denied"))).StatusCode);

        await app.Factory.GrantWorkspaceDeploymentPermissionAsync(app.WorkspaceId, accountId, HealingPermissions.ReportIncident);
        Assert.Equal(
            HttpStatusCode.Accepted,
            (await reader.PostControlJsonAsync(incidentUri, Signal(app.ApplicationId, app.EnvironmentId, "allowed"))).StatusCode);
    }

    private static HealingSignal Signal(Guid applicationId, Guid environmentId, string occurrenceId) => new(
        HealingContractVersions.SignalProfile,
        applicationId,
        environmentId,
        null,
        DateTimeOffset.Parse("2026-07-16T12:00:00Z"),
        "GET /orders/{id}",
        HealingFailureClasses.UnhandledRequest,
        HealingRetryStates.None,
        new HealingExceptionEvidence(
            "System.InvalidOperationException",
            "Order state was invalid.",
            "at Acme.Orders.Get()",
            [new HealingExceptionFrame("Acme.Orders", "Acme.Orders.Api", "Get", null, null)]),
        new HealingEvidenceMetadata(true, false, []),
        occurrenceId,
        ServiceName: "acme-orders",
        ResourceIdentity: "orders-api-1",
        Severity: "error");

    private static async Task<TestApplication> CreateApplicationAsync(
        string ownerSubject,
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new ControlApiTestApplication(configureServices: configureServices);
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
        return new TestApplication(factory, owner, workspaceId, application.Id, environment!.Id);
    }

    private static string ApplicationUri(TestApplication app, string suffix) =>
        $"/api/workspaces/{app.WorkspaceId:D}/healing/applications/{app.ApplicationId:D}/environments/{app.EnvironmentId:D}{suffix}";

    private sealed record TestApplication(
        ControlApiTestApplication Factory,
        HttpClient Owner,
        Guid WorkspaceId,
        Guid ApplicationId,
        Guid EnvironmentId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }

    private sealed class RecordingTelemetryQuery : IWorkspaceHealingOpenTelemetryQuery
    {
        public List<HealingTelemetryQueryScope> Scopes { get; } = [];
        public List<OpenTelemetryLogFilter> LogFilters { get; } = [];

        public ValueTask<CollectorConfiguration> GetCollectorConfigurationAsync(
            HealingTelemetryQueryScope scope,
            CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            return ValueTask.FromResult(new CollectorConfiguration(
                new CollectorEndpointInfo("http/protobuf", "https://control.test/otlp/v1", true, null),
                new CollectorEndpointInfo("grpc", null, false, "disabled"),
                "OTEL_SERVICE_NAME",
                "OTEL_EXPORTER_OTLP_ENDPOINT",
                "OTEL_EXPORTER_OTLP_PROTOCOL",
                new Dictionary<string, string>()));
        }

        public ValueTask<OpenTelemetryLogResult> GetLogsAsync(
            HealingTelemetryQueryScope scope,
            OpenTelemetryLogFilter filter,
            CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            LogFilters.Add(filter);
            return ValueTask.FromResult(new OpenTelemetryLogResult([], 0));
        }

        public ValueTask<OpenTelemetryTraceResult> GetTracesAsync(
            HealingTelemetryQueryScope scope,
            OpenTelemetryTraceFilter filter,
            CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            return ValueTask.FromResult(new OpenTelemetryTraceResult([], 0));
        }

        public ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(
            HealingTelemetryQueryScope scope,
            string traceId,
            CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            return ValueTask.FromResult<OpenTelemetryTraceDetail?>(null);
        }
    }
}
