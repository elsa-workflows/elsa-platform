namespace ElsaControl.Deployment.Core.Workspace;

public enum WorkspaceArtifactUploadStatus
{
    Pending,
    Uploading,
    Uploaded,
    Inspecting,
    Completed,
    Failed,
    Aborted,
    Expired
}

public sealed class ArtifactUploadOptions
{
    public string? LocalStorageRoot { get; set; }

    public long MaxUploadBytes { get; set; } = 52_428_800;

    public int SessionTtlMinutes { get; set; } = 30;

    public bool SampleGenerationEnabled { get; set; }
}

public sealed record WorkspaceArtifactUploadCapabilities(
    long MaxUploadBytes,
    bool SampleArtifactGenerationEnabled);

public sealed record WorkspaceArtifactUploadSession(
    Guid Id,
    Guid WorkspaceId,
    WorkspaceArtifactUploadStatus Status,
    string? FileName,
    string? ContentType,
    long? DeclaredSizeBytes,
    long? UploadedSizeBytes,
    string? StagedFilePath,
    string? IdempotencyKey,
    IReadOnlyList<WorkspaceArtifactDiagnostic> Diagnostics,
    DateTimeOffset ExpiresAt,
    Guid? CompletedArtifactRecordId,
    Guid CreatedByAccountId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateArtifactUploadRequest(
    string FileName,
    string? ContentType,
    long SizeBytes,
    string? IdempotencyKey = null);

public sealed record CreateArtifactUploadResponse(
    Guid UploadId,
    WorkspaceArtifactUploadStatus Status,
    DateTimeOffset ExpiresAt,
    long MaxUploadBytes);

public sealed record CompleteArtifactUploadResponse(
    Guid UploadId,
    WorkspaceArtifactUploadStatus Status,
    WorkspaceArtifact? Artifact,
    bool Created,
    IReadOnlyList<WorkspaceArtifactDiagnostic> Diagnostics);

public sealed record CreateSampleArtifactRequest(
    string ArtifactName,
    string Version,
    string Environment,
    string WorkflowId);

public interface IWorkspaceArtifactUploadStore
{
    Task<WorkspaceArtifactUploadSession> CreateArtifactUploadSessionAsync(
        WorkspaceArtifactUploadSession session,
        CancellationToken cancellationToken = default);

    Task<WorkspaceArtifactUploadSession?> GetArtifactUploadSessionAsync(
        Guid workspaceId,
        Guid uploadId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceArtifactUploadSession?> FindArtifactUploadByIdempotencyKeyAsync(
        Guid workspaceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<WorkspaceArtifactUploadSession> UpdateArtifactUploadSessionAsync(
        WorkspaceArtifactUploadSession session,
        CancellationToken cancellationToken = default);
}
