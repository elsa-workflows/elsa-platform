using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Extensions;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Elsa.Diagnostics.OpenTelemetry.Services;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.OpenTelemetry;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ValenceControl.Healing.OpenTelemetry.Tests;

public sealed class FoundationPackageIntegrationTests
{
    private const string PublishedPackageVersion = "4.0.0-preview.167";
    private static readonly Guid WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ApplicationId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid EnvironmentId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset AcceptedAt = DateTimeOffset.Parse("2026-07-16T12:00:00Z");

    [Fact]
    public async Task Published_foundation_otlp_handler_redacts_before_durable_healing_append_and_acknowledgement()
    {
        AssertPublishedPackage(typeof(OpenTelemetryIngestor).Assembly);
        AssertPublishedPackage(typeof(IOpenTelemetryIngestionContributor).Assembly);
        var appender = new BlockingDurableInboxAppender();
        await using var services = CreateServices(appender);
        await using var scope = services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<OtlpHttpIngestionHandler>();
        var context = CreateOtlpContext(OtlpExceptionLogPayload());

        var handling = handler.HandleAsync(context, OtlpSignal.Logs);
        var contributedItem = await appender.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        handling.IsCompleted.Should().BeFalse("the Foundation handler must await the durable contributor before acknowledging OTLP");
        contributedItem.RedactedEnvelopeJson.Should().NotContain("must-never-enter-healing");
        var contributedSignal = JsonSerializer.Deserialize<HealingSignal>(contributedItem.RedactedEnvelopeJson);
        contributedSignal.Should().NotBeNull();
        contributedSignal!.Exception.Message.Should().Be("Order lookup failed [Redacted]");
        contributedSignal.Evidence.IsRedacted.Should().BeTrue();

        appender.Commit.TrySetResult();
        await handling.WaitAsync(TimeSpan.FromSeconds(5));

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        appender.DurableItems.Should().ContainSingle().Which.Should().BeSameAs(contributedItem);
        contributedItem.WorkspaceId.Should().Be(WorkspaceId);
        contributedItem.ApplicationId.Should().Be(ApplicationId);
        contributedItem.EnvironmentId.Should().Be(EnvironmentId);
        contributedItem.IdempotencyKey.Should().Be("package-integration-occurrence");
        contributedItem.Status.Should().Be(HealingInboxStatus.Pending);
    }

    [Fact]
    public async Task Durable_inbox_failure_prevents_the_published_foundation_handler_from_acknowledging()
    {
        var appender = new BlockingDurableInboxAppender();
        await using var services = CreateServices(appender);
        await using var scope = services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<OtlpHttpIngestionHandler>();
        var context = CreateOtlpContext(OtlpExceptionLogPayload());

        var handling = handler.HandleAsync(context, OtlpSignal.Logs);
        await appender.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        appender.Commit.TrySetException(new InvalidOperationException("durable inbox unavailable"));

        var act = () => handling;

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("durable inbox unavailable");
        appender.DurableItems.Should().BeEmpty();
    }

    private static ServiceProvider CreateServices(IHealingSignalInboxAppender appender)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(AcceptedAt));
        services.AddSingleton(appender);
        services.AddSingleton<IHealingSignalInboxAppender>(appender);
        services.AddSingleton<IHealingTelemetryScopeResolver, AuthenticatedClaimHealingTelemetryScopeResolver>();
        services.AddScoped<IOtlpRequestAuthenticator, TrustedPackageTestAuthenticator>();
        services.AddOpenTelemetryDiagnosticsServices();
        services.AddOpenTelemetryIngestionContributor<HealingOpenTelemetryIngestionContributor>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static DefaultHttpContext CreateOtlpContext(byte[] payload)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/x-protobuf";
        context.Request.ContentLength = payload.Length;
        context.Request.Body = new MemoryStream(payload);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static void AssertPublishedPackage(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        version.Should().StartWith(
            $"{PublishedPackageVersion}+",
            $"T111 must execute the published {PublishedPackageVersion} package rather than a local Foundation artifact");
    }

    private static byte[] OtlpExceptionLogPayload()
    {
        var resource = Join(
            Message(1, KeyValue("service.name", "orders-api")),
            Message(1, KeyValue("service.instance.id", "orders-prod-1")));
        var log = Join(
            Varint(1, (ulong)(AcceptedAt - DateTimeOffset.UnixEpoch).Ticks * 100),
            Varint(2, 17),
            String(3, "Error"),
            Message(5, AnyString("Order lookup failed")),
            Message(6, KeyValue(HealingSignalAttributes.ProfileVersion, HealingContractVersions.SignalProfile)),
            Message(6, KeyValue(HealingSignalAttributes.ApplicationId, ApplicationId.ToString("D"))),
            Message(6, KeyValue(HealingSignalAttributes.EnvironmentId, EnvironmentId.ToString("D"))),
            Message(6, KeyValue(HealingSignalAttributes.OperationName, "GET /orders/{id}")),
            Message(6, KeyValue(HealingSignalAttributes.OccurrenceId, "package-integration-occurrence")),
            Message(6, KeyValue("exception.type", "System.InvalidOperationException")),
            Message(6, KeyValue("exception.message", "Order lookup failed token=must-never-enter-healing")),
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

    private sealed class TrustedPackageTestAuthenticator : IOtlpRequestAuthenticator
    {
        public ValueTask<OtlpRequestAuthenticationResult> AuthenticateAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OtlpRequestAuthenticationResult.Accept(
                OpenTelemetryIngestionContext.Authenticated(
                    "published-package-integration-test",
                    new Dictionary<string, string>
                    {
                        [HealingTelemetryScopeClaims.WorkspaceId] = WorkspaceId.ToString("D"),
                        [HealingTelemetryScopeClaims.ApplicationId] = ApplicationId.ToString("D"),
                        [HealingTelemetryScopeClaims.EnvironmentId] = EnvironmentId.ToString("D")
                    })));
    }

    private sealed class BlockingDurableInboxAppender : IHealingSignalInboxAppender
    {
        public TaskCompletionSource<HealingSignalInboxItem> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Commit { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<HealingSignalInboxItem> DurableItems { get; } = [];

        public async ValueTask AppendAsync(
            HealingSignalInboxItem item,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(item);
            await Commit.Task.WaitAsync(cancellationToken);
            DurableItems.Add(item);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
