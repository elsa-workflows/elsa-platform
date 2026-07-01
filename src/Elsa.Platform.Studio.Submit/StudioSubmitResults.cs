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
    DateTimeOffset? SubmittedAt = null,
    Guid? ArtifactRecordId = null,
    Guid? RevisionId = null)
{
    public bool Succeeded => Status is StudioSubmitStatus.Submitted or StudioSubmitStatus.Duplicate;
}

public enum StudioSubmitStatus
{
    Unknown = 0,
    Submitted = 1,
    Duplicate = 2,
    ValidationFailed = 3,
    Unauthorized = 4,
    Unavailable = 5,
    RetryableError = 6,
    Conflict = 7
}
