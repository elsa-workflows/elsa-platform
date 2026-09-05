namespace ElsaControl.Deployment.Core.Instances;

/// <summary>Lists durable lifecycle operations waiting for provider observation.</summary>
public interface IElsaInstanceProviderPendingOperationStore
{
    Task<IReadOnlyList<ElsaInstanceProviderPendingOperation>> ListPendingProviderOperationsAsync(
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A durable lifecycle reservation that still needs the provider hand-off or
/// provider observation. The optional submission is reconstructed from the
/// persisted, already-admitted plan; it is intentionally absent when the store
/// cannot prove that reconstruction is safe.
/// </summary>
public sealed record ElsaInstanceProviderPendingOperation(
    Guid WorkspaceId,
    Guid OperationId,
    ElsaInstanceProviderSubmission? Submission = null,
    ElsaInstanceProviderRecoveryEnvelope? Recovery = null);
