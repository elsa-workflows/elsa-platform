using ElsaControl.Deployment.Proof;

namespace ElsaControl.Deployment.Proof.Tests;

public sealed class DeploymentProofHarnessTests
{
    private static readonly DeploymentProofInput Input = new(
        "3.8.0-preview.5413",
        "Combined",
        ["DefaultAuthentication", "Liquid", "StructuredLogs", "StructuredLogsDashboard", "ConsoleLogs", "ConsoleLogsDashboard", "OpenTelemetry"],
        "valenceruntimeimages.azurecr.io/runtime-combined",
        "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

    private static readonly DeploymentProofEnvironment Environment = new(
        "proof-disposable",
        "westeurope",
        "fake",
        ["azure-workload-identity"]);

    [Fact]
    public async Task Dry_run_fake_provider_proves_exact_selection_health_workflow_idempotency_and_cleanup()
    {
        var report = await RunAsync();

        Assert.True(report.Passed, report.Failure?.Message);
        Assert.Equal(Input, report.Input);
        Assert.Equal(Environment, report.Environment);
        Assert.Equal(DeploymentProofOutcome.Passed, report.Outcome);
        Assert.Equal(
            [
                DeploymentProofStage.Selection,
                DeploymentProofStage.Plan,
                DeploymentProofStage.Provision,
                DeploymentProofStage.Health,
                DeploymentProofStage.Workflow,
                DeploymentProofStage.RepeatApply,
                DeploymentProofStage.Cleanup
            ],
            report.Stages.Select(stage => stage.Stage));
        Assert.All(report.Stages, stage => Assert.Equal(DeploymentProofStageStatus.Passed, stage.Status));
        Assert.Equal("3.8.0-preview.5413", report.Stages[0].Evidence["elsaVersion"]);
        Assert.Equal("Combined", report.Stages[0].Evidence["topology"]);
        Assert.Equal("DefaultAuthentication,Liquid,StructuredLogs,StructuredLogsDashboard,ConsoleLogs,ConsoleLogsDashboard,OpenTelemetry", report.Stages[0].Evidence["features"]);
        Assert.StartsWith("sha256:", report.Stages[0].Evidence["imageDigest"]);
        Assert.Equal("https://fake-elsa-3-8-combined.example.test", report.Stages[3].Evidence["endpoint"]);
        Assert.Equal("false", report.Stages[5].Evidence["applied"]);
        Assert.Equal("true", report.Stages[5].Evidence["noOp"]);
        Assert.Contains("\"outcome\": \"passed\"", report.ToJson(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DeploymentProofStage.Selection)]
    [InlineData(DeploymentProofStage.Plan)]
    [InlineData(DeploymentProofStage.Provision)]
    [InlineData(DeploymentProofStage.Health)]
    [InlineData(DeploymentProofStage.Workflow)]
    [InlineData(DeploymentProofStage.RepeatApply)]
    [InlineData(DeploymentProofStage.Cleanup)]
    public async Task Fake_provider_failure_is_reported_at_the_injected_seam(DeploymentProofStage failingStage)
    {
        var provider = new FakeDeploymentProofProvider(new HashSet<DeploymentProofStage> { failingStage });
        var report = await new DeploymentProofHarness().RunAsync(Input, Environment, provider);

        Assert.False(report.Passed);
        var failure = report.Failure;
        Assert.NotNull(failure);
        Assert.Equal(failingStage, failure!.Stage);
        Assert.Equal(DeploymentProofStageStatus.Failed, failure.Status);
        Assert.Contains(failingStage.ToString().ToLowerInvariant(), failure.Code, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            failingStage is DeploymentProofStage.Provision or DeploymentProofStage.Health or DeploymentProofStage.Workflow or DeploymentProofStage.RepeatApply or DeploymentProofStage.Cleanup ? 1 : 0,
            provider.CleanupCalls);

        if (failingStage == DeploymentProofStage.Provision)
        {
            Assert.Equal(DeploymentProofStageStatus.Passed, report.Stages.Single(stage => stage.Stage == DeploymentProofStage.Cleanup).Status);
            Assert.Equal("fake-resource-3.8-combined", Assert.Single(provider.CleanupResourceIds));
        }
    }

    [Fact]
    public async Task Repeat_runs_are_deterministic_for_selection_plan_and_safe_resource_identity()
    {
        var first = await RunAsync();
        var second = await RunAsync();

        Assert.True(first.Passed);
        Assert.True(second.Passed);
        Assert.Equal(first.Stages.Select(stage => (stage.Stage, stage.Status, stage.Code)), second.Stages.Select(stage => (stage.Stage, stage.Status, stage.Code)));
        Assert.Equal(first.Stages[0].Evidence, second.Stages[0].Evidence);
        Assert.Equal(first.Stages[1].Evidence, second.Stages[1].Evidence);
        Assert.Equal(first.Stages[2].Evidence, second.Stages[2].Evidence);
    }

    [Fact]
    public async Task Evidence_redacts_accidental_secret_assignments_from_provider_metadata()
    {
        var provider = new FakeDeploymentProofProvider(
            extraMetadata: new Dictionary<string, string>
            {
                ["diagnostic"] = "provider returned token=do-not-leak",
                ["password"] = "do-not-leak"
            });

        var report = await new DeploymentProofHarness().RunAsync(Input, Environment, provider);
        var evidence = report.ToJson();

        Assert.DoesNotContain("do-not-leak", evidence, StringComparison.Ordinal);
        Assert.Contains("redacted", evidence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("azure-workload-identity=", evidence, StringComparison.Ordinal);

        var unsafeInput = new DeploymentProofInput(
            Input.ElsaVersion,
            Input.Topology,
            Input.Features,
            "https://user:password@registry.example.test/runtime",
            Input.ImageDigest);
        var unsafeEvidence = (await new DeploymentProofHarness().RunAsync(unsafeInput, Environment, new FakeDeploymentProofProvider())).ToJson();
        Assert.DoesNotContain("user:password@", unsafeEvidence, StringComparison.Ordinal);
        Assert.Contains("redacted", unsafeEvidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_input_fails_at_selection_without_calling_provider()
    {
        var provider = new FakeDeploymentProofProvider();
        var invalid = new DeploymentProofInput(Input.ElsaVersion, Input.Topology, Input.Features, Input.ImageReference, "latest");

        var report = await new DeploymentProofHarness().RunAsync(invalid, Environment, provider);

        Assert.False(report.Passed);
        Assert.Equal(DeploymentProofStage.Selection, report.Failure!.Stage);
        Assert.Equal("proof.selection.imageDigestRequired", report.Failure.Code);
        Assert.All(report.Stages.Where(stage => stage.Stage != DeploymentProofStage.Selection), stage =>
            Assert.Equal(DeploymentProofStageStatus.Skipped, stage.Status));
    }

    [Theory]
    [InlineData("oci://user@registry.example.test/runtime-combined")]
    [InlineData("https://user@example.test/runtime-combined")]
    [InlineData("user@registry.example.test/runtime-combined")]
    [InlineData("user@registry.example.test")]
    [InlineData("user:password@registry.example.test/runtime-combined")]
    [InlineData("user:password@registry.example.test")]
    [InlineData("user:password@registry.example.test ")]
    public async Task Credential_bearing_image_references_fail_at_selection(string imageReference)
    {
        var provider = new FakeDeploymentProofProvider();
        var invalid = new DeploymentProofInput(
            Input.ElsaVersion,
            Input.Topology,
            Input.Features,
            imageReference,
            Input.ImageDigest);

        var report = await new DeploymentProofHarness().RunAsync(invalid, Environment, provider);

        Assert.False(report.Passed);
        Assert.Equal(DeploymentProofStage.Selection, report.Failure!.Stage);
        Assert.Equal("proof.selection.imageReferenceUnsafe", report.Failure.Code);
        Assert.All(report.Stages.Where(stage => stage.Stage != DeploymentProofStage.Selection), stage =>
            Assert.Equal(DeploymentProofStageStatus.Skipped, stage.Status));
        Assert.DoesNotContain("user", report.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain("password", report.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_internal_cancellation_is_reported_as_unexpected()
    {
        var report = await new DeploymentProofHarness().RunAsync(
            Input,
            Environment,
            new CancellationProofProvider(cancelSelectionInternally: true));

        Assert.Equal("proof.selection.unexpected", report.Failure?.Code);
    }

    [Fact]
    public async Task Repeat_apply_unexpected_failure_uses_the_established_stage_code()
    {
        var report = await new DeploymentProofHarness().RunAsync(
            Input,
            Environment,
            new CancellationProofProvider(failRepeatApplyUnexpectedly: true));

        Assert.Equal("proof.repeatApply.unexpected", report.Failure?.Code);
    }

    [Fact]
    public async Task Cleanup_is_bounded_when_the_provider_does_not_complete()
    {
        var report = await new DeploymentProofHarness(
                cleanupTimeout: TimeSpan.FromMilliseconds(20))
            .RunAsync(Input, Environment, new CancellationProofProvider(hangCleanup: true));

        Assert.Equal("proof.cleanup.cancelled", report.Failure?.Code);
    }

    private static async Task<DeploymentProofReport> RunAsync()
    {
        return await new DeploymentProofHarness().RunAsync(Input, Environment, new FakeDeploymentProofProvider());
    }

    private sealed class CancellationProofProvider(
        bool cancelSelectionInternally = false,
        bool hangCleanup = false,
        bool failRepeatApplyUnexpectedly = false) : IDeploymentProofProvider
    {
        private readonly FakeDeploymentProofProvider _inner = new();

        public Task<DeploymentProofSelection> SelectAsync(DeploymentProofInput input, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default) =>
            cancelSelectionInternally
                ? Task.FromException<DeploymentProofSelection>(new OperationCanceledException())
                : _inner.SelectAsync(input, environment, cancellationToken);

        public Task<DeploymentProofPlan> PlanAsync(DeploymentProofSelection selection, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default) =>
            _inner.PlanAsync(selection, environment, cancellationToken);

        public Task<DeploymentProofDeployment> ProvisionAsync(DeploymentProofPlan plan, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default) =>
            _inner.ProvisionAsync(plan, environment, cancellationToken);

        public Task<DeploymentProofHealth> WaitForHealthAsync(DeploymentProofDeployment deployment, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default) =>
            _inner.WaitForHealthAsync(deployment, environment, cancellationToken);

        public Task<DeploymentProofWorkflow> RunWorkflowAsync(DeploymentProofHealth health, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default) =>
            _inner.RunWorkflowAsync(health, environment, cancellationToken);

        public Task<DeploymentProofApply> ApplyAsync(DeploymentProofPlan plan, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default) =>
            failRepeatApplyUnexpectedly
                ? Task.FromException<DeploymentProofApply>(new InvalidOperationException("Injected failure."))
                : _inner.ApplyAsync(plan, environment, cancellationToken);

        public async Task<DeploymentProofCleanup> CleanupAsync(DeploymentProofPlan plan, DeploymentProofDeployment? deployment, DeploymentProofEnvironment environment, CancellationToken cancellationToken = default)
        {
            if (hangCleanup)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return await _inner.CleanupAsync(plan, deployment, environment, cancellationToken);
        }
    }
}
