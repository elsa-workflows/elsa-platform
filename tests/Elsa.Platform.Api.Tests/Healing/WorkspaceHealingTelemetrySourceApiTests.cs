using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.Api.Workspace.Healing;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.OpenTelemetry;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.Api.Tests.Healing;

public sealed class WorkspaceHealingTelemetrySourceApiTests
{
    [Fact]
    public async Task Authenticated_otlp_exception_is_durable_before_success_and_rejects_untrusted_scope()
    {
        await using var app = await CreateApplicationAsync("healing-otlp-owner");
        var sourceResponse = await app.Owner.PostPlatformJsonAsync(
            SourcesUri(app, app.EnvironmentId),
            new { name = "Orders collector" });
        var source = await sourceResponse.Content.ReadFromJsonAsync<HealingTelemetrySourceCredentialResponse>();
        sourceResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var otlp = app.Factory.CreateClient();
        using var acceptedRequest = OtlpLogsRequest(
            source!.Token,
            app.ApplicationId,
            app.EnvironmentId,
            occurrenceId: "otlp-occurrence-1");
        var accepted = await otlp.SendAsync(acceptedRequest);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
            var item = await db.HealingSignalInboxItems.AsNoTracking().SingleAsync();
            item.WorkspaceId.Should().Be(app.WorkspaceId);
            item.ApplicationId.Should().Be(app.ApplicationId);
            item.EnvironmentId.Should().Be(app.EnvironmentId);
            item.IdempotencyKey.Should().Be("otlp-occurrence-1");
            item.Status.Should().Be(HealingInboxStatus.Pending);
        }

        using var invalidRequest = OtlpLogsRequest(
            "elsa_otlp_v1.00000000000000000000000000000001.invalid",
            app.ApplicationId,
            app.EnvironmentId,
            occurrenceId: "invalid-token");
        (await otlp.SendAsync(invalidRequest)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var forgedRequest = OtlpLogsRequest(
            source.Token,
            Guid.NewGuid(),
            app.EnvironmentId,
            occurrenceId: "forged-scope");
        (await otlp.SendAsync(forgedRequest)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verificationScope = app.Factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<HealingDbContext>();
        (await verificationDb.HealingSignalInboxItems.AsNoTracking().CountAsync()).Should().Be(1);
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
        var sourceResponse = await app.Owner.PostPlatformJsonAsync(
            SourcesUri(app, app.EnvironmentId),
            new { name = "Failing collector" });
        var source = await sourceResponse.Content.ReadFromJsonAsync<HealingTelemetrySourceCredentialResponse>();

        using var request = OtlpLogsRequest(
            source!.Token,
            app.ApplicationId,
            app.EnvironmentId,
            occurrenceId: "must-not-ack");
        var response = await app.Factory.CreateClient().SendAsync(request);

        response.IsSuccessStatusCode.Should().BeFalse();
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        (await db.HealingSignalInboxItems.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Create_rotate_and_revoke_disclose_tokens_once_and_enforce_server_scope()
    {
        await using var app = await CreateApplicationAsync("healing-telemetry-source-owner");
        var sourceBaseUri = SourcesUri(app, app.EnvironmentId);

        var create = await app.Owner.PostPlatformJsonAsync(sourceBaseUri, new { name = "Orders production" });
        var created = await create.Content.ReadFromJsonAsync<HealingTelemetrySourceCredentialResponse>();
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Should().NotBeNull();
        created!.Token.Should().StartWith("elsa_otlp_v1.");
        created.HeaderName.Should().Be(HealingTelemetrySourceTokenService.HeaderName);

        var activeAuthentication = await AuthenticateAsync(app.Factory, created.Token);
        activeAuthentication.Accepted.Should().BeTrue();
        activeAuthentication.Context.Claims[HealingTelemetryScopeClaims.WorkspaceId].Should().Be(app.WorkspaceId.ToString("D"));
        activeAuthentication.Context.Claims[HealingTelemetryScopeClaims.ApplicationId].Should().Be(app.ApplicationId.ToString("D"));
        activeAuthentication.Context.Claims[HealingTelemetryScopeClaims.EnvironmentId].Should().Be(app.EnvironmentId.ToString("D"));

        var list = await app.Owner.GetAsync(sourceBaseUri);
        var listJson = await list.Content.ReadAsStringAsync();
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        listJson.Should().NotContain(created.Token);
        listJson.ToLowerInvariant().Should().NotContain("credentialhash").And.NotContain("credentialsalt");
        JsonDocument.Parse(listJson).RootElement.GetArrayLength().Should().Be(1);

        var crossEnvironmentRotate = await app.Owner.PostAsync(
            $"{SourcesUri(app, app.OtherEnvironmentId)}/{created.Source.Id:D}/rotate", null);
        crossEnvironmentRotate.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var rotate = await app.Owner.PostAsync($"{sourceBaseUri}/{created.Source.Id:D}/rotate", null);
        var rotated = await rotate.Content.ReadFromJsonAsync<HealingTelemetrySourceCredentialResponse>();
        rotate.StatusCode.Should().Be(HttpStatusCode.OK);
        rotated.Should().NotBeNull();
        rotated!.Token.Should().NotBe(created.Token);
        rotated.Source.CredentialVersion.Should().Be(2);
        (await AuthenticateAsync(app.Factory, created.Token)).Accepted.Should().BeFalse();
        (await AuthenticateAsync(app.Factory, rotated.Token)).Accepted.Should().BeTrue();

        var revoke = await app.Owner.PostAsync($"{sourceBaseUri}/{created.Source.Id:D}/revoke", null);
        var revoked = await revoke.Content.ReadFromJsonAsync<HealingTelemetrySourceResponse>();
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        revoked!.Status.Should().Be("Revoked");
        (await AuthenticateAsync(app.Factory, rotated.Token)).Accepted.Should().BeFalse();

        var afterLifecycle = await app.Owner.GetStringAsync(sourceBaseUri);
        afterLifecycle.Should().NotContain(created.Token).And.NotContain(rotated.Token);
        afterLifecycle.ToLowerInvariant().Should().NotContain("credentialhash").And.NotContain("credentialsalt");

        await using var auditScope = app.Factory.Services.CreateAsyncScope();
        var db = auditScope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var auditEvents = await db.Set<HealingAuditEvent>().AsNoTracking()
            .Where(x => x.AggregateId == created.Source.Id)
            .OrderBy(x => x.Sequence)
            .ToListAsync();
        auditEvents.Select(x => x.EventType).Should().Equal(
            "telemetry-source-created", "telemetry-source-rotated", "telemetry-source-revoked");
        string.Join(' ', auditEvents.Select(x => x.SafeDetailJson)).Should()
            .NotContain(created.Token).And.NotContain(rotated.Token);
    }

    private static async Task<OtlpRequestAuthenticationResult> AuthenticateAsync(
        PlatformApiTestApplication application,
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
        var factory = new PlatformApiTestApplication(configureServices: configureServices);
        await factory.SeedAsync(_ => Task.CompletedTask);
        await factory.SeedHealingAsync();
        var owner = factory.CreateTrustedWorkspaceClient(ownerSubject);
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var applicationResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Orders API", null));
        applicationResponse.EnsureSuccessStatusCode();
        var application = await applicationResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentApplication>();
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
        var response = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/applications/{applicationId:D}/environments",
            new WorkspaceDeploymentEnvironmentRequest(name, EnvironmentTier.Production));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadPlatformJsonAsync<WorkspaceDeploymentEnvironment>())!;
    }

    private static string SourcesUri(TestApplication app, Guid environmentId) =>
        $"/api/workspaces/{app.WorkspaceId:D}/healing/applications/{app.ApplicationId:D}/environments/{environmentId:D}/opentelemetry/sources";

    private static HttpRequestMessage OtlpLogsRequest(
        string token,
        Guid applicationId,
        Guid environmentId,
        string occurrenceId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/elsa/otlp/v1/logs")
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
        PlatformApiTestApplication Factory,
        HttpClient Owner,
        Guid WorkspaceId,
        Guid ApplicationId,
        Guid EnvironmentId,
        Guid OtherEnvironmentId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }
}
