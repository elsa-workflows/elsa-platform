using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Platform.Deployment.Artifacts;

namespace Elsa.Platform.Studio.Submit;

public sealed class StudioPlatformArtifactSubmitClient(HttpClient httpClient) : IStudioPlatformSubmitClient
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task<StudioSubmitResult> SubmitAsync(
        StudioSubmitPackage package,
        StudioSubmitOptions options,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            BuildSubmissionUri(options),
            ToRegistrationRequest(package.Envelope),
            JsonOptions,
            cancellationToken);

        var message = await SafeResponseMessageAsync(response, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.Created => await SubmittedAsync(response, StudioSubmitStatus.Submitted, message, cancellationToken),
            HttpStatusCode.OK => await SubmittedAsync(response, StudioSubmitStatus.Duplicate, message, cancellationToken),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new StudioSubmitResult(StudioSubmitStatus.Unauthorized, message),
            HttpStatusCode.Conflict => new StudioSubmitResult(StudioSubmitStatus.Conflict, message),
            HttpStatusCode.BadRequest => new StudioSubmitResult(StudioSubmitStatus.ValidationFailed, message),
            _ when (int)response.StatusCode >= 500 => new StudioSubmitResult(StudioSubmitStatus.RetryableError, message),
            _ => new StudioSubmitResult(StudioSubmitStatus.ValidationFailed, message)
        };
    }

    private Uri BuildSubmissionUri(StudioSubmitOptions options)
    {
        var endpoint = options.PlatformEndpoint ?? httpClient.BaseAddress
            ?? throw new InvalidOperationException("Platform endpoint is required before submitting to Platform.");
        if (options.WorkspaceId is null || options.WorkspaceId == Guid.Empty)
            throw new InvalidOperationException("Platform workspace is required before submitting to Platform.");

        return new Uri($"{endpoint.AbsoluteUri.TrimEnd('/')}/api/workspaces/{options.WorkspaceId:D}/artifacts");
    }

    private static WorkspaceArtifactRegistrationRequest ToRegistrationRequest(ArtifactEnvelope envelope) =>
        new(
            envelope.ArtifactId,
            ArtifactLayoutConstants.LayoutVersion,
            new WorkspaceArtifactDigest(envelope.ContentDigest.Algorithm, envelope.ContentDigest.Value),
            WorkspaceArtifactFormat.Unknown,
            envelope.PayloadReference.Provider,
            envelope.PayloadReference.Uri,
            new WorkspaceArtifactManifestSummary(
                envelope.DisplayMetadata.Name,
                envelope.DisplayMetadata.Version,
                null),
            [],
            envelope.Diagnostics
                .Select(x => new WorkspaceArtifactDiagnostic(x.Code, ToWorkspaceSeverity(x.Severity), x.Message))
                .ToList(),
            envelope.EnvelopeVersion,
            envelope.ArtifactTypeId,
            envelope.ArtifactSchemaVersion,
            envelope.ManifestDigest is null ? null : new WorkspaceArtifactDigest(envelope.ManifestDigest.Value.Algorithm, envelope.ManifestDigest.Value.Value),
            envelope.PayloadReference,
            envelope.Producer,
            envelope.DisplayMetadata,
            envelope.CompatibilityHints);

    private static async Task<StudioSubmitResult> SubmittedAsync(
        HttpResponseMessage response,
        StudioSubmitStatus status,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        WorkspaceArtifactResponse? artifact;
        try
        {
            artifact = await response.Content.ReadFromJsonAsync<WorkspaceArtifactResponse>(JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return new StudioSubmitResult(StudioSubmitStatus.RetryableError, "Platform submission response could not be read.");
        }

        if (artifact is null)
            return new StudioSubmitResult(StudioSubmitStatus.RetryableError, "Platform submission response could not be read.");

        return new StudioSubmitResult(
            status,
            fallbackMessage,
            artifact.ArtifactId,
            $"{artifact.ContentDigest.Algorithm}:{artifact.ContentDigest.Value}",
            artifact.RegisteredAt);
    }

    private static async Task<string> SafeResponseMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return response.StatusCode == HttpStatusCode.Created ? "Submitted to Platform." : "Artifact already exists in Platform.";

        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(JsonOptions, cancellationToken);
            return StudioSubmitMessageSanitizer.SafeMessage(problem?.Title ?? problem?.Detail ?? response.ReasonPhrase);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return StudioSubmitMessageSanitizer.SafeMessage(response.ReasonPhrase);
        }
    }

    private static WorkspaceArtifactDiagnosticSeverity ToWorkspaceSeverity(ArtifactEnvelopeDiagnosticSeverity severity) =>
        severity switch
        {
            ArtifactEnvelopeDiagnosticSeverity.Error => WorkspaceArtifactDiagnosticSeverity.Error,
            ArtifactEnvelopeDiagnosticSeverity.Warning => WorkspaceArtifactDiagnosticSeverity.Warning,
            _ => WorkspaceArtifactDiagnosticSeverity.Info
        };

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    private sealed record WorkspaceArtifactRegistrationRequest(
        string ArtifactId,
        string LayoutVersion,
        WorkspaceArtifactDigest ContentDigest,
        WorkspaceArtifactFormat Format,
        string ReferenceProvider,
        string Reference,
        WorkspaceArtifactManifestSummary Manifest,
        IReadOnlyList<WorkspaceArtifactResourceSummary> Resources,
        IReadOnlyList<WorkspaceArtifactDiagnostic> Diagnostics,
        string? EnvelopeVersion = null,
        string? ArtifactTypeId = null,
        string? ArtifactSchemaVersion = null,
        WorkspaceArtifactDigest? ManifestDigest = null,
        ArtifactPayloadReference? PayloadReference = null,
        ArtifactProducer? Producer = null,
        ArtifactDisplayMetadata? DisplayMetadata = null,
        IReadOnlyList<ArtifactCompatibilityHint>? CompatibilityHints = null);

    private sealed record WorkspaceArtifactDigest(string Algorithm, string Value);

    private sealed record WorkspaceArtifactManifestSummary(string? Name, string? Version, string? Environment);

    private sealed record WorkspaceArtifactResourceSummary(
        string Type,
        string LogicalId,
        string? Scope,
        string? Version,
        WorkspaceArtifactDigest? DesiredStateHash);

    private sealed record WorkspaceArtifactDiagnostic(string Code, WorkspaceArtifactDiagnosticSeverity Severity, string Message);

    private sealed record WorkspaceArtifactResponse(
        Guid Id,
        string ArtifactId,
        WorkspaceArtifactDigest ContentDigest,
        DateTimeOffset RegisteredAt);

    private sealed record ProblemDetailsResponse(string? Title, string? Detail);

    private enum WorkspaceArtifactFormat
    {
        Folder,
        Zip,
        Unknown
    }

    private enum WorkspaceArtifactDiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }
}
