using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;

namespace ValenceControl.Healing.GitHub;

public enum GitHubProviderOperationStatus { Reserved, Completed }

public static class GitHubProviderOperationDefaults
{
    public static readonly TimeSpan ReservationLifetime = TimeSpan.FromMinutes(5);
}

public sealed record GitHubProviderOperationKey(
    Guid ProviderConnectionId,
    ProviderOperationKind Kind,
    string IdempotencyKey);

public sealed record GitHubProviderOperationRecord(
    GitHubProviderOperationKey Key,
    string CanonicalPayloadJson,
    string PayloadHash,
    GitHubProviderOperationStatus Status,
    string? ResultJson,
    DateTimeOffset UpdatedAt);

public interface IGitHubProviderOperationLedger
{
    ValueTask<GitHubProviderOperationRecord?> GetAsync(
        GitHubProviderOperationKey key,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TryReserveAsync(
        GitHubProviderOperationKey key,
        string canonicalPayloadJson,
        string payloadHash,
        DateTimeOffset reservedAt,
        CancellationToken cancellationToken = default);

    ValueTask CompleteAsync(
        GitHubProviderOperationKey key,
        string canonicalPayloadJson,
        string payloadHash,
        string resultJson,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);
}

public sealed class GitHubRepairWorkProvider(
    HttpClient httpClient,
    GitHubAppTokenProvider tokenProvider,
    IGitHubRepositoryAuthorizationResolver authorizationResolver,
    IGitHubProviderOperationLedger operationLedger,
    TimeProvider? timeProvider = null) : IRepairWorkProvider
{
    private const int MaximumTitleLength = 200;
    private const int MaximumSummaryLength = 32_000;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<ProviderWorkItemReference> UpsertWorkItemAsync(
        RepairWorkItemUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProtocolAndIdempotency(request.ProtocolVersion, request.IdempotencyKey);
        if (request.IncidentId == Guid.Empty || request.EpisodeId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > MaximumTitleLength ||
            string.IsNullOrWhiteSpace(request.MachineSummary) || request.MachineSummary.Length > MaximumSummaryLength ||
            !IsSha256(request.MachineSummaryHash) || !FixedEquals(Hash(request.MachineSummary), request.MachineSummaryHash))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.InvalidRequest);

        var authorization = await ResolveAuthorizationAsync(request.Repository, cancellationToken);
        var canonicalPayload = JsonSerializer.Serialize(request);
        var payloadHash = Hash(canonicalPayload);
        var operationKey = new GitHubProviderOperationKey(
            request.Repository.ProviderConnectionId, ProviderOperationKind.UpsertWorkItem, request.IdempotencyKey);
        var replay = await GetReplayAsync<ProviderWorkItemReference>(operationKey, canonicalPayload, payloadHash, cancellationToken);
        if (replay is not null)
            return replay;
        await ReserveAsync(operationKey, canonicalPayload, payloadHash, cancellationToken);

        var token = await tokenProvider.CreateRepositoryTokenAsync(
            authorization.Credential,
            authorization.InstallationId,
            GitHubInstallationTokenRequest.IssueWrite(authorization.Name),
            cancellationToken) ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.TokenUnavailable);

        var marker = $"<!-- valence-control-healing:incident:{request.IncidentId:N}:episode:{request.EpisodeId:N} -->";
        var body = $"{marker}\n\n## Valence Control Healing incident\n\n{NeutralizeMentions(request.MachineSummary)}\n";
        var existing = await FindMachineIssueAsync(authorization, token.Value, marker, cancellationToken);
        var title = NeutralizeMentions(request.Title);
        var result = existing is null
            ? await CreateIssueAsync(authorization, token.Value, title, body, cancellationToken)
            : await UpdateIssueAsync(authorization, token.Value, existing.Number, title, body, cancellationToken);

        await CompleteAsync(operationKey, canonicalPayload, payloadHash, result, cancellationToken);
        return result;
    }

    public async ValueTask<ProviderOperationReceipt> DispatchWorkflowAsync(
        RepairWorkflowDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProtocolAndIdempotency(request.ProtocolVersion, request.IdempotencyKey);
        if (request.WorkspaceId == Guid.Empty || request.IncidentId == Guid.Empty || request.EpisodeId == Guid.Empty || request.AttemptId == Guid.Empty ||
            !IsWorkflowPath(request.WorkflowIdentity) || !IsCanonicalWorkflowReference(request.WorkflowReference) || !IsGitRevision(request.WorkflowRevision) ||
            !IsGitRevision(request.ExpectedTargetRevision) || !IsSafeRef(request.TargetBranch) ||
            !IsSafeInput(request.WorkloadAudience, 200) ||
            request.ProducingRevision is not null && !IsGitRevision(request.ProducingRevision) ||
            !IsSafeInput(request.ProducingRevisionStatus, 100) || !IsNonce(request.OneTimeNonce) ||
            !IsSecureControlUri(request.ControlBaseUrl))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.InvalidRequest);

        var authorization = await ResolveAuthorizationAsync(request.Repository, cancellationToken);
        if (!authorization.ApprovedWorkflows.TryGetValue(request.WorkflowIdentity, out var approvedWorkflow) ||
            !FixedEquals(approvedWorkflow.Identity, request.WorkflowIdentity) ||
            !FixedEquals(approvedWorkflow.Reference, request.WorkflowReference) ||
            !FixedEquals(approvedWorkflow.Revision, request.WorkflowRevision))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.WorkflowNotAuthorized);

        var payloadHash = Hash(JsonSerializer.Serialize(request));
        var canonicalPayload = JsonSerializer.Serialize(request with { OneTimeNonce = "[REDACTED]" });
        var operationKey = new GitHubProviderOperationKey(
            request.Repository.ProviderConnectionId, ProviderOperationKind.DispatchWorkflow, request.IdempotencyKey);
        var replay = await GetReplayAsync<ProviderOperationReceipt>(operationKey, canonicalPayload, payloadHash, cancellationToken);
        if (replay is not null)
            return replay with { IsReplay = true };
        await ReserveAsync(operationKey, canonicalPayload, payloadHash, cancellationToken);

        var token = await tokenProvider.CreateRepositoryTokenAsync(
            authorization.Credential,
            authorization.InstallationId,
            GitHubInstallationTokenRequest.WorkflowDispatch(authorization.Name),
            cancellationToken) ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.TokenUnavailable);

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"repos/{Escape(authorization.Owner)}/{Escape(authorization.Name)}/actions/workflows/{EscapePath(request.WorkflowIdentity)}/dispatches")
        {
            Content = JsonContent.Create(new
            {
                // GitHub accepts a branch or tag here. OIDC exchange separately requires that
                // this configured ref resolved to the approved immutable workflow revision.
                @ref = WorkflowDispatchRef(request.WorkflowReference),
                inputs = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["valence_control_url"] = request.ControlBaseUrl.AbsoluteUri.TrimEnd('/'),
                    ["workspace_id"] = request.WorkspaceId.ToString("D"),
                    ["incident_id"] = request.IncidentId.ToString("D"),
                    ["attempt_id"] = request.AttemptId.ToString("D"),
                    ["attempt_nonce"] = request.OneTimeNonce,
                    ["producing_revision_status"] = request.ProducingRevisionStatus,
                    ["producing_revision"] = request.ProducingRevision ?? string.Empty,
                    ["workload_audience"] = request.WorkloadAudience,
                    ["target_branch"] = request.TargetBranch,
                    ["expected_target_revision"] = request.ExpectedTargetRevision
                }
            })
        };
        GitHubAppTokenProvider.AddGitHubHeaders(message, token.Value);
        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NoContent)
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);

        var acceptedAt = _timeProvider.GetUtcNow();
        var receipt = new ProviderOperationReceipt(request.IdempotencyKey, GetRequestId(response), false, acceptedAt);
        await CompleteAsync(operationKey, canonicalPayload, payloadHash, receipt, cancellationToken);
        return receipt;
    }

    private async ValueTask<GitHubRepositoryAuthorization> ResolveAuthorizationAsync(
        ProviderRepositoryReference repository,
        CancellationToken cancellationToken)
    {
        var authorization = await authorizationResolver.ResolveAsync(repository.ProviderConnectionId, cancellationToken);
        if (authorization is null || authorization.ProviderConnectionId != repository.ProviderConnectionId ||
            !FixedEquals(authorization.RepositoryProviderId, repository.RepositoryProviderId) ||
            !FixedEquals(authorization.Owner, repository.Owner) || !FixedEquals(authorization.Name, repository.Name))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.RepositoryNotAuthorized);
        return authorization;
    }

    private async ValueTask<ProviderWorkItemReference?> FindMachineIssueAsync(
        GitHubRepositoryAuthorization authorization,
        string token,
        string marker,
        CancellationToken cancellationToken)
    {
        ProviderWorkItemReference? match = null;
        for (var page = 1; page <= 100; page++)
        {
            using var request = NewRequest(HttpMethod.Get,
                $"repos/{Escape(authorization.Owner)}/{Escape(authorization.Name)}/issues?state=all&labels=valence-control-healing&per_page=100&page={page}", token);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
            var issues = await response.Content.ReadFromJsonAsync<IReadOnlyList<IssueResponse>>(cancellationToken) ?? [];
            foreach (var issue in issues.Where(x => x.Body?.Contains(marker, StringComparison.Ordinal) == true))
            {
                if (match is not null)
                    throw new GitHubSecurityException(GitHubSecurityReasonCodes.IdempotencyConflict);
                match = ToReference(issue, response);
            }
            if (issues.Count < 100)
                return match;
        }
        throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
    }

    private ValueTask<ProviderWorkItemReference> CreateIssueAsync(
        GitHubRepositoryAuthorization authorization, string token, string title, string body, CancellationToken cancellationToken) =>
        WriteIssueAsync(HttpMethod.Post,
            $"repos/{Escape(authorization.Owner)}/{Escape(authorization.Name)}/issues", token, title, body, cancellationToken);

    private ValueTask<ProviderWorkItemReference> UpdateIssueAsync(
        GitHubRepositoryAuthorization authorization, string token, long number, string title, string body, CancellationToken cancellationToken) =>
        WriteIssueAsync(HttpMethod.Patch,
            $"repos/{Escape(authorization.Owner)}/{Escape(authorization.Name)}/issues/{number}", token, title, body, cancellationToken);

    private async ValueTask<ProviderWorkItemReference> WriteIssueAsync(
        HttpMethod method, string uri, string token, string title, string body, CancellationToken cancellationToken)
    {
        using var request = NewRequest(method, uri, token);
        request.Content = JsonContent.Create(new { title, body, labels = new[] { "valence-control-healing" } });
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
        var issue = await response.Content.ReadFromJsonAsync<IssueResponse>(cancellationToken)
                    ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
        return ToReference(issue, response);
    }

    private async ValueTask<T?> GetReplayAsync<T>(
        GitHubProviderOperationKey key,
        string canonicalPayload,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var operation = await operationLedger.GetAsync(key, cancellationToken);
        if (operation is null)
            return default;
        if (!FixedEquals(operation.PayloadHash, payloadHash) ||
            !FixedEquals(operation.CanonicalPayloadJson, canonicalPayload))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.IdempotencyConflict);
        if (operation.Status == GitHubProviderOperationStatus.Reserved &&
            operation.UpdatedAt.Add(GitHubProviderOperationDefaults.ReservationLifetime) <= _timeProvider.GetUtcNow())
            return default;
        if (operation.Status != GitHubProviderOperationStatus.Completed || string.IsNullOrWhiteSpace(operation.ResultJson))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.OperationInProgress);
        return JsonSerializer.Deserialize<T>(operation.ResultJson);
    }

    private async ValueTask ReserveAsync(
        GitHubProviderOperationKey key,
        string canonicalPayload,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        if (!await operationLedger.TryReserveAsync(key, canonicalPayload, payloadHash, _timeProvider.GetUtcNow(), cancellationToken))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.OperationInProgress);
    }

    private async ValueTask CompleteAsync<T>(
        GitHubProviderOperationKey key,
        string canonicalPayload,
        string payloadHash,
        T result,
        CancellationToken cancellationToken) =>
        await operationLedger.CompleteAsync(
            key, canonicalPayload, payloadHash, JsonSerializer.Serialize(result), _timeProvider.GetUtcNow(), cancellationToken);

    private static ProviderWorkItemReference ToReference(IssueResponse issue, HttpResponseMessage response)
    {
        if (issue.Id <= 0 || issue.Number <= 0 || issue.HtmlUrl is null || !issue.HtmlUrl.IsAbsoluteUri)
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
        return new ProviderWorkItemReference(issue.Id.ToString(), issue.Number, issue.HtmlUrl, issue.State, GetRequestId(response));
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string uri, string token)
    {
        var request = new HttpRequestMessage(method, uri);
        GitHubAppTokenProvider.AddGitHubHeaders(request, token);
        return request;
    }

    private static string? GetRequestId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-GitHub-Request-Id", out var values) ? values.FirstOrDefault() : null;

    private static void ValidateProtocolAndIdempotency(string protocolVersion, string key)
    {
        if (protocolVersion != HealingContractVersions.ProviderProtocol || string.IsNullOrWhiteSpace(key) || key.Length > 200)
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.InvalidRequest);
    }

    private static bool IsWorkflowPath(string value) =>
        value.StartsWith(".github/workflows/", StringComparison.Ordinal) &&
        (value.EndsWith(".yml", StringComparison.Ordinal) || value.EndsWith(".yaml", StringComparison.Ordinal)) &&
        !value.Contains("..", StringComparison.Ordinal) && !value.Contains('\\') && value.Length <= 200;

    private static bool IsCanonicalWorkflowReference(string value)
    {
        const string headPrefix = "refs/heads/";
        const string tagPrefix = "refs/tags/";
        var name = value.StartsWith(headPrefix, StringComparison.Ordinal) ? value[headPrefix.Length..] :
            value.StartsWith(tagPrefix, StringComparison.Ordinal) ? value[tagPrefix.Length..] : null;
        return name is not null && IsSafeRef(name);
    }

    private static string WorkflowDispatchRef(string value) =>
        value.StartsWith("refs/heads/", StringComparison.Ordinal) ? value["refs/heads/".Length..] : value["refs/tags/".Length..];

    private static bool IsGitRevision(string value) => value.Length is 40 or 64 && value.All(Uri.IsHexDigit);
    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    private static bool IsNonce(string value) => value.Length is >= 32 and <= 256 && value.All(x => char.IsLetterOrDigit(x) || x is '-' or '_');
    private static bool IsSafeRef(string value) => value.Length is > 0 and <= 255 && !value.Contains("..", StringComparison.Ordinal) &&
        !value.Any(x => char.IsControl(x) || char.IsWhiteSpace(x) || x is '~' or '^' or ':' or '?' or '*' or '[' or '\\');
    private static bool IsSecureControlUri(Uri value) => value.IsAbsoluteUri && value.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(value.UserInfo) && string.IsNullOrEmpty(value.Fragment) && string.IsNullOrEmpty(value.Query);
    private static bool IsSafeInput(string value, int maximumLength) =>
        value.Length is > 0 && value.Length <= maximumLength && !value.Any(char.IsControl);
    private static string NeutralizeMentions(string value) => System.Net.WebUtility.HtmlEncode(
        string.Concat(value.Where(x => !char.IsControl(x) || x is '\n' or '\r' or '\t')))
        .Replace("@", "@\u200B", StringComparison.Ordinal);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool FixedEquals(string left, string right) => left.Length == right.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    private static string Escape(string value) => Uri.EscapeDataString(value);
    private static string EscapePath(string value) => string.Join('/', value.Split('/').Select(Uri.EscapeDataString));

    private sealed record IssueResponse(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("number")] long Number,
        [property: JsonPropertyName("html_url")] Uri? HtmlUrl,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("body")] string? Body);
}
