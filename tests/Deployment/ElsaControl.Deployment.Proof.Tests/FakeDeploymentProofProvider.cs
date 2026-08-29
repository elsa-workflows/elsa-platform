using System.Security.Cryptography;
using System.Text;
using ElsaControl.Deployment.Proof;

namespace ElsaControl.Deployment.Proof.Tests;

internal sealed class FakeDeploymentProofProvider(
    IReadOnlySet<DeploymentProofStage>? failures = null,
    IReadOnlyDictionary<string, string>? extraMetadata = null) : IDeploymentProofProvider
{
    private readonly IReadOnlySet<DeploymentProofStage> _failures = failures ?? new HashSet<DeploymentProofStage>();
    private readonly IReadOnlyDictionary<string, string> _extraMetadata = extraMetadata ?? new Dictionary<string, string>();
    private readonly HashSet<string> _appliedPlans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _partialProvisionResources = new(StringComparer.Ordinal);
    private readonly List<string> _cleanupResourceIds = [];

    public int CleanupCalls { get; private set; }

    public IReadOnlyList<string> CleanupResourceIds => _cleanupResourceIds;

    public Task<DeploymentProofSelection> SelectAsync(DeploymentProofInput input, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured(DeploymentProofStage.Selection);
        return Task.FromResult(new DeploymentProofSelection(
            "selection-3.8-combined",
            input.ElsaVersion,
            input.Topology,
            input.Features,
            input.ImageReference,
            input.ImageDigest));
    }

    public Task<DeploymentProofPlan> PlanAsync(DeploymentProofSelection selection, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured(DeploymentProofStage.Plan);
        var canonical = string.Join("|", [selection.ElsaVersion, selection.Topology, string.Join(",", selection.Features), selection.ImageReference, selection.ImageDigest]);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return Task.FromResult(new DeploymentProofPlan(
            "plan-3.8-combined",
            selection.SelectionId,
            fingerprint,
            _extraMetadata));
    }

    public Task<DeploymentProofDeployment> ProvisionAsync(DeploymentProofPlan plan, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default)
    {
        var resourceId = "fake-resource-3.8-combined";
        if (_failures.Contains(DeploymentProofStage.Provision))
        {
            // Model ARM returning an error after creating a disposable resource. The harness
            // must still offer cleanup with only the plan available.
            _partialProvisionResources[plan.PlanId] = resourceId;
            ThrowIfConfigured(DeploymentProofStage.Provision);
        }

        _appliedPlans.Add(plan.PlanId);
        return Task.FromResult(new DeploymentProofDeployment(
            resourceId,
            "https://fake-elsa-3-8-combined.example.test",
            plan.PlanId,
            _extraMetadata));
    }

    public Task<DeploymentProofHealth> WaitForHealthAsync(DeploymentProofDeployment deployment, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured(DeploymentProofStage.Health);
        return Task.FromResult(new DeploymentProofHealth(true, deployment.Endpoint, "healthy", _extraMetadata));
    }

    public Task<DeploymentProofWorkflow> RunWorkflowAsync(DeploymentProofHealth health, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured(DeploymentProofStage.Workflow);
        return Task.FromResult(new DeploymentProofWorkflow("proof-workflow", true, "completed", _extraMetadata));
    }

    public Task<DeploymentProofApply> ApplyAsync(DeploymentProofPlan plan, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured(DeploymentProofStage.RepeatApply);
        var applied = _appliedPlans.Add(plan.PlanId);
        return Task.FromResult(new DeploymentProofApply(applied, !applied, plan.PlanId, _extraMetadata));
    }

    public Task<DeploymentProofCleanup> CleanupAsync(DeploymentProofPlan plan, DeploymentProofDeployment? deployment, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default)
    {
        CleanupCalls++;
        ThrowIfConfigured(DeploymentProofStage.Cleanup);
        var resourceId = deployment?.ResourceId
            ?? _partialProvisionResources.GetValueOrDefault(plan.PlanId)
            ?? $"fake-resource-for-{plan.PlanId}";
        _cleanupResourceIds.Add(resourceId);
        return Task.FromResult(new DeploymentProofCleanup(true, resourceId, _extraMetadata));
    }

    private void ThrowIfConfigured(DeploymentProofStage stage)
    {
        if (_failures.Contains(stage))
            throw new DeploymentProofStageException(stage, $"fake.{stage.ToString().ToLowerInvariant()}.failed", "Injected fake-provider failure.");
    }
}
