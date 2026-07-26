using System.Security.Cryptography;
using System.Text;
using ValenceControl.Healing.Abstractions;

namespace ValenceControl.Healing.GitHub;

public sealed record TrustedGitHubPublicationPolicy(
    string PolicyVersion,
    string PolicyHash,
    IReadOnlyList<string> AllowedRoots,
    IReadOnlyList<string> ForbiddenRoots,
    int MaximumFiles,
    int MaximumChangedLines,
    int MaximumPatchBytes);

public sealed record TrustedGitHubPublicationContext(
    GitHubRepositoryAuthorization Authorization,
    TrustedGitHubPublicationPolicy PathPolicy,
    string EvidenceTier = "unknown",
    string ProducingRevisionStatus = "unknown");

public interface ITrustedGitHubPublicationContextResolver
{
    ValueTask<TrustedGitHubPublicationContext?> ResolveAsync(
        RepairPublicationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record TrustedGitHubPatchPlan(
    string Branch,
    string BaseBranch,
    string ExpectedBaseRevision,
    string CommitMessage,
    string PullRequestTitle,
    string PullRequestBody,
    bool IsDraft,
    ParsedUnifiedDiff Patch);

/// <summary>Provider mutation boundary. Implementations receive a token only after all publisher gates pass.</summary>
public interface ITrustedGitHubRepositoryPublisher
{
    ValueTask<string?> GetTargetRevisionAsync(
        GitHubRepositoryAuthorization authorization,
        string targetBranch,
        string installationToken,
        CancellationToken cancellationToken = default);

    ValueTask<bool> IsCommitReachableAsync(
        GitHubRepositoryAuthorization authorization,
        string ancestorRevision,
        string targetRevision,
        string installationToken,
        CancellationToken cancellationToken = default);

    ValueTask<ProviderPullRequestReference> PublishAsync(
        GitHubRepositoryAuthorization authorization,
        string installationToken,
        TrustedGitHubPatchPlan plan,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public sealed class TrustedGitHubPatchPublisher(
    GitHubAppTokenProvider tokenProvider,
    ITrustedGitHubPublicationContextResolver contextResolver,
    ITrustedGitHubRepositoryPublisher repositoryPublisher) : ITrustedPatchPublisher
{
    private static readonly string[] PermanentForbiddenPaths =
    [
        ".github/workflows",
        ".github/actions",
        ".elsa/healing",
        "templates/healing",
        "CODEOWNERS"
    ];

    public async ValueTask<ProviderPullRequestReference> PublishAsync(
        RepairPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = await contextResolver.ResolveAsync(request, cancellationToken)
                      ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.PublicationDenied);
        ValidateAuthority(request, context.Authorization);
        ValidatePolicy(request, context.PathPolicy);
        ValidateResult(request);

        var patch = UnifiedDiffParser.Parse(request.Result.UnifiedDiff, context.PathPolicy.MaximumPatchBytes);
        ValidatePatch(patch, context.PathPolicy);
        var digest = $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Result.UnifiedDiff))).ToLowerInvariant()}";
        if (!FixedEquals(digest, request.Result.PatchDigest))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.PatchInvalid);

        var inspectionToken = await tokenProvider.CreateRepositoryTokenAsync(
            context.Authorization.Credential,
            context.Authorization.InstallationId,
            GitHubInstallationTokenRequest.MetadataRead(context.Authorization.Name),
            cancellationToken) ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.TokenUnavailable);
        var currentRevision = await repositoryPublisher.GetTargetRevisionAsync(
            context.Authorization, request.TargetBranch, inspectionToken.Value, cancellationToken);
        if (currentRevision is null || !FixedEquals(currentRevision, request.ExpectedTargetRevision))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.TargetRevisionStale);

        var token = await tokenProvider.CreateRepositoryTokenAsync(
            context.Authorization.Credential,
            context.Authorization.InstallationId,
            GitHubInstallationTokenRequest.ContentAndPullRequestWrite(context.Authorization.Name),
            cancellationToken) ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.TokenUnavailable);

        var draft = !string.Equals(request.Result.Classification, "reproduced", StringComparison.OrdinalIgnoreCase);
        var plan = new TrustedGitHubPatchPlan(
            $"valence-control-healing/{request.AttemptId:N}",
            request.TargetBranch,
            request.ExpectedTargetRevision,
            $"fix: remediate Valence Control Healing incident {request.IncidentId:N}",
            $"[Valence Control Healing] {SafeText(request.Result.CausalSummary, 160)}",
            BuildPullRequestBody(request, patch, context),
            draft,
            patch);
        return await repositoryPublisher.PublishAsync(
            context.Authorization, token.Value, plan, request.IdempotencyKey, cancellationToken);
    }

    private static void ValidateAuthority(RepairPublicationRequest request, GitHubRepositoryAuthorization authorization)
    {
        var repository = request.Repository;
        if (authorization.ProviderConnectionId != repository.ProviderConnectionId ||
            !FixedEquals(authorization.RepositoryProviderId, repository.RepositoryProviderId) ||
            !FixedEquals(authorization.Owner, repository.Owner) || !FixedEquals(authorization.Name, repository.Name))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.RepositoryNotAuthorized);
    }

    private static void ValidatePolicy(RepairPublicationRequest request, TrustedGitHubPublicationPolicy policy)
    {
        var evaluation = request.PublicationPolicy;
        if (request.ProtocolVersion != HealingContractVersions.ProviderProtocol ||
            request.Result.ProtocolVersion != HealingContractVersions.AgentProtocol ||
            evaluation.ProtocolVersion != HealingContractVersions.PolicyProtocol ||
            !FixedEquals(evaluation.PolicyVersion, policy.PolicyVersion) || !FixedEquals(evaluation.PolicyHash, policy.PolicyHash) ||
            evaluation.Decision != PolicyDecisions.AllowPublication || evaluation.Gates.Count == 0 ||
            evaluation.Gates.Any(x => x.State != PolicyGateState.Pass) ||
            policy.MaximumFiles <= 0 || policy.MaximumChangedLines <= 0 || policy.MaximumPatchBytes <= 0 ||
            policy.AllowedRoots.Count == 0)
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.PublicationDenied);
    }

    private static void ValidateResult(RepairPublicationRequest request)
    {
        if (request.IncidentId == Guid.Empty || request.EpisodeId == Guid.Empty || request.AttemptId == Guid.Empty ||
            request.Result.AttemptId != request.AttemptId || string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 200 || !IsGitRevision(request.ExpectedTargetRevision) ||
            !IsSafeRef(request.TargetBranch) || !FixedEquals(request.Result.TargetRevision, request.ExpectedTargetRevision) ||
            request.Result.Classification is not ("reproduced" or "inferred-high-confidence" or "revision-unverified"))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.PublicationDenied);
    }

    private static void ValidatePatch(ParsedUnifiedDiff patch, TrustedGitHubPublicationPolicy policy)
    {
        if (patch.Files.Count > policy.MaximumFiles || patch.ChangedLines > policy.MaximumChangedLines ||
            patch.SizeBytes > policy.MaximumPatchBytes)
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.PatchInvalid);

        var allowedRoots = policy.AllowedRoots.Select(NormalizePolicyRoot).ToArray();
        var forbiddenRoots = policy.ForbiddenRoots.Concat(PermanentForbiddenPaths).Select(NormalizePolicyRoot).ToArray();
        foreach (var path in patch.Files.Select(x => x.EffectivePath))
        {
            if (!allowedRoots.Any(root => IsWithin(path, root)) || forbiddenRoots.Any(root => IsWithin(path, root)) ||
                path.Split('/').Any(x => x.Equals("CODEOWNERS", StringComparison.OrdinalIgnoreCase)))
                throw new GitHubSecurityException(GitHubSecurityReasonCodes.PatchInvalid);
        }
    }

    private static string NormalizePolicyRoot(string value) => value.TrimEnd('/') is "" or "."
        ? string.Empty
        : UnifiedDiffParser.NormalizePath(value.TrimEnd('/'));
    private static bool IsWithin(string path, string root) => root.Length == 0 || path.Equals(root, StringComparison.Ordinal) || path.StartsWith(root + "/", StringComparison.Ordinal);
    private static bool IsGitRevision(string value) => value.Length is 40 or 64 && value.All(Uri.IsHexDigit);
    private static bool IsSafeRef(string value) => value.Length is > 0 and <= 255 && !value.Contains("..", StringComparison.Ordinal) &&
        !value.Any(x => char.IsControl(x) || char.IsWhiteSpace(x) || x is '~' or '^' or ':' or '?' or '*' or '[' or '\\');
    private static bool FixedEquals(string left, string right) => left.Length == right.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    private static string SafeText(string value, int max)
    {
        var safe = System.Net.WebUtility.HtmlEncode(string.Concat(value.Where(x => !char.IsControl(x))))
            .Replace("@", "@\u200B", StringComparison.Ordinal);
        return safe[..Math.Min(max, safe.Length)];
    }

    private static string BuildPullRequestBody(
        RepairPublicationRequest request,
        ParsedUnifiedDiff patch,
        TrustedGitHubPublicationContext context) => $"""
        <!-- valence-control-healing:attempt:{request.AttemptId:N}:patch:{request.Result.PatchDigest} -->

        ## Valence Control Healing repair

        - Incident: `{request.IncidentId:D}`
        - Episode: `{request.EpisodeId:D}`
        - Classification: `{SafeText(request.Result.Classification, 80)}`
        - Reproduced: `{request.Result.Reproduction.WasReproduced}`
        - Confidence: `{request.Result.Confidence}`
        - Evidence tier: `{SafeText(context.EvidenceTier, 80)}`
        - Producing revision: `{SafeText(context.ProducingRevisionStatus, 80)}`
        - Base revision: `{request.ExpectedTargetRevision}`
        - Changed files: `{patch.Files.Count}`
        - Changed lines: `{patch.ChangedLines}`

        ### Causal summary

        {SafeText(request.Result.CausalSummary, 2_000)}

        ### Regression evidence

        {SafeText(request.Result.Regression.Summary, 2_000)}

        ### Validation

        {SafeText(string.Join("\n", request.Result.Validation.Select(x => $"- {x.Kind}: {x.Outcome} — {x.SafeSummary}")), 2_000)}

        ### Risk summary

        {SafeText(string.Join("\n", request.Result.RiskSuggestions.Select(x => $"- {x}")), 2_000)}

        ### Rollback guidance

        {SafeText(request.Result.RollbackSummary, 2_000)}

        This pull request was published by the trusted Valence Control publisher. Repository content and agent output were treated as untrusted data.
        """;
}
