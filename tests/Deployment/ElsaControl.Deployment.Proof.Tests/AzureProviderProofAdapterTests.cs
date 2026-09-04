using ElsaControl.Deployment.Azure;
using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Proof;

namespace ElsaControl.Deployment.Proof.Tests;

public sealed class AzureProviderProofAdapterTests
{
    private static readonly Guid ProofOrganizationId = Guid.Parse("19519519-5195-4195-8195-195195195195");
    private static readonly Guid ProofInstanceId = Guid.Parse("29529529-5295-4295-8295-295295295295");

    [Fact]
    public async Task Selection_and_admitted_plan_preserve_exact_immutable_identity()
    {
        var imageDigest = "sha256:" + new string('c', 64);
        var input = new DeploymentProofInput(
            "5.0.1",
            "combined",
            ["managed-runtime"],
            $"valenceruntimeimages.azurecr.io/runtime-combined@{imageDigest}",
            imageDigest);
        var environment = new DeploymentProofEnvironment("proof-rg", "westeurope", "azure", ["database"]);
        var templateFingerprint = new string('b', 64);
        var plan = new AzureWorkloadPlan(
            "proof-workload",
            "westeurope",
            input.ElsaVersion,
            "5.0",
            input.Topology,
            "Dedicated",
            "valenceruntimeimages.azurecr.io/runtime-combined",
            imageDigest["sha256:".Length..],
            "oci://evidence.example/manifest",
            "sha256:" + new string('d', 64),
            "oci://evidence.example/signature",
            "sha256:" + new string('e', 64),
            new Dictionary<string, string> { ["database"] = "secret://vault/database" },
            new string('a', 64));
        var adapter = new AzureProviderProofAdapter(
            Guid.NewGuid(),
            templateFingerprint,
            null!,
            null!,
            new StaticPlanFactory(new AzureProviderOperationSubmission(
                "proof-1", templateFingerprint, plan, OrganizationId: ProofOrganizationId,
                InstanceId: ProofInstanceId, LifecycleAction: ElsaInstanceOperationAction.Reconcile)));

        var first = await adapter.SelectAsync(input, environment);
        var second = await adapter.SelectAsync(input, environment);
        var proofPlan = await adapter.PlanAsync(first, environment);

        Assert.Equal(first, second);
        Assert.Equal(first.ElsaVersion, input.ElsaVersion);
        Assert.Equal(first.ImageDigest, imageDigest);
        Assert.Equal(first.SelectionId, proofPlan.SelectionId);
        Assert.Equal(plan.Fingerprint, proofPlan.Fingerprint);
        Assert.Equal("azure-" + plan.Fingerprint, proofPlan.PlanId);

        var reordered = new DeploymentProofInput(
            input.ElsaVersion,
            input.Topology,
            [" managed-runtime "],
            input.ImageReference,
            input.ImageDigest);
        Assert.Equal(first.SelectionId, (await adapter.SelectAsync(reordered, environment)).SelectionId);
    }

    private sealed class StaticPlanFactory(AzureProviderOperationSubmission submission) : IAzureProviderProofPlanFactory
    {
        public AzureProviderOperationSubmission Create(DeploymentProofSelection selection, DeploymentProofEnvironment environment) => submission;
    }
}
