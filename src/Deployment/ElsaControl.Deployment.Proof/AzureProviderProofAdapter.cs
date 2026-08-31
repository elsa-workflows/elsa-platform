using System.Security.Cryptography;
using System.Text;
using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Proof;

/// <summary>
/// Converts a proof-harness selection into the already admitted Azure provider projection.
/// The factory is deliberately injected: production admission owns manifest/signature
/// verification, while the proof harness only exercises the durable provider boundary.
/// </summary>
public interface IAzureProviderProofPlanFactory
{
    AzureProviderOperationSubmission Create(
        DeploymentProofSelection selection,
        DeploymentProofEnvironment environment);
}

/// <summary>
/// Optional workflow probe supplied by the disposable live-proof host. It receives only the
/// verified HTTPS endpoint; credential values and Azure SDK objects never cross this seam.
/// </summary>
public interface IAzureProviderProofWorkflowProbe
{
    Task<DeploymentProofWorkflow> RunAsync(
        string endpoint,
        DeploymentProofEnvironment environment,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Proof-provider adapter backed by the durable Azure operation service and executor. It keeps
/// the harness's identity continuity and cleanup contract while leaving live Azure mutations to
/// the injected <see cref="IAzureProviderRunner"/> implementation.
/// </summary>
public sealed class AzureProviderProofAdapter(
    Guid workspaceId,
    string templateFingerprint,
    IAzureProviderOperationService operationService,
    AzureProviderExecutor executor,
    IAzureProviderProofPlanFactory planFactory,
    IAzureProviderProofWorkflowProbe? workflowProbe = null,
    Func<CancellationToken, Task>? prepareCleanup = null) : IDeploymentProofProvider
{
    private readonly Dictionary<string, AzureProviderOperationSubmission> _submissions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AzureProviderOperation> _operations = new(StringComparer.Ordinal);

    public async Task<DeploymentProofSelection> SelectAsync(
        DeploymentProofInput input,
        DeploymentProofEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(environment);
        cancellationToken.ThrowIfCancellationRequested();
        if (workspaceId == Guid.Empty)
            throw new DeploymentProofStageException(DeploymentProofStage.Selection, "azure.proof.workspaceRequired", "An Azure proof workspace is required.");
        if (!string.Equals(environment.Provider, "azure", StringComparison.OrdinalIgnoreCase))
            throw new DeploymentProofStageException(DeploymentProofStage.Selection, "azure.proof.providerRequired", "The proof environment must use the Azure provider.");

        return new DeploymentProofSelection(
            SelectionId(input, environment),
            input.ElsaVersion,
            input.Topology,
            input.Features,
            input.ImageReference,
            input.ImageDigest);
    }

    public Task<DeploymentProofPlan> PlanAsync(
        DeploymentProofSelection selection,
        DeploymentProofEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(environment);
        cancellationToken.ThrowIfCancellationRequested();
        AzureProviderOperationSubmission submission;
        try
        {
            submission = planFactory.Create(selection, environment)
                ?? throw new InvalidOperationException("The Azure proof plan factory returned no admitted plan.");
        }
        catch (DeploymentProofStageException exception) when (exception.Stage == DeploymentProofStage.Plan)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new DeploymentProofStageException(DeploymentProofStage.Plan, "azure.proof.planUnavailable", "An admitted Azure proof plan could not be created.");
        }

        if (submission.Plan is null)
            throw new DeploymentProofStageException(DeploymentProofStage.Plan, "azure.proof.planUnavailable", "An admitted Azure proof plan could not be created.");

        var planImageReference = $"{submission.Plan.ImageRepository}@sha256:{submission.Plan.ImageDigest}";
        if (!string.Equals(submission.TemplateFingerprint, templateFingerprint, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(submission.Plan.ElsaVersion, selection.ElsaVersion, StringComparison.Ordinal) ||
            !string.Equals(submission.Plan.Topology, selection.Topology, StringComparison.Ordinal) ||
            !string.Equals(planImageReference, selection.ImageReference, StringComparison.Ordinal) ||
            !string.Equals($"sha256:{submission.Plan.ImageDigest}", selection.ImageDigest, StringComparison.OrdinalIgnoreCase))
            throw new DeploymentProofStageException(DeploymentProofStage.Plan, "azure.proof.planMismatch", "The admitted Azure plan does not match the selected immutable workload.");

        var planId = $"azure-{submission.Plan.Fingerprint}";
        _submissions[planId] = submission;
        return Task.FromResult(new DeploymentProofPlan(
            planId,
            selection.SelectionId,
            submission.Plan.Fingerprint,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider"] = "azure",
                ["location"] = submission.Plan.Location,
                ["releaseLine"] = submission.Plan.ReleaseLine
            }));
    }

    public async Task<DeploymentProofDeployment> ProvisionAsync(
        DeploymentProofPlan plan,
        DeploymentProofEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        var submission = GetSubmission(plan, DeploymentProofStage.Provision);
        var operation = await operationService.SubmitAsync(workspaceId, submission, cancellationToken);
        var execution = await executor.ApplyAsync(
            AzureProviderOperationService.CreateOperationRequest(
                workspaceId, submission.IdempotencyKey, templateFingerprint, submission.Plan,
                AzureProviderOperationAction.Reconcile, submission.ProviderScopeFingerprint),
            submission.Plan,
            cancellationToken);
        if (!execution.Succeeded)
            throw new DeploymentProofStageException(DeploymentProofStage.Provision, "azure.proof.provisionFailed", "The Azure provider did not complete the disposable workload operation.");

        operation = execution.Operation;
        var endpoint = operation.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new DeploymentProofStageException(DeploymentProofStage.Provision, "azure.proof.endpointMissing", "The Azure provider did not return a workload endpoint.");
        if (string.IsNullOrWhiteSpace(operation.Resources.WorkloadResourceId))
            throw new DeploymentProofStageException(DeploymentProofStage.Provision, "azure.proof.resourceMissing", "The Azure provider did not return an owned workload resource identity.");
        _operations[plan.PlanId] = operation;
        return new DeploymentProofDeployment(
            operation.Resources.WorkloadResourceId,
            endpoint,
            plan.PlanId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["operationId"] = operation.Id.ToString("N"),
                ["status"] = operation.Status.ToString()
            });
    }

    public Task<DeploymentProofHealth> WaitForHealthAsync(
        DeploymentProofDeployment deployment,
        DeploymentProofEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_operations.TryGetValue(deployment.PlanId, out var operation))
            throw new DeploymentProofStageException(DeploymentProofStage.Health, "azure.proof.operationMissing", "The Azure provider operation is not available for health verification.");

        var healthy = operation.Status == AzureProviderOperationStatus.Succeeded && operation.Health == AzureProviderHealth.Healthy;
        return Task.FromResult(new DeploymentProofHealth(
            healthy,
            operation.Endpoint ?? deployment.Endpoint,
            operation.Health.ToString(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["operationId"] = operation.Id.ToString("N")
            }));
    }

    public async Task<DeploymentProofWorkflow> RunWorkflowAsync(
        DeploymentProofHealth health,
        DeploymentProofEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        if (!health.Healthy)
            throw new DeploymentProofStageException(DeploymentProofStage.Workflow, "azure.proof.healthRequired", "The Azure workload must be healthy before workflow verification.");
        if (workflowProbe is null)
            throw new DeploymentProofStageException(DeploymentProofStage.Workflow, "azure.proof.workflowProbeRequired", "A disposable Azure workflow probe is not configured.");
        return await workflowProbe.RunAsync(health.Endpoint, environment, cancellationToken);
    }

    public async Task<DeploymentProofApply> ApplyAsync(
        DeploymentProofPlan plan,
        DeploymentProofEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        var submission = GetSubmission(plan, DeploymentProofStage.RepeatApply);
        var execution = await executor.ApplyAsync(
            AzureProviderOperationService.CreateOperationRequest(
                workspaceId, submission.IdempotencyKey, templateFingerprint, submission.Plan,
                AzureProviderOperationAction.Reconcile, submission.ProviderScopeFingerprint),
            submission.Plan,
            cancellationToken);
        _operations[plan.PlanId] = execution.Operation;
        return new(execution.Outcome == AzureProviderExecutionOutcome.Succeeded,
            execution.Outcome == AzureProviderExecutionOutcome.NoOp,
            plan.PlanId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["operationId"] = execution.Operation.Id.ToString("N"),
                ["outcome"] = execution.Outcome.ToString()
            });
    }

    public async Task<DeploymentProofCleanup> CleanupAsync(
        DeploymentProofPlan plan,
        DeploymentProofDeployment? deployment,
        DeploymentProofEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        if (prepareCleanup is not null)
            await prepareCleanup(cancellationToken);
        var submission = GetSubmission(plan, DeploymentProofStage.Cleanup);
        var operation = await operationService.SubmitDeleteAsync(workspaceId, submission, cancellationToken);
        var execution = await executor.DeleteAsync(
            AzureProviderOperationService.CreateOperationRequest(
                workspaceId, operation.IdempotencyKey, templateFingerprint, submission.Plan,
                AzureProviderOperationAction.Delete, submission.ProviderScopeFingerprint),
            submission.Plan,
            cancellationToken);
        if (!execution.Succeeded)
            throw new DeploymentProofStageException(DeploymentProofStage.Cleanup, "azure.proof.cleanupFailed", "The Azure provider did not confirm cleanup.");

        var resourceId = deployment?.ResourceId ?? execution.Operation.Resources.WorkloadResourceId;
        return new(true, resourceId, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["operationId"] = execution.Operation.Id.ToString("N")
        });
    }

    private AzureProviderOperationSubmission GetSubmission(DeploymentProofPlan plan, DeploymentProofStage stage)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return _submissions.TryGetValue(plan.PlanId, out var submission)
            ? submission
            : throw new DeploymentProofStageException(stage, "azure.proof.planMissing", "The admitted Azure proof plan is not available.");
    }

    private static string SelectionId(DeploymentProofInput input, DeploymentProofEnvironment environment)
    {
        var features = input.Features
            .Select(feature => feature.Trim())
            .Order(StringComparer.Ordinal)
            .ToArray();
        var canonical = string.Join("|", input.ElsaVersion, input.Topology, string.Join(",", features), input.ImageReference, input.ImageDigest, environment.Name, environment.Region);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
