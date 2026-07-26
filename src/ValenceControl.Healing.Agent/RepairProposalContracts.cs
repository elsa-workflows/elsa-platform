using System.Security.Cryptography;
using System.Text;
using System.Collections.Immutable;
using ValenceControl.Healing.Abstractions;

namespace ValenceControl.Healing.Agent;

public static class RepairProposalLimits
{
    public const int MaximumSourceFiles = 128;
    public const int MaximumSourceBytes = 524_288;
    public const int MaximumSourceFileBytes = 65_536;
    public const int MaximumPathCharacters = 1_024;
    public const int MaximumOmittedPaths = 256;
    public const int MaximumRiskSuggestions = 128;
    public const int MaximumRiskSuggestionCharacters = 1_024;
    public const int MaximumRevisionCharacters = 256;
    public const int MaximumTierCharacters = 128;
}

/// <summary>
/// A bounded, inert snapshot of source text selected by a trusted host. The inference provider receives
/// this data directly and is never granted repository, source-control, filesystem, or network tools.
/// </summary>
public sealed record RepairSourceContextBundle(
    string TargetRevision,
    string Digest,
    IReadOnlyList<RepairSourceFile> Files,
    IReadOnlyList<string> OmittedPaths);

public sealed record RepairSourceFile(
    string Path,
    string Content,
    string Digest,
    bool IsTruncated = false);

public sealed record RepairProposalRequest(
    string ProtocolVersion,
    Guid AttemptId,
    string BaseRevision,
    string TargetRevision,
    string? ProducingRevision,
    RepairAgentInferenceEvidence Evidence,
    RepairSourceContextBundle SourceContext,
    RepairAgentBudget Budget);

/// <summary>
/// An immutable proposal based only on supplied evidence and source context. It deliberately contains no
/// reproduction, regression-test, repository-validation, workflow, credential, or publication claims.
/// </summary>
public sealed record RepairProposal(
    string Classification,
    decimal Confidence,
    string CausalSummary,
    string UnifiedDiff,
    ImmutableArray<RepairChangedPathSuggestion> ChangedPaths,
    ImmutableArray<string> RiskSuggestions,
    string RollbackSummary,
    RepairProposalUsage Usage);

public sealed record RepairProposalUsage(
    long InputUnits,
    long OutputUnits,
    TimeSpan InferenceDuration);

public interface IRepairProposalProvider
{
    ValueTask<RepairProposal> ProposeAsync(
        RepairProposalRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves a trusted, bounded source snapshot before provider inference. Implementations belong in a
/// source-provider adapter; the managed inference provider itself never receives repository access.
/// </summary>
public interface IRepairSourceContextProvider
{
    ValueTask<RepairSourceContextBundle> GetSourceContextAsync(
        RepairAgentRequest request,
        CancellationToken cancellationToken = default);
}

public static class RepairProposalProtocol
{
    public static void ValidateRequest(RepairProposalRequest request)
    {
        if (request is null ||
            request.SourceContext is null ||
            request.Evidence is null ||
            request.Budget is null ||
            request.ProtocolVersion != HealingContractVersions.AgentProtocol ||
            request.AttemptId == Guid.Empty ||
            !IsBoundedIdentifier(request.BaseRevision, RepairProposalLimits.MaximumRevisionCharacters) ||
            !IsBoundedIdentifier(request.TargetRevision, RepairProposalLimits.MaximumRevisionCharacters) ||
            request.ProducingRevision is not null && !IsBoundedIdentifier(request.ProducingRevision, RepairProposalLimits.MaximumRevisionCharacters) ||
            !IsBoundedIdentifier(request.Evidence.Tier, RepairProposalLimits.MaximumTierCharacters) ||
            request.Evidence.CanonicalJson is null ||
            request.Evidence.OmittedFields is null ||
            Encoding.UTF8.GetByteCount(request.Evidence.CanonicalJson) > RepairAgentGatewayLimits.MaximumEvidenceBytes ||
            request.Evidence.OmittedFields.Count > RepairAgentGatewayLimits.MaximumCollectionItems ||
            request.Evidence.OmittedFields.Any(x => !IsBoundedIdentifier(x, RepairProposalLimits.MaximumPathCharacters)) ||
            !IsDigest(request.Evidence.Digest) ||
            !FixedTimeEquals(request.Evidence.Digest, RepairAgentGateway.ComputeSha256Digest(request.Evidence.CanonicalJson)) ||
            request.Budget.TimeLimit <= TimeSpan.Zero ||
            request.Budget.TimeLimit > RepairAgentGatewayLimits.MaximumTimeLimit ||
            request.Budget.InferenceUnitLimit <= 0 ||
            request.Budget.RepositoryRunLimit < 0 ||
            !string.Equals(request.TargetRevision, request.SourceContext.TargetRevision, StringComparison.Ordinal))
            throw Invalid("repair-proposal.request.invalid");

        ValidateSourceContext(request.SourceContext);
    }

    public static void ValidateProposal(RepairProposal proposal, RepairAgentBudget budget)
    {
        if (proposal is null ||
            proposal.ChangedPaths.IsDefault ||
            proposal.RiskSuggestions.IsDefault ||
            proposal.Usage is null ||
            proposal.Classification is not (
                RepairAgentClassifications.InferredHighConfidence or
                RepairAgentClassifications.InsufficientConfidence or
                RepairAgentClassifications.RevisionUnverified) ||
            proposal.Confidence is < 0 or > 1 ||
            string.IsNullOrWhiteSpace(proposal.CausalSummary) ||
            proposal.UnifiedDiff is null ||
            string.IsNullOrWhiteSpace(proposal.RollbackSummary))
            throw Invalid("repair-proposal.result.invalid");

        if (proposal.Classification == RepairAgentClassifications.InsufficientConfidence &&
            (!string.IsNullOrEmpty(proposal.UnifiedDiff) || !proposal.ChangedPaths.IsEmpty))
            throw Invalid("repair-proposal.result.classification-inconsistent");

        if (proposal.Usage.InputUnits < 0 ||
            proposal.Usage.OutputUnits < 0 ||
            proposal.Usage.InputUnits > budget.InferenceUnitLimit - proposal.Usage.OutputUnits ||
            proposal.Usage.InferenceDuration < TimeSpan.Zero ||
            proposal.Usage.InferenceDuration > budget.TimeLimit)
            throw Invalid("repair-proposal.result.budget-exceeded");

        if (Encoding.UTF8.GetByteCount(proposal.UnifiedDiff) > RepairAgentGatewayLimits.MaximumPatchBytes ||
            proposal.CausalSummary.Length > RepairAgentGatewayLimits.MaximumSummaryCharacters ||
            proposal.RollbackSummary.Length > RepairAgentGatewayLimits.MaximumSummaryCharacters ||
            proposal.ChangedPaths.Length > RepairAgentGatewayLimits.MaximumCollectionItems ||
            proposal.RiskSuggestions.Length > RepairProposalLimits.MaximumRiskSuggestions ||
            proposal.ChangedPaths.Any(x =>
                x is null ||
                !IsSafeRelativePath(x.Path) ||
                x.ChangeKind is not ("modified" or "added" or "deleted") ||
                x.RiskCategory?.Length > RepairProposalLimits.MaximumRiskSuggestionCharacters) ||
            proposal.RiskSuggestions.Any(x => x is null || x.Length > RepairProposalLimits.MaximumRiskSuggestionCharacters))
            throw Invalid("repair-proposal.result.bounds");

        if ((proposal.Classification == RepairAgentClassifications.InsufficientConfidence && proposal.UnifiedDiff.Length > 0) ||
            (proposal.Classification == RepairAgentClassifications.InferredHighConfidence &&
             proposal.Confidence < 0.80m))
            throw Invalid("repair-proposal.result.classification-invalid");
    }

    public static string ComputeSourceContextDigest(RepairSourceContextBundle sourceContext)
    {
        var canonical = new StringBuilder(sourceContext.TargetRevision);
        foreach (var file in sourceContext.Files)
            canonical.Append('\n').Append(file.Path).Append('\n').Append(file.Digest).Append('\n').Append(file.IsTruncated ? '1' : '0');
        foreach (var path in sourceContext.OmittedPaths)
            canonical.Append("\nomitted:").Append(path);
        return RepairAgentGateway.ComputeSha256Digest(canonical.ToString());
    }

    private static void ValidateSourceContext(RepairSourceContextBundle sourceContext)
    {
        if (sourceContext.Files is null ||
            sourceContext.OmittedPaths is null ||
            sourceContext.Files.Count == 0 ||
            sourceContext.Files.Count > RepairProposalLimits.MaximumSourceFiles ||
            sourceContext.OmittedPaths.Count > RepairProposalLimits.MaximumOmittedPaths ||
            !IsDigest(sourceContext.Digest) ||
            ExceedsTotalSourceLimit(sourceContext.Files) ||
            sourceContext.OmittedPaths.Any(x => !IsSafeRelativePath(x)) ||
            sourceContext.Files.Any(x =>
                x is null ||
                !IsSafeRelativePath(x.Path) ||
                x.Content is null ||
                Encoding.UTF8.GetByteCount(x.Content) > RepairProposalLimits.MaximumSourceFileBytes ||
                !IsDigest(x.Digest) ||
                !FixedTimeEquals(x.Digest, RepairAgentGateway.ComputeSha256Digest(x.Content))) ||
            sourceContext.Files.Select(x => x.Path).Distinct(StringComparer.Ordinal).Count() != sourceContext.Files.Count ||
            !FixedTimeEquals(sourceContext.Digest, ComputeSourceContextDigest(sourceContext)))
            throw Invalid("repair-proposal.source-context.invalid");
    }

    private static bool ExceedsTotalSourceLimit(IReadOnlyList<RepairSourceFile> files)
    {
        var total = 0L;
        foreach (var file in files)
        {
            if (file?.Content is null)
                return true;
            total += Encoding.UTF8.GetByteCount(file.Content);
            if (total > RepairProposalLimits.MaximumSourceBytes)
                return true;
        }

        return false;
    }

    private static bool IsSafeRelativePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.Length <= RepairProposalLimits.MaximumPathCharacters &&
        !Path.IsPathRooted(path) &&
        !path.Contains('\\') &&
        !path.Contains(':') &&
        !path.Any(char.IsControl) &&
        path.Split('/').All(x => x is not ("" or "." or ".."));

    private static bool IsBoundedIdentifier(string? value, int maximumCharacters) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumCharacters &&
        !value.Any(char.IsControl);

    private static bool IsDigest(string? value) =>
        value is { Length: 71 } &&
        value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef".AsSpan()) < 0;

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));

    private static RepairAgentProtocolException Invalid(string reasonCode) => new(reasonCode);
}
