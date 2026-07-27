using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Agent;

namespace ValenceControl.Healing.Agent.Tests;

public sealed class RepairAgentGatewayTests
{
    [Fact]
    public async Task Sends_only_bounded_provider_neutral_evidence_and_returns_an_inert_envelope()
    {
        var provider = new RecordingRepairInferenceProvider(ReproducedResult());
        var gateway = new RepairAgentGateway(provider, TimeProvider.System);
        var request = Request();

        var result = await gateway.AnalyzeAsync(request);

        Assert.NotNull(provider.Request);
        Assert.Equal(request.AttemptId, provider.Request!.AttemptId);
        Assert.Equal(request.Evidence.CanonicalJson, provider.Request.Evidence.CanonicalJson);
        Assert.Contains("exception.message", provider.Request.Evidence.OmittedFields);
        Assert.DoesNotContain(typeof(RepairAgentInferenceRequest).Assembly.GetTypes()
            .Where(x => x.Namespace == typeof(RepairAgentInferenceRequest).Namespace &&
                        x.Name.StartsWith("RepairAgentInference", StringComparison.Ordinal))
            .SelectMany(x => x.GetProperties())
            .Select(x => x.Name), x =>
            x.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("installation", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(request.AttemptId, result.AttemptId);
        Assert.True(result.Reproduction.WasAttempted);
        Assert.True(result.Reproduction.WasReproduced);
        Assert.Equal(0.96m, result.Confidence);
        Assert.StartsWith("sha256:", result.PatchDigest);
    }

    [Theory]
    [InlineData("", "reproduced", 0.96)]
    [InlineData("unknown", "reproduced", 0.96)]
    [InlineData("inferred-high-confidence", "not-attempted", 1.01)]
    [InlineData("inferred-high-confidence", "not-attempted", -0.01)]
    public async Task Rejects_missing_or_invalid_classification_and_confidence(
        string classification,
        string reproductionStatus,
        decimal confidence)
    {
        var provider = new RecordingRepairInferenceProvider(
            ReproducedResult() with
            {
                Classification = classification,
                Confidence = confidence,
                Reproduction = new(reproductionStatus, "Safe summary", [])
            });
        var gateway = new RepairAgentGateway(provider, TimeProvider.System);

        var act = () => gateway.AnalyzeAsync(Request()).AsTask();

        await Assert.ThrowsAsync<RepairAgentProtocolException>(act);
    }

    [Fact]
    public async Task Allows_explicit_unreproduced_high_confidence_result_for_later_human_only_policy()
    {
        var provider = new RecordingRepairInferenceProvider(
            ReproducedResult() with
            {
                Classification = RepairAgentClassifications.InferredHighConfidence,
                Confidence = 0.92m,
                Reproduction = new(RepairReproductionStatuses.NotReproduced, "Could not reproduce in the available fixture.", ["dotnet test"])
            });
        var gateway = new RepairAgentGateway(provider, TimeProvider.System);

        var result = await gateway.AnalyzeAsync(Request());

        Assert.Equal(RepairAgentClassifications.InferredHighConfidence, result.Classification);
        Assert.True(result.Reproduction.WasAttempted);
        Assert.False(result.Reproduction.WasReproduced);
        Assert.Equal(RepairReproductionStatuses.NotReproduced, result.Reproduction.Classification);
    }

    [Fact]
    public async Task Rejects_expired_oversized_or_mismatched_evidence_before_invoking_provider()
    {
        var provider = new RecordingRepairInferenceProvider(ReproducedResult());
        var gateway = new RepairAgentGateway(provider, TimeProvider.System);
        var expired = Request() with
        {
            Evidence = Request().Evidence with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }
        };
        var oversized = Request() with
        {
            Evidence = Request().Evidence with { CanonicalJson = new string('x', RepairAgentGatewayLimits.MaximumEvidenceBytes + 1) }
        };
        var mismatched = Request() with
        {
            Evidence = Request().Evidence with { AttemptId = Guid.NewGuid() }
        };

        foreach (var invalid in new[] { expired, oversized, mismatched })
            await Assert.ThrowsAsync<RepairAgentProtocolException>(() => gateway.AnalyzeAsync(invalid).AsTask());

        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task Rejects_a_result_that_exceeds_output_bounds()
    {
        var provider = new RecordingRepairInferenceProvider(
            ReproducedResult() with { UnifiedDiff = new string('x', RepairAgentGatewayLimits.MaximumPatchBytes + 1) });
        var gateway = new RepairAgentGateway(provider, TimeProvider.System);

        var act = () => gateway.AnalyzeAsync(Request()).AsTask();

        await Assert.ThrowsAsync<RepairAgentProtocolException>(act);
    }

    private static RepairAgentRequest Request()
    {
        var attemptId = Guid.NewGuid();
        var evidenceJson = "{\"exceptionType\":\"System.InvalidOperationException\",\"operation\":\"GET /orders/{id}\"}";
        return new(
            HealingContractVersions.AgentProtocol,
            attemptId,
            "base-abc",
            "target-def",
            "producing-123",
            new(
                HealingContractVersions.AgentProtocol,
                attemptId,
                "default-redacted",
                evidenceJson,
                RepairAgentGateway.ComputeSha256Digest(evidenceJson),
                ["exception.message"],
                DateTimeOffset.UtcNow.AddMinutes(5)),
            new(TimeSpan.FromMinutes(10), 10_000, 20_000));
    }

    private static RepairAgentInferenceResult ReproducedResult() => new(
        "run-42",
        1,
        RepairAgentClassifications.Reproduced,
        0.96m,
        "The order projection omitted a required null guard.",
        "diff --git a/src/Orders.cs b/src/Orders.cs\n--- a/src/Orders.cs\n+++ b/src/Orders.cs\n@@ -1 +1 @@\n-old\n+new\n",
        [new("src/Orders.cs", "modified", "application-code")],
        new(RepairReproductionStatuses.Reproduced, "Failure reproduced before the change.", ["dotnet test"]),
        new(true, "Regression coverage added.", ["tests/OrdersTests.cs"]),
        [new("test", "dotnet test", "passed", "All focused tests passed.", TimeSpan.FromSeconds(4))],
        ["low-risk"],
        "Revert the repair commit.",
        new(120, 80, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6)),
        DateTimeOffset.UtcNow.AddSeconds(-6),
        DateTimeOffset.UtcNow);

    private sealed class RecordingRepairInferenceProvider(RepairAgentInferenceResult result) : IRepairInferenceProvider
    {
        public RepairAgentInferenceRequest? Request { get; private set; }
        public int CallCount { get; private set; }

        public ValueTask<RepairAgentInferenceResult> AnalyzeAsync(
            RepairAgentInferenceRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            CallCount++;
            return ValueTask.FromResult(result);
        }
    }
}
