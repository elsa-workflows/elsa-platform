using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Deployment.Manifest;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class WorkspaceArtifactUploadService(
    IWorkspaceArtifactUploadStore uploadStore,
    IWorkspaceArtifactStore artifactStore,
    WorkspaceArtifactService artifactService,
    IDeploymentArtifactReader artifactReader,
    IDeploymentArtifactBuilder artifactBuilder,
    ArtifactUploadOptions options,
    TimeProvider? timeProvider = null)
{
    private readonly ArtifactUploadOptions _options = options;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public WorkspaceArtifactUploadCapabilities GetCapabilities() =>
        new(NormalizedMaxUploadBytes, IsSampleGenerationEnabled);

    public async Task<CreateArtifactUploadResponse> CreateSessionAsync(
        Guid workspaceId,
        CreateArtifactUploadRequest request,
        Guid actorAccountId,
        CancellationToken cancellationToken = default)
    {
        if (request.SizeBytes <= 0)
            throw new InvalidOperationException("Artifact upload size is required.");
        if (request.SizeBytes > NormalizedMaxUploadBytes)
            throw new InvalidOperationException("Artifact upload exceeds the configured maximum size.");
        if (!IsZipFileName(request.FileName))
            throw new InvalidOperationException("Artifact upload must be a ZIP file.");

        var idempotencyKey = NormalizeOptional(request.IdempotencyKey);
        if (idempotencyKey is not null)
        {
            var existing = await uploadStore.FindArtifactUploadByIdempotencyKeyAsync(workspaceId, idempotencyKey, cancellationToken);
            if (existing is not null)
                return new CreateArtifactUploadResponse(existing.Id, existing.Status, existing.ExpiresAt, NormalizedMaxUploadBytes);
        }

        var now = _timeProvider.GetUtcNow();
        var session = new WorkspaceArtifactUploadSession(
            Guid.NewGuid(),
            workspaceId,
            WorkspaceArtifactUploadStatus.Pending,
            Path.GetFileName(request.FileName),
            NormalizeOptional(request.ContentType),
            request.SizeBytes,
            null,
            null,
            idempotencyKey,
            [],
            now.AddMinutes(NormalizedSessionTtlMinutes),
            null,
            actorAccountId,
            now,
            now);

        var created = await uploadStore.CreateArtifactUploadSessionAsync(session, cancellationToken);
        return new CreateArtifactUploadResponse(created.Id, created.Status, created.ExpiresAt, NormalizedMaxUploadBytes);
    }

    public async Task UploadContentAsync(
        Guid workspaceId,
        Guid uploadId,
        Stream content,
        long? contentLength,
        CancellationToken cancellationToken = default)
    {
        var session = await GetActiveSessionAsync(workspaceId, uploadId, cancellationToken);
        if (session.Status is not WorkspaceArtifactUploadStatus.Pending and not WorkspaceArtifactUploadStatus.Uploading)
            throw new InvalidOperationException("Artifact upload content cannot be replaced after processing has started.");
        if (contentLength.HasValue && contentLength.Value > NormalizedMaxUploadBytes)
            throw new InvalidOperationException("Artifact upload exceeds the configured maximum size.");
        if (session.DeclaredSizeBytes.HasValue && contentLength.HasValue && session.DeclaredSizeBytes.Value != contentLength.Value)
            throw new InvalidOperationException("Artifact upload content length does not match the upload session.");

        var stagingPath = session.StagedFilePath ?? CreateStagingPath(workspaceId, uploadId);
        Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
        var uploading = await uploadStore.UpdateArtifactUploadSessionAsync(session with
        {
            Status = WorkspaceArtifactUploadStatus.Uploading,
            StagedFilePath = stagingPath,
            UpdatedAt = _timeProvider.GetUtcNow()
        }, cancellationToken);

        long total = 0;
        try
        {
            await using var output = File.Create(stagingPath);
            var buffer = new byte[81920];
            while (true)
            {
                var read = await content.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;

                total += read;
                if (total > NormalizedMaxUploadBytes)
                    throw new InvalidOperationException("Artifact upload exceeds the configured maximum size.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (uploading.DeclaredSizeBytes.HasValue && total != uploading.DeclaredSizeBytes.Value)
                throw new InvalidOperationException("Artifact upload content length does not match the upload session.");

            await uploadStore.UpdateArtifactUploadSessionAsync(uploading with
            {
                Status = WorkspaceArtifactUploadStatus.Uploaded,
                UploadedSizeBytes = total,
                StagedFilePath = stagingPath,
                UpdatedAt = _timeProvider.GetUtcNow()
            }, cancellationToken);
        }
        catch
        {
            DeleteFileIfExists(stagingPath);
            throw;
        }
    }

    public async Task<CompleteArtifactUploadResponse> CompleteAsync(
        Guid workspaceId,
        Guid uploadId,
        Guid actorAccountId,
        CancellationToken cancellationToken = default)
    {
        var session = await GetActiveSessionAsync(workspaceId, uploadId, cancellationToken);
        if (session.Status == WorkspaceArtifactUploadStatus.Completed && session.CompletedArtifactRecordId is { } completedId)
        {
            var completedArtifact = await artifactStore.GetArtifactAsync(workspaceId, completedId, cancellationToken);
            return new CompleteArtifactUploadResponse(uploadId, session.Status, completedArtifact, Created: false, session.Diagnostics);
        }
        if (session.Status != WorkspaceArtifactUploadStatus.Uploaded)
            throw new InvalidOperationException("Upload content must be provided before completion.");
        if (string.IsNullOrWhiteSpace(session.StagedFilePath) || !File.Exists(session.StagedFilePath))
            return await FailAsync(session, Diagnostic("artifact.upload.content-missing", "Uploaded artifact content is unavailable."), cancellationToken);

        session = await uploadStore.UpdateArtifactUploadSessionAsync(session with
        {
            Status = WorkspaceArtifactUploadStatus.Inspecting,
            UpdatedAt = _timeProvider.GetUtcNow()
        }, cancellationToken);

        var inspection = await artifactReader.InspectZipAsync(session.StagedFilePath!, cancellationToken);
        if (!inspection.Succeeded || inspection.Metadata is null || string.IsNullOrWhiteSpace(inspection.ArtifactId))
            return await FailAsync(session, inspection.Diagnostics.Select(ToWorkspaceDiagnostic).ToList(), cancellationToken);

        var existing = await artifactStore.FindArtifactByIdentityAsync(workspaceId, inspection.ArtifactId!, cancellationToken);
        if (existing is not null)
        {
            DeleteFileIfExists(session.StagedFilePath);
            var completed = await uploadStore.UpdateArtifactUploadSessionAsync(session with
            {
                Status = WorkspaceArtifactUploadStatus.Completed,
                CompletedArtifactRecordId = existing.Id,
                Diagnostics = inspection.Diagnostics.Select(ToWorkspaceDiagnostic).ToList(),
                UpdatedAt = _timeProvider.GetUtcNow()
            }, cancellationToken);
            return new CompleteArtifactUploadResponse(uploadId, completed.Status, existing, Created: false, completed.Diagnostics);
        }

        var storedPath = CreateStoredArtifactPath(workspaceId, inspection.ArtifactId!);
        Directory.CreateDirectory(Path.GetDirectoryName(storedPath)!);
        if (File.Exists(storedPath))
            File.Delete(storedPath);
        File.Move(session.StagedFilePath!, storedPath);

        var registration = await artifactService.RegisterArtifactAsync(
            workspaceId,
            CreateRegistrationRequest(inspection, storedPath, new FileInfo(storedPath).Length, actorAccountId),
            cancellationToken);

        var result = await artifactStore.UpdateArtifactInspectionAsync(
            workspaceId,
            new WorkspaceArtifactInspectionUpdate(
                registration.Artifact.Id,
                registration.Artifact.ArtifactId,
                WorkspaceArtifactChecksumStatus.Verified,
                WorkspaceArtifactInspectionStatus.Valid,
                _timeProvider.GetUtcNow(),
                inspection.Metadata.Resources.Select(ToResourceSummary).ToList(),
                inspection.Diagnostics.Select(ToWorkspaceDiagnostic).ToList()),
            cancellationToken);

        var artifact = await artifactStore.GetArtifactAsync(workspaceId, result.ArtifactRecordId, cancellationToken)
            ?? registration.Artifact;
        var completedSession = await uploadStore.UpdateArtifactUploadSessionAsync(session with
        {
            Status = WorkspaceArtifactUploadStatus.Completed,
            CompletedArtifactRecordId = artifact.Id,
            Diagnostics = artifact.Diagnostics,
            UpdatedAt = _timeProvider.GetUtcNow()
        }, cancellationToken);

        return new CompleteArtifactUploadResponse(uploadId, completedSession.Status, artifact, registration.Created, completedSession.Diagnostics);
    }

    public async Task AbortAsync(
        Guid workspaceId,
        Guid uploadId,
        CancellationToken cancellationToken = default)
    {
        var session = await uploadStore.GetArtifactUploadSessionAsync(workspaceId, uploadId, cancellationToken)
            ?? throw new KeyNotFoundException("Artifact upload session does not exist in the workspace.");
        DeleteFileIfExists(session.StagedFilePath);
        await uploadStore.UpdateArtifactUploadSessionAsync(session with
        {
            Status = WorkspaceArtifactUploadStatus.Aborted,
            StagedFilePath = null,
            UpdatedAt = _timeProvider.GetUtcNow()
        }, cancellationToken);
    }

    public async Task<CompleteArtifactUploadResponse> CreateSampleArtifactAsync(
        Guid workspaceId,
        CreateSampleArtifactRequest request,
        Guid actorAccountId,
        CancellationToken cancellationToken = default)
    {
        if (!IsSampleGenerationEnabled)
            throw new InvalidOperationException("Sample artifact generation is not enabled.");

        var artifactName = NormalizeRequired(request.ArtifactName, "Artifact name is required.");
        var version = NormalizeRequired(request.Version, "Artifact version is required.");
        var environmentName = NormalizeRequired(request.Environment, "Artifact environment is required.");
        var workflowId = NormalizeRequired(request.WorkflowId, "Workflow id is required.");
        var sampleRoot = Path.Combine(CreateWorkspaceTempRoot(workspaceId), $"sample-{Guid.NewGuid():N}");
        var outputPath = Path.Combine(sampleRoot, "sample-artifact.zip");

        try
        {
            Directory.CreateDirectory(Path.Combine(sampleRoot, "workflows"));
            await File.WriteAllTextAsync(
                Path.Combine(sampleRoot, "workflows", $"{SafeFileSegment(workflowId)}.json"),
                $$"""{"id":"{{workflowId}}","name":"{{artifactName}} sample workflow"}""",
                cancellationToken);

            var manifest = $$"""
                apiVersion: platform.elsa.io/v1alpha1
                kind: EnvironmentManifest
                metadata:
                  name: {{artifactName}}
                  version: {{version}}
                  environment: {{environmentName}}
                resources:
                  workflows:
                    - id: {{workflowId}}
                      path: workflows/{{SafeFileSegment(workflowId)}}.json
                """;
            var build = await artifactBuilder.BuildZipAsync(
                new DeploymentArtifactBuildOptions(
                    manifest,
                    ManifestFormat.Yaml,
                    sampleRoot,
                    outputPath,
                    overwrite: true,
                    builtAt: _timeProvider.GetUtcNow(),
                    builder: "Elsa Platform Console",
                    source: "development-sample"),
                cancellationToken);
            if (!build.Succeeded || build.OutputPath is null)
                return new CompleteArtifactUploadResponse(Guid.Empty, WorkspaceArtifactUploadStatus.Failed, null, Created: false, build.Diagnostics.Select(ToWorkspaceDiagnostic).ToList());

            var fileInfo = new FileInfo(build.OutputPath);
            var create = await CreateSessionAsync(
                workspaceId,
                new CreateArtifactUploadRequest(fileInfo.Name, "application/zip", fileInfo.Length),
                actorAccountId,
                cancellationToken);
            await using var stream = File.OpenRead(build.OutputPath);
            await UploadContentAsync(workspaceId, create.UploadId, stream, fileInfo.Length, cancellationToken);
            return await CompleteAsync(workspaceId, create.UploadId, actorAccountId, cancellationToken);
        }
        finally
        {
            if (Directory.Exists(sampleRoot))
                Directory.Delete(sampleRoot, recursive: true);
        }
    }

    private async Task<WorkspaceArtifactUploadSession> GetActiveSessionAsync(
        Guid workspaceId,
        Guid uploadId,
        CancellationToken cancellationToken)
    {
        var session = await uploadStore.GetArtifactUploadSessionAsync(workspaceId, uploadId, cancellationToken)
            ?? throw new KeyNotFoundException("Artifact upload session does not exist in the workspace.");
        if (session.Status is WorkspaceArtifactUploadStatus.Aborted or WorkspaceArtifactUploadStatus.Failed or WorkspaceArtifactUploadStatus.Expired)
            throw new InvalidOperationException("Artifact upload session is no longer active.");
        if (session.ExpiresAt <= _timeProvider.GetUtcNow() && session.Status is not WorkspaceArtifactUploadStatus.Completed)
        {
            var expired = await uploadStore.UpdateArtifactUploadSessionAsync(session with
            {
                Status = WorkspaceArtifactUploadStatus.Expired,
                Diagnostics = [Diagnostic("artifact.upload.expired", "Artifact upload session has expired.")],
                UpdatedAt = _timeProvider.GetUtcNow()
            }, cancellationToken);
            DeleteFileIfExists(expired.StagedFilePath);
            throw new InvalidOperationException("Artifact upload session has expired.");
        }

        return session;
    }

    private async Task<CompleteArtifactUploadResponse> FailAsync(
        WorkspaceArtifactUploadSession session,
        WorkspaceArtifactDiagnostic diagnostic,
        CancellationToken cancellationToken) =>
        await FailAsync(session, [diagnostic], cancellationToken);

    private async Task<CompleteArtifactUploadResponse> FailAsync(
        WorkspaceArtifactUploadSession session,
        IReadOnlyList<WorkspaceArtifactDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        DeleteFileIfExists(session.StagedFilePath);
        var failed = await uploadStore.UpdateArtifactUploadSessionAsync(session with
        {
            Status = WorkspaceArtifactUploadStatus.Failed,
            StagedFilePath = null,
            Diagnostics = diagnostics,
            UpdatedAt = _timeProvider.GetUtcNow()
        }, cancellationToken);
        return new CompleteArtifactUploadResponse(failed.Id, failed.Status, null, Created: false, failed.Diagnostics);
    }

    private RegisterWorkspaceArtifactRequest CreateRegistrationRequest(
        DeploymentArtifactInspectionResult inspection,
        string storedPath,
        long sizeBytes,
        Guid actorAccountId)
    {
        var metadata = inspection.Metadata!;
        var manifestDigest = inspection.Checksums
            .Where(x => x.Kind == DeploymentArtifactEntryKind.Manifest && x.Status == DeploymentArtifactChecksumStatus.Verified)
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .Select(x => new WorkspaceArtifactDigest(ArtifactLayoutConstants.ChecksumAlgorithm, x.ActualDigest ?? x.ExpectedDigest ?? ""))
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Value));
        var displayMetadata = new ArtifactDisplayMetadata(
            metadata.Manifest.Name,
            metadata.Manifest.Version,
            null,
            metadata.Manifest.Labels,
            metadata.Manifest.Annotations,
            metadata.Manifest.Environment);
        var payloadReference = new ArtifactPayloadReference(
            "local",
            storedPath,
            "application/zip",
            sizeBytes,
            new ArtifactDigest(metadata.ContentDigest.Algorithm, metadata.ContentDigest.Value));

        return new RegisterWorkspaceArtifactRequest(
            metadata.ArtifactId,
            metadata.LayoutVersion,
            new WorkspaceArtifactDigest(metadata.ContentDigest.Algorithm, metadata.ContentDigest.Value),
            WorkspaceArtifactFormat.Zip,
            "local",
            storedPath,
            new WorkspaceArtifactManifestSummary(metadata.Manifest.Name, metadata.Manifest.Version, metadata.Manifest.Environment),
            metadata.Resources.Select(ToResourceSummary).ToList(),
            inspection.Diagnostics.Select(ToWorkspaceDiagnostic).ToList(),
            actorAccountId,
            ArtifactEnvelopeConstants.EnvelopeVersion,
            ArtifactTypeIds.ElsaLoomRecipe,
            ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion,
            manifestDigest,
            payloadReference,
            new ArtifactProducer("upload", "Elsa Platform Console", null, metadata.Source),
            displayMetadata,
            ArtifactEnvelopeDefaults.DefaultCompatibilityHints(ArtifactTypeIds.ElsaLoomRecipe));
    }

    private string CreateStagingPath(Guid workspaceId, Guid uploadId) =>
        Path.Combine(CreateWorkspaceTempRoot(workspaceId), "staging", $"{uploadId:N}.zip");

    private string CreateStoredArtifactPath(Guid workspaceId, string artifactId) =>
        Path.Combine(CreateWorkspaceStorageRoot(workspaceId), $"{SafeFileSegment(artifactId)}.zip");

    private string CreateWorkspaceTempRoot(Guid workspaceId) =>
        Path.Combine(LocalStorageRoot, workspaceId.ToString("N"), "tmp");

    private string CreateWorkspaceStorageRoot(Guid workspaceId) =>
        Path.Combine(LocalStorageRoot, workspaceId.ToString("N"), "artifacts");

    private string LocalStorageRoot =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(_options.LocalStorageRoot)
            ? Path.Combine(Path.GetTempPath(), "elsa-platform", "artifact-uploads")
            : _options.LocalStorageRoot);

    private long NormalizedMaxUploadBytes => _options.MaxUploadBytes <= 0 ? 52_428_800 : _options.MaxUploadBytes;

    private int NormalizedSessionTtlMinutes => _options.SessionTtlMinutes <= 0 ? 30 : _options.SessionTtlMinutes;

    private bool IsSampleGenerationEnabled => _options.SampleGenerationEnabled;

    private static WorkspaceArtifactResourceSummary ToResourceSummary(DeploymentArtifactResourceSummary resource) =>
        new(
            resource.Type,
            resource.LogicalId,
            resource.Scope,
            resource.Version,
            resource.DesiredStateHash is null ? null : new WorkspaceArtifactDigest(resource.DesiredStateHash.Value.Algorithm, resource.DesiredStateHash.Value.Value));

    private static WorkspaceArtifactDiagnostic ToWorkspaceDiagnostic(DeploymentDiagnostic diagnostic) =>
        new(
            diagnostic.Code,
            diagnostic.Severity == DeploymentDiagnosticSeverity.Error ? WorkspaceArtifactDiagnosticSeverity.Error : diagnostic.Severity == DeploymentDiagnosticSeverity.Warning ? WorkspaceArtifactDiagnosticSeverity.Warning : WorkspaceArtifactDiagnosticSeverity.Info,
            diagnostic.Message);

    private static WorkspaceArtifactDiagnostic Diagnostic(string code, string message) =>
        new(code, WorkspaceArtifactDiagnosticSeverity.Error, message);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeRequired(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(message) : value.Trim();

    private static bool IsZipFileName(string fileName) =>
        Path.GetExtension(fileName).Equals(".zip", StringComparison.OrdinalIgnoreCase);

    private static string SafeFileSegment(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-').ToArray();
        var segment = new string(chars).Trim('-', '.');
        return string.IsNullOrWhiteSpace(segment) ? Guid.NewGuid().ToString("N") : segment;
    }

    private static void DeleteFileIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            File.Delete(path);
    }
}
