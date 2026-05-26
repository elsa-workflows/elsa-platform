using Elsa.Platform.Deployment.Artifacts;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class WorkspaceArtifactService(
    IWorkspaceArtifactStore store,
    IDeploymentArtifactReader artifactReader,
    TimeProvider? timeProvider = null)
{
    private static readonly string[] UnsafeTerms = ["password", "token", "secret value", "private key"];
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<IReadOnlyList<WorkspaceArtifact>> ListArtifactsAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        store.ListArtifactsAsync(workspaceId, cancellationToken);

    public Task<WorkspaceArtifact?> GetArtifactAsync(Guid workspaceId, Guid artifactRecordId, CancellationToken cancellationToken = default) =>
        store.GetArtifactAsync(workspaceId, artifactRecordId, cancellationToken);

    public async Task<WorkspaceArtifactRegistrationResult> RegisterArtifactAsync(
        Guid workspaceId,
        RegisterWorkspaceArtifactRequest request,
        CancellationToken cancellationToken = default)
    {
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
                x.DesiredStateHash is null ? null : new WorkspaceArtifactDigest(x.DesiredStateHash.Algorithm, x.DesiredStateHash.Value)))
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

    private static void ValidateRegistration(RegisterWorkspaceArtifactRequest request)
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
        if (ContainsUnsafeText(request.Reference) || request.Diagnostics.Any(x => ContainsUnsafeText(x.Message)))
            throw new InvalidOperationException("Artifact metadata contains unsafe secret-like content.");
    }

    private static bool IsSameArtifact(WorkspaceArtifact existing, RegisterWorkspaceArtifactRequest request) =>
        existing.LayoutVersion == request.LayoutVersion
        && existing.ContentDigest.Algorithm == request.ContentDigest.Algorithm
        && existing.ContentDigest.Value == request.ContentDigest.Value
        && existing.Format == request.Format
        && existing.ReferenceProvider == request.ReferenceProvider
        && existing.Reference == request.Reference;

    private static WorkspaceArtifactDiagnostic SafeDiagnostic(WorkspaceArtifactDiagnostic diagnostic) =>
        diagnostic with { Message = SafeMessage(diagnostic.Message) };

    private static WorkspaceArtifactDiagnostic Diagnostic(string code, WorkspaceArtifactDiagnosticSeverity severity, string message) =>
        new(code, severity, SafeMessage(message));

    private static bool ContainsUnsafeText(string value) =>
        UnsafeTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string SafeMessage(string value)
    {
        var safe = value.Trim();
        foreach (var term in UnsafeTerms)
            safe = safe.Replace(term, "[redacted]", StringComparison.OrdinalIgnoreCase);
        return safe.Length <= 512 ? safe : safe[..512];
    }

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
