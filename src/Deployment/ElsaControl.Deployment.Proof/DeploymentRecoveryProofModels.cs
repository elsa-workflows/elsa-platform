namespace ElsaControl.Deployment.Proof;

/// <summary>
/// The ordered gates in a restore-to-new proof. There is intentionally no cutover mutation
/// operation in this contract: the final gate only determines whether cutover would be safe.
/// </summary>
public enum DeploymentRecoveryStage
{
    RecoveryPointValidation,
    CreateIsolatedTarget,
    RestoreRelationalState,
    RebindExternalSecrets,
    ValidateImmutableInputs,
    TargetHealth,
    WorkflowValidation,
    CutoverEligibility,
    Cleanup
}

public enum DeploymentRecoveryStageStatus
{
    Passed,
    Failed,
    Skipped
}

public enum DeploymentRecoveryProofOutcome
{
    Passed,
    Failed
}

/// <summary>
/// An immutable artifact identity. The payload is deliberately absent; providers resolve
/// bytes from this opaque reference at their own boundary.
/// </summary>
public sealed record DeploymentRecoveryArtifact(
    string Reference,
    string Digest);

/// <summary>
/// A sealed recovery point containing only control-plane identities and cryptographic
/// digests. Provider resource identifiers and secret values cannot be represented here.
/// </summary>
public sealed record DeploymentRecoveryPoint
{
    public DeploymentRecoveryPoint(
        string organizationId,
        string workspaceId,
        string sourceInstanceId,
        string recoveryPointId,
        DateTimeOffset capturedAt,
        DateTimeOffset sourceQuiescedAt,
        DateTimeOffset restorePointAt,
        string sourceLifecycle,
        string manifestDigest,
        string desiredRevisionId,
        string desiredRevisionHash,
        string resolvedPlanReference,
        string resolvedPlanDigest,
        IReadOnlyList<DeploymentRecoveryArtifact>? artifacts,
        string providerSnapshotReference,
        string providerSnapshotDigest,
        IReadOnlyList<string>? requiredSecretReferenceKeys)
    {
        OrganizationId = organizationId;
        WorkspaceId = workspaceId;
        SourceInstanceId = sourceInstanceId;
        RecoveryPointId = recoveryPointId;
        CapturedAt = capturedAt;
        SourceQuiescedAt = sourceQuiescedAt;
        RestorePointAt = restorePointAt;
        SourceLifecycle = sourceLifecycle;
        ManifestDigest = manifestDigest;
        DesiredRevisionId = desiredRevisionId;
        DesiredRevisionHash = desiredRevisionHash;
        ResolvedPlanReference = resolvedPlanReference;
        ResolvedPlanDigest = resolvedPlanDigest;
        Artifacts = artifacts?.ToArray() ?? [];
        ProviderSnapshotReference = providerSnapshotReference;
        ProviderSnapshotDigest = providerSnapshotDigest;
        RequiredSecretReferenceKeys = requiredSecretReferenceKeys?.ToArray() ?? [];
    }

    public string OrganizationId { get; }

    public string WorkspaceId { get; }

    public string SourceInstanceId { get; }

    public string RecoveryPointId { get; }

    public DateTimeOffset CapturedAt { get; }

    public DateTimeOffset SourceQuiescedAt { get; }

    public DateTimeOffset RestorePointAt { get; }

    public string SourceLifecycle { get; }

    public string ManifestDigest { get; }

    public string DesiredRevisionId { get; }

    public string DesiredRevisionHash { get; }

    public string ResolvedPlanReference { get; }

    public string ResolvedPlanDigest { get; }

    public IReadOnlyList<DeploymentRecoveryArtifact> Artifacts { get; }

    public string ProviderSnapshotReference { get; }

    public string ProviderSnapshotDigest { get; }

    public IReadOnlyList<string> RequiredSecretReferenceKeys { get; }
}

/// <summary>
/// Logical identity of the newly created target. It is not a provider resource ID.
/// </summary>
public sealed record DeploymentRecoveryTarget(string InstanceId);

/// <summary>
/// Relational metadata read back from the new target after restore. Every immutable field is
/// repeated so the harness can compare the result with the sealed recovery point.
/// </summary>
public sealed record DeploymentRecoveryRestoredState
{
    public DeploymentRecoveryRestoredState(
        string targetInstanceId,
        string sourceInstanceId,
        string recoveryPointId,
        string desiredRevisionId,
        string desiredRevisionHash,
        string resolvedPlanReference,
        string resolvedPlanDigest,
        IReadOnlyList<DeploymentRecoveryArtifact>? artifacts,
        string providerSnapshotReference,
        string providerSnapshotDigest)
    {
        TargetInstanceId = targetInstanceId;
        SourceInstanceId = sourceInstanceId;
        RecoveryPointId = recoveryPointId;
        DesiredRevisionId = desiredRevisionId;
        DesiredRevisionHash = desiredRevisionHash;
        ResolvedPlanReference = resolvedPlanReference;
        ResolvedPlanDigest = resolvedPlanDigest;
        Artifacts = artifacts?.ToArray() ?? [];
        ProviderSnapshotReference = providerSnapshotReference;
        ProviderSnapshotDigest = providerSnapshotDigest;
    }

    public string TargetInstanceId { get; }

    public string SourceInstanceId { get; }

    public string RecoveryPointId { get; }

    public string DesiredRevisionId { get; }

    public string DesiredRevisionHash { get; }

    public string ResolvedPlanReference { get; }

    public string ResolvedPlanDigest { get; }

    public IReadOnlyList<DeploymentRecoveryArtifact> Artifacts { get; }

    public string ProviderSnapshotReference { get; }

    public string ProviderSnapshotDigest { get; }
}

/// <summary>
/// Secret rebind confirmation. Only logical reference keys cross this seam; values are never
/// accepted by the recovery proof contract.
/// </summary>
public sealed record DeploymentRecoverySecretRebind
{
    public DeploymentRecoverySecretRebind(IReadOnlyList<string>? referenceKeys)
    {
        ReferenceKeys = referenceKeys?.ToArray() ?? [];
    }

    public IReadOnlyList<string> ReferenceKeys { get; }
}

public sealed record DeploymentRecoveryValidation(bool Valid);

public sealed record DeploymentRecoveryHealth(bool Healthy, string Status = "");

public sealed record DeploymentRecoveryWorkflow(bool Succeeded, string Result = "");

public sealed record DeploymentRecoveryCutoverEligibility(bool Eligible);

public sealed record DeploymentRecoveryCleanup(bool Succeeded);

public sealed record DeploymentRecoveryStageResult(
    DeploymentRecoveryStage Stage,
    DeploymentRecoveryStageStatus Status,
    string Code,
    string Message,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyDictionary<string, string> Evidence)
{
    public TimeSpan Duration => CompletedAt - StartedAt;
}

/// <summary>
/// Provider-neutral restore-to-new proof output. A passed report means the target was healthy,
/// workflow-verified, within the measured RPO/RTO objectives, and safe to consider for a
/// separately governed cutover. It never performs that cutover.
/// </summary>
public sealed record DeploymentRecoveryProofReport(
    DeploymentRecoveryProofOutcome Outcome,
    DeploymentRecoveryPoint RecoveryPoint,
    DeploymentRecoveryTarget? Target,
    TimeSpan RpoAge,
    TimeSpan Rto,
    bool CutoverEligible,
    IReadOnlyList<DeploymentRecoveryStageResult> Stages)
{
    public bool Passed => Outcome == DeploymentRecoveryProofOutcome.Passed;

    public DeploymentRecoveryStageResult? Failure => Stages.FirstOrDefault(stage => stage.Status == DeploymentRecoveryStageStatus.Failed);

    public string ToJson() => DeploymentRecoveryProofEvidence.Serialize(this);
}

/// <summary>
/// The provider adapter seam for restore-to-new verification. None of the methods accepts or
/// returns provider resource IDs, credentials, or payload bytes.
/// </summary>
public interface IDeploymentRecoveryProvider
{
    Task<DeploymentRecoveryTarget> CreateIsolatedTargetAsync(
        DeploymentRecoveryPoint recoveryPoint,
        CancellationToken cancellationToken = default);

    Task<DeploymentRecoveryRestoredState> RestoreRelationalStateAsync(
        DeploymentRecoveryPoint recoveryPoint,
        DeploymentRecoveryTarget target,
        CancellationToken cancellationToken = default);

    Task<DeploymentRecoverySecretRebind> RebindExternalSecretsAsync(
        DeploymentRecoveryPoint recoveryPoint,
        DeploymentRecoveryTarget target,
        CancellationToken cancellationToken = default);

    Task<DeploymentRecoveryValidation> ValidateImmutableInputsAsync(
        DeploymentRecoveryPoint recoveryPoint,
        DeploymentRecoveryTarget target,
        DeploymentRecoveryRestoredState restoredState,
        DeploymentRecoverySecretRebind secretRebind,
        CancellationToken cancellationToken = default);

    Task<DeploymentRecoveryHealth> ValidateTargetHealthAsync(
        DeploymentRecoveryPoint recoveryPoint,
        DeploymentRecoveryTarget target,
        CancellationToken cancellationToken = default);

    Task<DeploymentRecoveryWorkflow> ValidateWorkflowAsync(
        DeploymentRecoveryPoint recoveryPoint,
        DeploymentRecoveryTarget target,
        DeploymentRecoveryHealth health,
        CancellationToken cancellationToken = default);

    Task<DeploymentRecoveryCutoverEligibility> EvaluateCutoverEligibilityAsync(
        DeploymentRecoveryPoint recoveryPoint,
        DeploymentRecoveryTarget target,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up only the target returned by <see cref="CreateIsolatedTargetAsync"/>. There is
    /// no source-instance parameter by design, so source cleanup cannot be represented here.
    /// </summary>
    Task<DeploymentRecoveryCleanup> CleanupAsync(
        DeploymentRecoveryTarget target,
        CancellationToken cancellationToken = default);
}

public sealed class DeploymentRecoveryStageException(
    DeploymentRecoveryStage stage,
    string code,
    string message) : Exception(message)
{
    public DeploymentRecoveryStage Stage { get; } = stage;

    public string Code { get; } = code;
}
