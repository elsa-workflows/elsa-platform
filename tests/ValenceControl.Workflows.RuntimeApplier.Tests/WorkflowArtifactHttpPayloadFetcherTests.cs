using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ValenceControl.Deployment.Artifacts;
using ValenceControl.Workflows.RuntimeApplier;

namespace ValenceControl.Workflows.RuntimeApplier.Tests;

public sealed class WorkflowArtifactHttpPayloadFetcherTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-29T12:00:00Z");
    private readonly WorkflowArtifactRuntimeOptions _options = new()
    {
        ControlEndpoint = new Uri("https://control.example.test"),
        WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        EngineId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
        WorkerId = "worker-a",
        MaxPayloadBytes = 64,
        AllowedPayloadHosts = ["payloads.example.test"]
    };

    [Fact]
    public async Task Fetches_http_payload_with_media_type_and_accept_header()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"id":"payment-retry"}""", "application/vnd.elsa.workflow-definition+json");
        var fetcher = Fetcher(handler);
        var reference = Reference(
            "https://payloads.example.test/workflows/payment-retry?token=secret",
            mediaType: "application/vnd.elsa.workflow-definition+json",
            sizeBytes: 22,
            expiresAt: Now.AddMinutes(5));

        var payload = await fetcher.FetchAsync(reference);

        Assert.Equal(reference, payload.Reference);
        Assert.Equal("""{"id":"payment-retry"}""", Encoding.UTF8.GetString(payload.Content));
        Assert.Equal("application/vnd.elsa.workflow-definition+json", payload.MediaType);
        Assert.Equal(reference.Uri, handler.RequestUri);
        Assert.Equal("application/vnd.elsa.workflow-definition+json", handler.Accept);
    }

    [Fact]
    public async Task Fetches_response_media_type_when_declared_media_type_is_whitespace()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}", "application/vnd.elsa.workflow-definition+json");
        var fetcher = Fetcher(handler);

        var payload = await fetcher.FetchAsync(Reference(
            "https://payloads.example.test/workflows/payment-retry",
            mediaType: "   "));

        Assert.Equal("application/vnd.elsa.workflow-definition+json", payload.MediaType);
        Assert.Equal(string.Empty, handler.Accept);
    }

    [Fact]
    public async Task Rejects_expired_payload_reference_without_requesting_remote_payload()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = Fetcher(handler);

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry", expiresAt: Now));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload reference has expired.", exception.Message);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Rejects_unsupported_payload_reference_scheme()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = Fetcher(handler);

        var act = () => fetcher.FetchAsync(Reference("studio://workflows/payment-retry/snapshots/42"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload reference scheme is not supported by this runtime.", exception.Message);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Rejects_invalid_payload_reference_uri()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = Fetcher(handler);

        var act = () => fetcher.FetchAsync(Reference("not a uri"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload reference URI is invalid.", exception.Message);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Rejects_payload_reference_declared_larger_than_limit()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = Fetcher(handler);

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry", sizeBytes: 65));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload reference exceeds the configured runtime size limit.", exception.Message);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Rejects_negative_payload_reference_size_without_requesting_remote_payload()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = Fetcher(handler);

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry", sizeBytes: -1));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload reference size is invalid.", exception.Message);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Rejects_invalid_payload_media_type_without_requesting_remote_payload()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = Fetcher(handler);

        var act = () => fetcher.FetchAsync(Reference(
            "https://payloads.example.test/workflows/payment-retry",
            mediaType: "Bearer token"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload media type is invalid.", exception.Message);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Rejects_media_type_lists_without_requesting_remote_payload()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = Fetcher(handler);

        var act = () => fetcher.FetchAsync(Reference(
            "https://payloads.example.test/workflows/payment-retry",
            mediaType: "application/json, */*"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload media type is invalid.", exception.Message);
        Assert.Null(handler.RequestUri);
    }

    [Theory]
    [InlineData("*/*")]
    [InlineData("application/*")]
    public async Task Rejects_media_type_ranges_without_requesting_remote_payload(string mediaType)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = Fetcher(handler);

        var act = () => fetcher.FetchAsync(Reference(
            "https://payloads.example.test/workflows/payment-retry",
            mediaType: mediaType));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload media type is invalid.", exception.Message);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Rejects_payload_content_length_larger_than_limit()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, new string('x', 65));
        var fetcher = Fetcher(handler);

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload exceeds the configured runtime size limit.", exception.Message);
    }

    [Fact]
    public async Task Rejects_payload_stream_larger_than_limit_without_content_length()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, new string('x', 65), includeContentLength: false);
        var fetcher = Fetcher(handler);

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload exceeds the configured runtime size limit.", exception.Message);
    }

    [Fact]
    public async Task Rejects_unsuccessful_response_without_exposing_reference_uri()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, """{"token":"secret"}""");
        var fetcher = Fetcher(handler);
        var reference = Reference("https://payloads.example.test/workflows/payment-retry?token=secret");

        var act = () => fetcher.FetchAsync(reference);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal("Workflow artifact payload request failed with status 404.", exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
        Assert.DoesNotContain(reference.Uri, exception.Message);
    }

    [Fact]
    public async Task Rejects_transport_failures_without_exposing_remote_details()
    {
        var handler = new ThrowingRequestHandler(new HttpRequestException("payloads.example.test token=secret"));
        var fetcher = Fetcher(handler);

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry?token=secret"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal("Workflow artifact payload request failed.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("payloads.example.test", exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
        Assert.DoesNotContain("payloads.example.test", exception.ToString());
        Assert.DoesNotContain("secret", exception.ToString());
    }

    [Fact]
    public async Task Rejects_payload_stream_failures_without_exposing_remote_details()
    {
        var handler = new ThrowingContentHandler();
        var fetcher = Fetcher(handler);

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry?token=secret"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal("Workflow artifact payload request failed.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("payloads.example.test", exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
        Assert.DoesNotContain("payloads.example.test", exception.ToString());
        Assert.DoesNotContain("secret", exception.ToString());
    }

    [Fact]
    public async Task Rejects_payload_requests_that_exceed_runtime_timeout()
    {
        var handler = new HangingHandler();
        var fetcher = Fetcher(handler, _options with { PayloadRequestTimeout = TimeSpan.FromMilliseconds(10) });

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal("Workflow artifact payload request timed out.", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task Rejects_unapproved_payload_reference_provider_without_requesting_remote_payload()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = Fetcher(handler);

        var act = () => fetcher.FetchAsync(new ArtifactPayloadReference(
            "untrusted-provider",
            "https://payloads.example.test/workflows/payment-retry"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload reference provider is not approved by this runtime.", exception.Message);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Rejects_when_no_payload_reference_providers_are_approved()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = Fetcher(handler, _options with { AllowedPayloadReferenceProviders = [] });

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload reference provider is not approved by this runtime.", exception.Message);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Rejects_unapproved_payload_host_without_requesting_remote_payload()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = Fetcher(handler);

        var act = () => fetcher.FetchAsync(Reference("https://unapproved.example.test/workflows/payment-retry"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload host is not approved by this runtime.", exception.Message);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Rejects_when_no_payload_hosts_are_approved()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = Fetcher(handler, _options with { AllowedPayloadHosts = [] });

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload host is not approved by this runtime.", exception.Message);
        Assert.Null(handler.RequestUri);
    }

    [Theory]
    [InlineData("http://127.0.0.1/workflows/payment-retry")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://10.0.0.1/workflows/payment-retry")]
    [InlineData("http://100.64.0.1/workflows/payment-retry")]
    [InlineData("http://192.0.2.1/workflows/payment-retry")]
    [InlineData("http://198.18.0.1/workflows/payment-retry")]
    [InlineData("http://198.51.100.1/workflows/payment-retry")]
    [InlineData("http://203.0.113.1/workflows/payment-retry")]
    [InlineData("http://[fd00::1]/workflows/payment-retry")]
    [InlineData("http://[::ffff:127.0.0.1]/workflows/payment-retry")]
    [InlineData("http://[::127.0.0.1]/workflows/payment-retry")]
    public async Task Rejects_payload_hosts_that_resolve_to_non_public_addresses(string uri)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var options = _options with { AllowedPayloadHosts = [new Uri(uri).Host] };
        var fetcher = Fetcher(handler, options);

        var act = () => fetcher.FetchAsync(Reference(uri));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload host resolves to a non-public address.", exception.Message);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Rejects_dns_resolution_to_non_public_address_without_requesting_remote_payload()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = Fetcher(handler, resolver: new StaticHostResolver(IPAddress.Parse("192.168.1.25")));

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload host resolves to a non-public address.", exception.Message);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Rejects_hosts_that_cannot_be_resolved_without_requesting_remote_payload()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = Fetcher(handler, resolver: new StaticHostResolver());

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload host could not be resolved.", exception.Message);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Rejects_dns_resolution_errors_without_exposing_payload_host()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = Fetcher(handler, resolver: new ThrowingHostResolver());

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal("Workflow artifact payload host could not be resolved.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("payloads.example.test", exception.Message);
        Assert.DoesNotContain("payloads.example.test", exception.ToString());
        Assert.DoesNotContain("secret", exception.ToString());
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Rejects_payload_redirects_without_following_remote_location()
    {
        var handler = new RecordingHandler(HttpStatusCode.Redirect, "", location: "http://127.0.0.1/workflows/payment-retry");
        var fetcher = Fetcher(handler);

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload redirects are not supported.", exception.Message);
        Assert.Equal("https://payloads.example.test/workflows/payment-retry", handler.RequestUri);
    }

    [Fact]
    public async Task Rejects_successful_payload_response_with_changed_request_uri()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            "{}",
            responseRequestUri: "http://127.0.0.1/workflows/payment-retry");
        var fetcher = Fetcher(handler);

        var act = () => fetcher.FetchAsync(Reference("https://payloads.example.test/workflows/payment-retry"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Workflow artifact payload redirects are not supported.", exception.Message);
        Assert.Equal("https://payloads.example.test/workflows/payment-retry", handler.RequestUri);
    }

    [Fact]
    public void Dispose_does_not_dispose_injected_http_transport()
    {
        var handler = new DisposableRecordingHandler(HttpStatusCode.OK, "{}");
        var fetcher = Fetcher(handler);

        fetcher.Dispose();

        Assert.False(handler.IsDisposed);
    }

    [Fact]
    public async Task Connect_callback_connects_to_validated_address()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var acceptConnection = listener.AcceptTcpClientAsync();
            await using var stream = await WorkflowArtifactHttpPayloadFetcher.ConnectToValidatedAddressAsync(
                new DnsEndPoint("payloads.example.test", endpoint.Port),
                IPAddress.Loopback,
                CancellationToken.None);
            using var connection = await acceptConnection.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(stream.CanWrite);
            Assert.NotNull(connection.Client.RemoteEndPoint);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Default_http_handler_disables_proxy_usage()
    {
        using var handler = WorkflowArtifactHttpPayloadFetcher.CreateDefaultHttpHandler();

        Assert.False(handler.UseProxy);
        Assert.False(handler.AllowAutoRedirect);
    }

    private WorkflowArtifactHttpPayloadFetcher Fetcher(
        HttpMessageHandler handler,
        WorkflowArtifactRuntimeOptions? options = null,
        IWorkflowArtifactPayloadHostResolver? resolver = null) =>
        new(
            options ?? _options,
            new StaticTimeProvider(Now),
            resolver ?? new StaticHostResolver(IPAddress.Parse("93.184.216.34")),
            new HttpMessageInvoker(handler));

    private static ArtifactPayloadReference Reference(
        string uri,
        string? mediaType = null,
        long? sizeBytes = null,
        DateTimeOffset? expiresAt = null) =>
        new("producer-managed", uri, mediaType, sizeBytes, null, expiresAt);

    private class RecordingHandler(
        HttpStatusCode statusCode,
        string content,
        string mediaType = "application/json",
        bool includeContentLength = true,
        string? location = null,
        string? responseRequestUri = null) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }

        public string? Accept { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.AbsoluteUri;
            Accept = request.Headers.Accept.ToString();
            var response = new HttpResponseMessage(statusCode)
            {
                Content = includeContentLength
                    ? new StringContent(content, Encoding.UTF8, mediaType)
                    : new StreamingContent(content, mediaType),
                RequestMessage = responseRequestUri is null
                    ? request
                    : new HttpRequestMessage(HttpMethod.Get, responseRequestUri)
            };
            if (location is not null)
                response.Headers.Location = new Uri(location);

            return Task.FromResult(response);
        }
    }

    private sealed class DisposableRecordingHandler(
        HttpStatusCode statusCode,
        string content,
        string mediaType = "application/json") : RecordingHandler(statusCode, content, mediaType)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingRequestHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class ThrowingContentHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ThrowingReadContent(),
                RequestMessage = request
            });
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }
    }

    private sealed class StreamingContent : HttpContent
    {
        private readonly string _content;

        public StreamingContent(string content, string mediaType)
        {
            _content = content;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var bytes = Encoding.UTF8.GetBytes(_content);
            return stream.WriteAsync(bytes).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class ThrowingReadContent : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new ThrowingReadStream());

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new IOException("payloads.example.test token=secret");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class ThrowingReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("payloads.example.test token=secret"));
    }

    private sealed class StaticTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StaticHostResolver(params IPAddress[] addresses) : IWorkflowArtifactPayloadHostResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IPAddress>>(addresses);
    }

    private sealed class ThrowingHostResolver : IWorkflowArtifactPayloadHostResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken = default) =>
            throw new ArgumentException("payloads.example.test token=secret");
    }
}
