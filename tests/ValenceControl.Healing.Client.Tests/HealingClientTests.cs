using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Client;
using Microsoft.Extensions.Options;

namespace ValenceControl.Healing.Client.Tests;

public sealed class HealingClientTests
{
    [Fact]
    public void Enrichment_emits_only_the_versioned_signal_profile_attributes()
    {
        var applicationId = Guid.NewGuid();
        var environmentId = Guid.NewGuid();
        using var activity = new Activity("orders.load").Start();
        var context = new HealingTelemetryContext(
            applicationId,
            environmentId,
            "orders.load",
            HealingFailureClasses.UnhandledRequest,
            SourceRevision: new string('a', 40),
            ComponentKey: "package:Acme.Orders");

        activity.EnrichForHealing(context);

        Assert.Equal(HealingContractVersions.SignalProfile, activity.GetTagItem(HealingSignalAttributes.ProfileVersion));
        Assert.Equal(applicationId.ToString("D"), activity.GetTagItem(HealingSignalAttributes.ApplicationId));
        Assert.Equal(environmentId.ToString("D"), activity.GetTagItem(HealingSignalAttributes.EnvironmentId));
        Assert.Equal("orders.load", activity.GetTagItem(HealingSignalAttributes.OperationName));
        Assert.Equal("package:Acme.Orders", activity.GetTagItem(HealingSignalAttributes.ComponentKey));
        Assert.DoesNotContain(activity.TagObjects.Select(x => x.Key), x =>
            x.Contains("repository", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("workflow", StringComparison.OrdinalIgnoreCase) && x != HealingSignalAttributes.WorkflowDefinitionId ||
            x.Contains("credential", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Explicit_reporting_uses_the_configured_scope_and_idempotency_key()
    {
        var options = Microsoft.Extensions.Options.Options.Create(ClientOptions());
        var accepted = new ExplicitHealingIncidentAcceptedResponse(Guid.NewGuid(), false);
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent(JsonSerializer.Serialize(accepted, JsonOptions), Encoding.UTF8, "application/json")
        });
        var client = new HealingClient(new HttpClient(handler), options);

        var result = await client.ReportIncidentAsync(Request(), "occurrence-42");

        Assert.Equal(accepted, result);
        Assert.Equal(
            $"/api/workspaces/{options.Value.WorkspaceId:D}/healing/applications/{options.Value.ApplicationId:D}/environments/{options.Value.EnvironmentId:D}/incidents",
            handler.Path);
        Assert.Equal("occurrence-42", handler.IdempotencyKey);
        Assert.Contains("\"profileVersion\":\"1.0\"", handler.Body);
        Assert.False(handler.Body!.Contains("repository", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Explicit_reporting_rejects_unredacted_evidence_before_transport()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Accepted));
        var client = new HealingClient(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(ClientOptions()));
        var request = Request() with { Evidence = new(false, false, []) };

        var act = () => client.ReportIncidentAsync(request).AsTask();

        await Assert.ThrowsAsync<ArgumentException>(act);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Provider_errors_expose_only_the_bounded_reason_code()
    {
        const string secret = "must-never-escape-client-response";
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { code = "healing.intake.denied", detail = secret }, JsonOptions),
                Encoding.UTF8,
                "application/problem+json")
        });
        var client = new HealingClient(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(ClientOptions()));

        var act = () => client.ReportIncidentAsync(Request()).AsTask();

        var exception = await Assert.ThrowsAsync<HealingClientException>(act);
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal("healing.intake.denied", exception.ReasonCode);
        Assert.DoesNotContain(secret, exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Chunked_responses_are_enforced_by_the_same_byte_limit(HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler(new HttpResponseMessage(statusCode)
        {
            Content = new UnknownLengthContent(new byte[20_000])
        });
        var client = new HealingClient(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(ClientOptions()));

        var act = () => client.ReportIncidentAsync(Request()).AsTask();

        var exception = await Assert.ThrowsAsync<HealingClientException>(act);
        Assert.Equal(statusCode, exception.StatusCode);
        Assert.Equal("healing.client.response-too-large", exception.ReasonCode);
    }

    private static ExplicitHealingIncidentRequest Request() => new(
        HealingContractVersions.SignalProfile,
        null,
        DateTimeOffset.UtcNow,
        "orders.load",
        HealingFailureClasses.ExplicitIncident,
        HealingRetryStates.None,
        new("System.InvalidOperationException", "Redacted failure", "at Acme.Orders.Load()", []),
        new(true, false, []),
        "occurrence-42",
        ServiceName: "acme-orders");

    private static HealingClientOptions ClientOptions() => new()
    {
        ControlBaseAddress = new Uri("https://control.test/"),
        WorkspaceId = Guid.NewGuid(),
        ApplicationId = Guid.NewGuid(),
        EnvironmentId = Guid.NewGuid()
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? Path { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Path = request.RequestUri?.AbsolutePath;
            IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }

    private sealed class UnknownLengthContent(byte[] content) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(content).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
