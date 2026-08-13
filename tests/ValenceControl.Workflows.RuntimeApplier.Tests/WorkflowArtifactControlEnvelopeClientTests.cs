using System.Net;
using ValenceControl.Deployment.Abstractions.Artifacts;
using ValenceControl.Deployment.Artifacts;
using ValenceControl.Workflows.RuntimeApplier;

namespace ValenceControl.Workflows.RuntimeApplier.Tests;

public sealed class WorkflowArtifactControlEnvelopeClientTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid EngineId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid ArtifactRecordId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly ArtifactDigest Digest = new("sha256", new string('a', 64));

    private readonly WorkflowArtifactRuntimeOptions _options = new()
    {
        ControlEndpoint = new Uri("https://control.example.test"),
        WorkspaceId = WorkspaceId,
        EngineId = EngineId,
        WorkerId = "worker-a",
        RuntimeVersion = "4.0.0"
    };

    [Fact]
    public async Task Fetches_artifact_detail_and_maps_to_envelope()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ArtifactJson());
        var client = new WorkflowArtifactControlEnvelopeClient(new HttpClient(handler), _options);

        var envelope = await client.GetEnvelopeAsync(CommandArtifact());

        Assert.Equal(new Uri($"https://control.example.test/api/workspaces/{WorkspaceId:D}/artifacts/{ArtifactRecordId:D}"), handler.RequestUri);
        Assert.Equal("elsa.workflow-definition:payment-retry", envelope.ArtifactId);
        Assert.Equal(ArtifactTypeIds.ElsaWorkflowDefinition, envelope.ArtifactTypeId);
        Assert.Equal(Digest, envelope.ContentDigest);
        Assert.Equal("producer-managed", envelope.PayloadReference.Provider);
        Assert.Equal(Digest, envelope.PayloadReference.ReferenceDigest);
        Assert.Single(envelope.CompatibilityHints, x => x.RequiredArtifactType == ArtifactTypeIds.ElsaWorkflowDefinition);
    }

    [Fact]
    public async Task Rejects_command_artifact_digest_mismatch()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ArtifactJson());
        var client = new WorkflowArtifactControlEnvelopeClient(new HttpClient(handler), _options);

        var act = () => client.GetEnvelopeAsync(CommandArtifact() with { ContentDigest = new ArtifactDigest("sha256", new string('b', 64)) });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Runtime command artifact digest does not match the artifact record.", exception.Message);
    }

    [Fact]
    public async Task Rejects_artifact_detail_without_an_artifact_type()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ArtifactJson(artifactTypeId: null));
        var client = new WorkflowArtifactControlEnvelopeClient(new HttpClient(handler), _options);

        var act = () => client.GetEnvelopeAsync(CommandArtifact());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Control artifact response does not include an artifact type.", exception.Message);
    }

    [Fact]
    public async Task Rejects_artifact_detail_from_different_workspace()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ArtifactJson(workspaceId: Guid.Parse("90000000-0000-0000-0000-000000000001")));
        var client = new WorkflowArtifactControlEnvelopeClient(new HttpClient(handler), _options);

        var act = () => client.GetEnvelopeAsync(CommandArtifact());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Equal("Control artifact response workspace does not match the configured runtime workspace.", exception.Message);
    }

    [Fact]
    public async Task Sanitizes_control_error_messages()
    {
        var handler = new RecordingHandler(HttpStatusCode.InternalServerError, """{"title":"Bearer token leaked"}""");
        var client = new WorkflowArtifactControlEnvelopeClient(new HttpClient(handler), _options);

        var act = () => client.GetEnvelopeAsync(CommandArtifact());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.Contains("[redacted]", exception.Message);
    }

    private static WorkflowRuntimeCommandArtifactReference CommandArtifact() =>
        new(ArtifactRecordId, "elsa.workflow-definition:payment-retry", ArtifactTypeIds.ElsaWorkflowDefinition, Digest);

    private static string ArtifactJson(Guid? workspaceId = null, string? artifactTypeId = "elsa.workflow-definition") =>
        $$"""
          {
            "id": "{{ArtifactRecordId:D}}",
            "workspaceId": "{{(workspaceId ?? WorkspaceId):D}}",
            "artifactId": "elsa.workflow-definition:payment-retry",
            "layoutVersion": "valence-control/deployment-artifact/v1alpha1",
            "contentDigest": { "algorithm": "{{Digest.Algorithm}}", "value": "{{Digest.Value}}" },
            "format": "Zip",
            "referenceProvider": "producer-managed",
            "reference": "https://payloads.example.test/workflows/payment-retry",
            "manifest": { "name": "Payment Retry", "version": "42", "environment": "studio://workflows/payment-retry" },
            "resources": [],
            "checksumStatus": "Unverified",
            "inspectionStatus": "NeverInspected",
            "diagnostics": [],
            "registeredAt": "2026-05-29T12:00:00Z",
            "registeredByAccountId": "40000000-0000-0000-0000-000000000001",
            "lastInspectedAt": null,
            "createdAt": "2026-05-29T12:00:00Z",
            "updatedAt": "2026-05-29T12:00:00Z",
            "envelopeVersion": "valence-control/artifact-envelope/v1alpha1",
            "artifactTypeId": {{(artifactTypeId is null ? "null" : $"\"{artifactTypeId}\"")}},
            "artifactSchemaVersion": "1.0",
            "manifestDigest": null,
            "payloadReference": {
              "provider": "producer-managed",
              "uri": "https://payloads.example.test/workflows/payment-retry",
              "mediaType": "application/vnd.elsa.workflow-definition+json",
              "sizeBytes": 42,
              "referenceDigest": { "algorithm": "{{Digest.Algorithm}}", "value": "{{Digest.Value}}" },
              "expiresAt": null
            },
            "producer": { "producerType": "studio", "producerName": "Elsa Studio", "producerVersion": "4.0.0", "sourceReference": "workflow:payment-retry" },
            "displayMetadata": {
              "name": "Payment Retry",
              "version": "42",
              "description": "Retries payment collection failures.",
              "labels": {},
              "annotations": {},
              "source": "studio://workflows/payment-retry"
            },
            "compatibilityHints": [
              {
                "requiredArtifactType": "elsa.workflow-definition",
                "runtimeFamily": "elsa-workflows",
                "runtimeVersionRange": ">=4.0.0",
                "requiredCapabilities": ["workflow-definition.apply"],
                "environmentConstraints": {}
              }
            ]
          }
          """;

    private sealed class RecordingHandler(HttpStatusCode statusCode, string content, string contentType = "application/json") : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, contentType)
            });
        }
    }
}
