using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ValenceControl.Deployment.Artifacts;
using ValenceControl.Deployment.Manifest;

namespace ValenceControl.Studio.Submit;

public sealed class StudioControlArtifactSubmitClient(HttpClient httpClient) : IStudioControlRevisionSubmitClient
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly DeploymentArtifactBuilder _artifactBuilder = new();

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

    public async Task<StudioSubmitResult> SubmitRevisionAsync(
        StudioSubmitPackage package,
        StudioSubmitOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateRevisionOptions(options);

        var zipPath = await BuildArtifactZipAsync(package, options, cancellationToken);
        try
        {
            var artifactResult = await UploadArtifactAsync(package, options, zipPath, cancellationToken);
            if (!artifactResult.Succeeded || artifactResult.Artifact is null)
                return artifactResult.Result;

            var revisionResult = await CreateRevisionAsync(package, options, artifactResult.Artifact, cancellationToken);
            return revisionResult with
            {
                ArtifactId = artifactResult.Artifact.ArtifactId,
                ArtifactDigest = $"{artifactResult.Artifact.ContentDigest.Algorithm}:{artifactResult.Artifact.ContentDigest.Value}",
                ArtifactRecordId = artifactResult.Artifact.Id
            };
        }
        finally
        {
            TryDeleteBuildDirectory(zipPath);
        }
    }

    private Uri BuildSubmissionUri(StudioSubmitOptions options)
    {
        var endpoint = options.ControlEndpoint ?? httpClient.BaseAddress
            ?? throw new InvalidOperationException("Control endpoint is required before submitting to Control.");
        if (options.WorkspaceId is null || options.WorkspaceId == Guid.Empty)
            throw new InvalidOperationException("Control workspace is required before submitting to Control.");

        return new Uri($"{endpoint.AbsoluteUri.TrimEnd('/')}/api/workspaces/{options.WorkspaceId:D}/artifacts");
    }

    private Uri BuildWorkspaceUri(StudioSubmitOptions options, string relativePath)
    {
        var endpoint = options.ControlEndpoint ?? httpClient.BaseAddress
            ?? throw new InvalidOperationException("Control endpoint is required before submitting to Control.");
        if (options.WorkspaceId is null || options.WorkspaceId == Guid.Empty)
            throw new InvalidOperationException("Control workspace is required before submitting to Control.");

        return new Uri($"{endpoint.AbsoluteUri.TrimEnd('/')}/api/workspaces/{options.WorkspaceId:D}{relativePath}");
    }

    private static void ValidateRevisionOptions(StudioSubmitOptions options)
    {
        if (options.ApplicationId is null || options.ApplicationId == Guid.Empty)
            throw new InvalidOperationException("Control application is required before creating a desired-state revision.");
        if (options.EnvironmentId is null || options.EnvironmentId == Guid.Empty)
            throw new InvalidOperationException("Control environment is required before creating a desired-state revision.");
    }

    private async Task<string> BuildArtifactZipAsync(
        StudioSubmitPackage package,
        StudioSubmitOptions options,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), $"elsa-studio-control-submit-{Guid.NewGuid():N}");
        var recipeDirectory = Path.Combine(root, "recipes");
        var recipeFileName = $"{SafeFileName(package.Envelope.DisplayMetadata.Name ?? package.Envelope.ArtifactId)}.json";
        var recipePath = Path.Combine(recipeDirectory, recipeFileName);
        var zipPath = Path.Combine(root, "artifact.zip");

        Directory.CreateDirectory(recipeDirectory);
        await File.WriteAllTextAsync(recipePath, package.WorkflowDefinitionJson, cancellationToken);

        var manifest = BuildManifestJson(package, $"recipes/{recipeFileName}");
        var build = await _artifactBuilder.BuildZipAsync(new DeploymentArtifactBuildOptions(
            manifest,
            ManifestFormat.Json,
            root,
            zipPath,
            overwrite: true,
            package.PackagedAt,
            options.ProducerName,
            package.Envelope.Producer.SourceReference), cancellationToken);

        if (!build.Succeeded)
        {
            TryDeleteBuildDirectory(zipPath);
            var message = build.Diagnostics.FirstOrDefault(x => x.Severity == Deployment.Abstractions.Diagnostics.DeploymentDiagnosticSeverity.Error)?.Message
                ?? "Loom recipe artifact package could not be built.";
            throw new InvalidOperationException(message);
        }

        return zipPath;
    }

    private static string BuildManifestJson(StudioSubmitPackage package, string recipePath)
    {
        var recipe = new
        {
            id = package.Envelope.DisplayMetadata.Source ?? package.Envelope.ArtifactId,
            path = recipePath,
            version = package.Envelope.DisplayMetadata.Version,
            metadata = package.Envelope.DisplayMetadata.Labels
        };
        var manifest = new
        {
            apiVersion = DeploymentManifestConstants.ApiVersion,
            kind = DeploymentManifestConstants.Kind,
            metadata = new
            {
                name = package.Envelope.DisplayMetadata.Name,
                version = package.Envelope.DisplayMetadata.Version
            },
            resources = new
            {
                recipes = new[] { recipe }
            }
        };

        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    private async Task<ArtifactUploadResult> UploadArtifactAsync(
        StudioSubmitPackage package,
        StudioSubmitOptions options,
        string zipPath,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(zipPath);
        using var createResponse = await httpClient.PostAsJsonAsync(
            BuildWorkspaceUri(options, "/artifact-uploads"),
            new CreateArtifactUploadRequest("loom-recipe.zip", "application/zip", file.Length, package.Envelope.ArtifactId),
            JsonOptions,
            cancellationToken);
        if (createResponse.StatusCode != HttpStatusCode.Created)
            return ArtifactUploadResult.Failed(new StudioSubmitResult(MapStatus(createResponse.StatusCode), await SafeResponseMessageAsync(createResponse, cancellationToken)));

        var created = await createResponse.Content.ReadFromJsonAsync<CreateArtifactUploadResponse>(JsonOptions, cancellationToken);
        if (created is null)
            return ArtifactUploadResult.Failed(new StudioSubmitResult(StudioSubmitStatus.RetryableError, "Control artifact upload response could not be read."));

        await using (var content = File.OpenRead(zipPath))
        using (var uploadResponse = await httpClient.PutAsync(BuildWorkspaceUri(options, $"/artifact-uploads/{created.UploadId:D}/content"), new StreamContent(content), cancellationToken))
        {
            if (uploadResponse.StatusCode != HttpStatusCode.NoContent)
                return ArtifactUploadResult.Failed(new StudioSubmitResult(MapStatus(uploadResponse.StatusCode), await SafeResponseMessageAsync(uploadResponse, cancellationToken)));
        }

        using var completeResponse = await httpClient.PostAsync(BuildWorkspaceUri(options, $"/artifact-uploads/{created.UploadId:D}/complete"), null, cancellationToken);
        if (completeResponse.StatusCode is not HttpStatusCode.Created and not HttpStatusCode.OK)
            return ArtifactUploadResult.Failed(new StudioSubmitResult(MapStatus(completeResponse.StatusCode), await SafeResponseMessageAsync(completeResponse, cancellationToken)));

        var completed = await completeResponse.Content.ReadFromJsonAsync<CompleteArtifactUploadResponse>(JsonOptions, cancellationToken);
        if (completed?.Artifact is null)
            return ArtifactUploadResult.Failed(new StudioSubmitResult(StudioSubmitStatus.RetryableError, "Control artifact completion response could not be read."));

        return ArtifactUploadResult.Completed(completed.Artifact);
    }

    private async Task<StudioSubmitResult> CreateRevisionAsync(
        StudioSubmitPackage package,
        StudioSubmitOptions options,
        WorkspaceArtifactResponse artifact,
        CancellationToken cancellationToken)
    {
        var payload = new ArtifactReferencePayload(
            artifact.Id.ToString("D"),
            artifact.ArtifactId,
            artifact.ArtifactTypeId ?? package.Envelope.ArtifactTypeId,
            artifact.ContentDigest);
        var revisionRequest = new WorkspaceDesiredStateRevisionRequest(
            string.IsNullOrWhiteSpace(options.RevisionLabel)
                ? $"Submit {package.Envelope.DisplayMetadata.Name ?? package.Envelope.ArtifactId}"
                : options.RevisionLabel.Trim(),
            options.RevisionCommit,
            [new WorkspaceDesiredStateRecordRequest("ArtifactReference", package.Envelope.DisplayMetadata.Name ?? package.Envelope.ArtifactId, payload)]);

        using var response = await httpClient.PostAsJsonAsync(
            BuildWorkspaceUri(options, $"/deployments/applications/{options.ApplicationId!.Value:D}/environments/{options.EnvironmentId!.Value:D}/revisions"),
            revisionRequest,
            JsonOptions,
            cancellationToken);

        if (response.StatusCode != HttpStatusCode.Created)
            return new StudioSubmitResult(MapStatus(response.StatusCode), await SafeResponseMessageAsync(response, cancellationToken));

        var revision = await response.Content.ReadFromJsonAsync<WorkspaceDesiredStateRevisionResponse>(JsonOptions, cancellationToken);
        if (revision is null)
            return new StudioSubmitResult(StudioSubmitStatus.RetryableError, "Control revision response could not be read.");

        return new StudioSubmitResult(
            StudioSubmitStatus.Submitted,
            "Submitted to Control desired state.",
            artifact.ArtifactId,
            $"{artifact.ContentDigest.Algorithm}:{artifact.ContentDigest.Value}",
            revision.CreatedAt,
            artifact.Id,
            revision.Id);
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
            return new StudioSubmitResult(StudioSubmitStatus.RetryableError, "Control submission response could not be read.");
        }

        if (artifact is null)
            return new StudioSubmitResult(StudioSubmitStatus.RetryableError, "Control submission response could not be read.");

        return new StudioSubmitResult(
            status,
            fallbackMessage,
            artifact.ArtifactId,
            $"{artifact.ContentDigest.Algorithm}:{artifact.ContentDigest.Value}",
            artifact.RegisteredAt,
            artifact.Id);
    }

    private static StudioSubmitStatus MapStatus(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => StudioSubmitStatus.Unauthorized,
            HttpStatusCode.Conflict => StudioSubmitStatus.Conflict,
            HttpStatusCode.BadRequest => StudioSubmitStatus.ValidationFailed,
            _ when (int)statusCode >= 500 => StudioSubmitStatus.RetryableError,
            _ => StudioSubmitStatus.ValidationFailed
        };

    private static async Task<string> SafeResponseMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return response.StatusCode == HttpStatusCode.Created ? "Submitted to Control." : "Artifact already exists in Control.";

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

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim('-', '.', ' ');
        return string.IsNullOrWhiteSpace(safe) ? "loom-recipe" : safe;
    }

    private static void TryDeleteBuildDirectory(string zipPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only; callers should not fail after Control accepted a submission.
        }
    }

    private sealed record ArtifactUploadResult(WorkspaceArtifactResponse? Artifact, StudioSubmitResult Result)
    {
        public bool Succeeded => Artifact is not null;

        public static ArtifactUploadResult Completed(WorkspaceArtifactResponse artifact) =>
            new(artifact, new StudioSubmitResult(StudioSubmitStatus.Submitted, "Artifact uploaded to Control."));

        public static ArtifactUploadResult Failed(StudioSubmitResult result) =>
            new(null, result);
    }

    private sealed record CreateArtifactUploadRequest(string FileName, string? ContentType, long SizeBytes, string? IdempotencyKey);

    private sealed record CreateArtifactUploadResponse(Guid UploadId, string Status, DateTimeOffset ExpiresAt, long MaxUploadBytes);

    private sealed record CompleteArtifactUploadResponse(Guid UploadId, string Status, WorkspaceArtifactResponse? Artifact, bool Created);

    private sealed record WorkspaceDesiredStateRevisionRequest(string Label, string? Commit, IReadOnlyList<WorkspaceDesiredStateRecordRequest> Records);

    private sealed record WorkspaceDesiredStateRecordRequest(string Kind, string Name, ArtifactReferencePayload Payload);

    private sealed record ArtifactReferencePayload(
        string ArtifactRecordId,
        string ArtifactId,
        string ArtifactTypeId,
        WorkspaceArtifactDigest ContentDigest);

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
        DateTimeOffset RegisteredAt,
        string? ArtifactTypeId = null,
        string? ArtifactSchemaVersion = null);

    private sealed record WorkspaceDesiredStateRevisionResponse(
        Guid Id,
        Guid WorkspaceId,
        Guid ApplicationId,
        Guid EnvironmentId,
        int RevisionNumber,
        string Label,
        string? Commit,
        string ContentHash,
        string DesiredStateJson,
        DateTimeOffset AuthoredAt,
        DateTimeOffset CreatedAt,
        Guid? CreatedByAccountId);

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
