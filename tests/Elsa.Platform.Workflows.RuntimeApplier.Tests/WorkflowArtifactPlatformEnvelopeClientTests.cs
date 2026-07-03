using System.Net;
using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Workflows.RuntimeApplier;
using FluentAssertions;

namespace Elsa.Platform.Workflows.RuntimeApplier.Tests;

public sealed class WorkflowArtifactPlatformEnvelopeClientTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid EngineId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid ArtifactRecordId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly ArtifactDigest Digest = new("sha256", new string('a', 64));

    private readonly WorkflowArtifactRuntimeOptions _options = new()
    {
        PlatformEndpoint = new Uri("https://platform.example.test"),
        WorkspaceId = WorkspaceId,
        EngineId = EngineId,
        WorkerId = "worker-a",
        RuntimeVersion = "4.0.0"
    };

    [Fact]
    public async Task Fetches_artifact_detail_and_maps_to_envelope()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ArtifactJson());
        var client = new WorkflowArtifactPlatformEnvelopeClient(new HttpClient(handler), _options);

        var envelope = await client.GetEnvelopeAsync(CommandArtifact());

        handler.RequestUri.Should().Be(new Uri($"https://platform.example.test/api/workspaces/{WorkspaceId:D}/artifacts/{ArtifactRecordId:D}"));
        envelope.ArtifactId.Should().Be("elsa.workflow-definition:payment-retry");
        envelope.ArtifactTypeId.Should().Be(ArtifactTypeIds.ElsaWorkflowDefinition);
        envelope.ContentDigest.Should().Be(Digest);
        envelope.PayloadReference.Provider.Should().Be("producer-managed");
        envelope.PayloadReference.ReferenceDigest.Should().Be(Digest);
        envelope.CompatibilityHints.Should().ContainSingle(x => x.RequiredArtifactType == ArtifactTypeIds.ElsaWorkflowDefinition);
    }

    [Fact]
    public async Task Rejects_command_artifact_digest_mismatch()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ArtifactJson());
        var client = new WorkflowArtifactPlatformEnvelopeClient(new HttpClient(handler), _options);

        var act = () => client.GetEnvelopeAsync(CommandArtifact() with { ContentDigest = new ArtifactDigest("sha256", new string('b', 64)) });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Runtime command artifact digest does not match the artifact record.");
    }

    [Fact]
    public async Task Rejects_artifact_detail_without_an_artifact_type()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ArtifactJson(artifactTypeId: null));
        var client = new WorkflowArtifactPlatformEnvelopeClient(new HttpClient(handler), _options);

        var act = () => client.GetEnvelopeAsync(CommandArtifact());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Platform artifact response does not include an artifact type.");
    }

    [Fact]
    public async Task Rejects_artifact_detail_from_different_workspace()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ArtifactJson(workspaceId: Guid.Parse("90000000-0000-0000-0000-000000000001")));
        var client = new WorkflowArtifactPlatformEnvelopeClient(new HttpClient(handler), _options);

        var act = () => client.GetEnvelopeAsync(CommandArtifact());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Platform artifact response workspace does not match the configured runtime workspace.");
    }

    [Fact]
    public async Task Sanitizes_platform_error_messages()
    {
        var handler = new RecordingHandler(HttpStatusCode.InternalServerError, """{"title":"Bearer token leaked"}""");
        var client = new WorkflowArtifactPlatformEnvelopeClient(new HttpClient(handler), _options);

        var act = () => client.GetEnvelopeAsync(CommandArtifact());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*[redacted]*");
    }

    private static WorkflowRuntimeCommandArtifactReference CommandArtifact() =>
        new(ArtifactRecordId, "elsa.workflow-definition:payment-retry", ArtifactTypeIds.ElsaWorkflowDefinition, Digest);

    private static string ArtifactJson(Guid? workspaceId = null, string? artifactTypeId = "elsa.workflow-definition") =>
        $$"""
          {
            "id": "{{ArtifactRecordId:D}}",
            "workspaceId": "{{(workspaceId ?? WorkspaceId):D}}",
            "artifactId": "elsa.workflow-definition:payment-retry",
            "layoutVersion": "platform.elsa.io/deployment-artifact/v1alpha1",
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
            "envelopeVersion": "platform.elsa.io/artifact-envelope/v1alpha1",
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
