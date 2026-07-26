using System.Text.Json;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Repairs;
using Elsa.Platform.Healing.Core.Security;
using FluentAssertions;
using ContractGateResult = Elsa.Platform.Healing.Abstractions.PolicyGateResult;

namespace Elsa.Platform.Healing.Core.Tests.Repairs;

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

        result.Decision.Should().Be(PolicyDecisions.AllowAutomaticMerge);
        result.Gates.Should().OnlyContain(x => x.State == PolicyGateState.Pass);
        result.EvaluatedAt.Should().Be(Now);
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

        result.Decision.Should().Be(PolicyDecisions.HumanOnly);
        result.Gates.Should().ContainSingle(x => x.Gate == gate).Which.Should().Match<ContractGateResult>(x =>
            x.State != PolicyGateState.Pass && x.ReasonCode == $"{gate}-{StateReason(state)}");
    }

    [Fact]
    public void OmittedRequiredGateIsUnknownAndDeniesAutomaticMerge()
    {
        var input = EligibleInput();

        var result = AutoMergeEligibilityPolicy.Evaluate(
            Policy(),
            input with { Observations = input.Observations.Where(x => x.Gate != AutoMergePolicyGates.RequiredChecks).ToArray() },
            Now);

        result.Decision.Should().Be(PolicyDecisions.HumanOnly);
        result.Gates.Should().ContainSingle(x => x.Gate == AutoMergePolicyGates.RequiredChecks).Which.Should().BeEquivalentTo(
            new ContractGateResult(AutoMergePolicyGates.RequiredChecks, PolicyGateState.Unknown, "required-checks-missing"));
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

        result.Decision.Should().Be(PolicyDecisions.HumanOnly);
        result.Gates.Should().ContainSingle(x => x.Gate == AutoMergePolicyGates.IndependentVerification).Which.Should().BeEquivalentTo(
            new ContractGateResult(AutoMergePolicyGates.IndependentVerification, PolicyGateState.Block, "independent-verification-ambiguous"));
    }

    [Fact]
    public void DisabledRepositoryPolicyRemainsHumanMergeable()
    {
        var policy = Policy();
        policy.AutomaticMergeEnabled = false;

        var result = AutoMergeEligibilityPolicy.Evaluate(policy, EligibleInput(), Now);

        result.Decision.Should().Be(PolicyDecisions.HumanOnly);
        result.Gates.Should().ContainSingle(x => x.Gate == AutoMergePolicyGates.RepositoryOptIn).Which.ReasonCode
            .Should().Be("automatic-merge-disabled");
    }

    [Fact]
    public void AutoMergePolicyWithoutEveryMandatorySensitiveCategoryFailsClosed()
    {
        var policy = Policy();
        policy.ForbiddenChangeCategoriesJson = "[\"schema\"]";

        var result = AutoMergeEligibilityPolicy.Evaluate(policy, EligibleInput(), Now);

        result.Decision.Should().Be(PolicyDecisions.HumanOnly);
        result.Gates.Should().ContainSingle(x => x.Gate == "policy-definition").Which.ReasonCode
            .Should().Be("merge-policy-invalid");
    }

    [Fact]
    public void AutoMergePolicyWithoutAnExplicitRequiredCheckFailsClosed()
    {
        var policy = Policy();
        policy.RequiredChecksJson = "[]";

        var result = AutoMergeEligibilityPolicy.Evaluate(policy, EligibleInput(), Now);

        result.Decision.Should().Be(PolicyDecisions.HumanOnly);
        result.Gates.Should().ContainSingle(x => x.Gate == "policy-definition").Which.ReasonCode
            .Should().Be("merge-policy-invalid");
    }

    [Fact]
    public void AutoMergePolicyCannotDisableTheTrustedRollbackOrStopRequirement()
    {
        var policy = Policy();
        policy.RequireRollbackOrStopCapability = false;

        var result = AutoMergeEligibilityPolicy.Evaluate(policy, EligibleInput(), Now);

        result.Decision.Should().Be(PolicyDecisions.HumanOnly);
        result.Gates.Should().ContainSingle(x => x.Gate == "policy-definition").Which.ReasonCode
            .Should().Be("merge-policy-invalid");
    }

    [Fact]
    public void DeterministicRiskClassifierAllowsOnlyPrivateImplementationSourceChanges()
    {
        var lowRisk = RepairChangeRiskClassifier.Classify(
            [new("src/Orders/OrderCalculator.cs", ["return subtotal + tax;"])]);
        var publicContract = RepairChangeRiskClassifier.Classify(
            [new("src/Orders/OrderCalculator.cs", ["public decimal Calculate(Order order)"])]);

        lowRisk.Should().BeEmpty();
        publicContract.Should().Contain("public-contract");
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
        RepairChangeRiskClassifier.Classify([new(path, ["private static void Change() { }"])])
            .Should().Contain(expectedCategory);
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

        result.PolicyVersion.Should().Be("3");
        result.Decision.Should().Be(PolicyDecisions.Deny);
        result.Gates.Should().ContainSingle(x => x.Gate == "forbidden-roots").Which.ReasonCode.Should().Be("forbidden-path");
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

        result.Decision.Should().Be(PolicyDecisions.HumanOnly);
        result.Gates.Should().OnlyContain(x => x.State == PolicyGateState.Pass);
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

        result.Decision.Should().Be(PolicyDecisions.Deny);
        result.Gates.Should().ContainSingle(x => x.Gate == "kill-switches").Which.State.Should().Be(PolicyGateState.Unknown);
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

        result.AutomaticMergeAllowed.Should().BeTrue();
        result.Evaluation.EvaluatedAt.Should().Be(Now);
        result.Evaluation.GateResultsJson.Should().Contain(AutoMergePolicyGates.RequiredChecks);
        result.Evaluation.ReasonCodesJson.Should().Be("[]");
        evaluations.Saved.Should().ContainSingle().Which.Should().BeSameAs(result.Evaluation);
        audits.Events.Should().ContainSingle().Which.Should().Match<HealingAuditEvent>(x =>
            x.AggregateId == request.PullRequestId &&
            x.EventType == "merge-eligibility-evaluated" &&
            x.ReasonCode == "automatic-merge-allowed" &&
            x.PolicyVersion == policy.PolicyVersion &&
            x.InputHash == request.Input.InputDigest &&
            x.OutputHash != null && x.OutputHash.Length == 64 &&
            x.SafeDetailJson.Contains("allow-automatic-merge", StringComparison.Ordinal));
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

        result.AutomaticMergeAllowed.Should().BeFalse();
        result.Evaluation.Decision.Should().Be(PolicyDecision.HumanOnly);
        result.Evaluation.ReasonCodesJson.Should().Contain("required-checks-failed").And.Contain("provider-snapshot-stale");
        audits.Events.Should().ContainSingle().Which.ReasonCode.Should().Be("automatic-merge-blocked");
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

        audits.Events.Should().HaveCount(2);
        audits.Events.Select(x => x.CorrelationId).Should().BeEquivalentTo(
            [first.Evaluation.Id, second.Evaluation.Id]);
        audits.Events.Should().OnlyContain(x => x.CausationId == request.CorrelationId);
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
            Elsa.Platform.Healing.Core.Security.HealingAuditQuery query,
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
            Elsa.Platform.Healing.Core.Security.HealingAuditQuery query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<HealingAuditEvent>>(Events);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
