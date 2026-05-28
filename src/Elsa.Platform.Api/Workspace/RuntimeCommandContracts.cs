using Elsa.Platform.Deployment.Core.Workspace;

namespace Elsa.Platform.Api.Workspace;

public sealed record RuntimeCommandListResponse(IReadOnlyList<RuntimeCommandDto> Commands);

public sealed record RuntimeCommandClaimRequest(Guid EngineId, string WorkerId, int LeaseSeconds = 300);

public sealed record RuntimeCommandClaimResponse(RuntimeCommandDto Command, string LeaseToken);

public sealed record RuntimeCommandHeartbeatRequest(string LeaseToken, string WorkerId);

public sealed record RuntimeCommandProgressRequest(string LeaseToken, string Status, int? PercentComplete, string Message);

public sealed record RuntimeCommandCompleteRequest(
    string LeaseToken,
    WorkspaceArtifactDigest? ObservedArtifactDigest,
    string? RuntimeReference,
    IReadOnlyList<DeploymentCommandDiagnostic> Diagnostics);

public sealed record RuntimeCommandFailRequest(string LeaseToken, IReadOnlyList<DeploymentCommandDiagnostic> Diagnostics);

public sealed record RuntimeCommandRejectRequest(string LeaseToken, IReadOnlyList<DeploymentCommandDiagnostic> Diagnostics);

public sealed record RuntimeCommandDto(
    Guid Id,
    Guid WorkspaceId,
    Guid RunId,
    Guid EnvironmentId,
    Guid EngineId,
    DeploymentCommandAction Action,
    DeploymentCommandStatus Status,
    DeploymentCommandArtifactReference? Artifact,
    DeploymentCommandRevisionReference? Revision,
    string IdempotencyKey,
    string? WorkerId,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? HeartbeatAt,
    int AttemptNumber,
    int? PercentComplete,
    string? ProgressMessage,
    WorkspaceArtifactDigest? ObservedArtifactDigest,
    string? RuntimeReference,
    IReadOnlyList<DeploymentCommandDiagnostic> Diagnostics,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? AvailableAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? CompletedAt)
{
    public static RuntimeCommandDto FromCommand(DeploymentCommand command) =>
        new(
            command.Id,
            command.WorkspaceId,
            command.RunId,
            command.EnvironmentId,
            command.EngineId,
            command.Action,
            command.Status,
            command.Artifact,
            command.Revision,
            command.IdempotencyKey,
            command.WorkerId,
            command.ClaimedAt,
            command.LeaseExpiresAt,
            command.HeartbeatAt,
            command.AttemptNumber,
            command.PercentComplete,
            command.ProgressMessage,
            command.ObservedArtifactDigest,
            command.RuntimeReference,
            command.Diagnostics,
            command.CreatedAt,
            command.UpdatedAt,
            command.AvailableAt,
            command.ExpiresAt,
            command.CompletedAt);
}
