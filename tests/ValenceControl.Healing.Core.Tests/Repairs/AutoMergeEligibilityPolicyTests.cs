using System.Text.Json;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Repairs;
using ValenceControl.Healing.Core.Security;
using ContractGateResult = ValenceControl.Healing.Abstractions.PolicyGateResult;

namespace ValenceControl.Healing.Core.Tests.Repairs;

public sealed class AutoMergeEligibilityPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-16T18:00:00Z");

    public static TheoryData<string, RepairPolicyObservationState> EveryBlockingGateState()
    {
        var data = new TheoryData<string, RepairPolicyObservationState>();
        foreach (var gate in AutoMergeEligibilityPolicy.RequiredGates)
        foreach (var state in Enum.GetValues<RepairPolicyObservationState>().Where(x => x != RepairPolicyObservationState.Satisfied))
            data.Add(gate, state);
        return data;
    }

    [Fact]
    public void FullyEligibleOptInIsTheOnlyAutomaticMergeDecision()
    {
        var result = AutoMergeEligibilityPolicy.Evaluate(Policy(), EligibleInput(), Now);

        Assert.Equal(PolicyDecisions.AllowAutomaticMerge, result.Decision);
        Assert.All(result.Gates, x => Assert.Equal(PolicyGateState.Pass, x.State));
        Assert.Equal(Now, result.EvaluatedAt);
    }

    [Theory]
    [MemberData(nameof(EveryBlockingGateState))]
    public void EveryFailedMissingStaleAmbiguousOrUnknownGateDeniesAutomaticMerge(
        string gate,
        RepairPolicyObservationState state)
    {
        var input = EligibleInput();
        var observations = input.Observations
            .Select(x => x.Gate == gate ? x with { State = state, ReasonCode = $"{gate}-{StateReason(state)}" } : x)
            .ToArray();

        var result = AutoMergeEligibilityPolicy.Evaluate(Policy(), input with { Observations = observations }, Now);

        Assert.Equal(PolicyDecisions.HumanOnly, result.Decision);
        var gateResult = Assert.Single(result.Gates, x => x.Gate == gate);
        Assert.NotEqual(PolicyGateState.Pass, gateResult.State);
        Assert.Equal($"{gate}-{StateReason(state)}", gateResult.ReasonCode);
    }

    [Fact]
    public void OmittedRequiredGateIsUnknownAndDeniesAutomaticMerge()
    {
        var input = EligibleInput();

        var result = AutoMergeEligibilityPolicy.Evaluate(
            Policy(),
            input with { Observations = input.Observations.Where(x => x.Gate != AutoMergePolicyGates.RequiredChecks).ToArray() },
            Now);

        Assert.Equal(PolicyDecisions.HumanOnly, result.Decision);
        Assert.Equal(
            new ContractGateResult(AutoMergePolicyGates.RequiredChecks, PolicyGateState.Unknown, "required-checks-missing"),
            Assert.Single(result.Gates, x => x.Gate == AutoMergePolicyGates.RequiredChecks));
    }

    [Fact]
    public void DuplicateGateIsAmbiguousAndDeniesAutomaticMerge()
    {
        var input = EligibleInput();
        var duplicate = input.Observations.Single(x => x.Gate == AutoMergePolicyGates.IndependentVerification);

        var result = AutoMergeEligibilityPolicy.Evaluate(
            Policy(),
            input with { Observations = [.. input.Observations, duplicate] },
            Now);

        Assert.Equal(PolicyDecisions.HumanOnly, result.Decision);
        Assert.Equal(
            new ContractGateResult(AutoMergePolicyGates.IndependentVerification, PolicyGateState.Block, "independent-verification-ambiguous"),
            Assert.Single(result.Gates, x => x.Gate == AutoMergePolicyGates.IndependentVerification));
    }

    [Fact]
    public void DisabledRepositoryPolicyRemainsHumanMergeable()
    {
        var policy = Policy();
        policy.AutomaticMergeEnabled = false;

        var result = AutoMergeEligibilityPolicy.Evaluate(policy, EligibleInput(), Now);

        Assert.Equal(PolicyDecisions.HumanOnly, result.Decision);
        Assert.Equal(
            "automatic-merge-disabled",
            Assert.Single(result.Gates, x => x.Gate == AutoMergePolicyGates.RepositoryOptIn).ReasonCode);
    }

    [Fact]
    public void AutoMergePolicyWithoutEveryMandatorySensitiveCategoryFailsClosed()
    {
        var policy = Policy();
        policy.ForbiddenChangeCategoriesJson = "[\"schema\"]";

        var result = AutoMergeEligibilityPolicy.Evaluate(policy, EligibleInput(), Now);

        Assert.Equal(PolicyDecisions.HumanOnly, result.Decision);
        Assert.Equal("merge-policy-invalid", Assert.Single(result.Gates, x => x.Gate == "policy-definition").ReasonCode);
    }

    [Fact]
    public void AutoMergePolicyWithoutAnExplicitRequiredCheckFailsClosed()
    {
        var policy = Policy();
        policy.RequiredChecksJson = "[]";

        var result = AutoMergeEligibilityPolicy.Evaluate(policy, EligibleInput(), Now);

        Assert.Equal(PolicyDecisions.HumanOnly, result.Decision);
        Assert.Equal("merge-policy-invalid", Assert.Single(result.Gates, x => x.Gate == "policy-definition").ReasonCode);
    }

    [Fact]
    public void AutoMergePolicyCannotDisableTheTrustedRollbackOrStopRequirement()
    {
        var policy = Policy();
        policy.RequireRollbackOrStopCapability = false;

        var result = AutoMergeEligibilityPolicy.Evaluate(policy, EligibleInput(), Now);

        Assert.Equal(PolicyDecisions.HumanOnly, result.Decision);
        Assert.Equal("merge-policy-invalid", Assert.Single(result.Gates, x => x.Gate == "policy-definition").ReasonCode);
    }

    [Fact]
    public void DeterministicRiskClassifierAllowsOnlyPrivateImplementationSourceChanges()
    {
        var lowRisk = RepairChangeRiskClassifier.Classify(
            [new("src/Orders/OrderCalculator.cs", ["return subtotal + tax;"])]);
        var publicContract = RepairChangeRiskClassifier.Classify(
            [new("src/Orders/OrderCalculator.cs", ["public decimal Calculate(Order order)"])]);

        Assert.Empty(lowRisk);
        Assert.Contains("public-contract", publicContract);
    }

    [Theory]
    [InlineData("src/Identity/AuthenticationService.cs", "authentication")]
    [InlineData("src/Data/Migrations/ReviseOrders.cs", "schema")]
    [InlineData("src/Orders/Orders.csproj", "dependency")]
    [InlineData("src/Orders/IOrderService.cs", "public-contract")]
    [InlineData("infra/main.tf", "infrastructure")]
    [InlineData("deploy/app.yaml", "deployment")]
    [InlineData(".github/workflows/healing.yml", "self-protection")]
    public void DeterministicRiskClassifierBlocksMandatorySensitivePaths(string path, string expectedCategory)
    {
        Assert.Contains(
            expectedCategory,
            RepairChangeRiskClassifier.Classify([new(path, ["private static void Change() { }"])]));
    }

    [Fact]
    public void VersionedPathPolicyRejectsAForbiddenPathEvenWhenItIsUnderAnAllowedRoot()
    {
        var policy = new PathPolicy
        {
            Id = Guid.NewGuid(), WorkspaceId = Policy().WorkspaceId, ApplicationId = Policy().ApplicationId,
            PolicyVersion = "3", PolicyHash = new string('c', 64),
            AllowedRootsJson = "[\"src\"]", ForbiddenRootsJson = "[\"src/healing\"]",
            MaxFiles = 2, MaxChangedLines = 20, MaxPatchBytes = 2_000
        };

        var result = HealingPathPolicy.Evaluate(policy, new PathPolicyEvaluationInput(
            new string('d', 64), [new RepairPathChange("src/healing/Publisher.cs", 2)], 120), Now);

        Assert.Equal("3", result.PolicyVersion);
        Assert.Equal(PolicyDecisions.Deny, result.Decision);
        Assert.Equal("forbidden-path", Assert.Single(result.Gates, x => x.Gate == "forbidden-roots").ReasonCode);
    }

    [Fact]
    public void HighConfidenceInferenceMayBePublishedButAlwaysRequiresHumanMerge()
    {
        var policy = new EvidencePolicy
        {
            Id = Guid.NewGuid(), WorkspaceId = Policy().WorkspaceId, ApplicationId = Policy().ApplicationId,
            PolicyVersion = "2", PolicyHash = new string('e', 64), AllowHighConfidenceInference = true,
            MinimumInferenceConfidence = .85m, MaximumTier = EvidenceTier.DefaultRedacted, PermittedFieldsJson = "[]"
        };

        var result = HealingEvidencePolicy.Evaluate(policy, new EvidencePolicyEvaluationInput(
            new string('f', 64), RepairClassification.InferredHighConfidence, .92m,
            EvidenceTier.DefaultRedacted, [], true, false), Now);

        Assert.Equal(PolicyDecisions.HumanOnly, result.Decision);
        Assert.All(result.Gates, x => Assert.Equal(PolicyGateState.Pass, x.State));
    }

    [Fact]
    public void PublicationPolicyDeniesWhenAnyCurrentAuthorityObservationIsNotSatisfied()
    {
        var policy = Policy();
        var observations = HealingPublicationPolicy.RequiredGates.Select(gate =>
            gate == "kill-switches"
                ? new RepairPolicyObservation(gate, RepairPolicyObservationState.Unknown, "kill-switch-state-unknown")
                : RepairPolicyObservation.Satisfied(gate, $"{gate}-satisfied")).ToArray();

        var result = HealingPublicationPolicy.Evaluate(policy, new PublicationPolicyEvaluationInput(
            new string('1', 64), observations, false), Now);

        Assert.Equal(PolicyDecisions.Deny, result.Decision);
        Assert.Equal(PolicyGateState.Unknown, Assert.Single(result.Gates, x => x.Gate == "kill-switches").State);
    }

    [Fact]
    public async Task MergeServicePersistsFreshEvaluationAndAppendOnlyAuditForEveryDecision()
    {
        var evaluations = new RecordingMergeEvaluationStore();
        var audits = new RecordingAuditStore();
        var service = new HealingMergeService(
            evaluations,
            new HealingAuditService(audits, new FixedTimeProvider(Now)),
            new FixedTimeProvider(Now));
        var policy = Policy();
        var request = Request(policy, EligibleInput());

        var result = await service.EvaluateAsync(request);

        Assert.True(result.AutomaticMergeAllowed);
        Assert.Equal(Now, result.Evaluation.EvaluatedAt);
        Assert.Contains(AutoMergePolicyGates.RequiredChecks, result.Evaluation.GateResultsJson);
        Assert.Equal("[]", result.Evaluation.ReasonCodesJson);
        Assert.Same(result.Evaluation, Assert.Single(evaluations.Saved));
        var audit = Assert.Single(audits.Events);
        Assert.Equal(request.PullRequestId, audit.AggregateId);
        Assert.Equal("merge-eligibility-evaluated", audit.EventType);
        Assert.Equal("automatic-merge-allowed", audit.ReasonCode);
        Assert.Equal(policy.PolicyVersion, audit.PolicyVersion);
        Assert.Equal(request.Input.InputDigest, audit.InputHash);
        Assert.NotNull(audit.OutputHash);
        Assert.Equal(64, audit.OutputHash.Length);
        Assert.Contains("allow-automatic-merge", audit.SafeDetailJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MergeServicePersistsEveryBlockingReasonAndAuditsDeniedDecision()
    {
        var evaluations = new RecordingMergeEvaluationStore();
        var audits = new RecordingAuditStore();
        var service = new HealingMergeService(evaluations, new HealingAuditService(audits), new FixedTimeProvider(Now));
        var policy = Policy();
        var input = EligibleInput() with
        {
            Observations = EligibleInput().Observations.Select(x => x.Gate switch
            {
                AutoMergePolicyGates.RequiredChecks => x with { State = RepairPolicyObservationState.Failed, ReasonCode = "required-checks-failed" },
                AutoMergePolicyGates.ProviderSnapshot => x with { State = RepairPolicyObservationState.Stale, ReasonCode = "provider-snapshot-stale" },
                _ => x
            }).ToArray()
        };

        var result = await service.EvaluateAsync(Request(policy, input));

        Assert.False(result.AutomaticMergeAllowed);
        Assert.Equal(PolicyDecision.HumanOnly, result.Evaluation.Decision);
        Assert.Contains("required-checks-failed", result.Evaluation.ReasonCodesJson);
        Assert.Contains("provider-snapshot-stale", result.Evaluation.ReasonCodesJson);
        Assert.Equal("automatic-merge-blocked", Assert.Single(audits.Events).ReasonCode);
    }

    [Fact]
    public async Task MergeServiceUsesEachEvaluationAsItsAuditIdempotencyIdentity()
    {
        var evaluations = new RecordingMergeEvaluationStore();
        var audits = new UniqueEvaluationAuditStore();
        var service = new HealingMergeService(
            evaluations,
            new HealingAuditService(audits, new FixedTimeProvider(Now)),
            new FixedTimeProvider(Now));
        var request = Request(Policy(), EligibleInput());

        var first = await service.EvaluateAsync(request);
        var second = await service.EvaluateAsync(request);

        Assert.Equal(2, audits.Events.Count);
        Assert.Equal(
            new[] { first.Evaluation.Id, second.Evaluation.Id }.Order(),
            audits.Events.Select(x => x.CorrelationId).Order());
        Assert.All(audits.Events, x => Assert.Equal(request.CorrelationId, x.CausationId));
    }

    private static MergePolicy Policy() => new()
    {
        Id = Guid.Parse("a48ef222-e025-4b08-b28a-fd7e6867cddd"),
        WorkspaceId = Guid.Parse("81c211e5-82c3-4372-ab23-70cab72c89bb"),
        ApplicationId = Guid.Parse("2a017e57-1952-48dd-b667-bd39f3675874"),
        Name = "safe repairs",
        PolicyVersion = "7",
        PolicyHash = new string('a', 64),
        AutomaticMergeEnabled = true,
        RequiredChecksJson = "[\"build\",\"test\"]",
        IndependentVerifier = "security-verifier",
        ForbiddenChangeCategoriesJson = JsonSerializer.Serialize(AutoMergeEligibilityPolicy.RequiredForbiddenChangeCategories),
        RequireRollbackOrStopCapability = true,
        CreatedAt = Now
    };

    private static AutoMergeEligibilityInput EligibleInput() => new(
        new string('b', 64),
        AutoMergeEligibilityPolicy.RequiredGates
            .Select(x => RepairPolicyObservation.Satisfied(x, $"{x}-satisfied"))
            .ToArray());

    private static HealingMergeEvaluationRequest Request(MergePolicy policy, AutoMergeEligibilityInput input) => new(
        policy.WorkspaceId,
        policy.ApplicationId,
        Guid.Parse("ca2a436e-42c6-4a92-89db-5ef0f275fcd9"),
        Guid.Parse("0eb5fc5d-0363-489a-8bd0-58f568f1249f"),
        policy,
        input,
        Guid.Parse("b44d69da-aa5d-45a3-951e-638ff906510e"));

    private static string StateReason(RepairPolicyObservationState state) => state.ToString().ToLowerInvariant();

    private sealed class RecordingMergeEvaluationStore : IHealingMergeEvaluationStore
    {
        public List<PolicyEvaluation> Saved { get; } = [];

        public async ValueTask<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, ValueTask<T>> operation,
            CancellationToken cancellationToken = default) =>
            await operation(cancellationToken);

        public ValueTask SaveAsync(PolicyEvaluation evaluation, Guid pullRequestId, CancellationToken cancellationToken = default)
        {
            Saved.Add(evaluation);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingAuditStore : IHealingAuditStore
    {
        public List<HealingAuditEvent> Events { get; } = [];

        public ValueTask<HealingAuditEvent> AppendAsync(HealingAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            auditEvent.Sequence = Events.Count + 1;
            Events.Add(auditEvent);
            return ValueTask.FromResult(auditEvent);
        }

        public ValueTask<IReadOnlyList<HealingAuditEvent>> QueryAsync(
            ValenceControl.Healing.Core.Security.HealingAuditQuery query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<HealingAuditEvent>>(Events);
    }

    private sealed class UniqueEvaluationAuditStore : IHealingAuditStore
    {
        public List<HealingAuditEvent> Events { get; } = [];

        public ValueTask<HealingAuditEvent> AppendAsync(
            HealingAuditEvent auditEvent,
            CancellationToken cancellationToken = default)
        {
            if (Events.Any(x => x.WorkspaceId == auditEvent.WorkspaceId &&
                                x.AggregateType == auditEvent.AggregateType &&
                                x.AggregateId == auditEvent.AggregateId &&
                                x.EventType == auditEvent.EventType &&
                                x.CorrelationId == auditEvent.CorrelationId))
                throw new InvalidOperationException("Audit identity was reused.");
            Events.Add(auditEvent);
            return ValueTask.FromResult(auditEvent);
        }

        public ValueTask<IReadOnlyList<HealingAuditEvent>> QueryAsync(
            ValenceControl.Healing.Core.Security.HealingAuditQuery query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<HealingAuditEvent>>(Events);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
