using Elsa.Platform.Deployment.Artifacts;

namespace Elsa.Platform.Studio.Submit;

public sealed record StudioSubmitPackage(
    ArtifactEnvelope Envelope,
    string WorkflowDefinitionJson,
    DateTimeOffset PackagedAt);

public sealed record StudioSubmitResult(
    StudioSubmitStatus Status,
    string Message,
    string? ArtifactId = null,
    string? ArtifactDigest = null,
    DateTimeOffset? SubmittedAt = null)
{
    public bool Succeeded => Status is StudioSubmitStatus.Submitted or StudioSubmitStatus.Duplicate;
}

public enum StudioSubmitStatus
{
    Submitted,
    Duplicate,
    ValidationFailed,
    Unauthorized,
    Unavailable,
    RetryableError,
    Conflict
}
