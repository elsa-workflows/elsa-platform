using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using ValenceControl.Api.Workspace;
using ValenceControl.Api.Workspace.Healing;
using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.OpenTelemetry;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Api.Tests.Healing;

public sealed class WorkspaceHealingTelemetrySourceApiTests
{
    [Fact]
    public async Task Authenticated_otlp_exception_is_durable_before_success_and_rejects_untrusted_scope()
    {
        await using var app = await CreateApplicationAsync("healing-otlp-owner");
        var sourceResponse = await app.Owner.PostControlJsonAsync(
            SourcesUri(app, app.EnvironmentId),
            new { name = "Orders collector" });
        var source = await sourceResponse.Content.ReadFromJsonAsync<HealingTelemetrySourceCredentialResponse>();
        Assert.Equal(HttpStatusCode.Created, sourceResponse.StatusCode);

        var otlp = app.Factory.CreateClient();
        using var acceptedRequest = OtlpLogsRequest(
            source!.Token,
            app.ApplicationId,
            app.EnvironmentId,
            occurrenceId: "otlp-occurrence-1");
        var accepted = await otlp.SendAsync(acceptedRequest);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
            var item = await db.HealingSignalInboxItems.AsNoTracking().SingleAsync();
            Assert.Equal(app.WorkspaceId, item.WorkspaceId);
            Assert.Equal(app.ApplicationId, item.ApplicationId);
            Assert.Equal(app.EnvironmentId, item.EnvironmentId);
            Assert.Equal("otlp-occurrence-1", item.IdempotencyKey);
            Assert.Equal(HealingInboxStatus.Pending, item.Status);
        }

        using var invalidRequest = OtlpLogsRequest(
            "elsa_otlp_v1.00000000000000000000000000000001.invalid",
            app.ApplicationId,
            app.EnvironmentId,
            occurrenceId: "invalid-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await otlp.SendAsync(invalidRequest)).StatusCode);

        using var forgedRequest = OtlpLogsRequest(
            source.Token,
            Guid.NewGuid(),
            app.EnvironmentId,
            occurrenceId: "forged-scope");
        Assert.Equal(HttpStatusCode.OK, (await otlp.SendAsync(forgedRequest)).StatusCode);

        await using var verificationScope = app.Factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<HealingDbContext>();
        Assert.Equal(1, (await verificationDb.HealingSignalInboxItems.AsNoTracking().CountAsync()));
    }

    [Fact]
    public async Task Otlp_receiver_does_not_acknowledge_when_durable_healing_append_fails()
    {
        await using var app = await CreateApplicationAsync(
            "healing-otlp-failure-owner",
            services =>
            {
                services.RemoveAll<IHealingSignalInboxAppender>();
                services.AddScoped<IHealingSignalInboxAppender, FailingInboxAppender>();
            });
        var sourceResponse = await app.Owner.PostControlJsonAsync(
            SourcesUri(app, app.EnvironmentId),
            new { name = "Failing collector" });
        var source = await sourceResponse.Content.ReadFromJsonAsync<HealingTelemetrySourceCredentialResponse>();

        using var request = OtlpLogsRequest(
            source!.Token,
            app.ApplicationId,
            app.EnvironmentId,
            occurrenceId: "must-not-ack");
        var response = await app.Factory.CreateClient().SendAsync(request);

        Assert.False(response.IsSuccessStatusCode);
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        Assert.Equal(0, (await db.HealingSignalInboxItems.AsNoTracking().CountAsync()));
    }

    [Fact]
    public async Task Create_rotate_and_revoke_disclose_tokens_once_and_enforce_server_scope()
    {
        await using var app = await CreateApplicationAsync("healing-telemetry-source-owner");
        var sourceBaseUri = SourcesUri(app, app.EnvironmentId);

        var create = await app.Owner.PostControlJsonAsync(sourceBaseUri, new { name = "Orders production" });
        var created = await create.Content.ReadFromJsonAsync<HealingTelemetrySourceCredentialResponse>();
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(created);
        Assert.StartsWith("elsa_otlp_v1.", created!.Token);
        Assert.Equal(HealingTelemetrySourceTokenService.HeaderName, created.HeaderName);

        var activeAuthentication = await AuthenticateAsync(app.Factory, created.Token);
        Assert.True(activeAuthentication.Accepted);
        Assert.Equal(app.WorkspaceId.ToString("D"), activeAuthentication.Context.Claims[HealingTelemetryScopeClaims.WorkspaceId]);
        Assert.Equal(app.ApplicationId.ToString("D"), activeAuthentication.Context.Claims[HealingTelemetryScopeClaims.ApplicationId]);
        Assert.Equal(app.EnvironmentId.ToString("D"), activeAuthentication.Context.Claims[HealingTelemetryScopeClaims.EnvironmentId]);

        var list = await app.Owner.GetAsync(sourceBaseUri);
        var listJson = await list.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.DoesNotContain(created.Token, listJson);
        Assert.DoesNotContain("credentialhash", listJson.ToLowerInvariant());
        Assert.DoesNotContain("credentialsalt", listJson.ToLowerInvariant());
        Assert.Equal(1, JsonDocument.Parse(listJson).RootElement.GetArrayLength());

        var crossEnvironmentRotate = await app.Owner.PostAsync(
            $"{SourcesUri(app, app.OtherEnvironmentId)}/{created.Source.Id:D}/rotate", null);
        Assert.Equal(HttpStatusCode.NotFound, crossEnvironmentRotate.StatusCode);

        var rotate = await app.Owner.PostAsync($"{sourceBaseUri}/{created.Source.Id:D}/rotate", null);
        var rotated = await rotate.Content.ReadFromJsonAsync<HealingTelemetrySourceCredentialResponse>();
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        Assert.NotNull(rotated);
        Assert.NotEqual(created.Token, rotated!.Token);
        Assert.Equal(2, rotated.Source.CredentialVersion);
        Assert.False((await AuthenticateAsync(app.Factory, created.Token)).Accepted);
        Assert.True((await AuthenticateAsync(app.Factory, rotated.Token)).Accepted);

        var revoke = await app.Owner.PostAsync($"{sourceBaseUri}/{created.Source.Id:D}/revoke", null);
        var revoked = await revoke.Content.ReadFromJsonAsync<HealingTelemetrySourceResponse>();
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal("Revoked", revoked!.Status);
        Assert.False((await AuthenticateAsync(app.Factory, rotated.Token)).Accepted);

        var afterLifecycle = await app.Owner.GetStringAsync(sourceBaseUri);
        Assert.DoesNotContain(created.Token, afterLifecycle);
        Assert.DoesNotContain(rotated.Token, afterLifecycle);
        Assert.DoesNotContain("credentialhash", afterLifecycle.ToLowerInvariant());
        Assert.DoesNotContain("credentialsalt", afterLifecycle.ToLowerInvariant());

        await using var auditScope = app.Factory.Services.CreateAsyncScope();
        var db = auditScope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var auditEvents = await db.Set<HealingAuditEvent>().AsNoTracking()
            .Where(x => x.AggregateId == created.Source.Id)
            .OrderBy(x => x.Sequence)
            .ToListAsync();
        Assert.Equal(
            new[] { "telemetry-source-created", "telemetry-source-rotated", "telemetry-source-revoked" },
            auditEvents.Select(x => x.EventType));
        var safeDetails = string.Join(' ', auditEvents.Select(x => x.SafeDetailJson));
        Assert.DoesNotContain(created.Token, safeDetails);
        Assert.DoesNotContain(rotated.Token, safeDetails);
    }

    private static async Task<OtlpRequestAuthenticationResult> AuthenticateAsync(
        ControlApiTestApplication application,
        string token)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var authenticator = scope.ServiceProvider.GetRequiredService<IOtlpRequestAuthenticator>();
        var context = new DefaultHttpContext();
        context.Request.Headers[HealingTelemetrySourceTokenService.HeaderName] = token;
        return await authenticator.AuthenticateAsync(context);
    }

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
        var production = await CreateEnvironmentAsync(owner, workspaceId, application!.Id, "Production");
        var staging = await CreateEnvironmentAsync(owner, workspaceId, application.Id, "Staging");
        return new TestApplication(factory, owner, workspaceId, application.Id, production.Id, staging.Id);
    }

    private static async Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(
        HttpClient owner,
        Guid workspaceId,
        Guid applicationId,
        string name)
    {
        var response = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/applications/{applicationId:D}/environments",
            new WorkspaceDeploymentEnvironmentRequest(name, EnvironmentTier.Production));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadControlJsonAsync<WorkspaceDeploymentEnvironment>())!;
    }

    private static string SourcesUri(TestApplication app, Guid environmentId) =>
        $"/api/workspaces/{app.WorkspaceId:D}/healing/applications/{app.ApplicationId:D}/environments/{environmentId:D}/opentelemetry/sources";

    private static HttpRequestMessage OtlpLogsRequest(
        string token,
        Guid applicationId,
        Guid environmentId,
        string occurrenceId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/valence-control/otlp/v1/logs")
        {
            Content = new ByteArrayContent(OtlpExceptionLogPayload(applicationId, environmentId, occurrenceId))
        };
        request.Content.Headers.ContentType = new("application/x-protobuf");
        request.Headers.TryAddWithoutValidation(HealingTelemetrySourceTokenService.HeaderName, token);
        return request;
    }

    private static byte[] OtlpExceptionLogPayload(Guid applicationId, Guid environmentId, string occurrenceId)
    {
        var resource = Join(
            Message(1, KeyValue("service.name", "orders-api")),
            Message(1, KeyValue("service.instance.id", "orders-prod-1")));
        var log = Join(
            Varint(1, (ulong)(DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).Ticks * 100),
            Varint(2, 17),
            String(3, "Error"),
            Message(5, AnyString("Order lookup failed")),
            Message(6, KeyValue(HealingSignalAttributes.ProfileVersion, HealingContractVersions.SignalProfile)),
            Message(6, KeyValue(HealingSignalAttributes.ApplicationId, applicationId.ToString("D"))),
            Message(6, KeyValue(HealingSignalAttributes.EnvironmentId, environmentId.ToString("D"))),
            Message(6, KeyValue(HealingSignalAttributes.OperationName, "GET /orders/{id}")),
            Message(6, KeyValue(HealingSignalAttributes.OccurrenceId, occurrenceId)),
            Message(6, KeyValue("exception.type", "System.InvalidOperationException")),
            Message(6, KeyValue("exception.message", "Order lookup failed")),
            Message(6, KeyValue("exception.stacktrace", "at Acme.Orders.OrderService.Load()")));
        return Message(1, Join(Message(1, resource), Message(2, Message(2, log))));
    }

    private static byte[] KeyValue(string key, string value) => Join(String(1, key), Message(2, AnyString(value)));
    private static byte[] AnyString(string value) => String(1, value);
    private static byte[] Message(int fieldNumber, byte[] value) =>
        Join(Varint((ulong)((fieldNumber << 3) | 2)), Varint((ulong)value.Length), value);
    private static byte[] String(int fieldNumber, string value) => Message(fieldNumber, Encoding.UTF8.GetBytes(value));
    private static byte[] Varint(int fieldNumber, ulong value) => Join(Varint((ulong)(fieldNumber << 3)), Varint(value));

    private static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>();
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }
        bytes.Add((byte)value);
        return bytes.ToArray();
    }

    private static byte[] Join(params byte[][] segments) => segments.SelectMany(segment => segment).ToArray();

    private sealed class FailingInboxAppender : IHealingSignalInboxAppender
    {
        public ValueTask AppendAsync(HealingSignalInboxItem item, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new InvalidOperationException("Durable inbox unavailable."));
    }

    private sealed record TestApplication(
        ControlApiTestApplication Factory,
        HttpClient Owner,
        Guid WorkspaceId,
        Guid ApplicationId,
        Guid EnvironmentId,
        Guid OtherEnvironmentId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }
}
