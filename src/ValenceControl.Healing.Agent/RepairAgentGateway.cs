using System.Security.Cryptography;
using System.Text;
using ValenceControl.Healing.Abstractions;

namespace ValenceControl.Healing.Agent;

public static class RepairAgentGatewayLimits
{
    public const int MaximumEvidenceBytes = 65_536;
    public const int MaximumPatchBytes = 1_048_576;
    public const int MaximumSummaryCharacters = 8_192;
    public const int MaximumCollectionItems = 128;
    public static readonly TimeSpan MaximumTimeLimit = TimeSpan.FromHours(1);
}

public static class RepairAgentClassifications
{
    public const string Reproduced = "reproduced";
    public const string InferredHighConfidence = "inferred-high-confidence";
    public const string InsufficientConfidence = "insufficient-confidence";
    public const string RevisionUnverified = "revision-unverified";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Reproduced,
        InferredHighConfidence,
        InsufficientConfidence,
        RevisionUnverified
    };
}

public static class RepairReproductionStatuses
{
    public const string Reproduced = "reproduced";
    public const string NotReproduced = "not-reproduced";
    public const string NotAttempted = "not-attempted";
    public const string Failed = "failed";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Reproduced,
        NotReproduced,
        NotAttempted,
        Failed
    };
}

/// <summary>
/// Provider-neutral inference boundary. It contains no source-provider identity, mutation capability, token,
/// credential, or installation data. Evidence is an inert JSON value and must never be interpreted as instructions.
/// </summary>
public sealed record RepairAgentInferenceRequest(
    string ProtocolVersion,
    Guid AttemptId,
    string BaseRevision,
    string TargetRevision,
    string? ProducingRevision,
    RepairAgentInferenceEvidence Evidence,
    RepairAgentBudget Budget);

public sealed record RepairAgentInferenceEvidence(
    string Tier,
    string CanonicalJson,
    string Digest,
    IReadOnlyList<string> OmittedFields);

public sealed record RepairAgentInferenceResult(
    string WorkflowRunId,
    int WorkflowRunAttempt,
    string Classification,
    decimal Confidence,
    string CausalSummary,
    string UnifiedDiff,
    IReadOnlyList<RepairChangedPathSuggestion> ChangedPaths,
    RepairAgentReproductionResult Reproduction,
    RepairRegressionEvidence Regression,
    IReadOnlyList<RepairValidationResult> Validation,
    IReadOnlyList<string> RiskSuggestions,
    string RollbackSummary,
    RepairUsageSummary Usage,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed record RepairAgentReproductionResult(
    string Status,
    string Summary,
    IReadOnlyList<string> Commands);

public interface IRepairInferenceProvider
{
    ValueTask<RepairAgentInferenceResult> AnalyzeAsync(
        RepairAgentInferenceRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class RepairAgentGateway : IRepairAgentGateway
{
    private readonly IRepairProposalProvider? _proposalProvider;
    private readonly IRepairSourceContextProvider? _sourceContextProvider;
    private readonly IRepairInferenceProvider? _legacyProvider;
    private readonly TimeProvider _timeProvider;

    public RepairAgentGateway(
        IRepairProposalProvider proposalProvider,
        IRepairSourceContextProvider sourceContextProvider,
        TimeProvider timeProvider)
    {
        _proposalProvider = proposalProvider;
        _sourceContextProvider = sourceContextProvider;
        _timeProvider = timeProvider;
    }

    /// <summary>Compatibility constructor for externally hosted agents that report repository execution.</summary>
    public RepairAgentGateway(IRepairInferenceProvider provider, TimeProvider timeProvider)
    {
        _legacyProvider = provider;
        _timeProvider = timeProvider;
    }

    public async ValueTask<RepairResultEnvelope> AnalyzeAsync(
        RepairAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        if (_proposalProvider is not null && _sourceContextProvider is not null)
            return await AnalyzeProposalAsync(request, cancellationToken);

        var inferenceRequest = new RepairAgentInferenceRequest(
            request.ProtocolVersion,
            request.AttemptId,
            request.BaseRevision,
            request.TargetRevision,
            request.ProducingRevision,
            new(
                request.Evidence.Tier,
                request.Evidence.CanonicalJson,
                request.Evidence.Digest,
                request.Evidence.OmittedFields.ToArray()),
            request.Budget);

        var result = await _legacyProvider!.AnalyzeAsync(inferenceRequest, cancellationToken);
        ValidateResult(result, request.Budget, _timeProvider.GetUtcNow());

        var wasAttempted = result.Reproduction.Status != RepairReproductionStatuses.NotAttempted;
        var wasReproduced = result.Reproduction.Status == RepairReproductionStatuses.Reproduced;
        return new RepairResultEnvelope(
            HealingContractVersions.AgentProtocol,
            request.AttemptId,
            result.WorkflowRunId,
            result.WorkflowRunAttempt,
            request.BaseRevision,
            request.TargetRevision,
            result.Classification,
            result.Confidence,
            result.CausalSummary,
            result.UnifiedDiff,
            ComputeSha256Digest(result.UnifiedDiff),
            result.ChangedPaths.ToArray(),
            new(
                wasAttempted,
                wasReproduced,
                result.Reproduction.Status,
                result.Reproduction.Summary,
                result.Reproduction.Commands.ToArray()),
            result.Regression,
            result.Validation.ToArray(),
            result.RiskSuggestions.ToArray(),
            result.RollbackSummary,
            result.Usage,
            new(result.StartedAt, result.CompletedAt),
            result.CompletedAt);
    }

    private async ValueTask<RepairResultEnvelope> AnalyzeProposalAsync(
        RepairAgentRequest request,
        CancellationToken cancellationToken)
    {
        var sourceContext = await _sourceContextProvider!.GetSourceContextAsync(request, cancellationToken);
        var proposalRequest = new RepairProposalRequest(
            request.ProtocolVersion,
            request.AttemptId,
            request.BaseRevision,
            request.TargetRevision,
            request.ProducingRevision,
            new(
                request.Evidence.Tier,
                request.Evidence.CanonicalJson,
                request.Evidence.Digest,
                request.Evidence.OmittedFields.ToArray()),
            sourceContext,
            request.Budget);
        RepairProposalProtocol.ValidateRequest(proposalRequest);

        var proposal = await _proposalProvider!.ProposeAsync(proposalRequest, cancellationToken);
        RepairProposalProtocol.ValidateProposal(proposal, request.Budget);
        var completedAt = _timeProvider.GetUtcNow();
        var startedAt = completedAt - proposal.Usage.InferenceDuration;
        return new(
            HealingContractVersions.AgentProtocol,
            request.AttemptId,
            $"managed:{request.AttemptId:N}",
            1,
            request.BaseRevision,
            request.TargetRevision,
            proposal.Classification,
            proposal.Confidence,
            proposal.CausalSummary,
            proposal.UnifiedDiff,
            ComputeSha256Digest(proposal.UnifiedDiff),
            proposal.ChangedPaths.ToArray(),
            new(
                false,
                false,
                RepairReproductionStatuses.NotAttempted,
                "Managed inference did not execute repository reproduction.",
                []),
            new(false, "Managed inference did not execute or add regression tests.", []),
            [],
            proposal.RiskSuggestions.ToArray(),
            proposal.RollbackSummary,
            new(
                proposal.Usage.InputUnits,
                proposal.Usage.OutputUnits,
                proposal.Usage.InferenceDuration,
                TimeSpan.Zero,
                0),
            new(startedAt, completedAt),
            completedAt);
    }

    public static string ComputeSha256Digest(string value) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))}";

    private void ValidateRequest(RepairAgentRequest request)
    {
        if (request is null ||
            request.Evidence is null ||
            request.Budget is null ||
            request.ProtocolVersion != HealingContractVersions.AgentProtocol ||
            request.AttemptId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.BaseRevision) ||
            string.IsNullOrWhiteSpace(request.TargetRevision) ||
            request.Evidence.ProtocolVersion != HealingContractVersions.AgentProtocol ||
            request.Evidence.AttemptId != request.AttemptId)
            throw Invalid("repair-agent.request.invalid");

        if (request.Evidence.ExpiresAt <= _timeProvider.GetUtcNow())
            throw Invalid("repair-agent.evidence.expired");
        if (request.Evidence.CanonicalJson is null ||
            request.Evidence.Digest is null ||
            request.Evidence.Digest.Length != 71 ||
            !request.Evidence.Digest.StartsWith("sha256:", StringComparison.Ordinal) ||
            request.Evidence.OmittedFields is null ||
            Encoding.UTF8.GetByteCount(request.Evidence.CanonicalJson) > RepairAgentGatewayLimits.MaximumEvidenceBytes ||
            request.Evidence.OmittedFields.Count > RepairAgentGatewayLimits.MaximumCollectionItems)
            throw Invalid("repair-agent.evidence.bounds");
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(ComputeSha256Digest(request.Evidence.CanonicalJson)),
                Encoding.ASCII.GetBytes(request.Evidence.Digest)))
            throw Invalid("repair-agent.evidence.digest-mismatch");

        if (request.Budget.TimeLimit <= TimeSpan.Zero ||
            request.Budget.TimeLimit > RepairAgentGatewayLimits.MaximumTimeLimit ||
            request.Budget.InferenceUnitLimit <= 0 ||
            request.Budget.RepositoryRunLimit <= 0)
            throw Invalid("repair-agent.budget.invalid");
    }

    private static void ValidateResult(RepairAgentInferenceResult result, RepairAgentBudget budget, DateTimeOffset now)
    {
        if (result is null ||
            string.IsNullOrWhiteSpace(result.WorkflowRunId) ||
            result.WorkflowRunAttempt <= 0 ||
            result.Classification is null ||
            !RepairAgentClassifications.All.Contains(result.Classification) ||
            result.Confidence is < 0 or > 1 ||
            result.Reproduction is null ||
            result.Usage is null ||
            result.Reproduction.Status is null ||
            !RepairReproductionStatuses.All.Contains(result.Reproduction.Status))
            throw Invalid("repair-agent.result.invalid");

        if ((result.Classification == RepairAgentClassifications.Reproduced) !=
            (result.Reproduction.Status == RepairReproductionStatuses.Reproduced))
            throw Invalid("repair-agent.result.reproduction-inconsistent");
        if (result.CompletedAt < result.StartedAt || result.CompletedAt > now.AddMinutes(5))
            throw Invalid("repair-agent.result.timing-invalid");
        if (result.Usage.InputUnits < 0 ||
            result.Usage.OutputUnits < 0 ||
            result.Usage.InputUnits > budget.InferenceUnitLimit - result.Usage.OutputUnits ||
            result.Usage.AgentDuration < TimeSpan.Zero ||
            result.Usage.AgentDuration > budget.TimeLimit ||
            result.Usage.RepositoryRunDuration < TimeSpan.Zero ||
            result.Usage.RepositoryRunDuration > budget.TimeLimit ||
            result.Usage.RepositoryRuns < 0 ||
            result.Usage.RepositoryRuns > budget.RepositoryRunLimit)
            throw Invalid("repair-agent.result.budget-exceeded");
        if (result.UnifiedDiff is null ||
            result.CausalSummary is null ||
            result.RollbackSummary is null ||
            result.Reproduction.Summary is null ||
            result.ChangedPaths is null ||
            result.Reproduction.Commands is null ||
            result.Validation is null ||
            result.RiskSuggestions is null ||
            Encoding.UTF8.GetByteCount(result.UnifiedDiff) > RepairAgentGatewayLimits.MaximumPatchBytes ||
            result.CausalSummary.Length > RepairAgentGatewayLimits.MaximumSummaryCharacters ||
            result.RollbackSummary.Length > RepairAgentGatewayLimits.MaximumSummaryCharacters ||
            result.Reproduction.Summary.Length > RepairAgentGatewayLimits.MaximumSummaryCharacters ||
            result.ChangedPaths.Count > RepairAgentGatewayLimits.MaximumCollectionItems ||
            result.Reproduction.Commands.Count > RepairAgentGatewayLimits.MaximumCollectionItems ||
            result.Validation.Count > RepairAgentGatewayLimits.MaximumCollectionItems ||
            result.RiskSuggestions.Count > RepairAgentGatewayLimits.MaximumCollectionItems)
            throw Invalid("repair-agent.result.bounds");
    }

    private static RepairAgentProtocolException Invalid(string reasonCode) => new(reasonCode);
}

public sealed class RepairAgentProtocolException(string reasonCode) : Exception("The repair agent protocol payload is invalid.")
{
    public string ReasonCode { get; } = reasonCode;
}
