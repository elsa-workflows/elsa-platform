using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;

namespace Elsa.Platform.Healing.GitHub;

/// <summary>
/// Reads GitHub's current merge constraints and submits an already-authorized merge request. Product merge
/// policy remains in Core; this adapter neither invents gates nor relaxes a failed policy decision.
/// </summary>
public sealed class GitHubMergeProvider(
    HttpClient httpClient,
    GitHubAppTokenProvider tokenProvider,
    IGitHubRepositoryAuthorizationResolver authorizationResolver,
    IGitHubProviderOperationLedger operationLedger,
    TimeProvider? timeProvider = null) : IRepairMergeProvider
{
    private const int MaximumPages = 100;
    private const int PageSize = 100;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<ProviderMergeSnapshot> GetMergeSnapshotAsync(
        ProviderRepositoryReference repository,
        string pullRequestId,
        CancellationToken cancellationToken = default)
    {
        var pullRequestNumber = ParsePullRequestNumber(pullRequestId);
        var authorization = await ResolveAuthorizationAsync(repository, cancellationToken);

        var pullRequestToken = await CreateTokenAsync(
            authorization, GitHubInstallationTokenRequest.PullRequestRead(authorization.Name), cancellationToken);
        var pullRequest = await GetPullRequestAsync(authorization, pullRequestNumber, pullRequestToken, cancellationToken);
        var head = pullRequest.Head!;
        var @base = pullRequest.Base!;

        var protectionToken = await CreateTokenAsync(
            authorization, GitHubInstallationTokenRequest.BranchProtectionRead(authorization.Name), cancellationToken);
        var protection = await GetBranchProtectionAsync(
            authorization, @base.Reference, protectionToken, cancellationToken);

        var checksToken = await CreateTokenAsync(
            authorization, GitHubInstallationTokenRequest.ChecksAndStatusesRead(authorization.Name), cancellationToken);
        var observedAt = _timeProvider.GetUtcNow();
        var checks = await GetChecksAsync(authorization, head.Sha, checksToken, observedAt, cancellationToken);
        var requiredCheckConstraints = GetRequiredCheckConstraints(protection);
        var requiredChecks = requiredCheckConstraints.Select(x => x.Name).ToArray();
        var requiredChecksSatisfied = requiredCheckConstraints.All(required => checks.Any(check =>
            FixedEquals(check.Name, required.Name) &&
            FixedEquals(check.Revision ?? string.Empty, head.Sha) &&
            (required.ProviderAppId is null || check.ProviderAppId == required.ProviderAppId) &&
            string.Equals(check.State, "success", StringComparison.OrdinalIgnoreCase)));
        var providerConstraintsSatisfied = protection is not null && pullRequest.Mergeable is true &&
            string.Equals(pullRequest.State, "open", StringComparison.OrdinalIgnoreCase) && !pullRequest.Draft &&
            string.Equals(pullRequest.MergeableState, "clean", StringComparison.OrdinalIgnoreCase) &&
            requiredChecksSatisfied;

        return new ProviderMergeSnapshot(
            pullRequestNumber.ToString(CultureInfo.InvariantCulture),
            string.Equals(pullRequest.State, "open", StringComparison.OrdinalIgnoreCase),
            pullRequest.Draft,
            head.Sha,
            @base.Sha,
            checks,
            requiredChecks,
            providerConstraintsSatisfied,
            observedAt);
    }

    public async ValueTask<ProviderOperationReceipt> RequestMergeAsync(
        ProviderMergeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMergeRequest(request);
        var pullRequestNumber = ParsePullRequestNumber(request.PullRequestId);
        var authorization = await ResolveAuthorizationAsync(request.Repository, cancellationToken);
        var canonicalPayload = JsonSerializer.Serialize(request);
        var payloadHash = Hash(canonicalPayload);
        var operationKey = new GitHubProviderOperationKey(
            request.Repository.ProviderConnectionId, ProviderOperationKind.RequestMerge, request.IdempotencyKey);
        var replay = await GetReplayAsync(operationKey, canonicalPayload, payloadHash, cancellationToken);
        if (replay is not null)
            return replay with { IsReplay = true };

        await ReserveAsync(operationKey, canonicalPayload, payloadHash, cancellationToken);
        var token = await CreateTokenAsync(
            authorization, GitHubInstallationTokenRequest.MergeWrite(authorization.Name), cancellationToken);
        using var message = NewRequest(
            HttpMethod.Put,
            $"repos/{Escape(authorization.Owner)}/{Escape(authorization.Name)}/pulls/{pullRequestNumber}/merge",
            token);
        message.Content = JsonContent.Create(new { sha = request.ExpectedHeadRevision, merge_method = "squash" });
        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
        var result = await response.Content.ReadFromJsonAsync<MergeResponse>(cancellationToken);
        if (result is null || !result.Merged || !IsGitRevision(result.Sha))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);

        var receipt = new ProviderOperationReceipt(
            request.IdempotencyKey, GetRequestId(response), false, _timeProvider.GetUtcNow());
        await operationLedger.CompleteAsync(
            operationKey,
            canonicalPayload,
            payloadHash,
            JsonSerializer.Serialize(receipt),
            _timeProvider.GetUtcNow(),
            cancellationToken);
        return receipt;
    }

    private async ValueTask<PullRequestResponse> GetPullRequestAsync(
        GitHubRepositoryAuthorization authorization,
        long pullRequestNumber,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(
            HttpMethod.Get,
            $"repos/{Escape(authorization.Owner)}/{Escape(authorization.Name)}/pulls/{pullRequestNumber}", token);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
        var pullRequest = await response.Content.ReadFromJsonAsync<PullRequestResponse>(cancellationToken);
        if (pullRequest is null || pullRequest.Number != pullRequestNumber ||
            pullRequest.Head is null || pullRequest.Base is null ||
            !IsGitRevision(pullRequest.Head.Sha) || !IsGitRevision(pullRequest.Base.Sha) ||
            !IsSafeRef(pullRequest.Base.Reference))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
        return pullRequest;
    }

    private async ValueTask<BranchProtectionResponse?> GetBranchProtectionAsync(
        GitHubRepositoryAuthorization authorization,
        string baseBranch,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(
            HttpMethod.Get,
            $"repos/{Escape(authorization.Owner)}/{Escape(authorization.Name)}/branches/{Escape(baseBranch)}/protection",
            token);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
        return await response.Content.ReadFromJsonAsync<BranchProtectionResponse>(cancellationToken)
               ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
    }

    private async ValueTask<IReadOnlyList<ProviderCheckSnapshot>> GetChecksAsync(
        GitHubRepositoryAuthorization authorization,
        string headRevision,
        string token,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var checks = new List<ProviderCheckSnapshot>();
        for (var page = 1; page <= MaximumPages; page++)
        {
            using var request = NewRequest(HttpMethod.Get,
                $"repos/{Escape(authorization.Owner)}/{Escape(authorization.Name)}/commits/{headRevision}/check-runs?per_page={PageSize}&page={page}", token);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
            var payload = await response.Content.ReadFromJsonAsync<CheckRunsResponse>(cancellationToken)
                          ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
            if (payload.CheckRuns is null)
                throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
            foreach (var check in payload.CheckRuns)
            {
                if (!IsSafeCheckName(check.Name) || !IsGitRevision(check.HeadSha))
                    throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
                var state = string.Equals(check.Status, "completed", StringComparison.OrdinalIgnoreCase)
                    ? check.Conclusion ?? "unknown"
                    : check.Status;
                if (!IsSafeState(state))
                    throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
                if (check.App?.Id is not > 0)
                    throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
                checks.Add(new ProviderCheckSnapshot(check.Name, state, check.HeadSha, observedAt, check.App.Id));
            }
            if (payload.CheckRuns.Count < PageSize)
                break;
            if (page == MaximumPages)
                throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
        }

        for (var page = 1; page <= MaximumPages; page++)
        {
            using var request = NewRequest(HttpMethod.Get,
                $"repos/{Escape(authorization.Owner)}/{Escape(authorization.Name)}/commits/{headRevision}/statuses?per_page={PageSize}&page={page}", token);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
            var statuses = await response.Content.ReadFromJsonAsync<IReadOnlyList<CommitStatusResponse>>(cancellationToken)
                           ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
            foreach (var status in statuses)
            {
                if (!IsSafeCheckName(status.Context) || !IsSafeState(status.State) || !IsGitRevision(status.Sha))
                    throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
                checks.Add(new ProviderCheckSnapshot(status.Context, status.State, status.Sha, observedAt));
            }
            if (statuses.Count < PageSize)
                break;
            if (page == MaximumPages)
                throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
        }

        return checks
            .GroupBy(x => (x.Name, x.ProviderAppId))
            .Select(x => x.First())
            .OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.ProviderAppId)
            .ToArray();
    }

    private async ValueTask<GitHubRepositoryAuthorization> ResolveAuthorizationAsync(
        ProviderRepositoryReference repository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var authorization = await authorizationResolver.ResolveAsync(repository.ProviderConnectionId, cancellationToken);
        if (authorization is null || authorization.ProviderConnectionId != repository.ProviderConnectionId ||
            !FixedEquals(authorization.RepositoryProviderId, repository.RepositoryProviderId) ||
            !FixedEquals(authorization.Owner, repository.Owner) || !FixedEquals(authorization.Name, repository.Name))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.RepositoryNotAuthorized);
        return authorization;
    }

    private async ValueTask<string> CreateTokenAsync(
        GitHubRepositoryAuthorization authorization,
        GitHubInstallationTokenRequest request,
        CancellationToken cancellationToken) =>
        (await tokenProvider.CreateRepositoryTokenAsync(
            authorization.Credential, authorization.InstallationId, request, cancellationToken))?.Value
        ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.TokenUnavailable);

    private async ValueTask<ProviderOperationReceipt?> GetReplayAsync(
        GitHubProviderOperationKey key,
        string canonicalPayload,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var operation = await operationLedger.GetAsync(key, cancellationToken);
        if (operation is null)
            return null;
        if (!FixedEquals(operation.PayloadHash, payloadHash) ||
            !FixedEquals(operation.CanonicalPayloadJson, canonicalPayload))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.IdempotencyConflict);
        if (operation.Status == GitHubProviderOperationStatus.Reserved &&
            operation.UpdatedAt.Add(GitHubProviderOperationDefaults.ReservationLifetime) <= _timeProvider.GetUtcNow())
            return null;
        if (operation.Status != GitHubProviderOperationStatus.Completed || string.IsNullOrWhiteSpace(operation.ResultJson))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.OperationInProgress);
        return JsonSerializer.Deserialize<ProviderOperationReceipt>(operation.ResultJson)
               ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
    }

    private async ValueTask ReserveAsync(
        GitHubProviderOperationKey key,
        string canonicalPayload,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        if (!await operationLedger.TryReserveAsync(
                key, canonicalPayload, payloadHash, _timeProvider.GetUtcNow(), cancellationToken))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.OperationInProgress);
    }

    private static IReadOnlyList<RequiredCheckConstraint> GetRequiredCheckConstraints(BranchProtectionResponse? protection)
    {
        if (protection?.RequiredStatusChecks is null)
            return [];
        var statusChecks = protection.RequiredStatusChecks;
        if (statusChecks.Contexts is null || statusChecks.Checks is null ||
            statusChecks.Contexts.Any(x => !IsSafeCheckName(x)) ||
            statusChecks.Checks.Any(x => x is null || !IsSafeCheckName(x.Context) || x.AppId is < -1 or 0))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
        var appBound = statusChecks.Checks.Select(x =>
            new RequiredCheckConstraint(x!.Context, x.AppId is > 0 ? x.AppId : null)).ToArray();
        return appBound
            .Concat(statusChecks.Contexts
                .Where(context => appBound.All(x => !string.Equals(x.Name, context, StringComparison.Ordinal)))
                .Select(context => new RequiredCheckConstraint(context, null)))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateMergeRequest(ProviderMergeRequest request)
    {
        if (request.ProtocolVersion != HealingContractVersions.ProviderProtocol ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200 ||
            !IsGitRevision(request.ExpectedHeadRevision) ||
            request.MergePolicy is null ||
            request.MergePolicy.ProtocolVersion != HealingContractVersions.PolicyProtocol ||
            !IsSafePolicyVersion(request.MergePolicy.PolicyVersion) ||
            !IsSha256(request.MergePolicy.PolicyHash) || !IsSha256(request.MergePolicy.InputDigest) ||
            request.MergePolicy.Decision != PolicyDecisions.AllowAutomaticMerge ||
            request.MergePolicy.Gates.Count == 0 ||
            request.MergePolicy.Gates.Any(x => x.State != PolicyGateState.Pass))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.InvalidRequest);
    }

    private static long ParsePullRequestNumber(string value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : throw new GitHubSecurityException(GitHubSecurityReasonCodes.InvalidRequest);

    private static HttpRequestMessage NewRequest(HttpMethod method, string uri, string token)
    {
        var request = new HttpRequestMessage(method, uri);
        GitHubAppTokenProvider.AddGitHubHeaders(request, token);
        return request;
    }

    private static string? GetRequestId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-GitHub-Request-Id", out var values) ? values.FirstOrDefault() : null;
    private static bool IsGitRevision(string? value) => value is { Length: 40 or 64 } && value.All(Uri.IsHexDigit);
    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool IsSafePolicyVersion(string? value) => value is { Length: > 0 and <= 100 } && !value.Any(char.IsControl);
    private static bool IsSafeCheckName(string? value) => value is { Length: > 0 and <= 200 } && !value.Any(char.IsControl);
    private static bool IsSafeState(string? value) => value is { Length: > 0 and <= 50 } && !value.Any(char.IsControl);
    private static bool IsSafeRef(string? value) => value is { Length: > 0 and <= 255 } && !value.Contains("..", StringComparison.Ordinal) &&
        !value.Any(x => char.IsControl(x) || char.IsWhiteSpace(x) || x is '~' or '^' or ':' or '?' or '*' or '[' or '\\');
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool FixedEquals(string left, string right) => left.Length == right.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    private static string Escape(string value) => Uri.EscapeDataString(value);

    private sealed record PullRequestResponse(
        [property: JsonPropertyName("number")] long Number,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("head")] GitReferenceResponse? Head,
        [property: JsonPropertyName("base")] GitReferenceResponse? Base,
        [property: JsonPropertyName("mergeable")] bool? Mergeable,
        [property: JsonPropertyName("mergeable_state")] string? MergeableState);

    private sealed record GitReferenceResponse(
        [property: JsonPropertyName("sha")] string Sha,
        [property: JsonPropertyName("ref")] string Reference);

    private sealed record BranchProtectionResponse(
        [property: JsonPropertyName("required_status_checks")] RequiredStatusChecksResponse? RequiredStatusChecks);

    private sealed record RequiredStatusChecksResponse(
        [property: JsonPropertyName("contexts")] IReadOnlyList<string>? Contexts,
        [property: JsonPropertyName("checks")] IReadOnlyList<RequiredCheckResponse?>? Checks);

    private sealed record RequiredCheckResponse(
        [property: JsonPropertyName("context")] string Context,
        [property: JsonPropertyName("app_id")] long? AppId);

    private sealed record CheckRunsResponse(
        [property: JsonPropertyName("check_runs")] IReadOnlyList<CheckRunResponse>? CheckRuns);

    private sealed record CheckRunResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("conclusion")] string? Conclusion,
        [property: JsonPropertyName("head_sha")] string HeadSha,
        [property: JsonPropertyName("app")] CheckRunAppResponse? App);

    private sealed record CheckRunAppResponse([property: JsonPropertyName("id")] long Id);

    private sealed record RequiredCheckConstraint(string Name, long? ProviderAppId);

    private sealed record CommitStatusResponse(
        [property: JsonPropertyName("context")] string Context,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("sha")] string Sha);

    private sealed record MergeResponse(
        [property: JsonPropertyName("sha")] string Sha,
        [property: JsonPropertyName("merged")] bool Merged);
}
