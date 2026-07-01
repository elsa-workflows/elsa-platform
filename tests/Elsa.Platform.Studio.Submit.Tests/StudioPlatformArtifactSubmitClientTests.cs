using System.Net;
using System.Text.Json;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Studio.Submit;
using FluentAssertions;

namespace Elsa.Platform.Studio.Submit.Tests;

public sealed class StudioPlatformArtifactSubmitClientTests
{
    private readonly StudioWorkflowSnapshotPackager _packager = new();
    private readonly StudioSubmitOptions _options = new()
    {
        PlatformEndpoint = new Uri("https://platform.example.test"),
        WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001")
    };

    [Fact]
    public async Task Posts_artifact_registration_to_workspace_endpoint()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, """
            {
              "id": "20000000-0000-0000-0000-000000000001",
              "artifactId": "elsa.loom.recipe:payment-retry:abc",
              "contentDigest": { "algorithm": "sha256", "value": "abc" },
              "registeredAt": "2026-05-29T08:00:00Z"
            }
            """);
        var client = new StudioPlatformArtifactSubmitClient(new HttpClient(handler));

        var result = await client.SubmitAsync(Package(), _options);

        result.Status.Should().Be(StudioSubmitStatus.Submitted);
        result.ArtifactId.Should().Be("elsa.loom.recipe:payment-retry:abc");
        result.ArtifactDigest.Should().Be("sha256:abc");
        handler.RequestUri.Should().Be(new Uri("https://platform.example.test/api/workspaces/10000000-0000-0000-0000-000000000001/artifacts"));
        using var document = JsonDocument.Parse(handler.RequestBody!);
        document.RootElement.GetProperty("artifactTypeId").GetString().Should().Be("elsa.loom.recipe");
        document.RootElement.GetProperty("format").GetString().Should().Be("Unknown");
        document.RootElement.GetProperty("manifest").GetProperty("environment").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("payloadReference").GetProperty("provider").GetString().Should().Be("producer-managed");
        document.RootElement.GetProperty("displayMetadata").GetProperty("source").GetString().Should().Be("studio://workflows/payment-retry");
        handler.RequestBody.Should().NotContain("WorkflowDefinitionJson");
        handler.RequestBody.Should().NotContain("PaymentRetry");
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, StudioSubmitStatus.Conflict)]
    [InlineData(HttpStatusCode.BadRequest, StudioSubmitStatus.ValidationFailed)]
    [InlineData(HttpStatusCode.Unauthorized, StudioSubmitStatus.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError, StudioSubmitStatus.RetryableError)]
    public async Task Maps_platform_responses_to_safe_submit_states(HttpStatusCode statusCode, StudioSubmitStatus expectedStatus)
    {
        var handler = new RecordingHandler(statusCode, """{"title":"Bearer token rejected"}""");
        var client = new StudioPlatformArtifactSubmitClient(new HttpClient(handler));

        var result = await client.SubmitAsync(Package(), _options);

        result.Status.Should().Be(expectedStatus);
        result.Message.Should().NotContain("Bearer");
        result.Message.Should().Contain("[redacted]");
    }

    [Fact]
    public async Task Treats_duplicate_platform_response_as_success()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """
            {
              "id": "20000000-0000-0000-0000-000000000001",
              "artifactId": "elsa.loom.recipe:payment-retry:abc",
              "contentDigest": { "algorithm": "sha256", "value": "abc" },
              "registeredAt": "2026-05-29T08:00:00Z"
            }
            """);
        var client = new StudioPlatformArtifactSubmitClient(new HttpClient(handler));

        var result = await client.SubmitAsync(Package(), _options);

        result.Status.Should().Be(StudioSubmitStatus.Duplicate);
        result.Succeeded.Should().BeTrue();
        result.Message.Should().Be("Artifact already exists in Platform.");
    }

    [Fact]
    public async Task Maps_malformed_success_response_to_retryable_state()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, "not-json", "text/plain");
        var client = new StudioPlatformArtifactSubmitClient(new HttpClient(handler));

        var result = await client.SubmitAsync(Package(), _options);

        result.Status.Should().Be(StudioSubmitStatus.RetryableError);
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Be("Platform submission response could not be read.");
    }

    [Fact]
    public async Task Maps_non_json_error_response_to_safe_state()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, "upstream unavailable", "text/plain");
        var client = new StudioPlatformArtifactSubmitClient(new HttpClient(handler));

        var result = await client.SubmitAsync(Package(), _options);

        result.Status.Should().Be(StudioSubmitStatus.RetryableError);
        result.Message.Should().Be("Service Unavailable");
    }

    private StudioSubmitPackage Package() =>
        _packager.Package(
            new WorkflowSubmissionSnapshot(
                "payment-retry",
                "v42",
                "Payment Retry",
                "42",
                "Retries payment collection failures.",
                """{"id":"payment-retry","name":"PaymentRetry","version":42}""",
                ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion,
                "studio://workflows/payment-retry",
                new Dictionary<string, string>(),
                new Dictionary<string, string>()),
            _options);

    private sealed class RecordingHandler(HttpStatusCode statusCode, string content, string contentType = "application/json") : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, contentType)
            };
        }
    }
}
