using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ValenceControl.Deployment.Abstractions.Artifacts;
using ValenceControl.Deployment.Artifacts;

namespace ValenceControl.Workflows.RuntimeApplier;

public sealed class WorkflowArtifactControlEnvelopeClient(
    HttpClient httpClient,
    WorkflowArtifactRuntimeOptions options) : IWorkflowArtifactEnvelopeProvider
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly WorkflowArtifactRuntimeOptions _options = ValidateOptions(options);

    public async Task<ArtifactEnvelope> GetEnvelopeAsync(
        WorkflowRuntimeCommandArtifactReference artifact,
        CancellationToken cancellationToken = default)
    {
        if (artifact.ArtifactRecordId is null || artifact.ArtifactRecordId == Guid.Empty)
            throw new InvalidOperationException("Runtime command does not include an artifact record reference.");

        using var response = await httpClient.GetAsync(BuildUri($"/artifacts/{artifact.ArtifactRecordId.Value:D}"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("Runtime command artifact could not be found.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await SafeResponseMessageAsync(response, cancellationToken));

        var body = await ReadJsonAsync<WorkspaceArtifactDto>(response, cancellationToken)
            ?? throw new InvalidOperationException("Control artifact response could not be read.");
        if (body.WorkspaceId != _options.WorkspaceId!.Value)
            throw new InvalidOperationException("Control artifact response workspace does not match the configured runtime workspace.");
        if (body.Id != artifact.ArtifactRecordId.Value)
            throw new InvalidOperationException("Control artifact response does not match the requested artifact record.");

        var envelope = ToEnvelope(body);
        if (!string.IsNullOrWhiteSpace(artifact.ArtifactId)
            && !artifact.ArtifactId.Equals(envelope.ArtifactId, StringComparison.Ordinal))
            throw new InvalidOperationException("Runtime command artifact identity does not match the artifact record.");
        if (!string.IsNullOrWhiteSpace(artifact.ArtifactTypeId)
            && !artifact.ArtifactTypeId.Equals(envelope.ArtifactTypeId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Runtime command artifact type does not match the artifact record.");
        if (artifact.ContentDigest is { } digest && !DigestEquals(digest, envelope.ContentDigest))
            throw new InvalidOperationException("Runtime command artifact digest does not match the artifact record.");

        return envelope;
    }

    private Uri BuildUri(string relativePath)
    {
        var endpoint = _options.ControlEndpoint ?? httpClient.BaseAddress
            ?? throw new InvalidOperationException("Control endpoint is required before runtime command sync can start.");
        return new Uri($"{endpoint.AbsoluteUri.TrimEnd('/')}/api/workspaces/{_options.WorkspaceId!.Value:D}{relativePath}");
    }

    private static WorkflowArtifactRuntimeOptions ValidateOptions(WorkflowArtifactRuntimeOptions options)
    {
        options.Validate();
        return options;
    }

    private static ArtifactEnvelope ToEnvelope(WorkspaceArtifactDto dto)
    {
        // The runtime consumes an untrusted control response and routes strictly on the artifact
        // type (loom recipe vs plain workflow definition), so a missing type is a malformed response
        // to reject rather than silently default — an assumed type could route the payload to the
        // wrong applier path.
        if (string.IsNullOrWhiteSpace(dto.ArtifactTypeId))
            throw new InvalidOperationException("Control artifact response does not include an artifact type.");

        return ArtifactEnvelopeDefaults.CreateEnvelope(
            dto.ArtifactId,
            dto.EnvelopeVersion,
            dto.ArtifactTypeId,
            dto.ArtifactSchemaVersion,
            ToArtifactDigest(dto.ContentDigest),
            dto.ManifestDigest is null ? null : ToArtifactDigest(dto.ManifestDigest),
            dto.PayloadReference,
            dto.Producer,
            dto.DisplayMetadata,
            dto.CompatibilityHints,
            dto.Diagnostics.Select(x => new ArtifactEnvelopeDiagnostic(x.Code, ToEnvelopeSeverity(x.Severity), x.Message)).ToList(),
            new ArtifactEnvelopeFallback(dto.ReferenceProvider, dto.Reference, dto.Manifest.Name, dto.Manifest.Version, dto.Manifest.Environment));
    }

    private static bool DigestEquals(ArtifactDigest left, ArtifactDigest right) =>
        left.Algorithm.Equals(right.Algorithm, StringComparison.OrdinalIgnoreCase)
        && left.Value.Equals(right.Value, StringComparison.OrdinalIgnoreCase);

    private static ArtifactDigest ToArtifactDigest(WorkspaceArtifactDigestDto digest) =>
        new(digest.Algorithm, digest.Value);

    private static ArtifactEnvelopeDiagnosticSeverity ToEnvelopeSeverity(string severity) =>
        Enum.TryParse<ArtifactEnvelopeDiagnosticSeverity>(severity, ignoreCase: true, out var value) ? value : ArtifactEnvelopeDiagnosticSeverity.Info;

    private static async Task<string> SafeResponseMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(JsonOptions, cancellationToken);
            return WorkflowArtifactRuntimeDiagnosticSanitizer.SafeMessage(problem?.Title ?? problem?.Detail ?? response.ReasonPhrase);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return WorkflowArtifactRuntimeDiagnosticSanitizer.SafeMessage(response.ReasonPhrase);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return default;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    private sealed record WorkspaceArtifactDto(
        Guid Id,
        Guid WorkspaceId,
        string ArtifactId,
        string LayoutVersion,
        WorkspaceArtifactDigestDto ContentDigest,
        string Format,
        string ReferenceProvider,
        string Reference,
        WorkspaceArtifactManifestSummaryDto Manifest,
        IReadOnlyList<WorkspaceArtifactResourceSummaryDto> Resources,
        string ChecksumStatus,
        string InspectionStatus,
        IReadOnlyList<WorkspaceArtifactDiagnosticDto> Diagnostics,
        DateTimeOffset RegisteredAt,
        Guid RegisteredByAccountId,
        DateTimeOffset? LastInspectedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string? EnvelopeVersion = null,
        string? ArtifactTypeId = null,
        string? ArtifactSchemaVersion = null,
        WorkspaceArtifactDigestDto? ManifestDigest = null,
        ArtifactPayloadReference? PayloadReference = null,
        ArtifactProducer? Producer = null,
        ArtifactDisplayMetadata? DisplayMetadata = null,
        IReadOnlyList<ArtifactCompatibilityHint>? CompatibilityHints = null);

    private sealed record WorkspaceArtifactDigestDto(string Algorithm, string Value);

    private sealed record WorkspaceArtifactManifestSummaryDto(string? Name, string? Version, string? Environment);

    private sealed record WorkspaceArtifactResourceSummaryDto(
        string Type,
        string LogicalId,
        string? Scope,
        string? Version,
        WorkspaceArtifactDigestDto? DesiredStateHash);

    private sealed record WorkspaceArtifactDiagnosticDto(string Code, string Severity, string Message);

    private sealed record ProblemDetailsResponse(string? Title, string? Detail);
}
