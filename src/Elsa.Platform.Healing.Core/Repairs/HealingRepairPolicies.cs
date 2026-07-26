using System.Text.Json;
using Elsa.Platform.Healing.Abstractions;
using ContractGateResult = Elsa.Platform.Healing.Abstractions.PolicyGateResult;

namespace Elsa.Platform.Healing.Core.Repairs;

public enum RepairPolicyObservationState { Satisfied, Failed, Missing, Stale, Ambiguous, Unknown }

public sealed record RepairPolicyObservation(
    string Gate,
    RepairPolicyObservationState State,
    string ReasonCode,
    string? SafeDetail = null)
{
    public static RepairPolicyObservation Satisfied(string gate, string reasonCode, string? safeDetail = null) =>
        new(gate, RepairPolicyObservationState.Satisfied, reasonCode, safeDetail);
}

public static class AutoMergePolicyGates
{
    public const string RepositoryOptIn = "repository-opt-in";
    public const string Publication = "publication";
    public const string ProducingRevision = "producing-revision";
    public const string Reproduction = "reproduction";
    public const string RegressionBefore = "regression-before";
    public const string RegressionAfter = "regression-after";
    public const string IndependentVerification = "independent-verification";
    public const string RequiredChecks = "required-checks";
    public const string BranchProtection = "branch-protection";
    public const string LowRiskPaths = "low-risk-paths";
    public const string ChangeSize = "change-size";
    public const string ChangeCategories = "change-categories";
    public const string RollbackOrStop = "rollback-or-stop";
    public const string HeadRevision = "head-revision";
    public const string ProviderSnapshot = "provider-snapshot";
    public const string KillSwitches = "kill-switches";
}

public sealed record AutoMergeEligibilityInput(
    string InputDigest,
    IReadOnlyList<RepairPolicyObservation> Observations);

public static class AutoMergeEligibilityPolicy
{
    public static readonly IReadOnlySet<string> RequiredForbiddenChangeCategories =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "public-contract", "schema", "dependency", "authentication", "secret",
            "infrastructure", "deployment", "self-protection"
        };

    public static readonly IReadOnlyList<string> RequiredGates =
    [
        AutoMergePolicyGates.Publication,
        AutoMergePolicyGates.ProducingRevision,
        AutoMergePolicyGates.Reproduction,
        AutoMergePolicyGates.RegressionBefore,
        AutoMergePolicyGates.RegressionAfter,
        AutoMergePolicyGates.IndependentVerification,
        AutoMergePolicyGates.RequiredChecks,
        AutoMergePolicyGates.BranchProtection,
        AutoMergePolicyGates.LowRiskPaths,
        AutoMergePolicyGates.ChangeSize,
        AutoMergePolicyGates.ChangeCategories,
        AutoMergePolicyGates.RollbackOrStop,
        AutoMergePolicyGates.HeadRevision,
        AutoMergePolicyGates.ProviderSnapshot,
        AutoMergePolicyGates.KillSwitches
    ];

    public static PolicyEvaluationSnapshot Evaluate(
        MergePolicy policy,
        AutoMergeEligibilityInput input,
        DateTimeOffset evaluatedAt)
    {
        ValidatePolicy(policy);
        ArgumentNullException.ThrowIfNull(input);
        HealingRepairPolicies.ValidateDigest(input.InputDigest, nameof(input.InputDigest));
        ArgumentNullException.ThrowIfNull(input.Observations);

        var definitionValid = IsDefinitionValid(policy);
        var gates = new List<ContractGateResult>(RequiredGates.Count + 3)
        {
            new(
                "policy-definition",
                definitionValid ? PolicyGateState.Pass : PolicyGateState.Block,
                definitionValid ? "merge-policy-valid" : "merge-policy-invalid"),
            new(
                AutoMergePolicyGates.RepositoryOptIn,
                policy.AutomaticMergeEnabled ? PolicyGateState.Pass : PolicyGateState.Block,
                policy.AutomaticMergeEnabled ? "automatic-merge-enabled" : "automatic-merge-disabled")
        };
        gates.AddRange(HealingRepairPolicies.ResolveRequiredObservations(RequiredGates, input.Observations));

        var unexpected = input.Observations
            .Where(x => !RequiredGates.Contains(x.Gate, StringComparer.Ordinal))
            .Select(x => x.Gate)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unexpected.Length > 0)
            gates.Add(new ContractGateResult("gate-set", PolicyGateState.Block, "unexpected-merge-gate"));

        var decision = gates.All(x => x.State == PolicyGateState.Pass)
            ? PolicyDecisions.AllowAutomaticMerge
            : PolicyDecisions.HumanOnly;
        return HealingRepairPolicies.Snapshot(policy, input.InputDigest, decision, gates, evaluatedAt);
    }

    private static void ValidatePolicy(MergePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        HealingRepairPolicies.ValidatePolicyIdentity(policy);
    }

    private static bool IsDefinitionValid(MergePolicy policy)
    {
        var requiredChecks = ParseStringArray(policy.RequiredChecksJson);
        var forbiddenCategories = ParseStringArray(policy.ForbiddenChangeCategoriesJson);
        return requiredChecks is { Count: > 0 } &&
               forbiddenCategories is not null &&
               RequiredForbiddenChangeCategories.IsSubsetOf(forbiddenCategories) &&
               policy.RequireRollbackOrStopCapability &&
               policy.IndependentVerifier is { Length: > 0 and <= 256 };
    }

    private static IReadOnlySet<string>? ParseStringArray(string json)
    {
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(json);
            return values is not null && values.All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 256)
                ? values.ToHashSet(StringComparer.Ordinal)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record RepairDiffFileObservation(
    string Path,
    IReadOnlyList<string> ChangedLines);

/// <summary>
/// Conservative, deterministic classification for automatic merge. Unknown file types and any potential
/// governance-sensitive change stay human-only; agent-provided risk labels are never authority for this gate.
/// </summary>
public static class RepairChangeRiskClassifier
{
    private static readonly string[] SourceExtensions = [".cs", ".fs", ".vb"];
    private static readonly string[] DependencyNames =
        [".csproj", ".fsproj", ".vbproj", ".props", ".targets", ".sln", ".slnx", "packages.lock.json", "nuget.config", "global.json"];
    private static readonly string[] AuthenticationTerms =
        ["authentication", "authorization", "identity", "permission", "principal", "claims"];
    private static readonly string[] SecretTerms =
        ["secret", "credential", "password", "privatekey", "private-key", "connectionstring", "connection-string",
            "accesstoken", "access-token", "refreshtoken", "refresh-token", "bearertoken", "bearer-token", "apikey", "api-key"];

    public static IReadOnlySet<string> Classify(IReadOnlyList<RepairDiffFileObservation> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var categories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var path = file.Path.Replace('\\', '/').ToLowerInvariant();
            var originalName = file.Path.Replace('\\', '/').Split('/').LastOrDefault() ?? string.Empty;
            var name = path.Split('/').LastOrDefault() ?? string.Empty;
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (path.StartsWith(".github/", StringComparison.Ordinal) || path.StartsWith(".elsa/healing/", StringComparison.Ordinal) ||
                path.StartsWith("templates/healing/", StringComparison.Ordinal) || name.Equals("codeowners", StringComparison.Ordinal))
                categories.Add("self-protection");
            if (segments.Any(x => x is "migrations" or "migration" or "schema") || name.EndsWith("modelsnapshot.cs", StringComparison.Ordinal) ||
                name.Contains("dbcontext", StringComparison.Ordinal) || name.Contains("modelconfiguration", StringComparison.Ordinal) ||
                name.Contains("entityconfiguration", StringComparison.Ordinal) || name.EndsWith(".sql", StringComparison.Ordinal))
                categories.Add("schema");
            if (DependencyNames.Any(x => name.EndsWith(x, StringComparison.Ordinal)))
                categories.Add("dependency");
            if (segments.Any(x => x is "infra" or "infrastructure" or "terraform" or "helm" or "k8s" or "kubernetes") ||
                name.StartsWith("dockerfile", StringComparison.Ordinal) || name.EndsWith(".bicep", StringComparison.Ordinal) ||
                name.EndsWith(".tf", StringComparison.Ordinal))
                categories.Add("infrastructure");
            if (segments.Any(x => x is "deploy" or "deployment" or "deployments" or "release" or "releases"))
                categories.Add("deployment");
            if (segments.Any(x => x is "contracts" or "abstractions" or "controllers" or "endpoints" or "api") ||
                originalName.Length > 2 && originalName[0] == 'I' && char.IsUpper(originalName[1]) &&
                SourceExtensions.Any(x => name.EndsWith(x, StringComparison.Ordinal)) ||
                name.EndsWith("dto.cs", StringComparison.Ordinal) || name.EndsWith("request.cs", StringComparison.Ordinal) ||
                name.EndsWith("response.cs", StringComparison.Ordinal))
                categories.Add("public-contract");
            if (segments.Contains("auth", StringComparer.Ordinal) || ContainsTerm(segments, AuthenticationTerms))
                categories.Add("authentication");
            if (segments.Any(x => x is "token" or "tokens") || ContainsTerm(segments, SecretTerms))
                categories.Add("secret");

            foreach (var changedLine in file.ChangedLines)
            {
                var line = changedLine.ToLowerInvariant();
                if (ContainsWord(line, "public") || ContainsWord(line, "protected"))
                    categories.Add("public-contract");
                if (AuthenticationTerms.Any(line.Contains))
                    categories.Add("authentication");
                if (SecretTerms.Any(line.Contains))
                    categories.Add("secret");
                if (line.Contains("onmodelcreating", StringComparison.Ordinal) ||
                    line.Contains("entitytypebuilder", StringComparison.Ordinal) ||
                    line.Contains("hascolumn", StringComparison.Ordinal))
                    categories.Add("schema");
            }

            if (!SourceExtensions.Any(x => name.EndsWith(x, StringComparison.Ordinal)))
                categories.Add("unknown");
        }
        return categories;
    }

    private static bool ContainsTerm(IEnumerable<string> values, IEnumerable<string> terms) =>
        values.Any(value => terms.Any(term => value.Contains(term, StringComparison.Ordinal)));

    private static bool ContainsWord(string value, string word)
    {
        var index = value.IndexOf(word, StringComparison.Ordinal);
        while (index >= 0)
        {
            var before = index == 0 || !char.IsAsciiLetterOrDigit(value[index - 1]) && value[index - 1] != '_';
            var end = index + word.Length;
            var after = end == value.Length || !char.IsAsciiLetterOrDigit(value[end]) && value[end] != '_';
            if (before && after)
                return true;
            index = value.IndexOf(word, index + 1, StringComparison.Ordinal);
        }
        return false;
    }
}

public sealed record RepairPathChange(
    string Path,
    int ChangedLines,
    bool IsBinary = false,
    bool IsRename = false,
    bool IsSymlink = false,
    bool IsSubmodule = false);

public sealed record PathPolicyEvaluationInput(
    string InputDigest,
    IReadOnlyList<RepairPathChange> Changes,
    int PatchBytes);

public static class HealingPathPolicy
{
    public static PolicyEvaluationSnapshot Evaluate(PathPolicy policy, PathPolicyEvaluationInput input, DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(input);
        HealingRepairPolicies.ValidatePolicyIdentity(policy);
        HealingRepairPolicies.ValidateDigest(input.InputDigest, nameof(input.InputDigest));
        ArgumentNullException.ThrowIfNull(input.Changes);

        var allowedRoots = ParseRoots(policy.AllowedRootsJson);
        var forbiddenRoots = ParseRoots(policy.ForbiddenRootsJson);
        var paths = input.Changes.Select(x => NormalizePath(x.Path)).ToArray();
        var validPaths = paths.All(x => x is not null);
        var validLimits = policy.MaxFiles >= 0 && policy.MaxChangedLines >= 0 && policy.MaxPatchBytes >= 0 && input.PatchBytes >= 0 &&
                          input.Changes.All(x => x.ChangedLines >= 0);
        var gates = new ContractGateResult[]
        {
            Gate("nonempty-patch", input.Changes.Count > 0 && input.PatchBytes > 0, "patch-present", "empty-patch"),
            Gate("path-shape", validPaths, "path-shape-valid", "path-shape-invalid"),
            Gate("allowed-roots", allowedRoots is { Count: > 0 } && validPaths && paths.All(x => IsUnderAnyRoot(x!, allowedRoots)),
                "allowed-root-matched", allowedRoots is null ? "allowed-roots-invalid" : "path-outside-allowed-roots"),
            Gate("forbidden-roots", forbiddenRoots is not null && validPaths && paths.All(x => !IsUnderAnyRoot(x!, forbiddenRoots)),
                "forbidden-roots-clear", forbiddenRoots is null ? "forbidden-roots-invalid" : "forbidden-path"),
            Gate("file-count", validLimits && input.Changes.Count <= policy.MaxFiles, "file-count-allowed", "file-count-exceeded"),
            Gate("changed-lines", validLimits && input.Changes.Sum(x => (long)x.ChangedLines) <= policy.MaxChangedLines,
                "changed-lines-allowed", "changed-lines-exceeded"),
            Gate("patch-bytes", validLimits && input.PatchBytes <= policy.MaxPatchBytes, "patch-bytes-allowed", "patch-bytes-exceeded"),
            Gate("binary", policy.AllowBinary || input.Changes.All(x => !x.IsBinary), "binary-policy-satisfied", "binary-change-forbidden"),
            Gate("rename", policy.AllowRenames || input.Changes.All(x => !x.IsRename), "rename-policy-satisfied", "rename-forbidden"),
            Gate("symlink", policy.AllowSymlinks || input.Changes.All(x => !x.IsSymlink), "symlink-policy-satisfied", "symlink-forbidden"),
            Gate("submodule", policy.AllowSubmodules || input.Changes.All(x => !x.IsSubmodule), "submodule-policy-satisfied", "submodule-forbidden")
        };
        return HealingRepairPolicies.Snapshot(
            policy,
            input.InputDigest,
            gates.All(x => x.State == PolicyGateState.Pass) ? PolicyDecisions.AllowPublication : PolicyDecisions.Deny,
            gates,
            evaluatedAt);
    }

    private static IReadOnlyList<string>? ParseRoots(string json)
    {
        try
        {
            var roots = JsonSerializer.Deserialize<string[]>(json);
            if (roots is null)
                return null;
            var normalized = roots.Select(NormalizeRoot).ToArray();
            return normalized.Any(x => x is null)
                ? null
                : normalized.Select(x => x!).Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NormalizeRoot(string? root)
    {
        var normalized = NormalizePath(root);
        return normalized?.TrimEnd('/');
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var normalized = path.Replace('\\', '/').Trim();
        if (normalized.StartsWith('/') || normalized.Contains(':'))
            return null;
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(x => x is "." or ".."))
            return null;
        return string.Join('/', segments);
    }

    private static bool IsUnderAnyRoot(string path, IReadOnlyList<string> roots) =>
        roots.Any(root => path.Equals(root, StringComparison.Ordinal) || path.StartsWith($"{root}/", StringComparison.Ordinal));

    private static ContractGateResult Gate(string name, bool pass, string passed, string blocked) =>
        new(name, pass ? PolicyGateState.Pass : PolicyGateState.Block, pass ? passed : blocked);
}

public sealed record EvidencePolicyEvaluationInput(
    string InputDigest,
    RepairClassification Classification,
    decimal Confidence,
    EvidenceTier Tier,
    IReadOnlyCollection<string> ReleasedFields,
    bool ReproductionAttempted,
    bool Reproduced);

public static class HealingEvidencePolicy
{
    public static PolicyEvaluationSnapshot Evaluate(EvidencePolicy policy, EvidencePolicyEvaluationInput input, DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(input);
        HealingRepairPolicies.ValidatePolicyIdentity(policy);
        HealingRepairPolicies.ValidateDigest(input.InputDigest, nameof(input.InputDigest));
        ArgumentNullException.ThrowIfNull(input.ReleasedFields);

        var permittedFields = ParseFields(policy.PermittedFieldsJson);
        var validPolicy = Enum.IsDefined(policy.MaximumTier) && policy.MinimumInferenceConfidence is >= 0 and <= 1;
        var validClassification = Enum.IsDefined(input.Classification) && input.Confidence is >= 0 and <= 1;
        var reproduced = input.Classification == RepairClassification.Reproduced && input.ReproductionAttempted && input.Reproduced;
        var inferenceAllowed = input.Classification != RepairClassification.InferredHighConfidence ||
                               policy.AllowHighConfidenceInference && input.Confidence >= policy.MinimumInferenceConfidence;
        var classificationAllowed = validClassification && input.Classification != RepairClassification.InsufficientConfidence;
        var gates = new ContractGateResult[]
        {
            Gate("policy-definition", validPolicy, "evidence-policy-valid", "evidence-policy-invalid"),
            Gate("classification", classificationAllowed, "classification-eligible", "insufficient-confidence"),
            Gate("reproduction", !policy.RequireReproduction || reproduced, "reproduction-policy-satisfied", "reproduction-required"),
            Gate("inference", inferenceAllowed, "inference-policy-satisfied", "inference-not-allowed"),
            Gate("evidence-tier", input.Tier <= policy.MaximumTier, "evidence-tier-allowed", "evidence-tier-blocked"),
            Gate("evidence-fields", permittedFields is not null && (permittedFields.Count == 0 || input.ReleasedFields.All(permittedFields.Contains)),
                "evidence-fields-allowed", permittedFields is null ? "evidence-fields-invalid" : "evidence-field-blocked")
        };
        var decision = gates.Any(x => x.State != PolicyGateState.Pass)
            ? PolicyDecisions.Deny
            : input.Classification is RepairClassification.InferredHighConfidence or RepairClassification.RevisionUnverified
                ? PolicyDecisions.HumanOnly
                : PolicyDecisions.AllowPublication;
        return HealingRepairPolicies.Snapshot(policy, input.InputDigest, decision, gates, evaluatedAt);
    }

    private static IReadOnlySet<string>? ParseFields(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json)?.ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ContractGateResult Gate(string name, bool pass, string passed, string blocked) =>
        new(name, pass ? PolicyGateState.Pass : PolicyGateState.Block, pass ? passed : blocked);
}

public sealed record PublicationPolicyEvaluationInput(
    string InputDigest,
    IReadOnlyList<RepairPolicyObservation> Observations,
    bool HumanMergeRequired);

public static class HealingPublicationPolicy
{
    public static readonly IReadOnlyList<string> RequiredGates =
        ["current-authority", "target-revision", "path-policy", "evidence-policy", "kill-switches", "trusted-publisher"];

    public static PolicyEvaluationSnapshot Evaluate(
        HealingPolicyDefinition policy,
        PublicationPolicyEvaluationInput input,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(input);
        HealingRepairPolicies.ValidatePolicyIdentity(policy);
        HealingRepairPolicies.ValidateDigest(input.InputDigest, nameof(input.InputDigest));
        ArgumentNullException.ThrowIfNull(input.Observations);
        var gates = HealingRepairPolicies.ResolveRequiredObservations(RequiredGates, input.Observations);
        var hasUnexpectedGates = input.Observations.Any(x => !RequiredGates.Contains(x.Gate, StringComparer.Ordinal));
        if (hasUnexpectedGates)
            gates = [.. gates, new ContractGateResult("gate-set", PolicyGateState.Block, "unexpected-publication-gate")];
        var decision = gates.All(x => x.State == PolicyGateState.Pass)
            ? input.HumanMergeRequired ? PolicyDecisions.HumanOnly : PolicyDecisions.AllowPublication
            : PolicyDecisions.Deny;
        return HealingRepairPolicies.Snapshot(policy, input.InputDigest, decision, gates, evaluatedAt);
    }
}

public static class HealingRepairPolicies
{
    internal static IReadOnlyList<ContractGateResult> ResolveRequiredObservations(
        IReadOnlyList<string> requiredGates,
        IReadOnlyList<RepairPolicyObservation> observations)
    {
        var groups = observations
            .Where(x => !string.IsNullOrWhiteSpace(x.Gate))
            .GroupBy(x => x.Gate, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
        return requiredGates.Select(gate =>
        {
            if (!groups.TryGetValue(gate, out var candidates))
                return new ContractGateResult(gate, PolicyGateState.Unknown, $"{gate}-missing");
            if (candidates.Length != 1)
                return new ContractGateResult(gate, PolicyGateState.Block, $"{gate}-ambiguous");
            var observation = candidates[0];
            ValidateObservation(observation);
            return new ContractGateResult(gate, ToGateState(observation.State), observation.ReasonCode, observation.SafeDetail);
        }).ToArray();
    }

    internal static PolicyEvaluationSnapshot Snapshot(
        HealingPolicyDefinition policy,
        string inputDigest,
        string decision,
        IReadOnlyList<ContractGateResult> gates,
        DateTimeOffset evaluatedAt) =>
        new(
            HealingContractVersions.PolicyProtocol,
            policy.PolicyVersion,
            policy.PolicyHash,
            inputDigest,
            decision,
            gates,
            evaluatedAt);

    internal static void ValidatePolicyIdentity(HealingPolicyDefinition policy)
    {
        if (policy.Id == Guid.Empty || policy.WorkspaceId == Guid.Empty || policy.ApplicationId == Guid.Empty)
            throw new ArgumentException("A tenant-scoped policy identity is required.", nameof(policy));
        if (string.IsNullOrWhiteSpace(policy.PolicyVersion))
            throw new ArgumentException("PolicyVersion is required.", nameof(policy));
        ValidateDigest(policy.PolicyHash, nameof(policy.PolicyHash));
    }

    internal static void ValidateDigest(string value, string parameterName)
    {
        if (value.Length != 64 || !value.All(char.IsAsciiHexDigit))
            throw new ArgumentException("A lowercase or uppercase SHA-256 digest is required.", parameterName);
    }

    private static void ValidateObservation(RepairPolicyObservation observation)
    {
        if (!Enum.IsDefined(observation.State))
            throw new ArgumentOutOfRangeException(nameof(observation), "Observation state is invalid.");
        if (string.IsNullOrWhiteSpace(observation.ReasonCode) || observation.ReasonCode.Length > 128 ||
            observation.ReasonCode.Any(x => !IsSafeCodeCharacter(x)))
            throw new ArgumentException("Observation ReasonCode is required and limited to 128 characters.", nameof(observation));
        if (observation.SafeDetail?.Length > 256 || observation.SafeDetail?.Any(x => !IsSafeCodeCharacter(x)) == true)
            throw new ArgumentException("Observation SafeDetail is limited to 256 characters.", nameof(observation));
    }

    private static bool IsSafeCodeCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '-' or '_' or '.' or '/' or ':';

    private static PolicyGateState ToGateState(RepairPolicyObservationState state) => state switch
    {
        RepairPolicyObservationState.Satisfied => PolicyGateState.Pass,
        RepairPolicyObservationState.Failed or RepairPolicyObservationState.Ambiguous => PolicyGateState.Block,
        RepairPolicyObservationState.Stale => PolicyGateState.Stale,
        RepairPolicyObservationState.Missing or RepairPolicyObservationState.Unknown => PolicyGateState.Unknown,
        _ => PolicyGateState.Unknown
    };
}
