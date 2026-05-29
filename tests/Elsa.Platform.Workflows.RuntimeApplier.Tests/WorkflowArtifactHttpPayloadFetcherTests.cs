using System.Net;
using System.Text;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Workflows.RuntimeApplier;
using FluentAssertions;

namespace Elsa.Platform.Workflows.RuntimeApplier.Tests;

public sealed class WorkflowArtifactHttpPayloadFetcherTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-29T12:00:00Z");
    private readonly WorkflowArtifactRuntimeOptions _options = new()
    {
        PlatformEndpoint = new Uri("https://platform.example.test"),
        WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        EngineId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
        WorkerId = "worker-a",
        MaxPayloadBytes = 64
    };

    [Fact]
    public async Task Fetches_http_payload_with_media_type_and_accept_header()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"id":"payment-retry"}""", "application/vnd.elsa.workflow-definition+json");
        var fetcher = new WorkflowArtifactHttpPayloadFetcher(new HttpClient(handler), _options, new StaticTimeProvider(Now));
        var reference = Reference(
            "https://payloads.example.test/workflows/payment-retry?token=secret",
            mediaType: "application/vnd.elsa.workflow-definition+json",
            sizeBytes: 22,
            expiresAt: Now.AddMinutes(5));

        var payload = await fetcher.FetchAsync(reference);

        payload.Reference.Should().Be(reference);
        Encoding.UTF8.GetString(payload.Content).Should().Be("""{"id":"payment-retry"}""");
        payload.MediaType.Should().Be("application/vnd.elsa.workflow-definition+json");
        handler.RequestUri.Should().Be(reference.Uri);
        handler.Accept.Should().Be("application/vnd.elsa.workflow-definition+json");
    }

    [Fact]
    public async Task Rejects_expired_payload_reference_without_requesting_remote_payload()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = new WorkflowArtifactHttpPayloadFetcher(new HttpClient(handler), _options, new StaticTimeProvider(Now));

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry", expiresAt: Now));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Workflow artifact payload reference has expired.");
        handler.RequestUri.Should().BeNull();
    }

    [Fact]
    public async Task Rejects_unsupported_payload_reference_scheme()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = new WorkflowArtifactHttpPayloadFetcher(new HttpClient(handler), _options, new StaticTimeProvider(Now));

        var act = () => fetcher.FetchAsync(Reference("studio://workflows/payment-retry/snapshots/42"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Workflow artifact payload reference scheme is not supported by this runtime.");
        handler.RequestUri.Should().BeNull();
    }

    [Fact]
    public async Task Rejects_payload_reference_declared_larger_than_limit()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = new WorkflowArtifactHttpPayloadFetcher(new HttpClient(handler), _options, new StaticTimeProvider(Now));

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry", sizeBytes: 65));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Workflow artifact payload reference exceeds the configured runtime size limit.");
        handler.RequestUri.Should().BeNull();
    }

    [Fact]
    public async Task Rejects_invalid_payload_media_type_without_requesting_remote_payload()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = new WorkflowArtifactHttpPayloadFetcher(new HttpClient(handler), _options, new StaticTimeProvider(Now));

        var act = () => fetcher.FetchAsync(Reference(
            "https://payloads.example.test/workflows/payment-retry",
            mediaType: "Bearer token"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Workflow artifact payload media type is invalid.");
        handler.RequestUri.Should().BeNull();
    }

    [Fact]
    public async Task Rejects_payload_stream_larger_than_limit()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, new string('x', 65));
        var fetcher = new WorkflowArtifactHttpPayloadFetcher(new HttpClient(handler), _options, new StaticTimeProvider(Now));

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Workflow artifact payload exceeds the configured runtime size limit.");
    }

    [Fact]
    public async Task Rejects_unsuccessful_response_without_exposing_reference_uri()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, """{"token":"secret"}""");
        var fetcher = new WorkflowArtifactHttpPayloadFetcher(new HttpClient(handler), _options, new StaticTimeProvider(Now));
        var reference = Reference("https://payloads.example.test/workflows/payment-retry?token=secret");

        var act = () => fetcher.FetchAsync(reference);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Workflow artifact payload request failed with status 404.");
        exception.Which.Message.Should().NotContain("secret");
        exception.Which.Message.Should().NotContain(reference.Uri);
    }

    private static ArtifactPayloadReference Reference(
        string uri,
        string? mediaType = null,
        long? sizeBytes = null,
        DateTimeOffset? expiresAt = null) =>
        new("producer-managed", uri, mediaType, sizeBytes, null, expiresAt);

    private sealed class RecordingHandler(HttpStatusCode statusCode, string content, string mediaType = "application/json") : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }

        public string? Accept { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.AbsoluteUri;
            Accept = request.Headers.Accept.ToString();
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, mediaType)
            });
        }
    }

    private sealed class StaticTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
