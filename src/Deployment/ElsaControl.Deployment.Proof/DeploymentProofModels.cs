namespace ElsaControl.Deployment.Proof;

/// <summary>
/// The seam at which a deployment-proof run failed or completed.
/// </summary>
public enum DeploymentProofStage
{
    Selection,
    Plan,
    Provision,
    Health,
    Workflow,
    RepeatApply,
    Cleanup
}

public enum DeploymentProofStageStatus
{
    Passed,
    Failed,
    Skipped
}

public enum DeploymentProofOutcome
{
    Passed,
    Failed
}

/// <summary>
/// Exact, reproducible workload intent. The image digest is required separately from the
/// repository reference so a tag can never be mistaken for an immutable release identity.
/// </summary>
public sealed record DeploymentProofInput
{
    public DeploymentProofInput(
        string elsaVersion,
        string topology,
        IReadOnlyList<string> features,
        string imageReference,
        string imageDigest,
        string sourceCommit = "")
    {
        ElsaVersion = elsaVersion;
        Topology = topology;
        Features = features is null
            ? throw new ArgumentNullException(nameof(features))
            : features.ToArray();
        ImageReference = imageReference;
        ImageDigest = imageDigest;
        SourceCommit = sourceCommit;
    }

    public string ElsaVersion { get; }

    public string Topology { get; }

    public IReadOnlyList<string> Features { get; }

    public string ImageReference { get; }

    public string ImageDigest { get; }

    public string SourceCommit { get; }
}

/// <summary>
/// Disposable execution context. Secret values are intentionally not representable here;
/// providers resolve credentials at their own execution boundary from these reference names.
/// </summary>
public sealed record DeploymentProofEnvironment
{
    public DeploymentProofEnvironment(
        string name,
        string region,
        string provider,
        IReadOnlyList<string> secretReferenceNames)
    {
        Name = name;
        Region = region;
        Provider = provider;
        SecretReferenceNames = secretReferenceNames is null
            ? throw new ArgumentNullException(nameof(secretReferenceNames))
            : secretReferenceNames
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .Select(reference => reference.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    public string Name { get; }

    public string Region { get; }

    public string Provider { get; }

    public IReadOnlyList<string> SecretReferenceNames { get; }
}

public sealed record DeploymentProofSelection(
    string SelectionId,
    string ElsaVersion,
    string Topology,
    IReadOnlyList<string> Features,
    string ImageReference,
    string ImageDigest);

public sealed record DeploymentProofPlan(
    string PlanId,
    string SelectionId,
    string Fingerprint,
    IReadOnlyDictionary<string, string> SafeMetadata);

public sealed record DeploymentProofDeployment(
    string ResourceId,
    string Endpoint,
    string PlanId,
    IReadOnlyDictionary<string, string> SafeMetadata);

public sealed record DeploymentProofHealth(
    bool Healthy,
    string Endpoint,
    string Status,
    IReadOnlyDictionary<string, string> SafeMetadata);

public sealed record DeploymentProofWorkflow(
    string WorkflowId,
    bool Succeeded,
    string Result,
    IReadOnlyDictionary<string, string> SafeMetadata);

public sealed record DeploymentProofApply(
    bool Applied,
    bool NoOp,
    string PlanId,
    IReadOnlyDictionary<string, string> SafeMetadata);

public sealed record DeploymentProofCleanup(
    bool Succeeded,
    string? ResourceId,
    IReadOnlyDictionary<string, string> SafeMetadata);

public sealed record DeploymentProofStageResult(
    DeploymentProofStage Stage,
    DeploymentProofStageStatus Status,
    string Code,
    string Message,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyDictionary<string, string> Evidence)
{
    public TimeSpan Duration => CompletedAt - StartedAt;
}

public sealed record DeploymentProofReport(
    DeploymentProofOutcome Outcome,
    DeploymentProofInput Input,
    DeploymentProofEnvironment Environment,
    IReadOnlyList<DeploymentProofStageResult> Stages)
{
    public bool Passed => Outcome == DeploymentProofOutcome.Passed;

    public DeploymentProofStageResult? Failure => Stages.FirstOrDefault(stage => stage.Status == DeploymentProofStageStatus.Failed);

    public string ToJson() => DeploymentProofEvidence.Serialize(this);
}

/// <summary>
/// Public provider adapter seam. A real Azure implementation can satisfy this contract without
/// changing the proof orchestration or evidence format.
/// </summary>
public interface IDeploymentProofProvider
{
    Task<DeploymentProofSelection> SelectAsync(DeploymentProofInput input, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default);

    Task<DeploymentProofPlan> PlanAsync(DeploymentProofSelection selection, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default);

    Task<DeploymentProofDeployment> ProvisionAsync(DeploymentProofPlan plan, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default);

    Task<DeploymentProofHealth> WaitForHealthAsync(DeploymentProofDeployment deployment, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default);

    Task<DeploymentProofWorkflow> RunWorkflowAsync(DeploymentProofHealth health, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default);

    Task<DeploymentProofApply> ApplyAsync(DeploymentProofPlan plan, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up the disposable target represented by <paramref name="plan"/>. The deployment
    /// result is optional because a provider may have created resources before a provision
    /// operation reported failure; in that case it derives the target from the plan and
    /// environment.
    /// </summary>
    Task<DeploymentProofCleanup> CleanupAsync(
        DeploymentProofPlan plan,
        DeploymentProofDeployment? deployment,
        DeploymentProofEnvironment environment,
        CancellationToken cancellationToken = default);
}

public sealed class DeploymentProofStageException(
    DeploymentProofStage stage,
    string code,
    string message) : Exception(message)
{
    public DeploymentProofStage Stage { get; } = stage;

    public string Code { get; } = code;
}
