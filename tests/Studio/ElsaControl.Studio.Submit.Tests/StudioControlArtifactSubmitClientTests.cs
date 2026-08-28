using System.Net;
using System.Text.Json;
using ElsaControl.Deployment.Artifacts;
using ElsaControl.Studio.Submit;

namespace ElsaControl.Studio.Submit.Tests;

public sealed class StudioControlArtifactSubmitClientTests
{
    private readonly StudioWorkflowSnapshotPackager _packager = new();
    private readonly StudioSubmitOptions _options = new()
    {
        ControlEndpoint = new Uri("https://control.example.test"),
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
        var client = new StudioControlArtifactSubmitClient(new HttpClient(handler));

        var result = await client.SubmitAsync(Package(), _options);

        Assert.Equal(StudioSubmitStatus.Submitted, result.Status);
        Assert.Equal("elsa.loom.recipe:payment-retry:abc", result.ArtifactId);
        Assert.Equal("sha256:abc", result.ArtifactDigest);
        Assert.Equal(new Uri("https://control.example.test/api/workspaces/10000000-0000-0000-0000-000000000001/artifacts"), handler.RequestUri);
        using var document = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("elsa.loom.recipe", document.RootElement.GetProperty("artifactTypeId").GetString());
        Assert.Equal("Unknown", document.RootElement.GetProperty("format").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("manifest").GetProperty("environment").ValueKind);
        Assert.Equal("producer-managed", document.RootElement.GetProperty("payloadReference").GetProperty("provider").GetString());
        Assert.Equal("studio://workflows/payment-retry", document.RootElement.GetProperty("displayMetadata").GetProperty("source").GetString());
        Assert.DoesNotContain("WorkflowDefinitionJson", handler.RequestBody);
        Assert.DoesNotContain("PaymentRetry", handler.RequestBody);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, StudioSubmitStatus.Conflict)]
    [InlineData(HttpStatusCode.BadRequest, StudioSubmitStatus.ValidationFailed)]
    [InlineData(HttpStatusCode.Unauthorized, StudioSubmitStatus.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError, StudioSubmitStatus.RetryableError)]
    public async Task Maps_control_responses_to_safe_submit_states(HttpStatusCode statusCode, StudioSubmitStatus expectedStatus)
    {
        var handler = new RecordingHandler(statusCode, """{"title":"Bearer token rejected"}""");
        var client = new StudioControlArtifactSubmitClient(new HttpClient(handler));

        var result = await client.SubmitAsync(Package(), _options);

        Assert.Equal(expectedStatus, result.Status);
        Assert.DoesNotContain("Bearer", result.Message);
        Assert.Contains("[redacted]", result.Message);
    }

    [Fact]
    public async Task Treats_duplicate_control_response_as_success()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """
            {
              "id": "20000000-0000-0000-0000-000000000001",
              "artifactId": "elsa.loom.recipe:payment-retry:abc",
              "contentDigest": { "algorithm": "sha256", "value": "abc" },
              "registeredAt": "2026-05-29T08:00:00Z"
            }
            """);
        var client = new StudioControlArtifactSubmitClient(new HttpClient(handler));

        var result = await client.SubmitAsync(Package(), _options);

        Assert.Equal(StudioSubmitStatus.Duplicate, result.Status);
        Assert.True(result.Succeeded);
        Assert.Equal("Artifact already exists in Control.", result.Message);
    }

    [Fact]
    public async Task Maps_malformed_success_response_to_retryable_state()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, "not-json", "text/plain");
        var client = new StudioControlArtifactSubmitClient(new HttpClient(handler));

        var result = await client.SubmitAsync(Package(), _options);

        Assert.Equal(StudioSubmitStatus.RetryableError, result.Status);
        Assert.False(result.Succeeded);
        Assert.Equal("Control submission response could not be read.", result.Message);
    }

    [Fact]
    public async Task Maps_non_json_error_response_to_safe_state()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, "upstream unavailable", "text/plain");
        var client = new StudioControlArtifactSubmitClient(new HttpClient(handler));

        var result = await client.SubmitAsync(Package(), _options);

        Assert.Equal(StudioSubmitStatus.RetryableError, result.Status);
        Assert.Equal("Service Unavailable", result.Message);
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
