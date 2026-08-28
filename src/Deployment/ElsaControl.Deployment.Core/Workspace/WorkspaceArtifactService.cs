using ElsaControl.Deployment.Artifacts;

namespace ElsaControl.Deployment.Core.Workspace;

public sealed class WorkspaceArtifactService(
    IWorkspaceArtifactStore store,
    IDeploymentArtifactReader artifactReader,
    TimeProvider? timeProvider = null,
    ArtifactEnvelopeValidator? envelopeValidator = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ArtifactEnvelopeValidator _envelopeValidator = envelopeValidator ?? new ArtifactEnvelopeValidator(new ArtifactTypeRegistry());

    public Task<IReadOnlyList<WorkspaceArtifact>> ListArtifactsAsync(Guid workspaceId, bool includeArchived = false, CancellationToken cancellationToken = default) =>
        store.ListArtifactsAsync(workspaceId, includeArchived, cancellationToken);

    public Task<WorkspaceArtifact?> GetArtifactAsync(Guid workspaceId, Guid artifactRecordId, CancellationToken cancellationToken = default) =>
        store.GetArtifactAsync(workspaceId, artifactRecordId, cancellationToken);

    public Task<WorkspaceArtifact> ArchiveArtifactAsync(Guid workspaceId, Guid artifactRecordId, Guid actorAccountId, CancellationToken cancellationToken = default) =>
        store.ArchiveArtifactAsync(workspaceId, artifactRecordId, actorAccountId, cancellationToken);

    public Task<WorkspaceArtifact> RestoreArtifactAsync(Guid workspaceId, Guid artifactRecordId, CancellationToken cancellationToken = default) =>
        store.RestoreArtifactAsync(workspaceId, artifactRecordId, cancellationToken);

    public async Task<WorkspaceArtifactDownload> OpenDownloadAsync(
        Guid workspaceId,
        Guid artifactRecordId,
        CancellationToken cancellationToken = default)
    {
        var artifact = await store.GetArtifactAsync(workspaceId, artifactRecordId, cancellationToken)
            ?? throw new KeyNotFoundException("Artifact does not exist in the workspace.");
        if (!artifact.ReferenceProvider.Equals("local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Artifact reference provider is not downloadable.");

        var path = ResolveLocalPath(artifact.Reference);
        if (path is null || !File.Exists(path))
            throw new FileNotFoundException("Artifact file is unavailable.");
        if (Directory.Exists(path))
            throw new InvalidOperationException("Artifact folders cannot be downloaded from this endpoint.");

        var fileName = Path.GetFileName(path);
        var contentType = artifact.Format == WorkspaceArtifactFormat.Zip ? "application/zip" : "application/octet-stream";
        return new WorkspaceArtifactDownload(fileName, contentType, File.OpenRead(path));
    }

    public async Task<WorkspaceArtifactRegistrationResult> RegisterArtifactAsync(
        Guid workspaceId,
        RegisterWorkspaceArtifactRequest request,
        CancellationToken cancellationToken = default)
    {
        request = NormalizeRegistration(request);
        ValidateRegistration(request);
        var existing = await store.FindArtifactByIdentityAsync(workspaceId, request.ArtifactId, cancellationToken);
        if (existing is not null)
        {
            if (IsSameArtifact(existing, request))
                return new WorkspaceArtifactRegistrationResult(existing, Created: false);
            throw new InvalidOperationException("Artifact identity is already registered with different metadata.");
        }

        return new WorkspaceArtifactRegistrationResult(await store.RegisterArtifactAsync(workspaceId, request, cancellationToken), Created: true);
    }

    public async Task<WorkspaceArtifactInspectionResult> RefreshInspectionAsync(
        Guid workspaceId,
        Guid artifactRecordId,
        CancellationToken cancellationToken = default)
    {
        var artifact = await store.GetArtifactAsync(workspaceId, artifactRecordId, cancellationToken)
            ?? throw new KeyNotFoundException("Artifact does not exist in the workspace.");
        var inspectedAt = _timeProvider.GetUtcNow();

        if (!artifact.ReferenceProvider.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            return await store.UpdateArtifactInspectionAsync(
                workspaceId,
                new WorkspaceArtifactInspectionUpdate(
                    artifact.Id,
                    artifact.ArtifactId,
                    WorkspaceArtifactChecksumStatus.Unavailable,
                    WorkspaceArtifactInspectionStatus.Unsupported,
                    inspectedAt,
                    artifact.Resources,
                    [Diagnostic("artifact.reference.unsupported", WorkspaceArtifactDiagnosticSeverity.Warning, "Artifact reference provider is not supported for inspection refresh.")]),
                cancellationToken);
        }

        var path = ResolveLocalPath(artifact.Reference);
        if (path is null || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return await store.UpdateArtifactInspectionAsync(
                workspaceId,
                new WorkspaceArtifactInspectionUpdate(
                    artifact.Id,
                    artifact.ArtifactId,
                    WorkspaceArtifactChecksumStatus.Unavailable,
                    WorkspaceArtifactInspectionStatus.Unavailable,
                    inspectedAt,
                    artifact.Resources,
                    [Diagnostic("artifact.reference.unavailable", WorkspaceArtifactDiagnosticSeverity.Error, "Artifact reference is unavailable.")]),
                cancellationToken);
        }

        var inspection = artifact.Format == WorkspaceArtifactFormat.Zip
            ? await artifactReader.InspectZipAsync(path, cancellationToken)
            : await artifactReader.InspectFolderAsync(path, cancellationToken);
        var diagnostics = inspection.Diagnostics
            .Select(x => Diagnostic(x.Code, x.Severity.ToString().Equals("Error", StringComparison.OrdinalIgnoreCase) ? WorkspaceArtifactDiagnosticSeverity.Error : WorkspaceArtifactDiagnosticSeverity.Warning, x.Message))
            .ToList();
        var identityMatches = string.Equals(inspection.ArtifactId, artifact.ArtifactId, StringComparison.Ordinal);
        if (!identityMatches)
            diagnostics.Add(Diagnostic("artifact.identity.mismatch", WorkspaceArtifactDiagnosticSeverity.Error, "Referenced artifact identity does not match the registered identity."));

        var checksumStatus = inspection.Checksums.Any(x => x.Status == DeploymentArtifactChecksumStatus.Mismatched)
            ? WorkspaceArtifactChecksumStatus.Mismatched
            : inspection.Checksums.Any(x => x.Status == DeploymentArtifactChecksumStatus.Missing)
                ? WorkspaceArtifactChecksumStatus.Missing
                : inspection.Checksums.Any(x => x.Status == DeploymentArtifactChecksumStatus.Unexpected)
                    ? WorkspaceArtifactChecksumStatus.Unexpected
                    : WorkspaceArtifactChecksumStatus.Verified;
        var inspectionStatus = inspection.Succeeded && identityMatches
            ? WorkspaceArtifactInspectionStatus.Valid
            : WorkspaceArtifactInspectionStatus.Invalid;
        var resources = inspection.Metadata?.Resources
            .Select(x => new WorkspaceArtifactResourceSummary(
                x.Type,
                x.LogicalId,
                x.Scope,
                x.Version,
                x.DesiredStateHash is null ? null : new WorkspaceArtifactDigest(x.DesiredStateHash.Value.Algorithm, x.DesiredStateHash.Value.Value)))
            .ToList() ?? artifact.Resources;

        return await store.UpdateArtifactInspectionAsync(
            workspaceId,
            new WorkspaceArtifactInspectionUpdate(
                artifact.Id,
                artifact.ArtifactId,
                checksumStatus,
                inspectionStatus,
                inspectedAt,
                resources,
                diagnostics.Select(SafeDiagnostic).ToList()),
            cancellationToken);
    }

    private RegisterWorkspaceArtifactRequest NormalizeRegistration(RegisterWorkspaceArtifactRequest request)
    {
        var artifactTypeId = ArtifactEnvelopeDefaults.ArtifactTypeIdOrDefault(request.ArtifactTypeId);
        var producer = request.Producer ?? ArtifactEnvelopeDefaults.DefaultProducer();
        var displayMetadata = request.DisplayMetadata ?? new ArtifactDisplayMetadata(
            request.Manifest.Name,
            request.Manifest.Version,
            null,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            request.Manifest.Environment);
        var payloadReference = request.PayloadReference ?? new ArtifactPayloadReference(request.ReferenceProvider, request.Reference);
        var compatibilityHints = ArtifactEnvelopeDefaults.NormalizeCompatibilityHints(artifactTypeId, request.CompatibilityHints);

        return request with
        {
            EnvelopeVersion = string.IsNullOrWhiteSpace(request.EnvelopeVersion) ? ArtifactEnvelopeConstants.EnvelopeVersion : request.EnvelopeVersion.Trim(),
            ArtifactTypeId = artifactTypeId,
            ArtifactSchemaVersion = string.IsNullOrWhiteSpace(request.ArtifactSchemaVersion) ? ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion : request.ArtifactSchemaVersion.Trim(),
            PayloadReference = payloadReference,
            Producer = producer,
            DisplayMetadata = displayMetadata,
            CompatibilityHints = compatibilityHints
        };
    }

    private void ValidateRegistration(RegisterWorkspaceArtifactRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ArtifactId))
            throw new InvalidOperationException("Artifact identity is required.");
        if (string.IsNullOrWhiteSpace(request.LayoutVersion))
            throw new InvalidOperationException("Artifact layout version is required.");
        if (!request.LayoutVersion.Equals(ArtifactLayoutConstants.LayoutVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("Artifact layout version is not supported.");
        if (string.IsNullOrWhiteSpace(request.ContentDigest.Algorithm) || string.IsNullOrWhiteSpace(request.ContentDigest.Value))
            throw new InvalidOperationException("Artifact content digest is required.");
        if (string.IsNullOrWhiteSpace(request.ReferenceProvider) || string.IsNullOrWhiteSpace(request.Reference))
            throw new InvalidOperationException("Artifact reference is required.");
        _envelopeValidator.Validate(ToEnvelope(request));
    }

    private static bool IsSameArtifact(WorkspaceArtifact existing, RegisterWorkspaceArtifactRequest request) =>
        existing.LayoutVersion == request.LayoutVersion
        && existing.ContentDigest.Algorithm == request.ContentDigest.Algorithm
        && existing.ContentDigest.Value == request.ContentDigest.Value
        && existing.Format == request.Format
        && existing.ReferenceProvider == request.ReferenceProvider
        && existing.Reference == request.Reference
        && System.Text.Json.JsonSerializer.Serialize(existing.ToEnvelope()) == System.Text.Json.JsonSerializer.Serialize(ToEnvelope(request));

    private WorkspaceArtifactDiagnostic SafeDiagnostic(WorkspaceArtifactDiagnostic diagnostic) =>
        diagnostic with { Message = _envelopeValidator.SafeMessage(diagnostic.Message) };

    private WorkspaceArtifactDiagnostic Diagnostic(string code, WorkspaceArtifactDiagnosticSeverity severity, string message) =>
        new(code, severity, _envelopeValidator.SafeMessage(message));

    private static ArtifactEnvelope ToEnvelope(RegisterWorkspaceArtifactRequest request) =>
        ArtifactEnvelopeDefaults.CreateEnvelope(
            request.ArtifactId,
            request.EnvelopeVersion,
            request.ArtifactTypeId,
            request.ArtifactSchemaVersion,
            new ElsaControl.Deployment.Abstractions.Artifacts.ArtifactDigest(request.ContentDigest.Algorithm, request.ContentDigest.Value),
            request.ManifestDigest is null ? null : new ElsaControl.Deployment.Abstractions.Artifacts.ArtifactDigest(request.ManifestDigest.Algorithm, request.ManifestDigest.Value),
            request.PayloadReference,
            request.Producer,
            request.DisplayMetadata,
            request.CompatibilityHints,
            request.Diagnostics.Select(x => new ArtifactEnvelopeDiagnostic(x.Code, x.Severity.ToEnvelopeSeverity(), x.Message)).ToList(),
            new ArtifactEnvelopeFallback(request.ReferenceProvider, request.Reference, request.Manifest.Name, request.Manifest.Version, request.Manifest.Environment));

    private static string? ResolveLocalPath(string reference)
    {
        if (Uri.TryCreate(reference, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
                return uri.LocalPath;
            if (uri.Scheme.Equals("local", StringComparison.OrdinalIgnoreCase))
                return uri.LocalPath;
            return null;
        }

        return Path.IsPathRooted(reference) ? reference : null;
    }
}
