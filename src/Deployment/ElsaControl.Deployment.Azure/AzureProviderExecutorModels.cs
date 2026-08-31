namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Coarse lifecycle operations understood by the Azure provider runner. The runner maps these
/// steps to the checked-in Bicep and runbook authority; it does not receive raw ARM payloads.
/// </summary>
public enum AzureProviderRunnerStep
{
    Foundation,
    AcrPull,
    SeedSecrets,
    SqlBootstrap,
    Workload,
    Health,
    Promotion,
    RestoreStableTraffic,
    Cleanup
}

public enum AzureProviderRunnerOutcome
{
    Completed,
    /// <summary>
    /// The runner observed the same postcondition required for <see cref="Completed"/> without
    /// issuing a new mutation. A no-op must return the same complete safe resource observations
    /// as a completed step; absence of evidence is not convergence.
    /// </summary>
    NoOp,
    Failed,
    Uncertain
}

/// <summary>
/// Safe execution context passed to the provider implementation. All persisted values are
/// already bounded by <see cref="AzureProviderOperationValidation"/>; secret values are not
/// representable and the plan contains references only.
/// </summary>
public sealed record AzureProviderRunnerCommand(
    AzureProviderRunnerStep Step,
    AzureWorkloadPlan Plan,
    AzureProviderResourceReferences Resources,
    string? StableTrafficRevisionName,
    bool IsResume,
    int AttemptNumber,
    AzureProviderExecutionContext Context);

/// <summary>
/// Safe durable correlation supplied to every runner step. Target Azure scope remains explicit
/// runner configuration, while this context binds every mutation and observation to the exact
/// accepted operation and immutable plan/template identities.
/// </summary>
public sealed record AzureProviderExecutionContext(
    Guid WorkspaceId,
    Guid OperationId,
    string OperationIdentity,
    string IdempotencyKey,
    string TargetKey,
    string PlanFingerprint,
    string TemplateFingerprint,
    string? ProviderScopeFingerprint);

/// <summary>
/// Safe result returned by a provider step. A failed result means the provider knows the step
/// did not complete. An uncertain result means an external side effect may have committed and
/// the durable operation must be recovered before another mutation is attempted. For a
/// reference-only checkpoint, a null endpoint or Unknown health means that the step did not
/// provide a new observation; the prior durable observation is retained.
/// </summary>
public sealed record AzureProviderRunnerResult(
    AzureProviderRunnerOutcome Outcome,
    AzureProviderOperationPhase Phase,
    AzureProviderResourceReferences Resources,
    AzureProviderHealth Health,
    string? Endpoint,
    IReadOnlyList<AzureProviderDiagnostic> Diagnostics,
    string Code,
    string Message,
    bool OwnedResourcesAbsent = false,
    bool StableTrafficRestored = false);

public sealed record AzureProviderExecutionRequest(
    AzureProviderOperationRequest Operation,
    AzureWorkloadPlan Plan);

public enum AzureProviderExecutionOutcome
{
    Succeeded,
    NoOp,
    InProgress,
    Failed,
    RecoveryRequired
}

public sealed record AzureProviderExecutionResult(
    AzureProviderOperation Operation,
    AzureProviderExecutionOutcome Outcome,
    string Code,
    string Message)
{
    public bool Succeeded => Outcome is AzureProviderExecutionOutcome.Succeeded or AzureProviderExecutionOutcome.NoOp;
}

/// <summary>
/// Adapter over the checked-in Azure Bicep/runbook lifecycle. Implementations may use Azure CLI,
/// an SDK or a remote worker, but they must return only the safe result contract above.
/// </summary>
public interface IAzureProviderRunner
{
    Task<AzureProviderRunnerResult> RunAsync(
        AzureProviderRunnerCommand command,
        CancellationToken cancellationToken = default);
}
