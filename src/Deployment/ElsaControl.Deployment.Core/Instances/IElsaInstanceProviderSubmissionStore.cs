namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Durable hand-off marker for provider submission. Implementations move the
/// reserved lifecycle run to recovery-required after a provider operation has a
/// stable correlation, so reconciliation can resume after a process restart.
/// </summary>
public interface IElsaInstanceProviderSubmissionStore
{
    Task CommitProviderSubmissionAsync(
        ElsaInstanceProviderSubmissionCommit commit,
        CancellationToken cancellationToken = default);
}

public sealed record ElsaInstanceProviderSubmissionCommit(
    Guid WorkspaceId,
    Guid InstanceId,
    Guid OperationId,
    int AttemptNumber,
    string CorrelationId,
    DateTimeOffset SubmittedAt)
{
    public void Validate()
    {
        if (WorkspaceId == Guid.Empty || InstanceId == Guid.Empty || OperationId == Guid.Empty || AttemptNumber < 1 ||
            string.IsNullOrWhiteSpace(CorrelationId) || CorrelationId.Length > 128 ||
            CorrelationId.Any(char.IsControl) || CorrelationId.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("Provider submission commit identity is invalid.");
    }
}
